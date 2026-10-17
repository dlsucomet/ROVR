"""ROVR bridge: sits between the headset (or the Unity Editor) and Ollama.

    Headset --> bridge (this, port 8765) --> Ollama (127.0.0.1:11434)

- Passes every /api/... request on to Ollama unchanged and returns Ollama's answer.
- Prints each voice command live: what was heard, what is in view, the model's reply and how
  long it took. Also accepts plain log lines on POST /log (buttons, teleports, ...).
- Saves everything to Bridge/logs/bridge_<date>_<time>.txt.

Only the Python standard library is used. Run from the project folder:

    python Bridge/rovr_bridge.py                 # cable (adb reverse) or Unity Editor on this PC
    python Bridge/rovr_bridge.py --host 0.0.0.0  # also reachable over Wi-Fi (other devices)

Ollama itself stays on 127.0.0.1: only the bridge's port is ever reachable from the headset.
"""

import argparse
import datetime
import json
import os
import sys
import time
import urllib.error
import urllib.request
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
        elif self.path.startswith("/api/"):
            self._forward(b"")
        else:
            self._reply(404, b"not found", "text/plain")

    def do_POST(self):
        body = self._body()
        if self.path == "/log":
            self._log_line(body)
        elif self.path.startswith("/api/"):
            self._forward(body)
        else:
            self._reply(404, b"not found", "text/plain")

    # A line sent by the game: {"kind": "BUTTON", "details": "Maze"}
    def _log_line(self, body):
        try:
            data = json.loads(body)
            kind, details = str(data.get("kind", "LOG")), str(data.get("details", ""))
        except ValueError:
            kind, details = "LOG", body.decode("utf-8", "replace")
        colour = {"HEARD": BOLD, "PANEL": CYAN, "MIC": YELLOW, "SPEECH": GREY, "ERROR": RED,
                  "DROPPED": YELLOW, "SKIPPED": YELLOW, "WHISPER": YELLOW}.get(kind, "")
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
    args = parser.parse_args()

    Bridge.ollama = args.ollama.rstrip("/")

    logs = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
    os.makedirs(logs, exist_ok=True)
    path = os.path.join(logs, "bridge_" + datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S") + ".txt")
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
    print(f"{GREY}Ctrl+C to stop.{RESET}\n")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        _log_file.close()


if __name__ == "__main__":
    main()
