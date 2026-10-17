"""ROVR bridge: sits between the headset (or the Unity Editor) and Ollama.

    Headset --> bridge (this, port 8765) --> Ollama (127.0.0.1:11434)
                                         --> whisper.cpp server (127.0.0.1:8766), speech to text

- Passes every /api/... request on to Ollama unchanged and returns Ollama's answer.
- Turns speech into text on this PC (POST /transcribe, a WAV file): Whisper on the Quest's CPU takes
  14-17 s per sentence, whisper.cpp on a PC about 0.5 s. It uses the same model as the headset
  (Assets/StreamingAssets/ggml-base.bin). The audio is not saved, only the text.
- Prints each voice command live: what was heard, what is in view, the model's reply and how
  long it took. Also accepts plain log lines on POST /log (buttons, teleports, ...).
- Saves everything to Bridge/logs/bridge_<date>_<time>.txt.

Only the Python standard library is used. Run from the project folder:

    python Bridge/rovr_bridge.py                 # cable (adb reverse) or Unity Editor on this PC
    python Bridge/rovr_bridge.py --host 0.0.0.0  # also reachable over Wi-Fi (other devices)

Speech to text needs whisper.cpp's server program, which isn't in git: download whisper-bin-x64.zip
from https://github.com/ggml-org/whisper.cpp/releases and unzip it into Bridge/whisper/. Without it
the bridge still runs, and the headset transcribes by itself (slowly).

Ollama and the Whisper server stay on 127.0.0.1: only the bridge's port is ever reachable from the headset.
"""

import argparse
import datetime
import io
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock

# ---- terminal output ----------------------------------------------------------------------

os.system("")  # turns on colour codes in the Windows terminal

GREY, GREEN, YELLOW, RED, CYAN, BOLD, RESET = "\033[90m", "\033[92m", "\033[93m", "\033[91m", "\033[96m", "\033[1m", "\033[0m"

_print_lock = Lock()
_log_file = None


def log(kind, text, colour=""):
    """One timestamped line in the terminal and in the session log."""
    stamp = datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]
    with _print_lock:
        print(f"{GREY}{stamp}{RESET}  {colour}{kind:<8}{RESET}  {text}", flush=True)
        if _log_file:
            _log_file.write(f"{datetime.date.today()} {stamp}  {kind:<8}  {text}\n")
            _log_file.flush()


# ---- what a ROVR command looks like -------------------------------------------------------

# The throwaway command OllamaClient.WarmUp sends to load the model (see OllamaClient.cs).
WARM_UP_PROMPT = 'Visible objects: []\nPrevious command: none\nCommand: "stop"'


def describe_request(body):
    """Pulls the readable parts out of a /api/chat request from OllamaClient.cs."""
    try:
        data = json.loads(body)
    except ValueError:
        return None, None
    messages = data.get("messages") or []
    if not messages:
        return "warm-up", None  # an empty message list only loads the model
    user = next((m.get("content", "") for m in reversed(messages) if m.get("role") == "user"), "")
    if user.strip() == WARM_UP_PROMPT:
        return "warm-up", None  # OllamaClient.WarmUp's throwaway command, not something said
    heard, view = None, None
    for line in user.splitlines():
        if line.startswith("Command:"):
            heard = line[len("Command:"):].strip()
        elif line.startswith("Visible objects:"):
            view = line[len("Visible objects:"):].strip()
    return heard or user[:200], view


def describe_response(body):
    """The model's reply text and how long Ollama says it took."""
    try:
        data = json.loads(body)
    except ValueError:
        return body[:300].decode("utf-8", "replace") if isinstance(body, bytes) else str(body)[:300], None
    if "error" in data:
        return "ERROR: " + str(data["error"]), None
    content = (data.get("message") or {}).get("content", "")
    model_seconds = data.get("total_duration", 0) / 1e9 if data.get("total_duration") else None
    return content, model_seconds


def short(text, limit):
    text = " ".join(str(text).split())
    return text if len(text) <= limit else text[: limit - 3] + "..."


# ---- speech to text (whisper.cpp's server, started by the bridge) -------------------------

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
SLOW_PARTIAL_SECONDS = 1.5  # sentence-so-far pieces are only printed when they take longer than this


class Whisper:
    url = None       # "http://127.0.0.1:8766" once its server answers; None = the headset transcribes by itself
    process = None   # the server, when the bridge started it
    why_not = ""     # why there is no speech to text on this PC


def find_whisper_server(explicit):
    """whisper.cpp's server: --whisper, else anywhere in Bridge/whisper/, else in Voice Server/."""
    if explicit:
        return explicit if os.path.isfile(explicit) else None
    name = "whisper-server.exe" if os.name == "nt" else "whisper-server"
    for root in (os.path.join(HERE, "whisper"), os.path.join(PROJECT, "Voice Server")):
        for folder, _, files in os.walk(root):
            if name in files:
                return os.path.join(folder, name)
    return None


def answers(url, timeout=1):
    try:
        with urllib.request.urlopen(url, timeout=timeout):
            return True
    except urllib.error.HTTPError:
        return True  # something is there, it just doesn't like GET /
    except (urllib.error.URLError, OSError):
        return False


def start_whisper(args, logs, stamp):
    """Starts whisper.cpp's server on 127.0.0.1:<--whisper-port>, or reuses one already there."""
    url = f"http://127.0.0.1:{args.whisper_port}"
    if answers(url):
        Whisper.url = url  # e.g. left running by an earlier bridge
        return f"using the Whisper server already running at {url}"

    exe = find_whisper_server(args.whisper)
    if not exe:
        Whisper.why_not = "whisper.cpp's server isn't installed (unzip whisper-bin-x64.zip into Bridge/whisper/)"
        return None
    model = os.path.abspath(args.whisper_model)
    if not os.path.isfile(model) or os.path.getsize(model) < 1_000_000:
        # A clone without Git LFS has a small text file in place of the model.
        Whisper.why_not = f"no Whisper model at {model} (run: git lfs pull)"
        return None

    output = open(os.path.join(logs, f"whisper_{stamp}.txt"), "w", encoding="utf-8")
    threads = str(max(1, min(16, (os.cpu_count() or 4) // 2)))  # half the CPU: measured 0.48 s at 8, 0.39 s at 16
    # Same settings as the headset's WhisperManager: English, no translation, no timestamps.
    command = [exe, "-m", model, "-l", "en", "-nt", "-t", threads, "--host", "127.0.0.1", "--port", str(args.whisper_port)]
    try:
        Whisper.process = subprocess.Popen(command, cwd=os.path.dirname(exe), stdout=output, stderr=subprocess.STDOUT)
    except OSError as e:
        Whisper.why_not = f"can't start {exe}: {e}"
        return None

    deadline = time.time() + 60  # the model loads before the server starts listening
    while time.time() < deadline:
        if Whisper.process.poll() is not None:
            Whisper.why_not = f"the Whisper server stopped at start (exit code {Whisper.process.returncode}); see {output.name}"
            return None
        if answers(url):
            Whisper.url = url
            return f"whisper.cpp on this PC, model {os.path.basename(model)}"
        time.sleep(0.25)
    Whisper.why_not = f"the Whisper server didn't start within 60 s; see {output.name}"
    return None


def stop_whisper():
    if Whisper.process and Whisper.process.poll() is None:
        Whisper.process.terminate()
        try:
            Whisper.process.wait(5)
        except subprocess.TimeoutExpired:
            Whisper.process.kill()


def multipart(fields, file_field, file_name, file_bytes, file_type):
    """A multipart/form-data body (what whisper.cpp's /inference expects) and its Content-Type."""
    boundary = uuid.uuid4().hex
    parts = []
    for name, value in fields.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{file_field}"; filename="{file_name}"\r\n'
                 f'Content-Type: {file_type}\r\n\r\n'.encode() + file_bytes + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    return b"".join(parts), "multipart/form-data; boundary=" + boundary


def wav_seconds(body):
    try:
        with wave.open(io.BytesIO(body)) as w:
            return w.getnframes() / float(w.getframerate())
    except (wave.Error, EOFError, ZeroDivisionError):
        return None


# ---- the server ---------------------------------------------------------------------------

class Bridge(BaseHTTPRequestHandler):
    ollama = "http://127.0.0.1:11434"
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):  # silence the default one-line-per-request log
        pass

    def _reply(self, status, body, content_type="application/json"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _body(self):
        length = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(length) if length else b""

    def do_GET(self):
        if self.path == "/health":
            ok = self._ollama_up()
            self._reply(200 if ok else 503, b"ok" if ok else b"ollama not reachable", "text/plain")
        elif self.path == "/transcribe":
            # The game asks this to know whether to send its speech here (200) or transcribe it itself.
            ok = Whisper.url is not None
            self._reply(200 if ok else 503, b"ready" if ok else Whisper.why_not.encode(), "text/plain")
        elif self.path.startswith("/api/"):
            self._forward(b"")
        else:
            self._reply(404, b"not found", "text/plain")

    def do_POST(self):
        body = self._body()
        if self.path == "/log":
            self._log_line(body)
        elif self.path.split("?")[0] == "/transcribe":
            self._transcribe(body, "kind=partial" not in self.path)
        elif self.path.startswith("/api/"):
            self._forward(body)
        else:
            self._reply(404, b"not found", "text/plain")

    # A WAV file from the game -> {"text": "..."}. Sentences are printed; the sentence-so-far pieces
    # (sent every half second while someone speaks, for live captions and "stop") only when slow.
    def _transcribe(self, body, is_sentence):
        if Whisper.url is None:
            self._reply(503, json.dumps({"error": Whisper.why_not or "no speech to text on this PC"}).encode())
            return
        audio_seconds = wav_seconds(body)
        form, content_type = multipart({"response_format": "json", "temperature": "0.0"}, "file", "speech.wav", body, "audio/wav")
        request = urllib.request.Request(Whisper.url + "/inference", data=form, method="POST")
        request.add_header("Content-Type", content_type)
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                text = json.loads(response.read()).get("text", "")
        except (urllib.error.URLError, OSError, ValueError) as e:
            stopped = Whisper.process is not None and Whisper.process.poll() is not None
            if stopped:
                Whisper.url, Whisper.why_not = None, "the Whisper server stopped"
            log("ERROR", f"speech to text failed: {e}" + (" (the Whisper server stopped)" if stopped else ""), RED)
            self._reply(502, json.dumps({"error": f"speech to text failed: {e}"}).encode())
            return
        seconds = time.perf_counter() - started

        text = text.strip()
        length = f"{audio_seconds:.1f} s" if audio_seconds is not None else "?"
        if is_sentence:
            log("STT", f'{length} of speech -> "{short(text, 120)}"  {GREY}[{seconds:.2f} s on this PC]{RESET}', CYAN)
        elif seconds > SLOW_PARTIAL_SECONDS:
            log("STT", f'slow: {length} so far -> "{short(text, 80)}" took {seconds:.2f} s', YELLOW)
        self._reply(200, json.dumps({"text": text, "seconds": round(seconds, 3)}).encode())

    # A line sent by the game: {"kind": "BUTTON", "details": "Maze"}
    def _log_line(self, body):
        try:
            data = json.loads(body)
            kind, details = str(data.get("kind", "LOG")), str(data.get("details", ""))
        except ValueError:
            kind, details = "LOG", body.decode("utf-8", "replace")
        colour = {"HEARD": BOLD, "PANEL": CYAN, "MIC": YELLOW, "SPEECH": GREY, "ERROR": RED,
                  "DROPPED": YELLOW, "SKIPPED": YELLOW, "WHISPER": YELLOW, "STT": CYAN}.get(kind, "")
        log(kind, details, colour)
        self._reply(200, b"{}")

    def _forward(self, body):
        is_chat = self.path.startswith("/api/chat")
        heard, view = describe_request(body) if is_chat and body else (None, None)
        if heard == "warm-up":
            log("WARM-UP", "loading the model", GREY)
        elif heard:
            log("COMMAND", short(heard, 120), BOLD)  # already in quotes, as the game sends it
            if view:
                log("VIEW", short(view, 160), GREY)

        request = urllib.request.Request(self.ollama + self.path, data=body or None, method=self.command)
        request.add_header("Content-Type", self.headers.get("Content-Type", "application/json"))
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                status, answer = response.status, response.read()
        except urllib.error.HTTPError as e:
            status, answer = e.code, e.read()
        except (urllib.error.URLError, OSError) as e:
            log("ERROR", f"can't reach Ollama at {self.ollama} ({e}). Is it running?", RED)
            self._reply(502, json.dumps({"error": f"bridge could not reach Ollama: {e}"}).encode())
            return
        seconds = time.perf_counter() - started

        if is_chat and heard and heard != "warm-up":
            reply, model_seconds = describe_response(answer)
            timing = f"{seconds:.2f} s" + (f" (model {model_seconds:.2f} s)" if model_seconds else "")
            log("REPLY", f"{short(reply, 200)}  {GREY}[{timing}]{RESET}", RED if reply.startswith("ERROR") else GREEN)
        elif heard == "warm-up":
            log("WARM-UP", f"done in {seconds:.1f} s", GREY)

        self._reply(status, answer, response_type(answer))

    def _ollama_up(self):
        try:
            with urllib.request.urlopen(self.ollama + "/api/tags", timeout=3):
                return True
        except (urllib.error.URLError, OSError):
            return False


def response_type(body):
    return "application/json" if body[:1] in (b"{", b"[") else "text/plain"


def main():
    global _log_file
    parser = argparse.ArgumentParser(description="ROVR bridge between the headset and Ollama")
    parser.add_argument("--host", default="127.0.0.1", help="127.0.0.1 = this PC and the cable only; 0.0.0.0 = also Wi-Fi")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--ollama", default="http://127.0.0.1:11434", help="where Ollama is")
    parser.add_argument("--whisper", help="whisper.cpp's whisper-server program (default: looks in Bridge/whisper/)")
    parser.add_argument("--whisper-model", default=os.path.join(PROJECT, "Assets", "StreamingAssets", "ggml-base.bin"),
                        help="the Whisper model (default: the headset's own, so both transcribe alike)")
    parser.add_argument("--whisper-port", type=int, default=8766, help="the Whisper server's port, on 127.0.0.1 only")
    parser.add_argument("--no-whisper", action="store_true", help="no speech to text here: the headset transcribes by itself")
    args = parser.parse_args()

    Bridge.ollama = args.ollama.rstrip("/")

    logs = os.path.join(HERE, "logs")
    os.makedirs(logs, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    path = os.path.join(logs, "bridge_" + stamp + ".txt")
    _log_file = open(path, "w", encoding="utf-8")

    try:
        server = ThreadingHTTPServer((args.host, args.port), Bridge)
    except OSError as e:
        print(f"{RED}Can't listen on {args.host}:{args.port}: {e}{RESET}")
        print("Is the bridge already running in another terminal?")
        sys.exit(1)

    print(f"{BOLD}ROVR bridge{RESET} listening on {args.host}:{args.port}, forwarding to {Bridge.ollama}")
    print(f"Saving to {path}")
    up = Bridge._ollama_up(Bridge)
    print(f"Ollama: {GREEN + 'reachable' if up else RED + 'NOT reachable, start it with: ollama serve'}{RESET}")
    how = None
    if args.no_whisper:
        Whisper.why_not = "turned off (--no-whisper)"
    else:
        print(f"{GREY}Starting speech to text...{RESET}", end="\r", flush=True)
        how = start_whisper(args, logs, stamp)
    if Whisper.url:
        print(f"Speech to text: {GREEN}{how}{RESET}      ")
    else:
        print(f"Speech to text: {YELLOW}not on this PC, {Whisper.why_not}.{RESET}\n"
              f"  The headset transcribes by itself instead (slow on a Quest: about 15 s a sentence).")
    print(f"{GREY}Ctrl+C to stop.{RESET}\n")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        stop_whisper()
        _log_file.close()


if __name__ == "__main__":
    main()
