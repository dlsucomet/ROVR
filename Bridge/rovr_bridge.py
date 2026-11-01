"""ROVR bridge: sits between the headset (or the Unity Editor) and Ollama.

    Headset --> bridge (this, port 8765) --> Ollama (127.0.0.1:11434)
                                         --> whisper.cpp server (127.0.0.1:8766), speech to text

- Passes every /api/... request on to Ollama unchanged and returns Ollama's answer.
- Turns speech into text on this PC (POST /transcribe, a WAV file): Whisper on the Quest's CPU takes
  14-17 s per sentence, whisper.cpp on a PC about 0.5 s. It uses the same model as the headset
  (Assets/StreamingAssets/ggml-base.bin). The audio is not saved, only the text.
- Prints each voice command live: what was heard, what is in view, the model's reply and how
  long it took. Also accepts plain log lines on POST /log (buttons, teleports, ...).
- Runs the study session: the operator's control page at http://127.0.0.1:8765/control (this PC
  only) sets the participant code and condition, moves them between worlds and starts, skips and
  restarts tasks; the headset picks the commands up and reports back twice a second. Each task's
  result is saved in Bridge/logs/tasks_<code>_<date>_<time>.csv. The same commands can be typed here:
  setup PP1 rovr | setup PP1 manual | practice | plain | maze | house | start | skip | restart
- Saves everything to Bridge/logs/bridge_<date>_<time>.txt.

Only the Python standard library is used. Run from the project folder:

    python Bridge/rovr_bridge.py         # cable (adb reverse) or Unity Editor on this PC
    python Bridge/rovr_bridge.py --wifi  # also over Wi-Fi: the headset finds this PC by itself

With --wifi the headset can be on the same network instead of the cable. It broadcasts "ROVR-BRIDGE?"
(UDP, port 8765) and the bridge answers, so the PC's address never has to be typed in. Anyone on the
same network could then reach the bridge, so use it on your own Wi-Fi or a phone hotspot, not a shared
one. Only local-network addresses are let in (not the internet, not VPNs such as Radmin).

Speech to text needs whisper.cpp's server program, which isn't in git: download whisper-bin-x64.zip
from https://github.com/ggml-org/whisper.cpp/releases and unzip it into Bridge/whisper/. Without it
the bridge still runs, and the headset transcribes by itself (slowly).

Ollama and the Whisper server stay on 127.0.0.1: only the bridge's port is ever reachable from the headset.
"""

import argparse
import datetime
import io
import ipaddress
import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from collections import deque
from threading import Lock, Thread

# ---- terminal output ----------------------------------------------------------------------

os.system("")  # turns on colour codes in the Windows terminal

GREY, GREEN, YELLOW, RED, CYAN, BOLD, RESET = "\033[90m", "\033[92m", "\033[93m", "\033[91m", "\033[96m", "\033[1m", "\033[0m"

_print_lock = Lock()
_log_file = None
_recent = deque(maxlen=40)  # the latest lines, for the control page
_colour_codes = re.compile(r"\x1b\[[0-9;]*m")


def log(kind, text, colour=""):
    """One timestamped line in the terminal and in the session log."""
    stamp = datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]
    with _print_lock:
        print(f"{GREY}{stamp}{RESET}  {colour}{kind:<8}{RESET}  {text}", flush=True)
        plain = _colour_codes.sub("", str(text))
        _recent.append(f"{stamp[:8]}  {kind:<8}  {plain}")
        if _log_file:
            _log_file.write(f"{datetime.date.today()} {stamp}  {kind:<8}  {plain}\n")
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


# ---- Wi-Fi: the headset finds this PC by itself (--wifi) ----------------------------------

DISCOVERY_QUESTION = b"ROVR-BRIDGE?"  # what BridgeLink.cs broadcasts


def on_local_network(ip):
    """This PC, or a device on a home/lab network (192.168.x, 10.x, 172.16-31.x). Not the internet or a VPN."""
    try:
        address = ipaddress.ip_address(ip)
    except ValueError:
        return False
    return address.is_loopback or address.is_private


def lan_addresses():
    """This PC's addresses on local networks, for the startup message."""
    try:
        found = {info[4][0] for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET)}
    except OSError:
        found = set()
    return sorted(a for a in found if on_local_network(a) and not ipaddress.ip_address(a).is_loopback)


def answer_headsets(sock, port):
    """Answers each "ROVR-BRIDGE?" broadcast on sock (UDP) with the bridge's port and the PC's name. A thread."""
    reply = json.dumps({"rovr_bridge": 1, "port": port, "pc": socket.gethostname()}).encode()
    last_logged = {}  # ip -> time; one search sends several broadcasts, so print it once
    while True:
        try:
            question, (ip, their_port) = sock.recvfrom(256)
        except OSError:
            continue  # on Windows, an earlier reply that bounced shows up here; harmless
        if question.strip() != DISCOVERY_QUESTION or not on_local_network(ip):
            continue
        sock.sendto(reply, (ip, their_port))
        if time.time() - last_logged.get(ip, 0) > 5:
            log("WI-FI", f"found by {ip}", GREEN)
        last_logged[ip] = time.time()


# ---- the study session (Assets/GUI/StudySession.cs on the headset) -----------------------

class Session:
    lock = Lock()
    commands = []     # {"id", "kind", "code", "condition", "world"}, oldest first
    next_id = 1
    state = {}        # what the headset last said: code, condition, world, task, tasks, instruction, status, seconds
    seen = 0.0        # when the headset last synced (time.time())
    logs = ""         # Bridge/logs
    stamp = ""        # this run's date and time, for the CSV names


WORLDS = ("practice", "plain", "maze", "house")
CSV_COLUMNS = ["participant", "condition", "world", "task", "instruction", "outcome", "seconds", "started", "ended"]


def command(kind, code="", condition="", world=""):
    """Queues a command for the headset, which picks it up on its next sync."""
    with Session.lock:
        c = {"id": Session.next_id, "kind": kind, "code": code, "condition": condition, "world": world}
        Session.next_id += 1
        Session.commands.append(c)
        del Session.commands[:-50]
    log("CONTROL", " ".join(x for x in (kind, code, condition, world) if x), CYAN)
    return c


def parse_command(text):
    """'setup PP1 rovr', 'plain', 'start'... -> queued, or an error message."""
    words = text.strip().lower().split()
    if not words:
        return None
    if words[0] == "setup":
        if len(words) != 3 or words[2] not in ("rovr", "manual"):
            return "usage: setup <code> rovr|manual"
        command("setup", code=words[1].upper(), condition="Manual" if words[2] == "manual" else "ROVR")
    elif words[0] in WORLDS or (words[0] == "world" and len(words) == 2 and words[1] in WORLDS):
        command("world", world=(words[-1]).capitalize())
    elif words[0] in ("start", "skip", "restart"):
        command(words[0])
    else:
        return "commands: setup <code> rovr|manual, practice, plain, maze, house, start, skip, restart"
    return None


def read_typed_commands():
    """Commands typed into the bridge window (a thread; ends quietly if there's no keyboard)."""
    try:
        for line in sys.stdin:
            problem = parse_command(line)
            if problem:
                log("CONTROL", problem, YELLOW)
    except (OSError, ValueError):
        pass


def save_task(result):
    """One row per finished task, in a CSV per participant code."""
    import csv
    code = re.sub(r"[^A-Za-z0-9_-]", "", str(result.get("code") or "")) or "no-code"
    path = os.path.join(Session.logs, f"tasks_{code}_{Session.stamp}.csv")
    new = not os.path.exists(path)
    row = {"participant": result.get("code", ""), "condition": result.get("condition", ""), "world": result.get("world", ""),
           "task": result.get("task", ""), "instruction": result.get("instruction", ""), "outcome": result.get("outcome", ""),
           "seconds": f"{float(result.get('seconds') or 0):.2f}", "started": result.get("started", ""), "ended": result.get("ended", "")}
    with open(path, "a", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_COLUMNS)
        if new:
            writer.writeheader()
        writer.writerow(row)
    log("TASK", f"{row['world']} task {row['task']}: {row['outcome']} in {row['seconds']} s  {GREY}[{os.path.basename(path)}]{RESET}", GREEN)


CONTROL_PAGE = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>ROVR session control</title>
<style>
  :root { --bg:#f5f6f8; --card:#fff; --text:#1d2330; --muted:#6b7385; --line:#dfe3ea; --accent:#2f6fe4; --ok:#1f9d55; --warn:#c77d00; --bad:#c43c3c; }
  * { box-sizing:border-box; }
  body { margin:0; font:15px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; background:var(--bg); color:var(--text); }
  main { max-width:860px; margin:0 auto; padding:20px 16px 40px; display:grid; gap:14px; }
  h1 { font-size:20px; margin:0; display:flex; align-items:center; gap:10px; }
  .dot { width:10px; height:10px; border-radius:50%; background:var(--bad); display:inline-block; }
  .dot.on { background:var(--ok); }
  section { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:14px 16px; }
  h2 { font-size:13px; text-transform:uppercase; letter-spacing:.06em; color:var(--muted); margin:0 0 10px; }
  .row { display:flex; flex-wrap:wrap; gap:8px; align-items:center; }
  input, select, button { font:inherit; border-radius:8px; border:1px solid var(--line); padding:8px 12px; background:#fff; color:var(--text); }
  input { width:110px; text-transform:uppercase; }
  button { cursor:pointer; background:#eef1f6; }
  button:hover { background:#e2e7f0; }
  button.primary { background:var(--accent); color:#fff; border-color:var(--accent); }
  button.primary:hover { filter:brightness(1.07); }
  .who { font-size:16px; font-weight:600; }
  .instr { font-size:20px; font-weight:600; margin:6px 0 4px; min-height:28px; }
  .status { display:flex; gap:18px; align-items:baseline; margin:4px 0 12px; color:var(--muted); }
  .timer { font:600 34px/1 ui-monospace, Consolas, monospace; color:var(--text); }
  .badge { font-size:13px; padding:2px 8px; border-radius:99px; background:#eef1f6; color:var(--muted); }
  .badge.running { background:#e5f6ec; color:var(--ok); }
  .badge.ready { background:#fff4dc; color:var(--warn); }
  pre { margin:0; max-height:300px; overflow:auto; font:12px/1.5 ui-monospace, Consolas, monospace; white-space:pre-wrap; }
  .note { color:var(--muted); font-size:13px; }
</style></head><body><main>
<h1><span id="dot" class="dot"></span> ROVR session control <span id="link" class="note"></span></h1>

<section><h2>Participant</h2>
  <div class="row">
    <input id="code" placeholder="PP1" maxlength="8" aria-label="Participant code">
    <select id="condition" aria-label="Condition"><option value="ROVR">ROVR (voice)</option><option value="Manual">Manual (controller)</option></select>
    <button class="primary" onclick="setup()">Set up</button>
    <span class="who" id="who"></span>
  </div>
  <p class="note">Set up moves them to the practice area and shows their tutorial.</p>
</section>

<section><h2>World</h2>
  <div class="row">
    <button onclick="send({kind:'world', world:'Practice'})">Practice</button>
    <button onclick="send({kind:'world', world:'Plain'})">Plain</button>
    <button onclick="send({kind:'world', world:'Maze'})">Maze</button>
    <button onclick="send({kind:'world', world:'House'})">House</button>
  </div>
</section>

<section><h2>Task <span id="taskno"></span></h2>
  <div class="instr" id="instr">No task</div>
  <div class="status"><span class="timer" id="timer">0.0 s</span><span class="badge" id="status">-</span></div>
  <div class="row">
    <button class="primary" onclick="send({kind:'start'})">Start (F5)</button>
    <button onclick="send({kind:'skip'})">Skip</button>
    <button onclick="send({kind:'restart'})">Restart</button>
  </div>
  <p class="note">Read the instruction, say "go", then Start. The task ends by itself when they reach the goal. Results: <span id="csv">Bridge/logs/tasks_&lt;code&gt;_*.csv</span></p>
</section>

<section><h2>Log</h2><pre id="log"></pre></section>
</main>
<script>
async function send(c) {
  await fetch('/control/command', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(c) });
  refresh();
}
function setup() {
  const code = document.getElementById('code').value.trim().toUpperCase();
  if (!code) { document.getElementById('code').focus(); return; }
  send({kind:'setup', code, condition: document.getElementById('condition').value});
}
document.addEventListener('keydown', e => { if (e.key === 'F5') { e.preventDefault(); send({kind:'start'}); } });
async function refresh() {
  try {
    const r = await (await fetch('/control/status')).json();
    const s = r.state || {};
    document.getElementById('dot').className = 'dot' + (r.connected ? ' on' : '');
    document.getElementById('link').textContent = r.connected ? 'headset connected' : 'headset not connected';
    document.getElementById('who').textContent = s.code ? s.code + ' \u00b7 ' + s.condition + (s.world ? ' \u00b7 ' + s.world : '') : 'not set up';
    document.getElementById('taskno').textContent = s.tasks ? (s.task <= s.tasks ? s.task + ' of ' + s.tasks : 'all done') : '';
    document.getElementById('instr').textContent = s.instruction || (s.tasks ? 'No more tasks in this world' : 'No task');
    document.getElementById('timer').textContent = (s.seconds || 0).toFixed(1) + ' s';
    const st = document.getElementById('status');
    st.textContent = s.status || '-';
    st.className = 'badge ' + (s.status || '');
    document.getElementById('log').textContent = r.recent.slice().reverse().join('\n');
  } catch (e) {
    document.getElementById('link').textContent = 'bridge not answering';
  }
}
setInterval(refresh, 500);
refresh();
</script></body></html>
"""


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

    # With --wifi the bridge listens on the network: only this PC and local-network devices get in.
    def _allowed(self):
        if on_local_network(self.client_address[0]):
            return True
        self.close_connection = True
        self._reply(403, b"only devices on the local network", "text/plain")
        return False

    # The control page is for the operator on this PC only, even when the bridge listens on Wi-Fi.
    def _from_this_pc(self):
        if ipaddress.ip_address(self.client_address[0]).is_loopback:
            return True
        self.close_connection = True
        self._reply(403, b"the control page is for this PC only", "text/plain")
        return False

    def do_GET(self):
        if not self._allowed():
            return
        if self.path in ("/control", "/control/"):
            if self._from_this_pc():
                self._reply(200, CONTROL_PAGE.encode("utf-8"), "text/html; charset=utf-8")
        elif self.path == "/control/status":
            if self._from_this_pc():
                with Session.lock:
                    status = {"connected": time.time() - Session.seen < 3, "state": Session.state, "recent": list(_recent)}
                self._reply(200, json.dumps(status).encode())
        elif self.path == "/ping":
            # BridgeLink.cs checks with this that it found a ROVR bridge, and that it still answers.
            self._reply(200, ("rovr-bridge " + socket.gethostname()).encode(), "text/plain")
        elif self.path == "/health":
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
        body = self._body()  # read even when refusing, so the connection stays in step
        if not self._allowed():
            return
        if self.path == "/log":
            self._log_line(body)
        elif self.path == "/session/sync":
            self._session_sync(body)
        elif self.path == "/session/task":
            try:
                save_task(json.loads(body))
                self._reply(200, b"{}")
            except (ValueError, OSError) as e:
                log("ERROR", f"task result not saved: {e}", RED)
                self._reply(400, json.dumps({"error": str(e)}).encode())
        elif self.path == "/control/command":
            if self._from_this_pc():
                try:
                    c = json.loads(body)
                    kind = str(c.get("kind", "")).lower()
                    if kind == "setup":
                        command("setup", code=str(c.get("code", "")).upper(), condition="Manual" if str(c.get("condition")) == "Manual" else "ROVR")
                    elif kind == "world":
                        command("world", world=str(c.get("world", "")))
                    elif kind in ("start", "skip", "restart"):
                        command(kind)
                    self._reply(200, b"{}")
                except ValueError:
                    self._reply(400, b"{}")
        elif self.path.split("?")[0] == "/transcribe":
            self._transcribe(body, "kind=partial" not in self.path)
        elif self.path.startswith("/api/"):
            self._forward(body)
        else:
            self._reply(404, b"not found", "text/plain")

    # The headset's state in, any newer commands out. On its first sync (after = -1) it is told where the
    # commands are up to and gets none, so a restarted app doesn't replay old ones.
    def _session_sync(self, body):
        try:
            state = json.loads(body)
        except ValueError:
            self._reply(400, b"{}")
            return
        after = int(state.pop("after", -1))
        with Session.lock:
            first = Session.seen == 0 or time.time() - Session.seen > 10
            Session.state, Session.seen = state, time.time()
            latest = Session.next_id - 1
            pending = [] if after < 0 else [c for c in Session.commands if c["id"] > after]
        if first:
            log("SESSION", "headset connected" + (f" ({state.get('code')}, {state.get('condition')})" if state.get("code") else ""), GREEN)
        self._reply(200, json.dumps({"commands": pending, "latest": latest}).encode())

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
    parser.add_argument("--wifi", action="store_true",
                        help="also over Wi-Fi: listen on the network and answer the headset's search for this PC")
    parser.add_argument("--host", help="the address to listen on (default 127.0.0.1, or 0.0.0.0 with --wifi)")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--ollama", default="http://127.0.0.1:11434", help="where Ollama is")
    parser.add_argument("--whisper", help="whisper.cpp's whisper-server program (default: looks in Bridge/whisper/)")
    parser.add_argument("--whisper-model", default=os.path.join(PROJECT, "Assets", "StreamingAssets", "ggml-base.bin"),
                        help="the Whisper model (default: the headset's own, so both transcribe alike)")
    parser.add_argument("--whisper-port", type=int, default=8766, help="the Whisper server's port, on 127.0.0.1 only")
    parser.add_argument("--no-whisper", action="store_true", help="no speech to text here: the headset transcribes by itself")
    args = parser.parse_args()
    if args.host is None:
        args.host = "0.0.0.0" if args.wifi else "127.0.0.1"

    Bridge.ollama = args.ollama.rstrip("/")

    logs = os.path.join(HERE, "logs")
    os.makedirs(logs, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    Session.logs, Session.stamp = logs, stamp
    path = os.path.join(logs, "bridge_" + stamp + ".txt")
    _log_file = open(path, "w", encoding="utf-8")

    try:
        server = ThreadingHTTPServer((args.host, args.port), Bridge)
        if args.wifi:
            finder = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            finder.bind(("0.0.0.0", args.port))  # UDP; same number as the bridge's TCP port
            Thread(target=answer_headsets, args=(finder, args.port), daemon=True).start()
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
    if args.wifi:
        addresses = lan_addresses()
        if addresses:
            print(f"Wi-Fi: {GREEN}on{RESET}. A headset on the same network finds this PC by itself "
                  f"(this PC: {', '.join('http://%s:%d' % (a, args.port) for a in addresses)}).")
        else:
            print(f"Wi-Fi: {YELLOW}on, but this PC has no local-network address. Is it connected?{RESET}")
    else:
        print(f"Wi-Fi: off (cable and this PC only). For a headset on Wi-Fi: run_bridge_wifi.bat, or --wifi.")
    print(f"Session control: {BOLD}http://127.0.0.1:{args.port}/control{RESET} (this PC only), or type here: "
          f"setup PP1 rovr | plain | start | skip | restart")
    print(f"{GREY}Ctrl+C to stop.{RESET}\n")
    Thread(target=read_typed_commands, daemon=True).start()

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        stop_whisper()
        _log_file.close()


if __name__ == "__main__":
    main()
