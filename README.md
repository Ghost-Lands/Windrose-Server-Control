# Windrose Server Control

A standalone server manager for [Windrose](https://store.steampowered.com/app/3041230/Windrose/) dedicated servers. Available in two editions:

| Edition | Platform | Requirements |
|---------|----------|-------------|
| **Windows** (`build_exe.bat`) | Windows 10/11 | .NET 4.0 (built into Windows) |
| **Linux** (`windrose_server.py`) | Linux | Python 3.6+ |

Both editions share the same browser-based dashboard at `localhost:7777` with identical features.

---

## Features

- **Start / Stop / Restart** the server with one click
- **Auto-start** — server launches automatically when the app opens
- **Auto-restart** — detects crashes and relaunches automatically
- **SteamCMD auto-update** — checks on a configurable schedule, only restarts if an update is actually found
- **Manual update** button to trigger an update anytime
- **Daily restart** — configurable time using local system time (24h)
- **Discord webhooks** — per-event toggles for every notification type
- **Live stats** — CPU usage, RAM, uptime
- **Player list** — live player detection via log tailing + RCON poll
- **Player actions** — info modal, kick, and ban per player row
- **Banlist** — view banned players and unban them
- **Live log** — color-coded server log with noise filtering
- **System tray icon** — green/red/orange status, right-click menu, double-click to reopen *(Windows only)*
- **Multi-server support** — run multiple instances in separate folders on the same machine

---

## Windows Edition

### Requirements
- Windows 10 or 11
- .NET Framework 4.0 (built into Windows — no download needed)
- Windrose Dedicated Server installed via SteamCMD or Steam
- SteamCMD (optional — only needed for auto-updates)

### Installation
1. Download all files into the same folder
2. Place `windrose.png` in the same folder (used as the app icon)
3. Double-click `build_exe.bat` to compile `Windrose Server Control.exe`
4. Double-click `Windrose Server Control.exe` to launch

You only need to build once. After that just use the exe.

### First Launch
The app auto-detects your server and SteamCMD by scanning:
1. Windows registry for Steam install path
2. Steam's `libraryfolders.vdf` for all library locations
3. Desktop subfolders
4. All connected drives
5. Full recursive drive scan as a last resort

If detection fails a folder picker will open. Paths are saved to `windrose_settings.json`.

### Building from Source

| File | Purpose |
|------|---------|
| `WindroseServerGUI.cs` | C# source code |
| `build_exe.bat` | Compiles the exe |
| `extract_icon.ps1` | Converts `windrose.png` to `.ico` |
| `windrose.png` | Windrose logo (used as app icon) |

---

## Linux Edition

### Requirements
- Python 3.6 or newer
- Windrose Dedicated Server installed via SteamCMD
- SteamCMD (optional — only needed for auto-updates)

### Installation

```bash
python3 windrose_server.py
```

Open `http://localhost:7777` in your browser.

### Installing SteamCMD (Ubuntu / Debian)

```bash
sudo apt-get install lib32gcc-s1
mkdir ~/steamcmd && cd ~/steamcmd
wget https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz
tar -xvzf steamcmd_linux.tar.gz
```

### Installing the Windrose Dedicated Server

```bash
~/steamcmd/steamcmd.sh \
  +force_install_dir ~/windrose-server \
  +login anonymous \
  +app_update 4129620 \
  +quit
```

### Running as a Systemd Service

```bash
sudo cp windrose.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable windrose
sudo systemctl start windrose
```

---

## Configuration

Click **⚙ config** in the dashboard, or edit `windrose_settings.json` directly:

```json
{
  "serverDir": "",
  "steamCmd": "",
  "webhookUrl": "",
  "guiPort": 7777,
  "rconApiUrl": "http://localhost:9600",
  "rconApiKey": "",
  "rconPollSec": 5,
  "restartHour": -1,
  "updateHours": 3,
  "notifyOnline": true,
  "notifyOffline": true,
  "notifyCrash": true,
  "notifyRestart": true,
  "notifyUpdate": true,
  "notifyDaily": true,
  "notifyJoin": true,
  "notifyLeave": true,
  "notifyKick": true
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `serverDir` | Path to Windrose Dedicated Server folder | auto-detect |
| `steamCmd` | Path to steamcmd executable | auto-detect |
| `webhookUrl` | Discord webhook URL | empty |
| `guiPort` | Dashboard HTTP port | 7777 |
| `rconApiUrl` | Windrose REST API URL | http://localhost:9600 |
| `rconApiKey` | REST API key | empty |
| `rconPollSec` | Player list poll interval (seconds) | 5 |
| `restartHour` | Daily restart hour 0-23, -1 to disable | -1 |
| `updateHours` | Auto-update check interval (hours) | 3 |

---

## Discord Notifications

| Event | Description |
|-------|-------------|
| Server Online | Server started successfully |
| Server Offline | Server stopped manually |
| Server Crash | Crashed + auto-restart triggered |
| Restart | Manual or scheduled restart |
| Update | Update found and installed |
| Daily Restart | Scheduled daily restart |
| Player Join | Player connected |
| Player Leave | Player disconnected |
| Player Kick/Ban | Player kicked or banned |

Setup: Discord channel → **Settings** → **Integrations** → **Webhooks** → **New Webhook** → copy URL → paste in config panel.

---

## Player Management

Each player row has three buttons:

- **info** — name, account ID, status, ping with kick/ban inside
- **kick** — kicks immediately (requires REST API mod)
- **ban** — bans permanently (requires REST API mod)

Click **🚫 banlist** to view banned players. Click **refresh** to load, **unban** to remove.

---

## Kick / Ban / Banlist (REST API Mod)

1. Install [UE4SS for Windrose](https://www.nexusmods.com/windrose/mods/43)
2. Install [Windrose Server RESTAPI](https://www.nexusmods.com/windrose/mods/44)
3. Copy mod to: `R5\Binaries\Win64\ue4ss\Mods\WindroseAPI\`
4. Add to `mods.txt`: `WindroseAPI : 1`
5. Set API key in the mod's `settings.ini`
6. Enter URL and key in the dashboard config panel and click save

---

## Multi-Server Support

Put two copies in separate folders with separate `windrose_settings.json` files. Set different `guiPort` values (e.g. 7777 and 7778) and different `serverDir` paths.

---

## Notes

- Dashboard runs on port `7777` by default — change `guiPort` in settings if needed
- The banlist is read from `R5\Binaries\Win64\ue4ss\Mods\WindroseAPI\banlist.json`
- Linux: make the server executable with `chmod +x R5/Binaries/Linux/WindroseServer-Linux-Shipping`

## License

MIT — free to use, modify, and distribute.
