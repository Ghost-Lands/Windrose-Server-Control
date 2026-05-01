# Windrose Server Control - Linux Edition

Browser-based dedicated server manager for Windrose on Linux. No dependencies beyond Python 3 — just run and go.

## Requirements

- Python 3.6 or newer (pre-installed on most Linux distros)
- Windrose Dedicated Server installed via SteamCMD
- SteamCMD (optional — only needed for auto-updates)

## Quick Start

1. Copy `windrose_server.py` and `windrose_settings.json` to a folder on your server
2. Edit `windrose_settings.json` — set `serverDir` to your Windrose server path
3. Run:
```bash
python3 windrose_server.py
```
4. Open `http://localhost:7777` in your browser

## Installing SteamCMD (Ubuntu / Debian)

```bash
sudo apt-get install lib32gcc-s1
mkdir ~/steamcmd && cd ~/steamcmd
wget https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz
tar -xvzf steamcmd_linux.tar.gz
```

## Installing the Windrose Dedicated Server

```bash
~/steamcmd/steamcmd.sh \
  +force_install_dir ~/windrose-server \
  +login anonymous \
  +app_update 4129620 \
  +quit
```

Set `serverDir` in `windrose_settings.json` to `~/windrose-server` (use the full path e.g. `/home/yourname/windrose-server`).

## windrose_settings.json

```json
{
  "serverDir": "/home/yourname/windrose-server",
  "steamCmd": "/home/yourname/steamcmd/steamcmd.sh",
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
| `serverDir` | Full path to Windrose Dedicated Server folder | auto-detect |
| `steamCmd` | Full path to `steamcmd.sh` | auto-detect |
| `webhookUrl` | Discord webhook URL | empty |
| `guiPort` | Dashboard port | 7777 |
| `rconApiUrl` | Windrose REST API URL | http://localhost:9600 |
| `rconApiKey` | REST API key from mod settings | empty |
| `rconPollSec` | Player list poll interval in seconds | 5 |
| `restartHour` | Daily restart hour 0-23, -1 to disable | -1 |
| `updateHours` | Auto-update check interval in hours | 3 |

## Running in the Background

```bash
nohup python3 windrose_server.py > windrose.log 2>&1 &
```

To stop it:
```bash
pkill -f windrose_server.py
```

## Running as a Systemd Service (Recommended)

Auto-starts on boot and restarts on crash.

1. Edit `windrose.service` — replace `YOUR_USERNAME` and paths:
```ini
User=steam
WorkingDirectory=/home/steam/windrose-server-control
ExecStart=/usr/bin/python3 /home/steam/windrose-server-control/windrose_server.py
```

2. Install and enable:
```bash
sudo cp windrose.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable windrose
sudo systemctl start windrose
```

3. Useful commands:
```bash
sudo systemctl status windrose    # check status
sudo systemctl stop windrose      # stop
sudo systemctl restart windrose   # restart
journalctl -u windrose -f         # view live logs
```

## Accessing the Dashboard Remotely

The dashboard binds to `0.0.0.0` so it's accessible from any machine on your network:

```
http://YOUR_SERVER_IP:7777
```

Open port 7777 in your firewall:
```bash
sudo ufw allow 7777
```

## Features

- Start, stop, restart the Windrose server
- Crash detection with automatic restart
- Live CPU, RAM, and uptime stats
- Real-time server log with noise filtering
- Automatic player detection via log tailing and REST API polling
- Player info, kick, ban, unban, and banlist (requires Windrose REST API Nexus mod)
- Discord webhook notifications with per-event toggles
- SteamCMD auto-update — only restarts if an update is actually found
- Configurable daily restart
- Multi-server support — run multiple instances with different ports

## Kick / Ban / Banlist (Optional)

Requires the Windrose REST API mod:

1. Install [UE4SS for Windrose](https://www.nexusmods.com/windrose/mods/43)
2. Install [Windrose Server RESTAPI](https://www.nexusmods.com/windrose/mods/44)
3. Set your API key in the mod's `settings.ini`
4. Enter the API URL and key in the dashboard config panel and click save

## Multi-Server Support

Put two copies of the script in separate folders with separate `windrose_settings.json` files. Set different `guiPort` values (e.g. 7777 and 7778) and different `serverDir` paths.

## Troubleshooting

**Port already in use:**
```bash
lsof -i :7777
kill -9 <PID>
```

**Server not found:**
Set `serverDir` to the full absolute path in `windrose_settings.json`.

**Permission denied launching server:**
```bash
chmod +x ~/windrose-server/R5/Binaries/Linux/WindroseServer-Linux-Shipping
```

**SteamCMD not found:**
```bash
sudo apt install steamcmd
```

## License

MIT — free to use, modify, and distribute.
