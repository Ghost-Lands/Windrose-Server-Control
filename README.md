# Windrose Server Control v1.1

A standalone Windows app for managing your [Windrose](https://store.steampowered.com/app/3041230/Windrose/) dedicated server. No Python, no PowerShell, no dependencies — just double-click and go.

## Features

- **Start / Stop / Restart** the server with one click
- **Auto-start** — server launches automatically when the app opens
- **Auto-restart** — detects crashes and relaunches automatically
- **Duplicate instance protection** — won't launch a second server if one is already running
- **SteamCMD auto-update** — checks on a configurable schedule, only restarts if an update is actually found
- **Manual update** button to trigger an update anytime
- **Daily restart** — configurable time (24h, uses local system time)
- **Discord webhooks** — per-event toggles for every notification type
- **Live stats** — CPU usage, RAM, uptime
- **Player list** — live player detection via log tailing + RCON poll
- **Player actions** — info modal, kick, and ban per player row
- **Banlist** — view banned players and unban them
- **Live log** — color-coded server log with noise filtering
- **System tray icon** — green/red/orange status, right-click menu, double-click to reopen
- **Multi-server support** — run multiple instances in separate folders on the same machine

## Requirements

- Windows 10 or 11
- .NET Framework 4.0 (built into Windows — no download needed)
- Windrose Dedicated Server installed via SteamCMD
- SteamCMD installed (auto-detected)

## Installation

1. Download all files into the same folder
2. Double-click `build_exe.bat` to compile `Windrose Server Control.exe`
3. Double-click `Windrose Server Control.exe` to launch

You only need to build once. After that just use the exe.

## First Launch

The app auto-detects your Windrose server and SteamCMD by scanning:

1. Windows registry for Steam install path
2. Steam's `libraryfolders.vdf` for all library locations
3. Desktop subfolders (most common install location)
4. All connected drives for common Steam folder names
5. Full recursive drive scan as a last resort

If detection fails a folder picker will open. Paths are saved to `windrose_settings.json`.

To reset paths, delete `windrose_settings.json` and relaunch.

## System Tray

The app runs in the system tray (bottom right of taskbar):

- 🟢 **Green** — server online
- 🔴 **Red** — server offline
- 🟠 **Orange** — starting, stopping, or updating
- **Double-click** — opens GUI in browser
- **Right-click** — Open GUI / Restart / Stop / Exit

## Configuration

Click **⚙ config** in the GUI:

| Setting | Description |
|---------|-------------|
| Daily Restart Time | Hour in 24h format using local system time |
| Discord Webhook | Paste webhook URL to enable notifications |
| Discord Notifications | Toggle each event type on/off |
| REST API URL | URL for Windrose REST API mod (default `http://localhost:9600`) |
| REST API Key | API key from the mod's `settings.ini` |
| Poll Interval | How often to poll the REST API for player list (seconds, min 2) |
| Update Check | How often to check for server updates (hours, min 1) |
| GUI Port | HTTP port for the dashboard (default 7777, change for multi-server) |

### windrose_settings.json

```json
{
  "serverDir": "C:\\path\\to\\Windrose Dedicated Server",
  "steamCmd": "C:\\path\\to\\steamcmd.exe",
  "webhookUrl": "https://discord.com/api/webhooks/...",
  "guiPort": 7777,
  "rconApiUrl": "http://localhost:9600",
  "rconApiKey": "your_api_key",
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

## Discord Notifications

Each event can be toggled individually in the config panel:

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

To set up a webhook: Discord channel → **Settings** → **Integrations** → **Webhooks** → **New Webhook** → copy URL → paste in config panel.

## Player Management

Each player row has three buttons:

- **info** — opens a modal showing name, account ID, status, ping, with kick/ban buttons inside
- **kick** — kicks the player immediately (requires REST API mod)
- **ban** — bans the player permanently (requires REST API mod)

Click **🚫 banlist** in the top bar to open the ban list panel. Click **refresh** to load current bans. Each banned player has an **unban** button.

## Kick / Ban / Banlist (REST API Mod)

Kick, ban, and banlist require the Windrose REST API mod.

### Setup

1. Install **UE4SS for Windrose** from [Nexus Mods #43](https://www.nexusmods.com/windrose/mods/43)
2. Install **Windrose Server RESTAPI** from [Nexus Mods #44](https://www.nexusmods.com/windrose/mods/44)
3. Copy mod folder to: `R5\Binaries\Win64\ue4ss\Mods\WindroseAPI\`
4. Add to `mods.txt`: `WindroseAPI : 1`
5. Set your API key in the mod's `settings.ini`
6. In the GUI config panel enter `http://localhost:9600` and your API key, click **save**
7. Restart the server

## Player Detection

Players are detected two ways:

1. **Log tailing** — watches `R5\Saved\Logs\R5.log` for `Join succeeded` and `FarewellReason` lines
2. **RCON polling** — polls `/players` every N seconds when REST API is configured (more reliable)

On startup the full log is scanned to find players already connected before the GUI launched.

## Auto-Update

The app checks for Windrose updates via SteamCMD on a configurable schedule (default 3 hours, min 1 hour) without interrupting the server.

- **No update found** — logs the result and schedules the next check, server keeps running
- **Update found** — stops the server, installs the update, restarts automatically, sends a Discord notification

Manual update available anytime via the **↑ update** button.

## Multi-Server Support

To run two servers on the same machine, put two copies of the exe in separate folders, each with their own `windrose_settings.json`. Set a different `guiPort` (e.g. 7777 and 7778) and `serverDir` in each settings file. Launch both independently.

## Building from Source

Files needed in the same folder:

| File | Purpose |
|------|---------|
| `WindroseServerGUI.cs` | C# source code |
| `build_exe.bat` | Compiles the exe |
| `extract_icon.ps1` | Converts `windrose.png` to `.ico` |
| `windrose.png` | Windrose logo (used as app icon) |

Run `build_exe.bat`. Uses the C# compiler built into Windows — no Visual Studio needed.

## Notes

- The GUI runs a local HTTP server on port `7777` by default. Change `guiPort` in settings if needed.
- Any previous instance of `Windrose Server Control.exe` is automatically killed on launch to free the port.
- The banlist is read directly from `R5\Binaries\Win64\ue4ss\Mods\WindroseAPI\banlist.json`.

## License

MIT — free to use, modify, and distribute.
