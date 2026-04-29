# Windrose Server Control

A lightweight GUI for managing your [Windrose](https://store.steampowered.com/app/3041230/Windrose/) dedicated server on Windows. Control your server, monitor stats, manage players, and keep the server up to date — all from a browser tab.

## Features

- **Start / Stop / Restart** the server with one click
- **Auto-start** — server launches automatically when the GUI opens
- **Auto-restart** — detects crashes and relaunches automatically
- **SteamCMD auto-update** every 3 hours — stops server, updates, restarts
- **Manual update** button to trigger an update anytime
- **Daily restart** — configurable time and timezone
- **Discord webhook** notifications for all server events
- **Live stats** — CPU usage, RAM, uptime
- **Player list** — see who's connected with ping and a kick button
- **Live log** — color-coded server log streamed in real time
- **Config panel** — set restart time, timezone, and Discord webhook in one place

## Requirements

- Windows 10 or 11
- Windrose Dedicated Server installed via Steam
- SteamCMD installed (common locations are detected automatically)

## Installation

1. Download `windrose_backend.ps1` and `build_exe.bat`
2. Put both files in the same folder
3. Double-click `build_exe.bat` — it installs the compiler and builds `WindroseServerGUI.exe`
4. Double-click `WindroseServerGUI.exe` to launch

> You only need to run `build_exe.bat` once. After that just use `WindroseServerGUI.exe`.

## First Launch

On first launch the GUI automatically detects your Windrose server and SteamCMD locations by checking all common Steam library paths. If it can't find them it will ask you to enter the paths once and save them for future launches.

To reset the detected paths, delete `windrose_gui_config.txt` from the same folder and relaunch.

## Configuration

Click the **⚙ config** button in the GUI to open the config panel. From there you can set:

- **Daily restart time** — choose any hour and timezone (EST, CST, MST, PST, UTC, GMT, CET, AEST)
- **Discord webhook URL** — paste your webhook to enable notifications

Settings are saved to `windrose_settings.json` in the same folder as the exe.

## Discord Notifications

The GUI sends Discord notifications for:

- Server online / offline
- Server crashed + auto-restart
- Server restart
- Update started / completed
- Daily scheduled restart
- Player kicked

To set up a webhook:
1. In Discord go to your channel settings → **Integrations** → **Webhooks** → **New Webhook**
2. Copy the webhook URL
3. Open the **⚙ config** panel in the GUI, paste the URL and click **save**

## Auto-Update

Every 3 hours the GUI checks for Windrose server updates via SteamCMD. When triggered it stops the server, runs the update, and restarts automatically. You can also trigger a manual update anytime with the **↑ update** button.

SteamCMD is detected automatically in common locations. If not found you will be prompted to enter the path on first launch.

## Usage

1. Double-click `WindroseServerGUI.exe` (or `start_gui.bat`)
2. Your browser opens to `http://localhost:7777`
3. The server starts automatically
4. Keep the window open while the server is running — closing it stops the backend

## Transferring to Another PC

Copy `WindroseServerGUI.exe` to the other PC. On first launch it will auto-detect all paths. If it can't find the server or SteamCMD it will ask once and save the answer.

Requirements on the other PC:
- Windows 10 or 11
- Windrose Dedicated Server installed via Steam
- SteamCMD installed

## Files

| File | Purpose |
|------|---------|
| `windrose_backend.ps1` | Main source file |
| `build_exe.bat` | Compiles the ps1 into an exe |
| `start_gui.bat` | Runs the script directly without building |
| `windrose_gui_config.txt` | Auto-generated — stores detected server paths |
| `windrose_settings.json` | Auto-generated — stores webhook URL and restart time |

## Notes

- Windrose is in Early Access and has no built-in admin console. This GUI manages the server process directly.
- The GUI runs a local HTTP server on port `7777`. If something else is using that port, free it with:
  ```
  Stop-Process -Id (Get-NetTCPConnection -LocalPort 7777).OwningProcess -Force
  ```
- If the exe won't rebuild because it's locked, run `taskkill /F /IM WindroseServerGUI.exe` first.

## Built With

- PowerShell — backend HTTP server and process management
- HTML / CSS / JavaScript — frontend GUI served from the backend
- [ps2exe](https://github.com/MScholtes/PS2EXE) — compiles PowerShell into a Windows exe

## License

MIT — free to use, modify, and distribute.
