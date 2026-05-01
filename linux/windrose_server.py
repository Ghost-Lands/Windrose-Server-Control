#!/usr/bin/env python3
"""
Windrose Server Control - Linux Edition
Browser-based dedicated server manager for Windrose
Run with: python3 windrose_server.py
Dashboard: http://localhost:7777
"""

import os, sys, json, time, threading, subprocess, re, signal, socket
import urllib.request, urllib.error
from http.server import HTTPServer, BaseHTTPRequestHandler
from datetime import datetime, date
from pathlib import Path

# ─── Settings ────────────────────────────────────────────────────────────────

SETTINGS_FILE = Path(__file__).parent / "windrose_settings.json"

DEFAULT_SETTINGS = {
    "serverDir": "",
    "steamCmd": "",
    "webhookUrl": "",
    "guiPort": 7777,
    "rconApiUrl": "http://localhost:9600",
    "rconApiKey": "",
    "rconPollSec": 5,
    "restartHour": -1,
    "updateHours": 3,
    "notifyOnline": True,
    "notifyOffline": True,
    "notifyCrash": True,
    "notifyRestart": True,
    "notifyUpdate": True,
    "notifyDaily": True,
    "notifyJoin": True,
    "notifyLeave": True,
    "notifyKick": True,
}

def load_settings():
    s = dict(DEFAULT_SETTINGS)
    try:
        if SETTINGS_FILE.exists():
            text = SETTINGS_FILE.read_text(encoding="utf-8")
            data = json.loads(text)
            s.update(data)
    except Exception as e:
        print(f"Settings load error: {e} — using defaults")
    return s

def save_settings(s):
    try:
        SETTINGS_FILE.write_text(json.dumps(s, indent=2), encoding="utf-8")
    except Exception as e:
        print(f"Settings save error: {e}")

# ─── State ────────────────────────────────────────────────────────────────────

cfg = load_settings()
state_lock = threading.Lock()
players = []       # list of {"name": str, "accountId": str, "status": str, "ping": int}
banlist = []       # list of {"name": str, "accountId": str}
logs = []          # list of {"ts": str, "type": str, "msg": str}
server_proc = None
status = "offline"
uptime_start = None
last_update = "never"
next_update = datetime.now()
last_daily = date.today().replace(day=date.today().day - 1) if date.today().day > 1 else date.today()
manual_stop = False
server_exe = ""
log_file = ""
banlist_file = ""

MAX_LOGS = 300

def log(typ, msg):
    ts = datetime.now().strftime("%H:%M")
    entry = {"ts": ts, "type": typ, "msg": msg}
    with state_lock:
        logs.append(entry)
        if len(logs) > MAX_LOGS:
            logs.pop(0)
    print(f"[{ts}] {msg}")

# ─── Path Detection ───────────────────────────────────────────────────────────

def detect_paths():
    global server_exe, log_file, banlist_file, cfg

    server_dir = cfg.get("serverDir", "")
    steam_cmd  = cfg.get("steamCmd", "")

    # Validate saved paths
    if server_dir:
        p = Path(server_dir)
        valid = (
            (p / "R5" / "Binaries" / "Linux" / "WindroseServer-Linux-Shipping").exists() or
            (p / "R5" / "Binaries" / "Win64" / "WindroseServer-Win64-Shipping.exe").exists() or
            (p / "WindroseServer.exe").exists() or
            (p / "WindroseServer").exists()
        )
        if not valid:
            print(f"Saved serverDir invalid: {server_dir}")
            server_dir = ""

    # Auto-detect server
    if not server_dir:
        print("Scanning for Windrose server...")
        candidates = [
            Path.home() / "Windrose Dedicated Server",
            Path.home() / "windrose-dedicated",
            Path.home() / "windrose_server",
            Path("/opt/windrose"),
            Path("/srv/windrose"),
            Path("C:/Program Files (x86)/Steam/steamapps/common/Windrose Dedicated Server"),
            Path("C:/Program Files/Steam/steamapps/common/Windrose Dedicated Server"),
            Path.home() / "Desktop" / "Windrose Dedicated Server",
        ]
        # Also scan home subdirectories
        try:
            for d in Path.home().iterdir():
                if d.is_dir():
                    candidates.append(d)
        except: pass
        # Scan Desktop subdirectories
        try:
            desktop = Path.home() / "Desktop"
            for d in desktop.iterdir():
                if d.is_dir():
                    candidates.append(d)
        except: pass

        for c in candidates:
            if not c.exists(): continue
            if (
                (c / "R5" / "Binaries" / "Linux" / "WindroseServer-Linux-Shipping").exists() or
                (c / "R5" / "Binaries" / "Win64" / "WindroseServer-Win64-Shipping.exe").exists() or
                (c / "WindroseServer.exe").exists() or
                (c / "WindroseServer").exists()
            ):
                server_dir = str(c)
                print(f"Found server: {server_dir}")
                break

    # Auto-detect SteamCMD
    if not steam_cmd:
        log("info", "Scanning for SteamCMD...")
        steam_candidates = [
            Path.home() / "steamcmd" / "steamcmd.sh",
            Path.home() / ".steam" / "steamcmd" / "steamcmd.sh",
            Path("/usr/games/steamcmd"),
            Path("/usr/bin/steamcmd"),
            Path("/opt/steamcmd/steamcmd.sh"),
        ]
        for c in steam_candidates:
            if c.exists():
                steam_cmd = str(c)
                log("ok", f"Found SteamCMD: {steam_cmd}")
                break

    # Save if changed
    if server_dir != cfg.get("serverDir") or steam_cmd != cfg.get("steamCmd"):
        cfg["serverDir"] = server_dir
        cfg["steamCmd"] = steam_cmd
        save_settings(cfg)

    # Set derived paths
    if server_dir:
        sd = Path(server_dir)
        exe_candidates = [
            sd / "R5" / "Binaries" / "Win64" / "WindroseServer-Win64-Shipping.exe",
            sd / "R5" / "Binaries" / "Linux" / "WindroseServer-Linux-Shipping",
            sd / "WindroseServer.exe",
            sd / "WindroseServer",
        ]
        server_exe = next((str(e) for e in exe_candidates if e.exists()), str(exe_candidates[0]))
        banlist_path = sd / "R5" / "Binaries" / "Win64" / "ue4ss" / "Mods" / "WindroseAPI" / "banlist.json"
        if not banlist_path.exists():
            banlist_path = sd / "R5" / "Binaries" / "Linux" / "ue4ss" / "Mods" / "WindroseAPI" / "banlist.json"
        banlist_file = str(banlist_path)
        log("info", f"Banlist file: {banlist_file}")
        log_dir = sd / "R5" / "Saved" / "Logs"
        find_log_file(log_dir)
    else:
        log("warn", "Server directory not found. Set serverDir in windrose_settings.json")

def find_log_file(log_dir):
    global log_file
    try:
        logs_found = sorted(log_dir.glob("R5*.log"), key=lambda f: f.stat().st_mtime, reverse=True)
        for lf in logs_found:
            if "backup" not in lf.name.lower():
                log_file = str(lf)
                log("info", f"Log file: {log_file}")
                return
    except: pass

# ─── Server Control ───────────────────────────────────────────────────────────

def start_server():
    global server_proc, status, uptime_start, manual_stop
    if not server_exe or not Path(server_exe).exists():
        log("err", f"EXE not found: {server_exe}")
        return
    # Check if already running
    if is_server_running():
        log("ok", "Server already running - attaching")
        status = "online"
        uptime_start = datetime.now()
        return

    log("info", "Starting server...")
    status = "starting"
    server_dir = cfg.get("serverDir", "")
    try:
        sd = Path(server_dir)
        # Use the same launch method as StartServerForeground.bat
        win_exe = sd / "R5" / "Binaries" / "Win64" / "WindroseServer-Win64-Shipping.exe"
        linux_exe = sd / "R5" / "Binaries" / "Linux" / "WindroseServer-Linux-Shipping"
        root_exe = sd / "WindroseServer.exe"
        root_exe_linux = sd / "WindroseServer"

        if win_exe.exists():
            launch_exe = str(win_exe)
            args = [launch_exe, "-log"]
        elif linux_exe.exists():
            launch_exe = str(linux_exe)
            args = [launch_exe, "-log"]
        elif root_exe.exists():
            launch_exe = str(root_exe)
            args = [launch_exe, "-log"]
        elif root_exe_linux.exists():
            launch_exe = str(root_exe_linux)
            args = [launch_exe, "-log"]
        else:
            log("err", f"EXE not found in {server_dir}")
            status = "offline"
            return

        proc = subprocess.Popen(
            args,
            cwd=server_dir,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        # Wait for server to come up
        for _ in range(30):
            time.sleep(1)
            if proc.poll() is not None:
                log("err", "Server exited immediately")
                status = "offline"
                return
            if is_server_running():
                break
        server_proc = proc
        status = "online"
        uptime_start = datetime.now()
        log("ok", f"Server running PID {proc.pid}")
        if cfg.get("notifyOnline"):
            send_webhook(0x3dba8c, "Server Online", "Windrose server is online.")
        # Refresh log file
        sd = Path(cfg.get("serverDir", ""))
        find_log_file(sd / "R5" / "Saved" / "Logs")
    except Exception as e:
        log("err", f"Failed to start server: {e}")
        status = "offline"

def stop_server():
    global server_proc, status, uptime_start, manual_stop
    log("info", "Stopping server...")
    status = "stopping"
    try:
        if os.name == "nt":
            subprocess.run(["taskkill", "/F", "/IM", "WindroseServer-Win64-Shipping.exe"], capture_output=True)
        else:
            result = subprocess.run(["pgrep", "-f", "WindroseServer"], capture_output=True, text=True)
            if result.returncode == 0:
                for pid in result.stdout.strip().split("\n"):
                    try: subprocess.run(["kill", "-TERM", pid.strip()])
                    except: pass
    except: pass
    if server_proc:
        try:
            server_proc.terminate()
            server_proc.wait(timeout=10)
        except: pass
        server_proc = None
    status = "offline"
    uptime_start = None
    with state_lock:
        players.clear()
    log("warn", "Server stopped")
    if cfg.get("notifyOffline"):
        send_webhook(0xe74c3c, "Server Offline", "Windrose server stopped.")

def restart_server():
    stop_server()
    time.sleep(2)
    start_server()

def is_server_running():
    try:
        if os.name == "nt":
            result = subprocess.run(
                ["tasklist", "/FI", "IMAGENAME eq WindroseServer-Win64-Shipping.exe", "/NH"],
                capture_output=True, text=True)
            return "WindroseServer-Win64-Shipping.exe" in result.stdout
        else:
            result = subprocess.run(["pgrep", "-f", "WindroseServer"], capture_output=True, text=True)
            return result.returncode == 0
    except:
        return False

# ─── Crash Monitor ────────────────────────────────────────────────────────────

def crash_monitor():
    global status, manual_stop
    while True:
        time.sleep(10)
        if status == "online" and not manual_stop:
            if not is_server_running():
                log("err", "Server crash detected! Restarting...")
                status = "offline"
                if cfg.get("notifyCrash"):
                    send_webhook(0xe74c3c, "Server Crash", "Windrose server crashed — restarting.")
                time.sleep(3)
                start_server()

# ─── Log Tail ─────────────────────────────────────────────────────────────────

NOISE_FILTERS = [
    "R5LogAccountTracker", "R5LogIceProtocol", "R5LogNetCm:Verbose",
    "R5LogNetBL", "R5LogP2pGate", "R5LogHttp", "R5LogDataKeeper:Verbose",
]

def should_filter(line):
    return any(f in line for f in NOISE_FILTERS)

def log_tail():
    global log_file
    log("info", "Log tail started")
    history_scanned = False
    last_pos = 0
    last_inode = None

    while True:
        if not log_file or not Path(log_file).exists():
            time.sleep(2)
            continue

        try:
            inode = Path(log_file).stat().st_ino
            if inode != last_inode:
                # Log rotated or new file
                last_inode = inode
                last_pos = 0
                history_scanned = False

            with open(log_file, "r", encoding="utf-8", errors="replace") as f:
                if not history_scanned:
                    history_scan(f)
                    history_scanned = True
                    last_pos = f.tell()

                f.seek(last_pos)
                for line in f:
                    process_log_line(line.rstrip())
                last_pos = f.tell()
        except Exception as e:
            pass

        time.sleep(0.5)

def history_scan(f):
    joined = left = online = 0
    f.seek(0)
    seen = set()
    for line in f:
        line = line.rstrip()
        mj = re.search(r"LogNet: Join succeeded: (.+)", line)
        if mj:
            name = mj.group(1).strip()
            seen.add(name)
            joined += 1
        ml = re.search(r"FarewellReason.*Name '([^']+)'", line)
        if not ml:
            ml = re.search(r"Name '([^']+)'.*SaidFarewell", line)
        if ml:
            name = ml.group(1).strip()
            if name in seen:
                seen.discard(name)
            left += 1
        # Capture account IDs
        ma = re.search(r"Name=(\w+)-([0-9A-F]{16,})", line)
        if ma:
            pname = ma.group(1)
            aid = ma.group(2)
            with state_lock:
                for p in players:
                    if p["name"] == pname and not p.get("accountId"):
                        p["accountId"] = aid

    # Add remaining as online
    for name in seen:
        with state_lock:
            if not any(p["name"] == name for p in players):
                players.append({"name": name, "accountId": "", "status": "online", "ping": 0})
        online += 1

    log("info", f"History scan: {joined} joined, {left} left, {online} online")

def process_log_line(line):
    if not line or should_filter(line):
        return

    # Classify log type
    typ = "info"
    if "Error:" in line or "error:" in line:
        typ = "err"
    elif "Warning:" in line or "warning:" in line:
        typ = "warn"
    elif "Join succeeded:" in line or "Server running" in line:
        typ = "ok"

    # Add to log (truncate long lines)
    display = line if len(line) <= 200 else line[:200] + "..."
    ts = datetime.now().strftime("%H:%M")
    with state_lock:
        logs.append({"ts": ts, "type": typ, "msg": display})
        if len(logs) > MAX_LOGS:
            logs.pop(0)

    # Player join
    mj = re.search(r"LogNet: Join succeeded: (.+)", line)
    if mj:
        name = mj.group(1).strip()
        with state_lock:
            if not any(p["name"] == name for p in players):
                players.append({"name": name, "accountId": "", "status": "online", "ping": 0})
        log("ok", f"{name} joined")
        if cfg.get("notifyJoin"):
            send_webhook(0x3dba8c, "Player Joined", f"{name} joined the server.")

    # Account ID capture
    ma = re.search(r"Name=(\w+)-([0-9A-F]{16,})", line)
    if ma:
        pname, aid = ma.group(1), ma.group(2)
        with state_lock:
            for p in players:
                if p["name"] == pname:
                    p["accountId"] = aid

    # Player leave
    ml = re.search(r"FarewellReason.*Name '([^']+)'", line)
    if not ml:
        ml = re.search(r"Account farewell.*AccountId ([0-9A-F]{16,})", line)
    if ml:
        name = ml.group(1).strip()
        with state_lock:
            players[:] = [p for p in players if p["name"] != name and p.get("accountId") != name]
        log("warn", f"{name} left")
        if cfg.get("notifyLeave"):
            send_webhook(0xe67e22, "Player Left", f"{name} left the server.")

# ─── RCON Poll ────────────────────────────────────────────────────────────────

def rcon_poll():
    while True:
        interval = max(2, cfg.get("rconPollSec", 5))
        time.sleep(interval)
        if not cfg.get("rconApiKey"):
            continue
        try:
            url = cfg.get("rconApiUrl", "http://localhost:9600").rstrip("/") + "/players"
            req = urllib.request.Request(url, headers={"Authorization": f"Bearer {cfg['rconApiKey']}"})
            with urllib.request.urlopen(req, timeout=3) as resp:
                data = json.loads(resp.read().decode())
                plist = data.get("players", data) if isinstance(data, dict) else data
                with state_lock:
                    for p in plist:
                        name = p.get("playerName") or p.get("name", "")
                        aid = p.get("accountId") or p.get("account_id", "")
                        ping = p.get("ping", 0)
                        existing = next((x for x in players if x["name"] == name), None)
                        if existing:
                            existing["accountId"] = aid or existing["accountId"]
                            existing["ping"] = ping
                        else:
                            players.append({"name": name, "accountId": aid, "status": "online", "ping": ping})
        except: pass

def rcon_request(endpoint, body):
    if not cfg.get("rconApiKey"):
        return
    def do_request():
        try:
            url = cfg.get("rconApiUrl", "http://localhost:9600").rstrip("/") + endpoint
            data = json.dumps(body).encode()
            req = urllib.request.Request(url, data=data, method="POST",
                headers={"Authorization": f"Bearer {cfg['rconApiKey']}", "Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=5) as resp:
                result = json.loads(resp.read().decode())
                log("ok", f"RCON {endpoint}: {result}")
        except Exception as e:
            log("err", f"RCON {endpoint} error: {e}")
    threading.Thread(target=do_request, daemon=True).start()

def rcon_get(endpoint):
    try:
        url = cfg.get("rconApiUrl", "http://localhost:9600").rstrip("/") + endpoint
        req = urllib.request.Request(url, headers={"Authorization": f"Bearer {cfg['rconApiKey']}"})
        with urllib.request.urlopen(req, timeout=5) as resp:
            return json.loads(resp.read().decode())
    except: return None

# ─── Scheduler ────────────────────────────────────────────────────────────────

def scheduler():
    global last_daily, next_update
    next_update = datetime.now()
    while True:
        time.sleep(60)
        now = datetime.now()

        # Daily restart
        rh = cfg.get("restartHour", -1)
        if rh >= 0 and now.hour == rh and now.minute == 0 and last_daily < date.today() and status == "online":
            last_daily = date.today()
            log("info", f"Daily {rh:02d}:00 restart triggered")
            if cfg.get("notifyDaily"):
                send_webhook(0xf39c12, "Daily Restart", "Scheduled daily restart.")
            threading.Thread(target=restart_server, daemon=True).start()

        # Auto-update
        if now >= next_update:
            next_update = now.replace(microsecond=0)  # will be reset in update_server
            threading.Thread(target=update_server, daemon=True).start()

def update_server():
    global last_update, next_update
    steam = cfg.get("steamCmd", "")
    server_dir = cfg.get("serverDir", "")
    hours = max(1, cfg.get("updateHours", 3))
    next_update = datetime.now().__class__.now().replace(microsecond=0)
    from datetime import timedelta
    next_update_time = datetime.now() + timedelta(hours=hours)

    if not steam or not Path(steam).exists():
        log("warn", "SteamCMD not found — skipping update check")
        next_update = next_update_time
        return

    log("info", "Checking for updates...")
    try:
        # Self-update SteamCMD
        subprocess.run([steam, "+quit"], cwd=str(Path(steam).parent), capture_output=True, timeout=60)

        result = subprocess.run(
            [steam, "+force_install_dir", server_dir, "+login", "anonymous", "+app_update", "4129620", "-validate", "+quit"],
            cwd=str(Path(steam).parent),
            capture_output=True, text=True, timeout=600
        )
        output = result.stdout + result.stderr
        last_update = datetime.now().strftime("%H:%M")
        next_update = next_update_time

        if "0x606" in output or "0x602" in output or "Error!" in output:
            log("err", f"SteamCMD error — retrying...")
            result2 = subprocess.run(
                [steam, "+force_install_dir", server_dir, "+login", "anonymous", "+app_update", "4129620", "-validate", "+quit"],
                cwd=str(Path(steam).parent),
                capture_output=True, text=True, timeout=600
            )
            output = result2.stdout + result2.stderr

        updated = "Update state (0x61)" in output or "Update state (0x11)" in output or "Downloading" in output
        if updated:
            log("ok", "Update found! Restarting server...")
            if cfg.get("notifyUpdate"):
                send_webhook(0x3066993, "Update Complete", "Windrose updated — restarting.")
            was_online = status == "online"
            if was_online:
                stop_server()
                time.sleep(3)
            start_server()
        else:
            log("ok", f"No update needed. Next check at {next_update.strftime('%H:%M')}")
    except Exception as e:
        log("err", f"Update error: {e}")
        next_update = next_update_time

# ─── Discord Webhook ─────────────────────────────────────────────────────────

def send_webhook(color, title, desc):
    url = cfg.get("webhookUrl", "")
    if not url:
        return
    def do_send():
        try:
            payload = json.dumps({"embeds": [{"title": title, "description": desc, "color": color}]}).encode()
            req = urllib.request.Request(url, data=payload, method="POST",
                headers={"Content-Type": "application/json"})
            urllib.request.urlopen(req, timeout=5)
        except: pass
    threading.Thread(target=do_send, daemon=True).start()

# ─── CPU/RAM Stats ────────────────────────────────────────────────────────────

def get_stats():
    cpu = 0.0
    ram = 0.0
    try:
        if os.name == "nt":
            # Windows
            result = subprocess.run(["wmic", "cpu", "get", "loadpercentage"], capture_output=True, text=True)
            for line in result.stdout.splitlines():
                line = line.strip()
                if line.isdigit():
                    cpu = float(line)
                    break
            result2 = subprocess.run(["wmic", "OS", "get", "FreePhysicalMemory,TotalVisibleMemorySize"],
                capture_output=True, text=True)
            lines = [l.strip() for l in result2.stdout.splitlines() if l.strip() and not l.strip().startswith("Free")]
            if lines:
                parts = lines[0].split()
                if len(parts) == 2:
                    free_kb, total_kb = int(parts[0]), int(parts[1])
                    used_kb = total_kb - free_kb
                    ram = round(used_kb / 1024 / 1024, 1)
        else:
            # Linux
            with open("/proc/stat") as f:
                line = f.readline()
            vals = list(map(int, line.split()[1:]))
            idle = vals[3]
            total = sum(vals)
            get_stats._prev = getattr(get_stats, "_prev", (idle, total))
            prev_idle, prev_total = get_stats._prev
            diff_idle = idle - prev_idle
            diff_total = total - prev_total
            cpu = round((1.0 - diff_idle / max(diff_total, 1)) * 100, 1)
            get_stats._prev = (idle, total)
            with open("/proc/meminfo") as f:
                lines = f.readlines()
            mem = {}
            for l in lines:
                k, v = l.split(":")
                mem[k.strip()] = int(v.strip().split()[0])
            used_kb = mem.get("MemTotal", 0) - mem.get("MemAvailable", 0)
            ram = round(used_kb / 1024 / 1024, 1)
    except: pass
    return cpu, ram

# ─── HTML Dashboard ───────────────────────────────────────────────────────────

def html_page(port):
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Windrose Server Control</title>
<style>
:root{{--bg0:#0d1117;--bg1:#161b22;--bg2:#21262d;--bd:#30363d;--t1:#e6edf3;--t2:#8b949e;--t3:#484f58;--green:#3dba8c;--red:#f85149;--orange:#d29922;--blue:#58a6ff;--mono:'Courier New',monospace;--rl:8px}}
*{{box-sizing:border-box;margin:0;padding:0}}
body{{background:var(--bg0);color:var(--t1);font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;font-size:14px;min-height:100vh}}
.wrap{{max-width:1100px;margin:0 auto;padding:24px 16px}}
.header{{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:20px}}
.server-name{{font-size:26px;font-weight:700;margin-bottom:4px}}
.server-addr{{font-size:13px;color:var(--t2);font-family:var(--mono)}}
.badge{{display:flex;align-items:center;gap:8px;padding:6px 14px;border-radius:20px;border:.5px solid var(--bd);font-size:12px;font-family:var(--mono);background:var(--bg1)}}
.dot{{width:8px;height:8px;border-radius:50%}}
.dot.online{{background:var(--green);box-shadow:0 0 6px var(--green)}}
.dot.offline{{background:var(--red)}}
.dot.starting,.dot.stopping{{background:var(--orange)}}
.controls{{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:14px}}
.btn{{padding:7px 16px;border-radius:var(--rl);border:.5px solid var(--bd);background:var(--bg1);color:var(--t1);cursor:pointer;font-size:13px;transition:background .15s}}
.btn:hover{{background:var(--bg2)}}
.btn.success{{border-color:var(--green);color:var(--green)}}
.btn.danger{{border-color:var(--red);color:var(--red)}}
.btn.warn{{border-color:var(--orange);color:var(--orange)}}
.btn:disabled{{opacity:.4;cursor:default}}
.meta{{font-size:11px;font-family:var(--mono);color:var(--t3);margin-bottom:18px}}
.grid{{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-bottom:18px}}
.stat{{background:var(--bg1);border:.5px solid var(--bd);border-radius:var(--rl);padding:16px}}
.stat-label{{font-size:11px;text-transform:uppercase;letter-spacing:.5px;color:var(--t3);margin-bottom:8px}}
.stat-value{{font-size:28px;font-weight:700}}
.stat-sub{{font-size:12px;color:var(--t2);margin-top:2px}}
.bar-track{{height:3px;background:var(--bg2);border-radius:2px;margin-top:10px}}
.bar-fill{{height:100%;border-radius:2px;transition:width .5s}}
.bar-fill.cpu{{background:var(--blue)}}
.bar-fill.ram{{background:var(--green)}}
.card{{background:var(--bg1);border:.5px solid var(--bd);border-radius:var(--rl);margin-bottom:10px}}
.card-head{{display:flex;justify-content:space-between;align-items:center;padding:12px 16px;border-bottom:.5px solid var(--bd)}}
.card-title{{font-size:11px;text-transform:uppercase;letter-spacing:.5px;color:var(--t3)}}
.player-list{{padding:6px 0;min-height:60px}}
.player-row{{display:flex;align-items:center;gap:10px;padding:8px 16px;border-bottom:.5px solid var(--bd)}}
.player-row:last-child{{border-bottom:none}}
.avatar{{width:32px;height:32px;border-radius:50%;background:var(--bg2);display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:700;flex-shrink:0;border:.5px solid var(--bd)}}
.pname{{flex:1;font-weight:500}}
.tag{{font-size:11px;font-family:var(--mono);padding:2px 8px;border-radius:4px;border:.5px solid}}
.tag.online{{color:var(--green);border-color:var(--green)}}
.pping{{font-size:11px;font-family:var(--mono);color:var(--t3);width:48px;text-align:right}}
.kbtn{{font-size:11px;padding:3px 10px;border-radius:4px;border:.5px solid var(--bd);background:var(--bg2);color:var(--t2);cursor:pointer}}
.kbtn:hover{{background:var(--bg0)}}
.log-box{{height:320px;overflow-y:auto;padding:10px 14px;font-family:var(--mono);font-size:12px;line-height:1.7}}
.log-box .ts{{color:var(--t3);margin-right:8px}}
.log-box .ok{{color:var(--green)}}
.log-box .err{{color:var(--red)}}
.log-box .warn{{color:var(--orange)}}
.log-box .info{{color:var(--t2)}}
.empty{{padding:28px 14px;text-align:center;color:var(--t3);font-size:13px;font-family:var(--mono)}}
.cfg-panel{{background:var(--bg1);border:.5px solid var(--bd);border-radius:var(--rl);padding:20px;margin-bottom:10px;display:none}}
.cfg-grid{{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:14px}}
.cfg-label{{font-size:11px;color:var(--t3);text-transform:uppercase;letter-spacing:.5px;margin-bottom:6px}}
input,select{{width:100%;padding:8px 10px;background:var(--bg0);border:.5px solid var(--bd);border-radius:var(--rl);color:var(--t1);font-size:13px}}
.notify-grid{{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-bottom:12px}}
.notify-item{{display:flex;align-items:center;gap:8px;font-size:13px}}
.rcon-grid{{display:grid;grid-template-columns:1fr 1fr 1fr 1fr;gap:8px;margin-bottom:8px}}
@media(max-width:640px){{.grid{{grid-template-columns:repeat(2,1fr)}}.cfg-grid{{grid-template-columns:1fr}}.rcon-grid{{grid-template-columns:1fr 1fr}}}}
</style>
</head>
<body>
<div id="pinfo-modal" style="display:none;position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:100;align-items:center;justify-content:center">
<div style="background:var(--bg0);border:.5px solid var(--bd);border-radius:var(--rl);padding:24px;min-width:340px;max-width:480px;width:90%">
<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
<div style="font-size:15px;font-weight:700" id="pinfo-name">Player Info</div>
<button class="btn" onclick="id('pinfo-modal').style.display='none'">&#x2715;</button>
</div>
<div id="pinfo-body" style="font-family:var(--mono);font-size:12px;line-height:2;color:var(--t2)"></div>
<div style="margin-top:16px;display:flex;gap:8px" id="pinfo-btns"></div>
</div></div>
<div class="wrap">
<div class="header">
<div>
<div class="server-name" id="sname">Windrose Server</div>
<div class="server-addr" id="addr">localhost - connecting...</div>
</div>
<div class="badge"><div class="dot offline" id="dot"></div><span id="stxt">connecting...</span></div>
</div>
<div class="controls">
<button class="btn success" id="bstart" onclick="act('start')" disabled>&#9654; start</button>
<button class="btn danger" id="bstop" onclick="act('stop')" disabled>&#9632; stop</button>
<button class="btn warn" id="brst" onclick="act('restart')" disabled>&#8635; restart</button>
<button class="btn" onclick="act('update')">&#8593; update</button>
<button class="btn" id="bbanlist" onclick="toggleBanlist()">&#128683; banlist</button>
<button class="btn" onclick="toggleCfg()">&#9881; config</button>
</div>
<div class="meta">last update: <span id="lupd">never</span>&nbsp;&nbsp;|&nbsp;&nbsp;next check: <span id="nupd">--</span></div>
<div class="cfg-panel" id="cfg">
<div class="cfg-label" style="margin-bottom:12px;font-size:13px;font-weight:600;color:var(--t1)">Configuration</div>
<div class="cfg-grid">
<div><div class="cfg-label">Daily Restart Time (24h)</div>
<select id="cfg-hour"><option value="-1">Disabled</option>
{"".join(f'<option value="{h}">{h:02d}:00</option>' for h in range(24))}</select></div>
<div><div class="cfg-label">Discord Webhook</div>
<div style="display:flex;align-items:center;gap:8px"><input id="cfg-wh" type="text" placeholder="https://discord.com/api/webhooks/..."><span id="cfg-wh-status" style="font-size:11px;font-family:var(--mono);color:var(--t3);white-space:nowrap"></span></div></div>
</div>
<div class="cfg-label" style="margin-top:14px;margin-bottom:8px">Discord Notifications</div>
<div class="notify-grid">
<label class="notify-item"><input type="checkbox" id="n-online"> server online</label>
<label class="notify-item"><input type="checkbox" id="n-offline"> server offline</label>
<label class="notify-item"><input type="checkbox" id="n-crash"> server crash</label>
<label class="notify-item"><input type="checkbox" id="n-restart"> restart</label>
<label class="notify-item"><input type="checkbox" id="n-update"> update</label>
<label class="notify-item"><input type="checkbox" id="n-daily"> daily restart</label>
<label class="notify-item"><input type="checkbox" id="n-join"> player join</label>
<label class="notify-item"><input type="checkbox" id="n-leave"> player leave</label>
</div>
<div style="margin-top:6px;margin-bottom:8px"><button class="btn" onclick="saveCfg()">save</button>&nbsp;<button class="btn danger" id="btn-rmwh" onclick="removeWebhook()">remove webhook</button></div>
<div class="cfg-label" style="margin-top:14px;margin-bottom:8px">Windrose REST API <a href="https://www.nexusmods.com/windrose/mods/44" target="_blank" style="color:var(--blue);font-size:10px">NEXUS MOD REQUIRED</a></div>
<div class="rcon-grid">
<div><div class="cfg-label">API URL</div><input id="cfg-rcon-url" type="text" placeholder="http://localhost:9600"></div>
<div><div class="cfg-label">API Key</div><input id="cfg-rcon-key" type="text" placeholder="your api key"></div>
<div><div class="cfg-label">Poll Interval (sec)</div><input id="cfg-poll" type="text" placeholder="5"></div>
<div><div class="cfg-label">Update Check (hrs)</div><input id="cfg-upd" type="text" placeholder="3"></div>
</div>
</div>
<div class="grid">
<div class="stat"><div class="stat-label">CPU</div><div class="stat-value" id="cpu">--</div><div class="bar-track"><div class="bar-fill cpu" id="cpub" style="width:0%"></div></div></div>
<div class="stat"><div class="stat-label">RAM</div><div class="stat-value" id="ram">--</div><div class="bar-track"><div class="bar-fill ram" id="ramb" style="width:0%"></div></div></div>
<div class="stat"><div class="stat-label">Uptime</div><div class="stat-value" id="upt">--</div><div class="stat-sub">since last restart</div></div>
</div>
<div class="card">
<div class="card-head"><span class="card-title">players</span><span style="font-size:12px;font-family:var(--mono);color:var(--t3)" id="pcnt">0/20</span></div>
<div class="player-list" id="plist"><div class="empty">no players connected</div></div>
</div>
<div class="card">
<div class="card-head"><span class="card-title">live log</span><button class="btn" style="font-size:11px;padding:3px 10px" onclick="clrLog()">clear</button></div>
<div class="log-box" id="lbox"></div>
</div>
<div class="card" id="banlist-card" style="display:none">
<div class="card-head"><span class="card-title">&#128683; ban list</span><button class="btn" style="font-size:11px;padding:3px 10px" onclick="refreshBanlist()">refresh</button></div>
<div class="player-list" id="blist"><div class="empty">click refresh to load</div></div>
</div>
</div>
<script>
const API='http://localhost:{port}';
let logs=[],maxPlayers=20,rconEnabled=false,selectedPlayer=null,lastPlayersJson='',lastPlayers=[],notifyLoaded=false,cfgLoaded=false;
function id(x){{return document.getElementById(x)}}
function set(x,v){{id(x).textContent=v}}
function setSt(s){{
  const dot=id('dot'),txt=id('stxt'),bs=id('bstart'),bp=id('bstop'),br=id('brst');
  dot.className='dot '+s; txt.textContent=s;
  bs.disabled=s==='online'||s==='starting'||s==='stopping';
  bp.disabled=s==='offline'||s==='stopping';
  br.disabled=s==='stopping';
}}
async function poll(){{
  try{{
    const r=await fetch(API+'/state');
    const d=await r.json();
    setSt(d.status);
    if(d.maxPlayers)maxPlayers=d.maxPlayers;
    const h=Math.floor(d.uptime/3600),m=Math.floor((d.uptime%3600)/60);
    set('upt',d.uptime>0?h+'h '+m+'m':'0h 0m');
    set('cpu',d.cpu+'%'); id('cpub').style.width=Math.min(d.cpu,100)+'%';
    set('ram',d.ram.toFixed(1)+' GB');
    set('lupd',d.lastUpdate||'never'); set('nupd',d.nextUpdate||'--');
    id('addr').textContent='localhost - '+d.status;
    if(d.rconEnabled!==undefined)rconEnabled=d.rconEnabled;
    if(d.restartHour!==undefined&&!cfgLoaded){{cfgLoaded=true;
      id('cfg-hour').value=d.restartHour;
      if(d.rconApiUrl)id('cfg-rcon-url').value=d.rconApiUrl;
      if(d.rconPollSec)id('cfg-poll').value=d.rconPollSec;
      if(d.updateHours)id('cfg-upd').value=d.updateHours;
    }}
    if(d.notifyOnline!==undefined&&!notifyLoaded){{notifyLoaded=true;
      id('n-online').checked=d.notifyOnline;id('n-offline').checked=d.notifyOffline;
      id('n-crash').checked=d.notifyCrash;id('n-restart').checked=d.notifyRestart;
      id('n-update').checked=d.notifyUpdate;id('n-daily').checked=d.notifyDaily;
      id('n-join').checked=d.notifyJoin;id('n-leave').checked=d.notifyLeave;
    }}
    id('cfg-wh-status').textContent=d.webhookSet==='true'?'active':'not set';
    id('cfg-wh-status').style.color=d.webhookSet==='true'?'var(--green)':'var(--t3)';
    const newJson=JSON.stringify(d.players.map(p=>p.name+p.accountId));
    lastPlayers=d.players;
    if(newJson!==lastPlayersJson){{lastPlayersJson=newJson;
      if(selectedPlayer&&!d.players.find(p=>p.name===selectedPlayer.name)){{selectedPlayer=null;}}
      renderP(d.players);
    }}
    renderBanlist(d.banlist); logs=d.logs; renderL();
  }}catch{{}}
}}
function renderP(ps){{
  set('pcnt',ps.length+'/'+maxPlayers);
  if(!ps.length){{id('plist').innerHTML='<div class="empty">no players connected</div>';return;}}
  id('plist').innerHTML=ps.map(p=>{{
    const pc=p.ping<60?'pg':p.ping<120?'pm':'pb';
    const btns='<button class="kbtn" style="color:var(--blue)" onclick="showPlayerInfo('+JSON.stringify(p)+')">info</button>'+(rconEnabled?'<button class="kbtn" onclick="kickPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\')">kick</button><button class="kbtn" style="font-weight:700" onclick="banPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\')">ban</button>':'');
    return '<div class="player-row"><div class="avatar">'+p.name.slice(0,2).toUpperCase()+'</div><span class="pname">'+p.name+'</span><span class="tag online">'+(p.status||'online')+'</span><span class="pping">'+(p.ping>0?p.ping+'ms':'--')+'</span>'+btns+'</div>';
  }}).join('');
}}
function renderBanlist(bl){{
  if(!bl||!bl.length){{id('blist').innerHTML='<div class="empty">no banned players</div>';return;}}
  id('blist').innerHTML=bl.map(b=>'<div class="player-row"><span class="pname" style="font-size:12px">'+b.name+'</span><span style="font-size:10px;font-family:var(--mono);color:var(--t3);flex:1;padding:0 8px">'+b.accountId+'</span><button class="kbtn" onclick="unbanPlayer(\\''+b.accountId+'\\')">unban</button></div>').join('');
}}
function renderL(){{
  const box=id('lbox');
  const atBottom=box.scrollHeight-box.scrollTop<=box.clientHeight+40;
  box.innerHTML=logs.map(l=>'<div><span class="ts">'+l.ts+'</span><span class="'+l.type+'">'+l.msg.replace(/</g,'&lt;')+'</span></div>').join('');
  if(atBottom)box.scrollTop=box.scrollHeight;
}}
function clrLog(){{logs=[];renderL();fetch(API+'/clearlogs',{{method:'POST'}});}}
function toggleCfg(){{const c=id('cfg');c.style.display=c.style.display==='none'||!c.style.display?'block':'none';}}
function toggleBanlist(){{const c=id('banlist-card');c.style.display=c.style.display==='none'||!c.style.display?'block':'none';if(c.style.display==='block')refreshBanlist();}}
async function act(a){{await fetch(API+'/action',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{action:a}})}});}}
async function saveCfg(){{
  const body={{restartHour:parseInt(id('cfg-hour').value),
    notifyOnline:id('n-online').checked,notifyOffline:id('n-offline').checked,
    notifyCrash:id('n-crash').checked,notifyRestart:id('n-restart').checked,
    notifyUpdate:id('n-update').checked,notifyDaily:id('n-daily').checked,
    notifyJoin:id('n-join').checked,notifyLeave:id('n-leave').checked,
    rconPollSec:Math.max(2,parseInt(id('cfg-poll').value)||5),
    updateHours:Math.max(1,parseInt(id('cfg-upd').value)||3)}};
  const wh=id('cfg-wh').value.trim();if(wh)body.webhookUrl=wh;
  const ru=id('cfg-rcon-url').value.trim();if(ru)body.rconApiUrl=ru;
  const rk=id('cfg-rcon-key').value.trim();if(rk)body.rconApiKey=rk;
  await fetch(API+'/settings',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify(body)}});
}}
async function removeWebhook(){{await fetch(API+'/settings',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{webhookUrl:''}})}});id('cfg-wh').value='';}}
function showPlayerInfo(p){{
  id('pinfo-name').textContent=p.name;
  id('pinfo-body').innerHTML='<div><span style="color:var(--t3)">Name</span>: '+p.name+'</div><div><span style="color:var(--t3)">Account ID</span>: '+(p.accountId||'unknown')+'</div><div><span style="color:var(--t3)">Ping</span>: '+(p.ping>0?p.ping+'ms':'--')+'</div>';
  id('pinfo-btns').innerHTML=rconEnabled&&p.accountId?'<button class="btn danger" onclick="kickPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\');id(\\'pinfo-modal\\').style.display=\\'none\\'">kick</button><button class="btn danger" style="font-weight:700" onclick="banPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\');id(\\'pinfo-modal\\').style.display=\\'none\\'">ban</button>':'';
  id('pinfo-modal').style.display='flex';
}}
async function kickPlayer(name,aid){{if(!confirm('Kick '+name+'?'))return;await fetch(API+'/action',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{action:'kick',name:name,accountId:aid}})}});}}
async function banPlayer(name,aid){{if(!confirm('Ban '+name+'?'))return;await fetch(API+'/action',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{action:'ban',name:name,accountId:aid}})}});}}
async function refreshBanlist(){{await fetch(API+'/action',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{action:'banlist'}})}});}}
async function unbanPlayer(aid){{if(!confirm('Unban '+aid+'?'))return;await fetch(API+'/action',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{action:'unban',accountId:aid}})}});}}
poll();setInterval(poll,2000);
</script>
</body>
</html>"""

# ─── HTTP Server ──────────────────────────────────────────────────────────────

class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args): pass  # suppress access logs

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            html = html_page(cfg.get("guiPort", 7777)).encode()
            self.send_response(200)
            self.send_header("Content-Type", "text/html")
            self.send_header("Content-Length", len(html))
            self.end_headers()
            self.wfile.write(html)
        elif self.path == "/state":
            self.send_json(build_state())
        else:
            self.send_response(404)
            self.end_headers()

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = {}
        if length:
            try: body = json.loads(self.request_body())
            except: pass

        if self.path == "/action":
            handle_action(body)
            self.send_json({"ok": True})
        elif self.path == "/settings":
            handle_settings(body)
            self.send_json({"ok": True})
        elif self.path == "/clearlogs":
            with state_lock: logs.clear()
            self.send_json({"ok": True})
        else:
            self.send_response(404)
            self.end_headers()

    def request_body(self):
        length = int(self.headers.get("Content-Length", 0))
        return self.rfile.read(length)

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def send_json(self, data):
        body = json.dumps(data).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", len(body))
        self.end_headers()
        self.wfile.write(body)

def build_state():
    cpu, ram = get_stats()
    uptime = 0
    if uptime_start:
        uptime = int((datetime.now() - uptime_start).total_seconds())
    with state_lock:
        p_list = list(players)
        b_list = list(banlist)
        l_list = list(logs[-100:])
    return {
        "status": status,
        "uptime": uptime,
        "cpu": cpu,
        "ram": ram,
        "maxPlayers": cfg.get("maxPlayers", 20),
        "lastUpdate": last_update,
        "nextUpdate": next_update.strftime("%H:%M") if next_update else "--",
        "webhookSet": "true" if cfg.get("webhookUrl") else "false",
        "rconEnabled": bool(cfg.get("rconApiKey")),
        "rconApiUrl": cfg.get("rconApiUrl", ""),
        "rconPollSec": cfg.get("rconPollSec", 5),
        "updateHours": cfg.get("updateHours", 3),
        "restartHour": cfg.get("restartHour", -1),
        "notifyOnline": cfg.get("notifyOnline", True),
        "notifyOffline": cfg.get("notifyOffline", True),
        "notifyCrash": cfg.get("notifyCrash", True),
        "notifyRestart": cfg.get("notifyRestart", True),
        "notifyUpdate": cfg.get("notifyUpdate", True),
        "notifyDaily": cfg.get("notifyDaily", True),
        "notifyJoin": cfg.get("notifyJoin", True),
        "notifyLeave": cfg.get("notifyLeave", True),
        "notifyKick": cfg.get("notifyKick", True),
        "players": p_list,
        "banlist": b_list,
        "logs": l_list,
    }

def handle_action(body):
    action = body.get("action", "")
    if action == "start":
        manual_stop_flag = False
        threading.Thread(target=start_server, daemon=True).start()
    elif action == "stop":
        globals()["manual_stop"] = True
        threading.Thread(target=stop_server, daemon=True).start()
    elif action == "restart":
        threading.Thread(target=restart_server, daemon=True).start()
    elif action == "update":
        threading.Thread(target=update_server, daemon=True).start()
    elif action == "kick":
        name = body.get("name", "")
        aid = body.get("accountId", "")
        rcon_request("/kick", {"account_id": aid})
        with state_lock:
            players[:] = [p for p in players if p["name"] != name]
        log("warn", f"{name} was kicked")
        if cfg.get("notifyKick"):
            send_webhook(0xf39c12, "Player Kicked", f"{name} was kicked.")
    elif action == "ban":
        name = body.get("name", "")
        aid = body.get("accountId", "")
        rcon_request("/ban", {"account_id": aid, "reason": "Banned by admin"})
        with state_lock:
            players[:] = [p for p in players if p["name"] != name]
        log("warn", f"{name} was banned")
        if cfg.get("notifyKick"):
            send_webhook(0xe74c3c, "Player Banned", f"{name} was banned.")
    elif action == "unban":
        aid = body.get("accountId", "")
        rcon_request("/unban", {"account_id": aid})
        with state_lock:
            banlist[:] = [b for b in banlist if b.get("accountId") != aid]
        log("ok", f"Unbanned: {aid}")
    elif action == "banlist":
        def load_banlist():
            data = rcon_get("/banlist")
            if data:
                bans = data.get("bans", []) if isinstance(data, dict) else data
                with state_lock:
                    banlist.clear()
                    for b in bans:
                        banlist.append({
                            "name": b.get("playerName", b.get("accountId", "")),
                            "accountId": b.get("accountId", "")
                        })
                log("info", f"Banlist refreshed: {len(banlist)} banned")
            # Also try reading local file
            elif banlist_file and Path(banlist_file).exists():
                try:
                    data = json.loads(Path(banlist_file).read_text())
                    with state_lock:
                        banlist.clear()
                        for b in (data if isinstance(data, list) else data.get("bans", [])):
                            banlist.append({
                                "name": b.get("playerName", b.get("accountId", "")),
                                "accountId": b.get("accountId", "")
                            })
                    log("info", f"Banlist loaded from file: {len(banlist)} banned")
                except Exception as e:
                    log("err", f"Banlist file error: {e}")
        threading.Thread(target=load_banlist, daemon=True).start()

def handle_settings(body):
    global cfg, next_update
    if "webhookUrl" in body: cfg["webhookUrl"] = body["webhookUrl"]
    if "restartHour" in body: cfg["restartHour"] = int(body["restartHour"])
    if "rconApiUrl" in body: cfg["rconApiUrl"] = body["rconApiUrl"]
    if "rconApiKey" in body: cfg["rconApiKey"] = body["rconApiKey"]
    if "rconPollSec" in body: cfg["rconPollSec"] = max(2, int(body["rconPollSec"]))
    if "updateHours" in body:
        cfg["updateHours"] = max(1, int(body["updateHours"]))
        from datetime import timedelta
        next_update = datetime.now() + timedelta(hours=cfg["updateHours"])
    for key in ["notifyOnline","notifyOffline","notifyCrash","notifyRestart",
                "notifyUpdate","notifyDaily","notifyJoin","notifyLeave","notifyKick"]:
        if key in body: cfg[key] = bool(body[key])
    save_settings(cfg)
    log("ok", "Settings saved")

# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    global cfg
    print("""
╔══════════════════════════════════════╗
║  Windrose Server Control - Linux     ║
╚══════════════════════════════════════╝
""")
    detect_paths()

    port = cfg.get("guiPort", 7777)

    # Start background threads
    threading.Thread(target=log_tail, daemon=True).start()
    threading.Thread(target=rcon_poll, daemon=True).start()
    threading.Thread(target=crash_monitor, daemon=True).start()
    threading.Thread(target=scheduler, daemon=True).start()

    # Auto-start server
    if cfg.get("serverDir"):
        log("info", "Auto-starting server...")
        threading.Thread(target=start_server, daemon=True).start()

    # Start HTTP server
    server = HTTPServer(("0.0.0.0", port), Handler)
    log("ok", f"Dashboard: http://localhost:{port}")
    print(f"\n  Open http://localhost:{port} in your browser\n  Press Ctrl+C to stop\n")

    def shutdown(sig, frame):
        print("\nShutting down...")
        sys.exit(0)
    signal.signal(signal.SIGINT, shutdown)
    signal.signal(signal.SIGTERM, shutdown)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass

if __name__ == "__main__":
    main()
