# Windrose Server GUI Backend
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force 2>$null

# ── Auto-detect paths ─────────────────────────────────────
$CONFIG_FILE = "$PSScriptRoot\windrose_gui_config.txt"

function Find-ServerDir {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Windrose Dedicated Server",
        "C:\Program Files\Steam\steamapps\common\Windrose Dedicated Server",
        "D:\Steam\steamapps\common\Windrose Dedicated Server",
        "D:\SteamLibrary\steamapps\common\Windrose Dedicated Server",
        "E:\Steam\steamapps\common\Windrose Dedicated Server",
        "E:\SteamLibrary\steamapps\common\Windrose Dedicated Server"
    )
    # Also check Steam libraryfolders.vdf for custom locations
    $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        $matches2 = Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value }
        foreach ($lib in $matches2) {
            $candidates += "$lib\steamapps\common\Windrose Dedicated Server"
        }
    }
    foreach ($c in $candidates) {
        $exe = "$c\R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe"
        if (Test-Path $exe) { return $c }
    }
    return $null
}

function Find-SteamCmd {
    $candidates = @(
        "C:\SteamCMD\steamcmd.exe",
        "C:\steamcmd.exe",
        "C:\Program Files (x86)\Steam\steamcmd.exe",
        "D:\SteamCMD\steamcmd.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

# Load config or auto-detect
if (Test-Path $CONFIG_FILE) {
    $cfg = [System.IO.File]::ReadAllLines($CONFIG_FILE)
    foreach ($line in $cfg) {
        if ($line -match '^SERVER_DIR=(.+)$') { $SERVER_DIR = $Matches[1] }
        if ($line -match '^STEAMCMD=(.+)$')   { $STEAMCMD   = $Matches[1] }
    }
} else {
    $SERVER_DIR = Find-ServerDir
    $STEAMCMD   = Find-SteamCmd

    if (-not $SERVER_DIR) {
        $SERVER_DIR = Read-Host "Could not find Windrose server. Enter full path to server folder"
    }
    if (-not $STEAMCMD) {
        $STEAMCMD = Read-Host "Could not find SteamCMD. Enter full path to steamcmd.exe (or leave blank to skip updates)"
    }

    # Save config
    $cfgContent = "SERVER_DIR=" + $SERVER_DIR + "`n" + "STEAMCMD=" + $STEAMCMD
    [System.IO.File]::WriteAllText($CONFIG_FILE, $cfgContent)
    Write-Host "Paths saved to windrose_gui_config.txt"
}

$SERVER_EXE = "$SERVER_DIR\R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe"
$PORT       = 7777
$MAX_RAM_GB = 64.0

$script:status      = "offline"
$script:manualStop  = $false
$script:uptime      = 0
$script:uptimeStart = $null
$script:cpu         = 0
$script:ram         = 0.0
$script:lastUpdate  = "never"
$script:nextUpdate  = (Get-Date).AddHours(3)
$script:players     = [System.Collections.Generic.List[hashtable]]::new()
$script:lastCpuTime      = 0.0
$script:lastCpuCheck     = [datetime]::MinValue
$script:lastDailyRestart = [datetime]::Today.AddDays(-1)
$SETTINGS_FILE = "$PSScriptRoot\windrose_settings.json"
$script:webhookUrl   = ""
$script:restartHour  = 4
$script:restartTZ    = "Eastern Standard Time"

if (Test-Path $SETTINGS_FILE) {
    try {
        $s = [System.IO.File]::ReadAllText($SETTINGS_FILE) | ConvertFrom-Json
        if ($s.webhookUrl)  { $script:webhookUrl  = $s.webhookUrl }
        if ($null -ne $s.restartHour) { $script:restartHour = [int]$s.restartHour }
        if ($s.restartTZ)   { $script:restartTZ   = $s.restartTZ }
    } catch {}
}

function Save-Settings {
    $s = @{ webhookUrl=$script:webhookUrl; restartHour=$script:restartHour; restartTZ=$script:restartTZ }
    [System.IO.File]::WriteAllText($SETTINGS_FILE, ($s | ConvertTo-Json))
}
$script:logs        = [System.Collections.Generic.List[hashtable]]::new()

function AddLog($level, $msg) {
    $t = Get-Date -Format "HH:mm"
    
    if ($script:logs.Count -ge 200) { $script:logs.RemoveAt(0) }
    $script:logs.Add(@{t=$t;level=$level;msg=$msg})
}

function MinimizeWindow {
    try {
        $code = @"
using System;
using System.Runtime.InteropServices;
public class WinAPI {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
}
"@
        Add-Type -TypeDefinition $code -ErrorAction SilentlyContinue
        $hwnd = [WinAPI]::GetConsoleWindow()
        if ($hwnd -ne [IntPtr]::Zero) { [WinAPI]::ShowWindow($hwnd, 6) | Out-Null }
    } catch {}
}

function SendWebhook($color, $title, $msg) {
    if (-not $script:webhookUrl) { return }
    try {
        $body = "{`"embeds`":[{`"title`":`"$title`",`"description`":`"$msg`",`"color`":$color}]}"
        Invoke-RestMethod -Uri $script:webhookUrl -Method Post -Body $body -ContentType "application/json" -ErrorAction SilentlyContinue
    } catch {}
}
# Colors: 3066993=green, 15158332=red, 16776960=yellow, 3447003=blue
function Webhook-Online  { SendWebhook 3066993  "Server Online"   "Windrose server is now online." }
function Webhook-Offline { SendWebhook 15158332 "Server Offline"  "Windrose server has stopped." }
function Webhook-Crash   { SendWebhook 15158332 "Server Crashed"  "Windrose server crashed - auto-restarting..." }
function Webhook-Restart { SendWebhook 16776960 "Server Restart"  "Windrose server is restarting." }
function Webhook-Update  { SendWebhook 3447003  "Update Started"  "Checking for Windrose updates via SteamCMD..." }
function Webhook-Updated { SendWebhook 3066993  "Update Complete" "Windrose server updated and restarted." }
function Webhook-Daily   { SendWebhook 16776960 "Daily Restart"   "Scheduled 4 AM EST daily restart." }
function Webhook-Join($name)  { SendWebhook 3066993  "Player Joined" "$name joined the server." }
function Webhook-Leave($name) { SendWebhook 15158332 "Player Left"   "$name left the server." }
function Webhook-Kick($name)  { SendWebhook 16776960 "Player Kicked" "$name was kicked from the server." }

function GetProc {
    Get-Process "WindroseServer-Win64-Shipping" -ErrorAction SilentlyContinue | Select-Object -First 1
}

function StartServer {
    $script:manualStop = $false
    if (-not (Test-Path $SERVER_EXE)) {
        AddLog "err" "EXE not found: $SERVER_EXE"
        $script:status = "offline"; return
    }
    $script:status = "starting"
    AddLog "info" "Starting server..."
    try {
        $proc = Start-Process $SERVER_EXE -ArgumentList "-log" -WorkingDirectory (Split-Path $SERVER_EXE) -PassThru
        AddLog "info" "Launched PID $($proc.Id) - waiting..."
        $waited = 0
        while ($waited -lt 30) {
            Start-Sleep 1; $waited++
            $p = GetProc
            if ($p) {
                $script:status = "online"
                $script:uptimeStart = Get-Date
                $script:uptime = 0
                AddLog "ok" "Server running PID $($p.Id)"
                Webhook-Online
                Start-Sleep 1
                MinimizeWindow
                return
            }
            if ($proc.HasExited) {
                AddLog "err" "Process exited with code $($proc.ExitCode)"
                $script:status = "offline"; return
            }
        }
        $script:status = "offline"
        AddLog "err" "Server did not appear after 30s"
    } catch {
        $script:status = "offline"
        AddLog "err" "Exception: $($_.Exception.Message)"
    }
}

function StopServer {
    $script:manualStop = $true
    $script:status = "stopping"
    AddLog "warn" "Stopping server..."
    $p = GetProc
    if ($p) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    $script:players.Clear()
    $script:status = "offline"
    $script:uptime = 0
    $script:uptimeStart = $null
    AddLog "warn" "Server stopped"
    Webhook-Offline
}

function UpdateServer {
    AddLog "info" "Starting update check..."
    Webhook-Update
    $wasOnline = ($script:status -eq "online")
    if ($wasOnline) { StopServer; Start-Sleep 3 }
    if (-not (Test-Path $STEAMCMD)) {
        AddLog "err" "SteamCMD not found at $STEAMCMD"
        if ($wasOnline) { $script:manualStop = $false; StartServer }
        return
    }
    AddLog "info" "Running SteamCMD self-update..."
    Start-Process $STEAMCMD -ArgumentList "+quit" -Wait -NoNewWindow -WorkingDirectory (Split-Path $STEAMCMD)
    Start-Process $STEAMCMD -ArgumentList "+quit" -Wait -NoNewWindow -WorkingDirectory (Split-Path $STEAMCMD)
    AddLog "info" "Running Windrose update..."
    try {
        $p = Start-Process $STEAMCMD -ArgumentList "+@ShutdownOnFailedCommand 1 +@NoPromptForPassword 1 +force_install_dir `"$SERVER_DIR`" +login anonymous +app_update 4129620 -validate +quit" -Wait -PassThru -NoNewWindow -WorkingDirectory (Split-Path $STEAMCMD)
        $script:lastUpdate = (Get-Date).ToString("HH:mm")
        $script:nextUpdate = (Get-Date).AddHours(3)
        if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 7) { AddLog "ok" "Update done. Next check at $($script:nextUpdate.ToString('HH:mm'))"; Webhook-Updated }
        else { AddLog "warn" "SteamCMD exit code $($p.ExitCode)" }
    } catch {
        AddLog "err" "Update error: $($_.Exception.Message)"
    }
    if ($wasOnline) { $script:manualStop = $false; StartServer }
}

# Check existing process on startup, auto-start if not running
$existing = GetProc
if ($existing) {
    $script:status = "online"
    $script:uptimeStart = Get-Date
    AddLog "ok" "Found running server PID $($existing.Id)"
} else {
    AddLog "info" "No server running - auto-starting..."
    StartServer
}

$HTML = @"
<!DOCTYPE html><html lang="en"><head><meta charset="UTF-8"><title>Windrose Server GUI</title>
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Syne:wght@400;500;700&display=swap" rel="stylesheet">
<style>
*{box-sizing:border-box;margin:0;padding:0}
:root{--bg0:#fff;--bg1:#f4f4f2;--bg2:#eaeae7;--t1:#1a1a18;--t2:#555550;--t3:#999990;--bd:rgba(0,0,0,.12);--bd2:rgba(0,0,0,.22);--green:#3B6D11;--gbg:#EAF3DE;--red:#A32D2D;--rbg:#FCEBEB;--amber:#854F0B;--abg:#FAEEDA;--blue:#185FA5;--r:8px;--rl:12px;--mono:'IBM Plex Mono',monospace;--sans:'Syne',sans-serif}
@media(prefers-color-scheme:dark){:root{--bg0:#1e1e1c;--bg1:#282826;--bg2:#323230;--t1:#f0f0ec;--t2:#aaaaA0;--t3:#666660;--bd:rgba(255,255,255,.1);--bd2:rgba(255,255,255,.2);--green:#97C459;--gbg:#173404;--red:#F09595;--rbg:#501313;--amber:#EF9F27;--abg:#412402;--blue:#85B7EB}}
body{font-family:var(--sans);background:var(--bg1);color:var(--t1);min-height:100vh;padding:24px}
.app{max-width:900px;margin:0 auto}
.header{display:flex;align-items:center;justify-content:space-between;margin-bottom:24px}
.server-name{font-size:26px;font-weight:700;letter-spacing:-.5px}
.server-addr{font-size:13px;color:var(--t3);font-family:var(--mono);margin-top:2px}
.badge{display:flex;align-items:center;gap:7px;font-size:13px;font-family:var(--mono);padding:5px 14px;border-radius:20px;border:.5px solid var(--bd);background:var(--bg0)}
.dot{width:8px;height:8px;border-radius:50%;flex-shrink:0}
.dot.online{background:var(--green);animation:pulse 2s infinite}
.dot.offline{background:var(--red)}
.dot.starting,.dot.stopping,.dot.updating{background:var(--amber);animation:pulse 1s infinite}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.35}}
.controls{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap}
.btn{font-family:var(--sans);font-size:13px;font-weight:500;padding:8px 18px;border-radius:var(--r);border:.5px solid var(--bd2);background:var(--bg0);color:var(--t1);cursor:pointer;transition:background .15s,transform .1s}
.btn:hover{background:var(--bg1)}.btn:active{transform:scale(.97)}
.btn.success{color:var(--green);border-color:var(--green)}.btn.success:hover{background:var(--gbg)}
.btn.danger{color:var(--red);border-color:var(--red)}.btn.danger:hover{background:var(--rbg)}
.btn.warn{color:var(--amber);border-color:var(--amber)}.btn.warn:hover{background:var(--abg)}
.btn:disabled{opacity:.35;cursor:not-allowed;transform:none}
.update-bar{font-size:11px;font-family:var(--mono);color:var(--t3);margin-bottom:16px}
.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin-bottom:20px}
.stat{background:var(--bg0);border-radius:var(--r);padding:14px 16px;border:.5px solid var(--bd)}
.stat-label{font-size:11px;color:var(--t3);font-family:var(--mono);text-transform:uppercase;letter-spacing:.5px;margin-bottom:5px}
.stat-value{font-size:22px;font-weight:700;font-family:var(--mono);line-height:1}
.stat-sub{font-size:11px;color:var(--t3);font-family:var(--mono);margin-top:3px}
.bar-track{height:4px;background:var(--bg2);border-radius:2px;margin-top:10px;overflow:hidden}
.bar-fill{height:100%;border-radius:2px;transition:width .6s ease}
.bar-fill.cpu{background:var(--blue)}.bar-fill.ram{background:var(--green)}.bar-fill.net{background:var(--amber)}
.two-col{display:grid;grid-template-columns:1fr 1fr;gap:10px}
.card{background:var(--bg0);border:.5px solid var(--bd);border-radius:var(--rl);overflow:hidden}
.card-head{padding:10px 14px;border-bottom:.5px solid var(--bd);display:flex;align-items:center;justify-content:space-between}
.card-title{font-size:11px;font-weight:500;color:var(--t3);letter-spacing:.5px;text-transform:uppercase;font-family:var(--mono)}
.player-list{max-height:220px;overflow-y:auto}
.player-row{display:flex;align-items:center;padding:8px 14px;border-bottom:.5px solid var(--bd);gap:10px;font-size:13px}
.player-row:last-child{border-bottom:none}
.avatar{width:28px;height:28px;border-radius:50%;background:var(--bg1);display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:500;color:var(--t2);flex-shrink:0;border:.5px solid var(--bd)}
.pname{flex:1;font-weight:500;font-size:13px}
.pping{font-family:var(--mono);font-size:11px}
.pg{color:var(--green)}.pm{color:var(--amber)}.pb{color:var(--red)}
.tag{font-size:10px;padding:2px 8px;border-radius:10px;font-family:var(--mono);font-weight:500}
.tag.online{background:var(--gbg);color:var(--green)}.tag.afk{background:var(--abg);color:var(--amber)}
.kbtn{font-size:11px;color:var(--red);cursor:pointer;padding:2px 8px;border:.5px solid transparent;border-radius:4px;background:none;font-family:var(--sans)}
.kbtn:hover{background:var(--rbg);border-color:var(--red)}
.log-box{font-family:var(--mono);font-size:11px;max-height:220px;overflow-y:auto;padding:10px 14px;line-height:1.8}
.ll{display:flex;gap:10px}
.lt{color:var(--t3);flex-shrink:0;width:54px}
.li{color:var(--t1)}.lw{color:var(--amber)}.le{color:var(--red)}.lo{color:var(--green)}
.empty{padding:28px 14px;text-align:center;color:var(--t3);font-size:13px;font-family:var(--mono)}
</style></head><body>
<div class="app">
<div class="header">
  <div><div class="server-name">Windrose Server</div><div class="server-addr" id="addr">connecting...</div></div>
  <div class="badge"><div class="dot offline" id="dot"></div><span id="stxt">connecting...</span></div>
</div>
<div class="controls">
  <button class="btn success" id="bstart" onclick="act('start')"   disabled>&#9654; start</button>
  <button class="btn danger"  id="bstop"  onclick="act('stop')"    disabled>&#9632; stop</button>
  <button class="btn warn"    id="brst"   onclick="act('restart')" disabled>&#8635; restart</button>
  <button class="btn"         id="bupd"   onclick="act('update')">&#8593; update</button>
  <button class="btn"         onclick="toggleConfig()">&#9881; config</button>
</div>
<div class="update-bar">last update: <span id="lupd">never</span> &nbsp;|&nbsp; next check: <span id="nupd">--</span></div>

<div id="cfg-panel" style="display:none;background:var(--bg0);border:.5px solid var(--bd);border-radius:var(--rl);padding:16px 20px;margin-bottom:20px">
  <div style="font-size:13px;font-weight:500;margin-bottom:14px">Configuration</div>

  <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:14px">
    <div>
      <div style="font-size:11px;color:var(--t3);font-family:var(--mono);margin-bottom:6px;text-transform:uppercase;letter-spacing:.5px">Daily Restart Time</div>
      <div style="display:flex;align-items:center;gap:8px">
        <select id="cfg-hour" style="font-family:var(--mono);font-size:13px;padding:6px 10px;border-radius:var(--r);border:.5px solid var(--bd2);background:var(--bg0);color:var(--t1)">
          <option value="-1">Disabled</option>
          <option value="0">12:00 AM</option><option value="1">1:00 AM</option><option value="2">2:00 AM</option>
          <option value="3">3:00 AM</option><option value="4">4:00 AM</option><option value="5">5:00 AM</option>
          <option value="6">6:00 AM</option><option value="7">7:00 AM</option><option value="8">8:00 AM</option>
          <option value="9">9:00 AM</option><option value="10">10:00 AM</option><option value="11">11:00 AM</option>
          <option value="12">12:00 PM</option><option value="13">1:00 PM</option><option value="14">2:00 PM</option>
          <option value="15">3:00 PM</option><option value="16">4:00 PM</option><option value="17">5:00 PM</option>
          <option value="18">6:00 PM</option><option value="19">7:00 PM</option><option value="20">8:00 PM</option>
          <option value="21">9:00 PM</option><option value="22">10:00 PM</option><option value="23">11:00 PM</option>
        </select>
        <select id="cfg-tz" style="font-family:var(--mono);font-size:13px;padding:6px 10px;border-radius:var(--r);border:.5px solid var(--bd2);background:var(--bg0);color:var(--t1)">
          <option value="Eastern Standard Time">EST</option>
          <option value="Central Standard Time">CST</option>
          <option value="Mountain Standard Time">MST</option>
          <option value="Pacific Standard Time">PST</option>
          <option value="UTC">UTC</option>
          <option value="GMT Standard Time">GMT</option>
          <option value="Central European Standard Time">CET</option>
          <option value="AUS Eastern Standard Time">AEST</option>
        </select>
      </div>
    </div>
    <div>
      <div style="font-size:11px;color:var(--t3);font-family:var(--mono);margin-bottom:6px;text-transform:uppercase;letter-spacing:.5px">Discord Webhook</div>
      <div style="display:flex;align-items:center;gap:8px">
        <input id="cfg-webhook" type="text" placeholder="https://discord.com/api/webhooks/..." style="flex:1;font-family:var(--mono);font-size:11px;padding:7px 10px;border-radius:var(--r);border:.5px solid var(--bd2);background:var(--bg1);color:var(--t1)">
        <span id="cfg-wh-status" style="font-size:11px;font-family:var(--mono);color:var(--t3);white-space:nowrap"></span>
      </div>
    </div>
  </div>

  <div style="display:flex;gap:8px;align-items:center">
    <button class="btn success" onclick="saveConfig()">save</button>
    <button class="btn danger"  onclick="clearWebhook()">remove webhook</button>
    <span id="cfg-saved" style="font-size:11px;font-family:var(--mono);color:var(--green);display:none">saved!</span>
  </div>
</div>

<div class="grid">
  <div class="stat"><div class="stat-label">CPU</div><div class="stat-value" id="cpu">--</div><div class="bar-track"><div class="bar-fill cpu" id="cpub" style="width:0%"></div></div></div>
  <div class="stat"><div class="stat-label">RAM</div><div class="stat-value" id="ram">--</div><div class="stat-sub" id="ramsub">of $MAX_RAM_GB GB</div><div class="bar-track"><div class="bar-fill ram" id="ramb" style="width:0%"></div></div></div>
  <div class="stat"><div class="stat-label">Uptime</div><div class="stat-value" id="upt">--</div><div class="stat-sub">since last restart</div></div>
  <div class="stat"><div class="stat-label">Network</div><div class="stat-value" id="net">--</div><div class="bar-track"><div class="bar-fill net" id="netb" style="width:0%"></div></div></div>
</div>
<div class="two-col">
  <div class="card">
    <div class="card-head"><span class="card-title">players</span><span style="font-size:12px;font-family:var(--mono);color:var(--t3)" id="pcnt">0/20</span></div>
    <div class="player-list" id="plist"><div class="empty">no players connected</div></div>
  </div>
  <div class="card">
    <div class="card-head"><span class="card-title">live log</span><button class="btn" style="font-size:11px;padding:3px 10px" onclick="clrLog()">clear</button></div>
    <div class="log-box" id="lbox"></div>
  </div>
</div>
</div>
<script>
const API='http://localhost:$PORT';
let logs=[],maxRam=$MAX_RAM_GB;
async function poll(){
  try{const d=await(await fetch(API+'/state')).json();apply(d);}
  catch{}
}
function apply(d){
  setSt(d.status);
  const h=Math.floor(d.uptime/3600),m=Math.floor((d.uptime%3600)/60);
  set('upt',d.uptime>0?h+'h '+m+'m':'--');
  set('cpu',d.cpu+'%');id('cpub').style.width=Math.min(d.cpu,100)+'%';
  set('ram',d.ram.toFixed(1)+' GB');id('ramb').style.width=Math.round(d.ram/maxRam*100)+'%';
  set('net',d.net.toFixed(1)+' MB/s');
  set('lupd',d.lastUpdate||'never');
  if(d.webhookSet==='true'){id('cfg-wh-status').textContent='active';id('cfg-wh-status').style.color='var(--green)';}
  else{id('cfg-wh-status').textContent='not set';id('cfg-wh-status').style.color='var(--t3)';}
  if(d.restartHour!==undefined)id('cfg-hour').value=d.restartHour;
  if(d.restartTZ)id('cfg-tz').value=d.restartTZ;
  set('nupd',d.nextUpdate||'--');
  renderP(d.players);logs=d.logs;renderL();
}
function setSt(s){
  id('dot').className='dot '+s;id('stxt').textContent=s;
  id('addr').textContent=s==='online'?'localhost - running':'localhost - '+s;
  id('bstart').disabled=s!=='offline';id('bstop').disabled=s!=='online';id('brst').disabled=s!=='online';
}
function renderP(ps){
  id('pcnt').textContent=ps.length+'/20';
  if(!ps.length){id('plist').innerHTML='<div class="empty">no players connected</div>';return;}
  id('plist').innerHTML=ps.map(p=>{
    const pc=p.ping<60?'pg':p.ping<120?'pm':'pb';
    return '<div class="player-row"><div class="avatar">'+p.name.slice(0,2).toUpperCase()+'</div><span class="pname">'+p.name+'</span><span class="tag '+(p.status||'online')+'">'+(p.status||'online')+'</span><span class="pping '+pc+'">'+(p.ping>0?p.ping+'ms':'--')+'</span><button class="kbtn" onclick="kick(\''+p.name+'\')">kick</button></div>';
  }).join('');
}
function renderL(){
  const el=id('lbox'),ab=el.scrollHeight-el.scrollTop<=el.clientHeight+20;
  el.innerHTML=logs.map(l=>'<div class="ll"><span class="lt">'+l.t+'</span><span class="l'+l.level[0]+'">'+l.msg.replace(/</g,'&lt;')+'</span></div>').join('');
  if(ab)el.scrollTop=el.scrollHeight;
}
function clrLog(){logs=[];renderL();}
async function act(c){try{await fetch(API+'/'+c,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});}catch{}}
async function kick(n){try{await fetch(API+'/kick',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:n})});}catch{}}
function id(x){return document.getElementById(x)}
function toggleConfig(){
  const p=id('cfg-panel');
  p.style.display=p.style.display==='none'?'block':'none';
}
async function saveConfig(){
  const hour=parseInt(id('cfg-hour').value);
  const tz=id('cfg-tz').value;
  const webhook=id('cfg-webhook').value.trim();
  const body={restartHour:hour,restartTZ:tz};
  if(webhook)body.webhookUrl=webhook;
  await fetch(API+'/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  id('cfg-webhook').value='';
  id('cfg-saved').style.display='';
  setTimeout(()=>id('cfg-saved').style.display='none',2000);
}
async function clearWebhook(){
  await fetch(API+'/webhook',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:''})});
  id('cfg-wh-status').textContent='removed';id('cfg-wh-status').style.color='var(--red)';
}
function set(x,v){id(x).textContent=v}
poll();setInterval(poll,2000);
</script></body></html>
"@

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$PORT/")
try { $listener.Start() } catch {
    
    
    pause; exit
}






Start-Process "http://localhost:$PORT"

$script:lastCheckMin = -1

while ($listener.IsListening) {
    try {
        $ctx  = $listener.GetContext()
        $req  = $ctx.Request
        $resp = $ctx.Response
        $resp.Headers.Add("Access-Control-Allow-Origin","*")
        $resp.Headers.Add("Access-Control-Allow-Methods","GET,POST,OPTIONS")
        $path = $req.Url.AbsolutePath.TrimStart('/')

        if ($req.HttpMethod -eq "OPTIONS") {
            $resp.StatusCode = 200
            try { $resp.OutputStream.Close() } catch {}
            continue
        }

        if ($req.HttpMethod -eq "GET" -and ($path -eq "" -or $path -eq "index.html")) {
            $resp.ContentType = "text/html; charset=utf-8"
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($HTML)
            $resp.OutputStream.Write($bytes,0,$bytes.Length)

        } elseif ($req.HttpMethod -eq "GET" -and $path -eq "state") {
            $resp.ContentType = "application/json"

            # Update stats
            $p = GetProc
            if ($p) {
                try { $script:ram = [math]::Round($p.WorkingSet64/1073741824,2) } catch { $script:ram = 0.0 }
                try {
                    $now = [datetime]::Now
                    $cpuNow = $p.CPU
                    $elapsed = ($now - $script:lastCpuCheck).TotalSeconds
                    if ($elapsed -gt 0.5 -and $script:lastCpuCheck -ne [datetime]::MinValue) {
                        $delta = $cpuNow - $script:lastCpuTime
                        $script:cpu = [math]::Min([math]::Round($delta / $elapsed / [Environment]::ProcessorCount * 100, 0), 100)
                    }
                    $script:lastCpuTime = $cpuNow
                    $script:lastCpuCheck = $now
                } catch { $script:cpu = 0 }
                if ($script:status -ne "online") { $script:status = "online"; if (-not $script:uptimeStart) { $script:uptimeStart = Get-Date } }
                if ($script:uptimeStart) { $script:uptime = [int]((Get-Date) - $script:uptimeStart).TotalSeconds }
            } else {
                $script:cpu = 0; $script:ram = 0.0
                if ($script:status -eq "online") {
                    if ($script:manualStop) {
                        $script:status = "offline"; $script:players.Clear(); AddLog "warn" "Server stopped"
                    } else {
                        AddLog "err" "Server crashed - auto-restarting in 5s..."
                        Webhook-Crash
                        $script:status = "starting"; $script:players.Clear()
                        Start-Sleep 5; StartServer
                    }
                }
            }

            # Daily restart at 4 AM EST
            $rstZone = [System.TimeZoneInfo]::FindSystemTimeZoneById($script:restartTZ)
            $rstNow  = [System.TimeZoneInfo]::ConvertTimeFromUtc([datetime]::UtcNow, $rstZone)
            if ($rstNow.Hour -eq $script:restartHour -and $rstNow.Minute -eq 0 -and $script:lastDailyRestart.Date -lt [datetime]::Today -and $script:status -eq "online") {
                $script:lastDailyRestart = [datetime]::Today
                AddLog "info" "Daily 4 AM EST restart triggered"
                Webhook-Daily
                StopServer
                Start-Sleep 2
                StartServer
            }

            # Auto-update check (once per minute, non-blocking)
            $nowMin = [int](Get-Date -UFormat "%s") / 60
            if ($nowMin -gt $script:lastCheckMin + 1) {
                $script:lastCheckMin = $nowMin
                if ((Get-Date) -ge $script:nextUpdate) {
                    AddLog "info" "3-hour update check triggered"
                    UpdateServer
                }
            }

            $nextStr = $script:nextUpdate.ToString("HH:mm")
            $pa = ($script:players | ForEach-Object { "{`"name`":`"$($_.name)`",`"ping`":$($_.ping),`"status`":`"$($_.status)`"}" }) -join ","
            $la = ($script:logs | ForEach-Object {
                $m = $_.msg -replace '\\','\\' -replace '"','\"'
                "{`"t`":`"$($_.t)`",`"level`":`"$($_.level)`",`"msg`":`"$m`"}"
            }) -join ","
            $webhookSet = if ($script:webhookUrl) { "true" } else { "false" }
            $json = "{`"status`":`"$($script:status)`",`"uptime`":$($script:uptime),`"cpu`":$($script:cpu),`"ram`":$($script:ram),`"net`":0.0,`"maxRam`":$MAX_RAM_GB,`"lastUpdate`":`"$($script:lastUpdate)`",`"nextUpdate`":`"$nextStr`",`"webhookSet`":$webhookSet,`"restartHour`":$($script:restartHour),`"restartTZ`":`"$($script:restartTZ)`",`"players`":[$pa],`"logs`":[$la]}"
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            $resp.OutputStream.Write($bytes,0,$bytes.Length)

        } elseif ($req.HttpMethod -eq "POST") {
            $resp.ContentType = "application/json"
            $bodyStr = (New-Object System.IO.StreamReader($req.InputStream)).ReadToEnd()
            switch ($path) {
                "start"   { StartServer }
                "stop"    { StopServer }
                "restart" { StopServer; Start-Sleep 2; StartServer }
                "update"  { UpdateServer }
                "settings" {
                    try {
                        $parsed = $bodyStr | ConvertFrom-Json
                        if ($parsed.webhookUrl -ne $null) { $script:webhookUrl  = $parsed.webhookUrl }
                        if ($parsed.restartHour -ne $null){ $script:restartHour = [int]$parsed.restartHour }
                        if ($parsed.restartTZ)            { $script:restartTZ   = $parsed.restartTZ }
                        Save-Settings
                        AddLog "ok" "Settings saved"
                        if ($script:webhookUrl) { SendWebhook 3066993 "Settings Updated" "Windrose Server GUI settings have been saved." }
                    } catch { AddLog "err" "Settings save error: $($_.Exception.Message)" }
                }
                "webhook" {
                    try {
                        $parsed = $bodyStr | ConvertFrom-Json
                        $wurl = $parsed.url
                        if ($wurl) {
                            $script:webhookUrl = $wurl
                            Save-Settings
                            AddLog "ok" "Discord webhook saved"
                            SendWebhook 3066993 "Webhook Connected" "Windrose Server GUI is now connected."
                        } else {
                            $script:webhookUrl = ""
                            Save-Settings
                            AddLog "info" "Discord webhook removed"
                        }
                    } catch { AddLog "err" "Webhook save error: $($_.Exception.Message)" }
                }
                "kick"    {
                    if ($bodyStr -match '"name"\s*:\s*"([^"]+)"') {
                        $n = $Matches[1]
                        $script:players.RemoveAll([Predicate[hashtable]]{ param($x) $x.name -eq $n }) | Out-Null
                        AddLog "warn" "$n was kicked"
                    Webhook-Kick $n
                    }
                }
            }
            $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"ok":true}')
            $resp.OutputStream.Write($bytes,0,$bytes.Length)

        } else {
            $resp.StatusCode = 404
            $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"error":"not found"}')
            $resp.OutputStream.Write($bytes,0,$bytes.Length)
        }
        try { $resp.OutputStream.Close() } catch {}
    } catch {
        
        try { $resp.OutputStream.Close() } catch {}
    }
}
