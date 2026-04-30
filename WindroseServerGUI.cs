using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Windrose Server Control")]
[assembly: AssemblyVersion("2.0.0")]

namespace WindroseServerControl
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ServerApp());
        }
    }

    class Settings
    {
        public string ServerDir   = "";
        public string SteamCmd    = "";
        public string WebhookUrl  = "";
        public int    RestartHour = 4;
        public bool NotifyOnline    = true;
        public bool NotifyOffline   = true;
        public bool NotifyCrash     = true;
        public bool NotifyRestart   = true;
        public bool NotifyUpdate    = true;
        public bool NotifyDaily     = true;
        public bool NotifyJoin      = true;
        public bool NotifyLeave     = true;
        public bool NotifyKick      = true;
        public string RconApiUrl    = "http://localhost:9600";
        public string RconApiKey    = "";
        public int    RconPollSec   = 5;
        public int    UpdateHours   = 3;

        static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windrose_settings.json"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    string sd = GetStr(json, "serverDir"); if (sd != null) s.ServerDir = sd;
                    string sc = GetStr(json, "steamCmd");  if (sc != null) s.SteamCmd  = sc;
                    string wh = GetStr(json, "webhookUrl");if (wh != null) s.WebhookUrl= wh;
                    int? rh = GetInt(json, "restartHour"); if (rh.HasValue) s.RestartHour = rh.Value;
                    s.NotifyOnline  = GetBool(json, "notifyOnline",  true);
                    s.NotifyOffline = GetBool(json, "notifyOffline", true);
                    s.NotifyCrash   = GetBool(json, "notifyCrash",   true);
                    s.NotifyRestart = GetBool(json, "notifyRestart", true);
                    s.NotifyUpdate  = GetBool(json, "notifyUpdate",  true);
                    s.NotifyDaily   = GetBool(json, "notifyDaily",   true);
                    s.NotifyJoin    = GetBool(json, "notifyJoin",    true);
                    s.NotifyLeave   = GetBool(json, "notifyLeave",   true);
                    s.NotifyKick    = GetBool(json, "notifyKick",    true);
                    string rurl = GetStr(json, "rconApiUrl"); if (!string.IsNullOrEmpty(rurl)) s.RconApiUrl = rurl;
                    string rkey = GetStr(json, "rconApiKey"); if (rkey != null) s.RconApiKey = rkey;
                    int? rps = GetInt(json, "rconPollSec"); if (rps.HasValue) s.RconPollSec = rps.Value;
                    int? uph = GetInt(json, "updateHours"); if (uph.HasValue) s.UpdateHours = Math.Max(1, uph.Value);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                string json = "{\n" +
                    "  \"serverDir\": \""    + Esc(ServerDir)  + "\",\n" +
                    "  \"steamCmd\": \""     + Esc(SteamCmd)   + "\",\n" +
                    "  \"webhookUrl\": \""   + Esc(WebhookUrl) + "\",\n" +
                    "  \"rconApiUrl\": \""   + Esc(RconApiUrl) + "\",\n" +
                    "  \"rconApiKey\": \""   + Esc(RconApiKey) + "\",\n" +
                    "  \"restartHour\": "    + RestartHour     + ",\n" +
                    "  \"notifyOnline\": "   + (NotifyOnline  ? "true" : "false") + ",\n" +
                    "  \"notifyOffline\": "  + (NotifyOffline ? "true" : "false") + ",\n" +
                    "  \"notifyCrash\": "    + (NotifyCrash   ? "true" : "false") + ",\n" +
                    "  \"notifyRestart\": "  + (NotifyRestart ? "true" : "false") + ",\n" +
                    "  \"notifyUpdate\": "   + (NotifyUpdate  ? "true" : "false") + ",\n" +
                    "  \"notifyDaily\": "    + (NotifyDaily   ? "true" : "false") + ",\n" +
                    "  \"notifyJoin\": "     + (NotifyJoin    ? "true" : "false") + ",\n" +
                    "  \"notifyLeave\": "    + (NotifyLeave   ? "true" : "false") + ",\n" +
                    "  \"notifyKick\": "     + (NotifyKick    ? "true" : "false") + "\n" +
                    "}";
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        static bool GetBool(string json, string key, bool def)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(true|false)");
            if (!m.Success) return def;
            return m.Groups[1].Value == "true";
        }
        static string GetStr(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
        }
        static int? GetInt(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            if (!m.Success) return null;
            return int.Parse(m.Groups[1].Value);
        }
        static double? GetDbl(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*([\\d.]+)");
            if (!m.Success) return null;
            return double.Parse(m.Groups[1].Value);
        }
    }

    class LogEntry { public string T; public string Level; public string Msg; }
    class Player   { public string Name; public string Status; public int Ping; public string AccountId; }

    class ServerApp : ApplicationContext
    {
        Settings cfg;
        string serverExe = "", serverDir = "", steamCmd = "", logFile = "", banlistFile = "";
        string serverName = "", inviteCode = "", serverDescFile = "";
        int maxPlayers = 20;
        bool passwordProtected = false;
        string status = "offline";
        bool   manualStop = false;
        DateTime uptimeStart = DateTime.MinValue;
        bool     hasUptimeStart = false;
        double   cpu = 0, ram = 0;
        DateTime lastCpuCheck = DateTime.MinValue;
        double   lastCpuTime  = 0;
        DateTime nextUpdate;
        string   lastUpdate = "never";
        DateTime lastDailyRestart;

        readonly List<LogEntry> logs    = new List<LogEntry>();
        readonly List<Player>   players = new List<Player>();
        readonly List<string>    banlist = new List<string>(); // "name|accountId" pairs
        readonly Dictionary<string,string> pendingAccountIds = new Dictionary<string,string>();
        readonly object         lk      = new object();

        HttpListener        listener;
        NotifyIcon          tray;
        ToolStripMenuItem   menuStatus;
        System.Windows.Forms.Timer trayTimer;
        const int PORT = 7777;

        public ServerApp()
        {
            lastDailyRestart = DateTime.Today.AddDays(-1);
            cfg = Settings.Load();
            nextUpdate       = DateTime.Now.AddHours(cfg.UpdateHours > 0 ? cfg.UpdateHours : 3);

            DetectPaths();
            BuildTray();
            StartHttp();

            ThreadPool.QueueUserWorkItem(_ => LogTailLoop());
            ThreadPool.QueueUserWorkItem(_ => RconPollLoop());

            trayTimer = new System.Windows.Forms.Timer();
            trayTimer.Interval = 3000;
            trayTimer.Tick += OnTick;
            trayTimer.Start();

            AddLog("info", "GUI started. Auto-starting server...");
            ThreadPool.QueueUserWorkItem(_ => StartServer());
        }

        // ── Paths ─────────────────────────────────────────────────────────
        void DetectPaths()
        {
            serverDir = cfg.ServerDir;
            steamCmd  = cfg.SteamCmd;

            if (!string.IsNullOrEmpty(serverDir))
            {
                string chk = Path.Combine(serverDir, "R5", "Binaries", "Win64", "WindroseServer-Win64-Shipping.exe");
                if (!File.Exists(chk)) { serverDir = ""; steamCmd = ""; }
            }

            if (string.IsNullOrEmpty(serverDir))
            {
                AddLog("info", "Scanning for Windrose server...");
                serverDir = FindServerDir() ?? "";
            }
            if (string.IsNullOrEmpty(steamCmd))
            {
                AddLog("info", "Scanning for SteamCMD...");
                steamCmd = FindSteamCmd() ?? "";
            }

            if (string.IsNullOrEmpty(serverDir))
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "Could not auto-detect Windrose server.\nSelect your Windrose Dedicated Server folder.";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() == DialogResult.OK)
                    serverDir = dlg.SelectedPath;
            }

            if (string.IsNullOrEmpty(steamCmd))
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Title  = "Locate steamcmd.exe (cancel to skip auto-updates)";
                dlg.Filter = "steamcmd.exe|steamcmd.exe|All files|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                    steamCmd = dlg.FileName;
            }

            serverExe = string.IsNullOrEmpty(serverDir) ? "" :
                Path.Combine(serverDir, "R5", "Binaries", "Win64", "WindroseServer-Win64-Shipping.exe");
            logFile = string.IsNullOrEmpty(serverDir) ? "" :
                Path.Combine(serverDir, "R5", "Saved", "Logs", "R5");
            serverDescFile = string.IsNullOrEmpty(serverDir) ? "" :
                Path.Combine(serverDir, "ServerDescription.json");
            LoadServerDescription();
            // Copy banlist.json from mod folder to app folder for manual editing
            SyncBanlistFile();

            // Read max players from ServerDescription.json
            try
            {
                string descPath = Path.Combine(serverDir, "R5", "ServerDescription.json");
                if (File.Exists(descPath))
                {
                    string json = File.ReadAllText(descPath);
                    Match m = Regex.Match(json, "\"MaxPlayerCount\"\\s*:\\s*(\\d+)");
                    if (!m.Success) m = Regex.Match(json, "\"maxPlayerCount\"\\s*:\\s*(\\d+)");
                    if (!m.Success) m = Regex.Match(json, "\"MaxPlayers\"\\s*:\\s*(\\d+)");
                    if (m.Success) maxPlayers = int.Parse(m.Groups[1].Value);
                }
            }
            catch { }

            cfg.ServerDir = serverDir;
            cfg.SteamCmd  = steamCmd;
            cfg.Save();
        }

        string FindServerDir()
        {
            List<string> cands = new List<string>();

            string[] regKeys = { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Valve\Steam" };
            foreach (string rk in regKeys)
            {
                try
                {
                    object val = Registry.GetValue(rk, "SteamPath", null);
                    if (val != null)
                    {
                        string sp = val.ToString().Replace('/', '\\').TrimEnd('\\');
                        cands.Add(Path.Combine(sp, "steamapps", "common", "Windrose Dedicated Server"));
                        string vdf = Path.Combine(sp, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (string line in File.ReadAllLines(vdf))
                            {
                                Match m = Regex.Match(line, "\"path\"\\s+\"([^\"]+)\"");
                                if (m.Success)
                                    cands.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", "Windrose Dedicated Server"));
                            }
                    }
                }
                catch { }
            }

            string[] folderNames = { "Steam", "SteamLibrary", "SteamCMD", @"Games\Steam", @"Games\SteamLibrary", @"Program Files (x86)\Steam", @"Program Files\Steam" };
            foreach (DriveInfo drv in DriveInfo.GetDrives())
                if (drv.IsReady && drv.DriveType == DriveType.Fixed)
                {
                    string d = drv.RootDirectory.FullName.TrimEnd('\\');
                    foreach (string f in folderNames)
                        cands.Add(Path.Combine(d, f, "steamapps", "common", "Windrose Dedicated Server"));
                }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desktop     = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string docs        = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string downloads   = Path.Combine(userProfile, "Downloads");
            foreach (string dir in new[] { userProfile, desktop, docs, downloads })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                cands.Add(Path.Combine(dir, "Windrose Dedicated Server"));
                try { foreach (string sub in Directory.GetDirectories(dir)) cands.Add(sub); } catch { }
            }

            foreach (string c in cands.Distinct())
            {
                string exe = Path.Combine(c, "R5", "Binaries", "Win64", "WindroseServer-Win64-Shipping.exe");
                if (File.Exists(exe)) return c;
            }

            AddLog("info", "Doing full drive scan...");
            foreach (DriveInfo drv in DriveInfo.GetDrives())
                if (drv.IsReady && drv.DriveType == DriveType.Fixed)
                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(drv.RootDirectory.FullName, "WindroseServer-Win64-Shipping.exe", SearchOption.AllDirectories))
                        {
                            DirectoryInfo di = new DirectoryInfo(f);
                            if (di.Parent != null && di.Parent.Parent != null && di.Parent.Parent.Parent != null && di.Parent.Parent.Parent.Parent != null)
                                return di.Parent.Parent.Parent.Parent.Parent.FullName;
                        }
                    }
                    catch { }
            return null;
        }

        string FindSteamCmd()
        {
            List<string> cands = new List<string>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desktop     = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            foreach (DriveInfo drv in DriveInfo.GetDrives())
                if (drv.IsReady && drv.DriveType == DriveType.Fixed)
                {
                    string d = drv.RootDirectory.FullName.TrimEnd('\\');
                    cands.Add(Path.Combine(d, "SteamCMD",    "steamcmd.exe"));
                    cands.Add(Path.Combine(d, "steamcmd",    "steamcmd.exe"));
                    cands.Add(Path.Combine(d,                "steamcmd.exe"));
                    cands.Add(Path.Combine(d, "Program Files (x86)", "Steam", "steamcmd.exe"));
                }
            cands.Add(Path.Combine(userProfile, "Downloads", "steamcmd", "steamcmd.exe"));
            cands.Add(Path.Combine(desktop,     "steamcmd",  "steamcmd.exe"));

            foreach (string c in cands) if (File.Exists(c)) return c;

            foreach (DriveInfo drv in DriveInfo.GetDrives())
                if (drv.IsReady && drv.DriveType == DriveType.Fixed)
                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(drv.RootDirectory.FullName, "steamcmd.exe", SearchOption.AllDirectories))
                            return f;
                    }
                    catch { }
            return null;
        }

        // ── Logging ───────────────────────────────────────────────────────
        void LoadServerDescription()
        {
            try
            {
                if (string.IsNullOrEmpty(serverDescFile) || !File.Exists(serverDescFile)) return;
                string json = File.ReadAllText(serverDescFile);
                Match mn = Regex.Match(json, "\"ServerName\"\\s*:\\s*\"([^\"]*)\"");
                if (mn.Success && !string.IsNullOrEmpty(mn.Groups[1].Value)) serverName = mn.Groups[1].Value;
                Match mi = Regex.Match(json, "\"InviteCode\"\\s*:\\s*\"([^\"]+)\"");
                if (mi.Success) inviteCode = mi.Groups[1].Value;
                Match mm = Regex.Match(json, "\"MaxPlayerCount\"\\s*:\\s*(\\d+)");
                if (mm.Success) maxPlayers = int.Parse(mm.Groups[1].Value);
                Match mp = Regex.Match(json, "\"IsPasswordProtected\"\\s*:\\s*(true|false)");
                if (mp.Success) passwordProtected = mp.Groups[1].Value == "true";
            }
            catch { }
        }

        void SyncBanlistFile()
        {
            try
            {
                string modBanlist = string.IsNullOrEmpty(serverDir) ? "" :
                    Path.Combine(serverDir, "R5", "Binaries", "Win64", "ue4ss", "Mods", "WindroseAPI", "banlist.json");
                if (File.Exists(modBanlist))
                    banlistFile = modBanlist;
                else
                    banlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "banlist.json");
                AddLog("info", "Banlist file: " + banlistFile);
            }
            catch { }
        }

        void AddLog(string level, string msg)
        {
            lock (lk) { if (logs.Count >= 200) logs.RemoveAt(0); logs.Add(new LogEntry { T = DateTime.Now.ToString("HH:mm"), Level = level, Msg = msg }); }
        }

        // ── Webhook ───────────────────────────────────────────────────────
        void SendWebhook(int color, string title, string msg)
        {
            if (string.IsNullOrEmpty(cfg.WebhookUrl)) return;
            string url  = cfg.WebhookUrl;
            string body = "{\"embeds\":[{\"title\":\"" + title + "\",\"description\":\"" + msg + "\",\"color\":" + color + "}]}";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method        = "POST";
                    req.ContentType   = "application/json";
                    req.UserAgent     = "WindroseServerControl/2.0";
                    byte[] bytes      = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = bytes.Length;
                    Stream s = req.GetRequestStream();
                    s.Write(bytes, 0, bytes.Length);
                    s.Close();
                    req.GetResponse().Close();
                }
                catch (Exception ex) { AddLog("err", "Webhook error: " + ex.Message); }
            });
        }

        void RconRequest(string endpoint, string body)
        {
            if (string.IsNullOrEmpty(cfg.RconApiKey)) return;
            string url = cfg.RconApiUrl.TrimEnd('/') + endpoint;
            string key = cfg.RconApiKey;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Use curl.exe (built into Windows 10/11) - avoids all PowerShell encoding issues
                    ProcessStartInfo psi = new ProcessStartInfo("curl.exe",
                        "-s -X POST" +
                        " -H \"Authorization: Bearer " + key + "\"" +
                        " -H \"Content-Type: application/json\"" +
                        " -d \"" + body.Replace("\"", "\\\"") + "\"" +
                        " \"" + url + "\"");
                    psi.UseShellExecute        = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError  = true;
                    psi.CreateNoWindow         = true;
                    Process p = Process.Start(psi);
                    string output = p.StandardOutput.ReadToEnd();
                    string errors = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(errors.Trim())) AddLog("err", "RCON " + endpoint + " error: " + errors.Trim());
                    else AddLog("ok", "RCON " + endpoint + ": " + output.Trim());
                }
                catch (Exception ex) { AddLog("err", "RCON " + endpoint + " error: " + ex.Message); }
            });
        }

        string RconGet(string endpoint)
        {
            if (string.IsNullOrEmpty(cfg.RconApiKey)) return null;
            try
            {
                string url = cfg.RconApiUrl.TrimEnd('/') + endpoint;
                string key = cfg.RconApiKey;
                ProcessStartInfo psi = new ProcessStartInfo("curl.exe",
                    "-s -X GET" +
                    " -H \"Authorization: Bearer " + key + "\"" +
                    " -H \"Content-Type: application/json\"" +
                    " \"" + url + "\"");
                psi.UseShellExecute        = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow         = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output;
            }
            catch { return null; }
        }

        void RconPollLoop()
        {
            while (true)
            {
                Thread.Sleep(Math.Max(2, cfg.RconPollSec) * 1000);
                if (string.IsNullOrEmpty(cfg.RconApiKey) || status != "online") continue;
                try
                {
                    string json = RconGet("/players");
                    if (string.IsNullOrEmpty(json)) continue;

                    // Parse player names and account IDs from response
                    // Format: {"players":[{"player_name":"Ghost","account_id":"B701...","object_name":"..."},...]}
                    var newPlayers = new List<Player>();
                    MatchCollection matches = Regex.Matches(json, "\"player_name\"\\s*:\\s*\"([^\"]+)\"[^}]*\"account_id\"\\s*:\\s*\"([^\"]+)\"");
                    foreach (Match m in matches)
                    {
                        string name = m.Groups[1].Value;
                        string aid  = m.Groups[2].Value;
                        newPlayers.Add(new Player { Name = name, AccountId = aid, Ping = 0, Status = "online" });
                        pendingAccountIds[name] = aid;
                    }
                    // Also try account_id before player_name
                    if (newPlayers.Count == 0)
                    {
                        MatchCollection matches2 = Regex.Matches(json, "\"account_id\"\\s*:\\s*\"([^\"]+)\"[^}]*\"player_name\"\\s*:\\s*\"([^\"]+)\"");
                        foreach (Match m in matches2)
                        {
                            string aid  = m.Groups[1].Value;
                            string name = m.Groups[2].Value;
                            newPlayers.Add(new Player { Name = name, AccountId = aid, Ping = 0, Status = "online" });
                            pendingAccountIds[name] = aid;
                        }
                    }

                    if (newPlayers.Count > 0 || json.Contains("[]") || json.Contains("\"players\":[]"))
                    {
                        lock (lk)
                        {
                            // Detect joins
                            foreach (Player p in newPlayers)
                                if (!players.Exists(x => x.Name == p.Name))
                                {
                                    players.Add(p);
                                    if (cfg.NotifyJoin) SendWebhook(3066993, "Player Joined", p.Name + " joined the server.");
                                }
                            // Detect leaves
                            List<Player> toRemove = players.FindAll(p => !newPlayers.Exists(x => x.Name == p.Name));
                            foreach (Player p in toRemove)
                            {
                                players.Remove(p);
                                if (cfg.NotifyLeave) SendWebhook(15158332, "Player Left", p.Name + " left the server.");
                            }
                            // Update pings and account IDs
                            foreach (Player p in newPlayers)
                            {
                                Player ex = players.Find(x => x.Name == p.Name);
                                if (ex != null) { ex.Ping = p.Ping; ex.AccountId = p.AccountId; }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // ── Server control ────────────────────────────────────────────────
        Process GetServerProcess()
        {
            Process[] procs = Process.GetProcessesByName("WindroseServer-Win64-Shipping");
            return procs.Length > 0 ? procs[0] : null;
        }

        void StartServer()
        {
            manualStop = false;
            if (!File.Exists(serverExe)) { AddLog("err", "EXE not found: " + serverExe); status = "offline"; return; }

            // Check if already running
            Process existing = GetServerProcess();
            if (existing != null)
            {
                status = "online";
                if (!hasUptimeStart) { uptimeStart = DateTime.Now; hasUptimeStart = true; }
                AddLog("ok", "Server already running PID " + existing.Id + " - attaching");
                return;
            }

            status = "starting";
            AddLog("info", "Starting server...");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(serverExe, "-log");
                psi.WorkingDirectory = Path.GetDirectoryName(serverExe);
                Process proc = Process.Start(psi);
                AddLog("info", "Launched PID " + proc.Id + " - waiting...");
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(1000);
                    if (GetServerProcess() != null)
                    {
                        status = "online"; uptimeStart = DateTime.Now; hasUptimeStart = true;
                        AddLog("ok", "Server running PID " + GetServerProcess().Id);
                        if (cfg.NotifyOnline)  SendWebhook(3066993,  "Server Online",   "Windrose server is now online.");
                        return;
                    }
                    if (proc.HasExited) { AddLog("err", "Process exited code " + proc.ExitCode); status = "offline"; return; }
                }
                status = "offline"; AddLog("err", "Server did not appear after 30s");
            }
            catch (Exception ex) { status = "offline"; AddLog("err", "Exception: " + ex.Message); }
        }

        void StopServer()
        {
            manualStop = true; status = "stopping";
            AddLog("warn", "Stopping server...");
            Process proc = GetServerProcess();
            if (proc != null) try { proc.Kill(); } catch { }
            lock (lk) players.Clear();
            status = "offline"; hasUptimeStart = false;
            AddLog("warn", "Server stopped");
            if (cfg.NotifyOffline) SendWebhook(15158332, "Server Offline",  "Windrose server has stopped.");
        }

        void UpdateServer()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                AddLog("info", "Checking for updates...");
                if (string.IsNullOrEmpty(steamCmd) || !File.Exists(steamCmd)) { AddLog("err", "SteamCMD not found: " + steamCmd); return; }

                // Check if update is available without stopping server first
                AddLog("info", "Running SteamCMD self-update...");
                RunCmd(steamCmd, "+quit");
                RunCmd(steamCmd, "+quit");

                // Run update and capture output to detect if anything changed
                string tmpLog = Path.Combine(Path.GetTempPath(), "windrose_update.txt");
                int code = RunCmdLog(steamCmd, "+force_install_dir \"" + serverDir + "\" +login anonymous +app_update 4129620 +quit", tmpLog);
                lastUpdate = DateTime.Now.ToString("HH:mm");
                nextUpdate = DateTime.Now.AddHours(cfg.UpdateHours);

                bool updated = false;
                if (File.Exists(tmpLog))
                {
                    string log = File.ReadAllText(tmpLog);
                    updated = log.Contains("downloading") || log.Contains("Downloading") || log.Contains("updating") || log.Contains("Updating") || log.Contains("installed");
                    try { File.Delete(tmpLog); } catch { }
                }

                if (updated)
                {
                    AddLog("ok", "Update found! Restarting server...");
                    if (cfg.NotifyUpdate) SendWebhook(3066993, "Update Complete", "Windrose updated — restarting server.");
                    bool wasOnline = status == "online";
                    if (wasOnline) { StopServer(); Thread.Sleep(3000); }
                    manualStop = false;
                    StartServer();
                }
                else
                {
                    AddLog("ok", "No update available. Next check at " + nextUpdate.ToString("HH:mm"));
                }
            });
        }

        int RunCmdLog(string exe, string args, string logFile)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.WorkingDirectory    = Path.GetDirectoryName(exe);
                psi.UseShellExecute     = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError  = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                File.WriteAllText(logFile, output);
                return p.ExitCode;
            }
            catch { return -1; }
        }

        int RunCmd(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute  = false;
                Process p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode;
            }
            catch { return -1; }
        }

        // ── Tray ──────────────────────────────────────────────────────────
        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menuStatus = new ToolStripMenuItem("● offline") { Enabled = false };
            ToolStripMenuItem miOpen    = new ToolStripMenuItem("Open GUI");
            ToolStripMenuItem miRestart = new ToolStripMenuItem("Restart Server");
            ToolStripMenuItem miStop    = new ToolStripMenuItem("Stop Server");
            ToolStripMenuItem miExit    = new ToolStripMenuItem("Exit");
            miOpen.Click    += (s, e) => Process.Start("http://localhost:" + PORT);
            miRestart.Click += (s, e) => ThreadPool.QueueUserWorkItem(_ => { StopServer(); Thread.Sleep(2000); StartServer(); });
            miStop.Click    += (s, e) => StopServer();
            miExit.Click    += (s, e) => { tray.Visible = false; StopServer(); Application.Exit(); };
            menu.Items.AddRange(new ToolStripItem[] { menuStatus, new ToolStripSeparator(), miOpen, miRestart, miStop, new ToolStripSeparator(), miExit });

            tray = new NotifyIcon();
            tray.Text = "Windrose Server Control";
            tray.Icon = MakeIcon(Color.Red);
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => Process.Start("http://localhost:" + PORT);
        }

        void OnTick(object sender, EventArgs e)
        {
            UpdateStats();
            CheckScheduled();
            Color col = status == "online" ? Color.LimeGreen : status == "offline" ? Color.Red : Color.Orange;
            tray.Icon = MakeIcon(col);
            tray.Text = "Windrose Server - " + status;
            if (menuStatus != null) menuStatus.Text = "● " + status;
        }

        Icon MakeIcon(Color c)
        {
            Bitmap bmp = new Bitmap(16, 16);
            Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.FillEllipse(new SolidBrush(c), 1, 1, 13, 13);
            g.Dispose();
            return Icon.FromHandle(bmp.GetHicon());
        }

        // ── Log tail ──────────────────────────────────────────────────────
        void LogTailLoop()
        {
            // Always pick the most recently modified R5* file in the Logs folder
            string actualLog = "";
            string logDir = string.IsNullOrEmpty(logFile) ? "" : Path.GetDirectoryName(logFile);

            if (!string.IsNullOrEmpty(logDir) && Directory.Exists(logDir))
            {
                try
                {
                    string[] candidates = Directory.GetFiles(logDir, "R5*")
                        .Where(f => !f.Contains("-backup-"))
                        .ToArray();
                    if (candidates.Length > 0)
                        actualLog = candidates.OrderByDescending(f => new FileInfo(f).LastWriteTime).First();
                }
                catch { }
            }

            // Fallback to exact path
            if (string.IsNullOrEmpty(actualLog) && File.Exists(logFile))
                actualLog = logFile;

            AddLog("info", "Log file: " + (string.IsNullOrEmpty(actualLog) ? "not found" : actualLog));

            while (true)
            {
                if (string.IsNullOrEmpty(actualLog) || !File.Exists(actualLog)) { Thread.Sleep(2000); continue; }
                try
                {
                    using (FileStream fs = new FileStream(actualLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        // Scan last 1000 lines to find already-connected players
                        long scanFrom = Math.Max(0, fs.Length - 200000);
                        sr.BaseStream.Seek(scanFrom, SeekOrigin.Begin);
                        sr.BaseStream.Seek(0, SeekOrigin.Begin);
                        var recentLines = new List<string>();
                        while (sr.BaseStream.Position < fs.Length)
                        {
                            string l = sr.ReadLine();
                            if (l != null) recentLines.Add(l);
                        }
                        // Find latest state - players who joined but haven't left
                        var joined  = new HashSet<string>();
                        var left    = new HashSet<string>();
                        var accountIds = new Dictionary<string,string>();
                        foreach (string l in recentLines)
                        {
                            // Capture account IDs from login request
                            Match la = Regex.Match(l, "Login request:.*Name=([^\\s-]+)-([A-F0-9]{32})", RegexOptions.IgnoreCase);
                            if (la.Success) accountIds[la.Groups[1].Value] = la.Groups[2].Value;

                            Match mjs = Regex.Match(l, "Join succeeded: ([^\\s]+)", RegexOptions.IgnoreCase);
                            if (mjs.Success) joined.Add(mjs.Groups[1].Value);
                            if (l.IndexOf("FarewellReason", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Match m = Regex.Match(l, "Name '([^']+)'");
                                if (m.Success) left.Add(m.Groups[1].Value);
                            }
                        }
                        foreach (string name in joined)
                        {
                            if (!left.Contains(name))
                            {
                                string aid = accountIds.ContainsKey(name) ? accountIds[name] : "";
                                lock (lk)
                                {
                                    if (!players.Exists(p => p.Name == name))
                                        players.Add(new Player { Name = name, Ping = 0, Status = "online", AccountId = aid });
                                    pendingAccountIds[name] = aid;
                                }
                            }
                        }
                        AddLog("info", "History scan: " + joined.Count + " joined, " + left.Count + " left, " + players.Count + " online");

                        // Now tail from end for new events
                        sr.BaseStream.Seek(0, SeekOrigin.End);
                        string prevLine = "";
                        while (true)
                        {
                            string line = sr.ReadLine();
                            if (line == null) { Thread.Sleep(500); continue; }
                            line = line.Trim();
                            if (string.IsNullOrEmpty(line)) continue;

                            string level = "info";
                            if (line.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0) level = "warn";
                            else if (line.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Fatal", StringComparison.OrdinalIgnoreCase) >= 0) level = "err";
                            else if (line.IndexOf("aved", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("uccess", StringComparison.OrdinalIgnoreCase) >= 0) level = "ok";

                            // Filter noisy verbose categories
                            bool skip = line.IndexOf("R5LogAccountTracker", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogIceProtocol", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogNetCm: Verbose", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogNetBL", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogP2pGate", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogSocketSubsystem: Verbose", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogHttp", StringComparison.Ordinal) >= 0
                                     || line.IndexOf("R5LogDataKeeper: Verbose", StringComparison.Ordinal) >= 0;

                            if (!skip)
                            {
                                string clean = Regex.Replace(line, @"^\[\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}:\d+\]\[\s*\d+\]", "");
                                if (clean.Length > 140) clean = clean.Substring(0, 140);
                                AddLog(level, clean);
                            }

                            // Player join - capture account ID from login request line
                            Match loginMatch = Regex.Match(line, "Login request:.*Name=([^\\s-]+)-([A-F0-9]{32})", RegexOptions.IgnoreCase);
                            if (loginMatch.Success)
                            {
                                string pname = loginMatch.Groups[1].Value;
                                string aid   = loginMatch.Groups[2].Value;
                                AddLog("info", "Account ID captured: " + pname + " = " + aid);
                                lock (lk) { Player ex = players.Find(p => p.Name == pname); if (ex != null) ex.AccountId = aid; pendingAccountIds[pname] = aid; }
                            }

                            // Player join - "Join succeeded: Ghost"
                            Match mj = Regex.Match(line, "Join succeeded: ([^\\s]+)", RegexOptions.IgnoreCase);
                            if (mj.Success)
                            {
                                string name = mj.Groups[1].Value;
                                lock (lk)
                                {
                                    if (!players.Exists(p => p.Name == name))
                                    {
                                        string aid2 = pendingAccountIds.ContainsKey(name) ? pendingAccountIds[name] : "";
                                        players.Add(new Player { Name = name, Ping = 0, Status = "online", AccountId = aid2 });
                                        if (cfg.NotifyJoin) SendWebhook(3066993, "Player Joined", name + " joined the server.");
                                    }
                                }
                            }

                            // Player leave - FarewellReason
                            if (line.IndexOf("FarewellReason", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Match ml = Regex.Match(line, "Name '([^']+)'");
                                if (ml.Success)
                                {
                                    string name = ml.Groups[1].Value;
                                    lock (lk)
                                    {
                                        if (players.Exists(p => p.Name == name))
                                        {
                                            players.RemoveAll(p => p.Name == name);
                                            if (cfg.NotifyLeave) SendWebhook(15158332, "Player Left", name + " left the server.");
                                        }
                                    }
                                }
                            }

                            prevLine = line;
                        }
                    }
                }
                catch { Thread.Sleep(3000); }
                // Log file may have been rotated - re-find it
                if (!string.IsNullOrEmpty(logFile))
                {
                    string logDir2 = Path.GetDirectoryName(logFile);
                    if (Directory.Exists(logDir2))
                    {
                        try
                        {
                            string[] candidates = Directory.GetFiles(logDir2, "R5*")
                                .Where(f => !f.Contains("-backup-"))
                                .ToArray();
                            if (candidates.Length > 0)
                            {
                                string newest = candidates.OrderByDescending(f => new FileInfo(f).LastWriteTime).First();
                                if (newest != actualLog) { actualLog = newest; AddLog("info", "Log rotated: " + actualLog); }
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        // ── Stats & schedule ──────────────────────────────────────────────
        void UpdateStats()
        {
            Process proc = GetServerProcess();
            if (proc != null)
            {
                try { ram = Math.Round(proc.WorkingSet64 / 1073741824.0, 2); } catch { ram = 0; }
                try
                {
                    DateTime now = DateTime.Now;
                    double cpuNow = proc.TotalProcessorTime.TotalSeconds;
                    double elapsed = (now - lastCpuCheck).TotalSeconds;
                    if (elapsed > 0.5 && lastCpuCheck != DateTime.MinValue)
                        cpu = Math.Min(Math.Round((cpuNow - lastCpuTime) / elapsed / Environment.ProcessorCount * 100, 0), 100);
                    lastCpuTime = cpuNow; lastCpuCheck = now;
                }
                catch { cpu = 0; }
                if (status != "online") { status = "online"; if (!hasUptimeStart) { uptimeStart = DateTime.Now; hasUptimeStart = true; } }
            }
            else
            {
                cpu = 0; ram = 0;
                if (status == "online")
                {
                    if (manualStop) { status = "offline"; lock (lk) players.Clear(); AddLog("warn", "Server stopped"); }
                    else
                    {
                        AddLog("err", "Server crashed - auto-restarting in 5s...");
                        if (cfg.NotifyCrash)   SendWebhook(15158332, "Server Crashed",  "Windrose server crashed - auto-restarting...");
                        status = "starting"; lock (lk) players.Clear();
                        ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(5000); StartServer(); });
                    }
                }
            }
        }

        void CheckScheduled()
        {
            try
            {
                DateTime now = DateTime.Now;
                if (now.Hour == cfg.RestartHour && now.Minute == 0 && lastDailyRestart.Date < DateTime.Today && status == "online")
                {
                    lastDailyRestart = DateTime.Today;
                    AddLog("info", "Daily " + cfg.RestartHour + ":00 restart triggered");
                    if (cfg.NotifyDaily)   SendWebhook(16776960, "Daily Restart",   "Scheduled daily restart.");
                    ThreadPool.QueueUserWorkItem(_ => { StopServer(); Thread.Sleep(2000); StartServer(); });
                }
            }
            catch { }
            if (DateTime.Now >= nextUpdate)
                UpdateServer();
        }

        // ── HTTP ──────────────────────────────────────────────────────────
        void StartHttp()
        {
            // Kill any previous instance holding port 7777
            try
            {
                foreach (Process p in Process.GetProcessesByName("Windrose Server Control"))
                    if (p.Id != Process.GetCurrentProcess().Id) { p.Kill(); Thread.Sleep(500); }
            }
            catch { }

            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:" + PORT + "/");
            try { listener.Start(); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start on port " + PORT + ".\n" + ex.Message + "\n\nIs another instance already running?", "Windrose Server Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit(); return;
            }
            ThreadPool.QueueUserWorkItem(_ => HttpLoop());
            Process.Start("http://localhost:" + PORT);
        }

        void HttpLoop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { continue; }
                HttpListenerContext capture = ctx;
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(capture));
            }
        }

        void HandleRequest(HttpListenerContext ctx)
        {
            HttpListenerRequest  req  = ctx.Request;
            HttpListenerResponse resp = ctx.Response;
            resp.Headers.Add("Access-Control-Allow-Origin",  "*");
            resp.Headers.Add("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
            string path = req.Url.AbsolutePath.TrimStart('/');
            try
            {
                if (req.HttpMethod == "OPTIONS") { resp.StatusCode = 200; return; }
                if (req.HttpMethod == "GET" && (path == "" || path == "index.html"))
                    { resp.ContentType = "text/html; charset=utf-8"; Write(resp, Html()); }
                else if (req.HttpMethod == "GET" && path == "state")
                    { resp.ContentType = "application/json"; Write(resp, StateJson()); }
                else if (req.HttpMethod == "POST")
                    { resp.ContentType = "application/json"; string body = new StreamReader(req.InputStream).ReadToEnd(); Post(path, body); Write(resp, "{\"ok\":true}"); }
                else { resp.StatusCode = 404; Write(resp, "{\"error\":\"not found\"}"); }
            }
            catch { }
            finally { try { resp.OutputStream.Close(); } catch { } }
        }

        void Post(string path, string body)
        {
            switch (path)
            {
                case "start":   ThreadPool.QueueUserWorkItem(_ => StartServer()); break;
                case "stop":    StopServer(); break;
                case "restart": ThreadPool.QueueUserWorkItem(_ => { StopServer(); Thread.Sleep(2000); StartServer(); }); break;
                case "update":  UpdateServer(); break;
                case "kick":
                    Match km = Regex.Match(body, "\"accountId\"\\s*:\\s*\"([^\"]+)\"");
                    if (km.Success)
                    {
                        string aid = km.Groups[1].Value;
                        string nm2 = Regex.Match(body, "\"name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                        RconRequest("/kick", "{\"account_id\":\"" + aid + "\"}");
                        lock(lk) players.RemoveAll(p => p.Name == nm2);
                        AddLog("warn", nm2 + " was kicked");
                        if (cfg.NotifyKick) SendWebhook(16776960, "Player Kicked", nm2 + " was kicked.");
                    }
                    break;
                case "ban":
                    Match bm = Regex.Match(body, "\"accountId\"\\s*:\\s*\"([^\"]+)\"");
                    if (bm.Success)
                    {
                        string aid = bm.Groups[1].Value;
                        string nm2 = Regex.Match(body, "\"name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                        RconRequest("/ban", "{\"account_id\":\"" + aid + "\",\"reason\":\"Banned by admin\"}");
                        lock(lk) players.RemoveAll(p => p.Name == nm2);
                        AddLog("warn", nm2 + " banned (account: " + aid + ")");
                        if (cfg.NotifyKick) SendWebhook(15158332, "Player Banned", nm2 + " was banned.");
                    }
                    break;
                case "playerinfo":
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        string json = RconGet("/players");
                        string info = RconGet("/info");
                        if (!string.IsNullOrEmpty(json)) AddLog("ok", "Players: " + json.Trim());
                        if (!string.IsNullOrEmpty(info)) AddLog("ok", "Server: " + info.Trim());
                    });
                    break;
                case "banlist":
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        string json = RconGet("/banlist");
                        if (string.IsNullOrEmpty(json)) return;
                        lock (lk)
                        {
                            banlist.Clear();
                            MatchCollection bm2 = Regex.Matches(json, "\"accountId\"\\s*:\\s*\"([^\"]+)\"");
                            foreach (Match m in bm2)
                            {
                                string aid = m.Groups[1].Value;
                                int start = Math.Max(0, m.Index - 200);
                                string chunk = json.Substring(start, Math.Min(400, json.Length - start));
                                Match nm = Regex.Match(chunk, "\"playerName\"\\s*:\\s*\"([^\"]+)\"");
                                string name = nm.Success ? nm.Groups[1].Value : aid;
                                banlist.Add(name + "|" + aid);
                            }
                        }
                        AddLog("info", "Banlist refreshed: " + banlist.Count + " banned");
                        SyncBanlistFile();
                    });
                    break;
                case "unban":
                    Match ubm = Regex.Match(body, "\"accountId\"\\s*:\\s*\"([^\"]+)\"");
                    if (ubm.Success)
                    {
                        string aid = ubm.Groups[1].Value;
                        AddLog("info", "Unbanning: " + aid);
                        RconRequest("/unban", "{\"account_id\":\"" + aid + "\"}");
                        lock(lk) banlist.RemoveAll(b => b.Contains(aid));
                        AddLog("ok", "Unbanned: " + aid);
                    }
                    break;
                case "settings":
                    try
                    {
                        Match wh = Regex.Match(body, "\"webhookUrl\"\\s*:\\s*\"([^\"]*)\"");
                        if (wh.Success) cfg.WebhookUrl = wh.Groups[1].Value;
                        Match rh = Regex.Match(body, "\"restartHour\"\\s*:\\s*(-?\\d+)");
                        if (rh.Success) cfg.RestartHour = int.Parse(rh.Groups[1].Value);
                        Match nb;
                        nb = Regex.Match(body, "\"notifyOnline\"\\s*:\\s*(true|false)");  if (nb.Success) cfg.NotifyOnline  = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyOffline\"\\s*:\\s*(true|false)"); if (nb.Success) cfg.NotifyOffline = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyCrash\"\\s*:\\s*(true|false)");   if (nb.Success) cfg.NotifyCrash   = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyRestart\"\\s*:\\s*(true|false)"); if (nb.Success) cfg.NotifyRestart = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyUpdate\"\\s*:\\s*(true|false)");  if (nb.Success) cfg.NotifyUpdate  = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyDaily\"\\s*:\\s*(true|false)");   if (nb.Success) cfg.NotifyDaily   = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyJoin\"\\s*:\\s*(true|false)");    if (nb.Success) cfg.NotifyJoin    = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyLeave\"\\s*:\\s*(true|false)");   if (nb.Success) cfg.NotifyLeave   = nb.Groups[1].Value == "true";
                        nb = Regex.Match(body, "\"notifyKick\"\\s*:\\s*(true|false)");    if (nb.Success) cfg.NotifyKick    = nb.Groups[1].Value == "true";
                        Match ru = Regex.Match(body, "\"rconApiUrl\"\\s*:\\s*\"([^\"]+)\""); if (ru.Success) cfg.RconApiUrl = ru.Groups[1].Value;
                        Match rk = Regex.Match(body, "\"rconApiKey\"\\s*:\\s*\"([^\"]+)\""); if (rk.Success) cfg.RconApiKey = rk.Groups[1].Value;
                        Match rp = Regex.Match(body, "\"rconPollSec\"\\s*:\\s*(\\d+)"); if (rp.Success) cfg.RconPollSec = Math.Max(2, int.Parse(rp.Groups[1].Value));
                        Match up = Regex.Match(body, "\"updateHours\"\\s*:\\s*(\\d+)"); if (up.Success) cfg.UpdateHours = Math.Max(1, int.Parse(up.Groups[1].Value));
                        cfg.Save();
                        AddLog("ok", "Settings saved");
                        if (!string.IsNullOrEmpty(cfg.WebhookUrl)) SendWebhook(3066993, "Settings Updated", "Windrose Server Control settings saved.");
                    }
                    catch (Exception ex) { AddLog("err", "Settings error: " + ex.Message); }
                    break;
            }
        }

        void Write(HttpListenerResponse resp, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
        }

        string StateJson()
        {
            int uptime = hasUptimeStart ? (int)(DateTime.Now - uptimeStart).TotalSeconds : 0;
            string pa, la, bl;
            lock (lk)
            {
                pa = string.Join(",", players.Select(p => "{\"name\":\"" + Esc(p.Name) + "\",\"ping\":" + p.Ping + ",\"status\":\"" + p.Status + "\",\"accountId\":\"" + p.AccountId + "\"}").ToArray());
                la = string.Join(",", logs.Select(l => "{\"t\":\"" + l.T + "\",\"level\":\"" + l.Level + "\",\"msg\":\"" + Esc(l.Msg) + "\"}").ToArray());
                bl = string.Join(",", banlist.Select(b => { var parts = b.Split('|'); return "{\"name\":\"" + Esc(parts[0]) + "\",\"accountId\":\"" + Esc(parts.Length > 1 ? parts[1] : parts[0]) + "\"}"; }).ToArray());
            }
            string whs = string.IsNullOrEmpty(cfg.WebhookUrl) ? "false" : "true";
            string notifyJson = ",\"notifyOnline\":" + (cfg.NotifyOnline ? "true" : "false") + ",\"notifyOffline\":" + (cfg.NotifyOffline ? "true" : "false") + ",\"notifyCrash\":" + (cfg.NotifyCrash ? "true" : "false") + ",\"notifyRestart\":" + (cfg.NotifyRestart ? "true" : "false") + ",\"notifyUpdate\":" + (cfg.NotifyUpdate ? "true" : "false") + ",\"notifyDaily\":" + (cfg.NotifyDaily ? "true" : "false") + ",\"notifyJoin\":" + (cfg.NotifyJoin ? "true" : "false") + ",\"notifyLeave\":" + (cfg.NotifyLeave ? "true" : "false") + ",\"notifyKick\":" + (cfg.NotifyKick ? "true" : "false");
            LoadServerDescription();
            string sname = string.IsNullOrEmpty(serverName) ? "Windrose Server" : serverName;
            return "{\"status\":\"" + status + "\",\"uptime\":" + uptime + ",\"cpu\":" + cpu + ",\"ram\":" + ram + ",\"maxPlayers\":" + maxPlayers + ",\"serverName\":\"" + Esc(sname) + "\",\"inviteCode\":\"" + Esc(inviteCode) + "\",\"passwordProtected\":" + (passwordProtected ? "true" : "false") + ",\"lastUpdate\":\"" + lastUpdate + "\",\"nextUpdate\":\"" + nextUpdate.ToString("HH:mm") + "\",\"webhookSet\":" + whs + ",\"rconEnabled\":" + (!string.IsNullOrEmpty(cfg.RconApiKey) ? "true" : "false") + ",\"rconApiUrl\":\"" + Esc(cfg.RconApiUrl) + "\",\"restartHour\":" + cfg.RestartHour + ",\"updateHours\":" + cfg.UpdateHours + notifyJson + ",\"players\":[" + pa + "],\"banlist\":[" + bl + "],\"logs\":[" + la + "]}";
        }

        string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", ""); }

        string Html()
        {
            return "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Windrose Server Control</title>" +
"<link href=\"https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Syne:wght@400;500;700&display=swap\" rel=\"stylesheet\">" +
"<style>" +
"*{box-sizing:border-box;margin:0;padding:0}" +
":root{--bg0:#fff;--bg1:#f4f4f2;--bg2:#eaeae7;--t1:#1a1a18;--t2:#555550;--t3:#999990;--bd:rgba(0,0,0,.12);--bd2:rgba(0,0,0,.22);--green:#3B6D11;--gbg:#EAF3DE;--red:#A32D2D;--rbg:#FCEBEB;--amber:#854F0B;--abg:#FAEEDA;--blue:#185FA5;--r:8px;--rl:12px;--mono:'IBM Plex Mono',monospace;--sans:'Syne',sans-serif}" +
"@media(prefers-color-scheme:dark){:root{--bg0:#1e1e1c;--bg1:#282826;--bg2:#323230;--t1:#f0f0ec;--t2:#aaaaA0;--t3:#666660;--bd:rgba(255,255,255,.1);--bd2:rgba(255,255,255,.2);--green:#97C459;--gbg:#173404;--red:#F09595;--rbg:#501313;--amber:#EF9F27;--abg:#412402;--blue:#85B7EB}}" +
"body{font-family:var(--sans);background:var(--bg1);color:var(--t1);min-height:100vh;padding:24px}" +
".app{max-width:900px;margin:0 auto}" +
".header{display:flex;align-items:center;justify-content:space-between;margin-bottom:24px}" +
".server-name{font-size:26px;font-weight:700;letter-spacing:-.5px}" +
".server-addr{font-size:13px;color:var(--t3);font-family:var(--mono);margin-top:2px}" +
".badge{display:flex;align-items:center;gap:7px;font-size:13px;font-family:var(--mono);padding:5px 14px;border-radius:20px;border:.5px solid var(--bd);background:var(--bg0)}" +
".dot{width:8px;height:8px;border-radius:50%;flex-shrink:0}" +
".dot.online{background:var(--green);animation:pulse 2s infinite}" +
".dot.offline{background:var(--red)}" +
".dot.starting,.dot.stopping{background:var(--amber);animation:pulse 1s infinite}" +
"@keyframes pulse{0%,100%{opacity:1}50%{opacity:.35}}" +
".controls{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap}" +
".btn{font-family:var(--sans);font-size:13px;font-weight:500;padding:8px 18px;border-radius:var(--r);border:.5px solid var(--bd2);background:var(--bg0);color:var(--t1);cursor:pointer;transition:background .15s,transform .1s}" +
".btn:hover{background:var(--bg1)}.btn:active{transform:scale(.97)}" +
".btn.success{color:var(--green);border-color:var(--green)}.btn.success:hover{background:var(--gbg)}" +
".btn.danger{color:var(--red);border-color:var(--red)}.btn.danger:hover{background:var(--rbg)}" +
".btn.warn{color:var(--amber);border-color:var(--amber)}.btn.warn:hover{background:var(--abg)}" +
".btn:disabled{opacity:.35;cursor:not-allowed;transform:none}" +
".update-bar{font-size:11px;font-family:var(--mono);color:var(--t3);margin-bottom:16px}" +
".cfg-panel{display:none;background:var(--bg0);border:.5px solid var(--bd);border-radius:var(--rl);padding:16px 20px;margin-bottom:20px}" +
".cfg-title{font-size:13px;font-weight:500;margin-bottom:14px}" +
".cfg-grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:14px}" +
".cfg-label{font-size:11px;color:var(--t3);font-family:var(--mono);margin-bottom:6px;text-transform:uppercase;letter-spacing:.5px}" +
"select,input[type=text]{font-family:var(--mono);font-size:13px;padding:6px 10px;border-radius:var(--r);border:.5px solid var(--bd2);background:var(--bg0);color:var(--t1);width:100%}" +
".grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-bottom:20px}" +
".stat{background:var(--bg0);border-radius:var(--r);padding:14px 16px;border:.5px solid var(--bd)}" +
".stat-label{font-size:11px;color:var(--t3);font-family:var(--mono);text-transform:uppercase;letter-spacing:.5px;margin-bottom:5px}" +
".stat-value{font-size:22px;font-weight:700;font-family:var(--mono);line-height:1}" +
".stat-sub{font-size:11px;color:var(--t3);font-family:var(--mono);margin-top:3px}" +
".bar-track{height:4px;background:var(--bg2);border-radius:2px;margin-top:10px;overflow:hidden}" +
".bar-fill{height:100%;border-radius:2px;transition:width .6s ease}" +
".bar-fill.cpu{background:var(--blue)}.bar-fill.ram{background:var(--green)}.bar-fill.net{background:var(--amber)}" +
".two-col{display:grid;grid-template-columns:1fr 1fr;gap:10px}" +
".card{background:var(--bg0);border:.5px solid var(--bd);border-radius:var(--rl);overflow:hidden}" +
".card-head{padding:10px 14px;border-bottom:.5px solid var(--bd);display:flex;align-items:center;justify-content:space-between}" +
".card-title{font-size:11px;font-weight:500;color:var(--t3);letter-spacing:.5px;text-transform:uppercase;font-family:var(--mono)}" +
".player-list{max-height:220px;overflow-y:auto}" +
".player-row{display:flex;align-items:center;padding:8px 14px;border-bottom:.5px solid var(--bd);gap:10px;font-size:13px}" +
".player-row:last-child{border-bottom:none}" +
".avatar{width:28px;height:28px;border-radius:50%;background:var(--bg1);display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:500;color:var(--t2);flex-shrink:0;border:.5px solid var(--bd)}" +
".pname{flex:1;font-weight:500;font-size:13px}" +
".pping{font-family:var(--mono);font-size:11px}" +
".pg{color:var(--green)}.pm{color:var(--amber)}.pb{color:var(--red)}" +
".tag{font-size:10px;padding:2px 8px;border-radius:10px;font-family:var(--mono);font-weight:500}" +
".tag.online{background:var(--gbg);color:var(--green)}.tag.afk{background:var(--abg);color:var(--amber)}" +
".kbtn{font-size:11px;color:var(--red);cursor:pointer;padding:2px 8px;border:.5px solid transparent;border-radius:4px;background:none;font-family:var(--sans)}" +
".kbtn:hover{background:var(--rbg);border-color:var(--red)}" +
".log-box{font-family:var(--mono);font-size:11px;max-height:220px;overflow-y:auto;padding:10px 14px;line-height:1.8}" +
".ll{display:flex;gap:10px}" +
".lt{color:var(--t3);flex-shrink:0;width:54px}" +
".li{color:var(--t1)}.lw{color:var(--amber)}.le{color:var(--red)}.lo{color:var(--green)}" +
".empty{padding:28px 14px;text-align:center;color:var(--t3);font-size:13px;font-family:var(--mono)}" +
".player-row.selected{background:var(--bg2);border-left:2px solid var(--green)}" +
"@media(max-width:640px){.grid{grid-template-columns:repeat(2,1fr)}.cfg-grid{grid-template-columns:1fr}}" +
"</style></head><body>" +
"<div class=\"app\">" +
"<div class=\"header\"><div><div class=\"server-name\" id=\"sname\">Windrose Server</div><div class=\"server-addr\" id=\"addr\">connecting...</div></div><div class=\"badge\"><div class=\"dot offline\" id=\"dot\"></div><span id=\"stxt\">connecting...</span></div></div>" +
"<div class=\"controls\">" +
"<button class=\"btn success\" id=\"bstart\" onclick=\"act('start')\" disabled>&#9654; start</button>" +
"<button class=\"btn danger\"  id=\"bstop\"  onclick=\"act('stop')\" disabled>&#9632; stop</button>" +
"<button class=\"btn warn\"    id=\"brst\"   onclick=\"act('restart')\" disabled>&#8635; restart</button>" +
"<button class=\"btn\"         onclick=\"act('update')\">&#8593; update</button>" +
"<button class=\"btn\"         id=\"bbanlist\" onclick=\"toggleBanlist()\">&#128683; banlist</button>" +
"<button class=\"btn\"         onclick=\"toggleCfg()\">&#9881; config</button>" +
"</div>" +
"<div class=\"update-bar\">last update: <span id=\"lupd\">never</span> &nbsp;|&nbsp; next check: <span id=\"nupd\">--</span></div>" +
"<div class=\"cfg-panel\" id=\"cfg\">" +
"<div class=\"cfg-title\">Configuration</div>" +
"<div class=\"cfg-grid\">" +
"<div><div class=\"cfg-label\">Daily Restart Time (24h)</div>" +
"<select id=\"cfg-hour\"><option value=\"-1\">Disabled</option><option value=\"0\">00:00</option><option value=\"1\">01:00</option><option value=\"2\">02:00</option><option value=\"3\">03:00</option><option value=\"4\">04:00</option><option value=\"5\">05:00</option><option value=\"6\">06:00</option><option value=\"7\">07:00</option><option value=\"8\">08:00</option><option value=\"9\">09:00</option><option value=\"10\">10:00</option><option value=\"11\">11:00</option><option value=\"12\">12:00</option><option value=\"13\">13:00</option><option value=\"14\">14:00</option><option value=\"15\">15:00</option><option value=\"16\">16:00</option><option value=\"17\">17:00</option><option value=\"18\">18:00</option><option value=\"19\">19:00</option><option value=\"20\">20:00</option><option value=\"21\">21:00</option><option value=\"22\">22:00</option><option value=\"23\">23:00</option></select>" +
"</div>" +
"<div><div class=\"cfg-label\">Discord Webhook</div><div style=\"display:flex;align-items:center;gap:8px\"><input id=\"cfg-wh\" type=\"text\" placeholder=\"https://discord.com/api/webhooks/...\"><span id=\"cfg-wh-status\" style=\"font-size:11px;font-family:var(--mono);color:var(--t3);white-space:nowrap\"></span></div></div>" +
"</div>" +"<div style=\"display:grid;grid-template-columns:repeat(3,1fr);gap:6px;margin-bottom:14px\">" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-online\" checked> server online</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-offline\" checked> server offline</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-crash\" checked> server crash</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-restart\" checked> restart</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-update\" checked> update</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-daily\" checked> daily restart</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-join\" checked> player join</label>" +"<label style=\"display:flex;align-items:center;gap:6px;font-size:12px;font-family:var(--mono);cursor:pointer\"><input type=\"checkbox\" id=\"n-leave\" checked> player leave</label>" +"</div>" +"<div style=\"display:flex;gap:8px;align-items:center\"><button class=\"btn success\" onclick=\"saveCfg()\">save</button><button class=\"btn danger\" onclick=\"clearWh()\">remove webhook</button><span id=\"cfg-saved\" style=\"font-size:11px;font-family:var(--mono);color:var(--green);display:none\">saved!</span></div>" +
"<div class=\"cfg-label\" style=\"margin-top:14px;margin-bottom:8px\">Windrose REST API (for kick/ban) <a href=\"https://www.nexusmods.com/windrose/mods/44\" target=\"_blank\" style=\"color:var(--blue);font-size:10px\">nexus mod required</a></div>" +
"<div style=\"display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px;margin-bottom:8px\">" +
"<div><div class=\"cfg-label\">API URL</div><input id=\"cfg-rcon-url\" type=\"text\" placeholder=\"http://localhost:9600\"></div>" +
"<div><div class=\"cfg-label\">API Key</div><input id=\"cfg-rcon-key\" type=\"text\" placeholder=\"your api key\"></div>" +
"<div><div class=\"cfg-label\">Poll Interval (seconds)</div><input id=\"cfg-poll\" type=\"text\" placeholder=\"5\" style=\"width:100%\"></div>" +
"<div><div class=\"cfg-label\">Update Check (hours)</div><input id=\"cfg-upd\" type=\"text\" placeholder=\"3\" style=\"width:100%\"></div>" +
"</div>" +
"</div>" +
"<div id=\"pinfo-modal\" style=\"display:none;position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:100;align-items:center;justify-content:center\">" +
"<div style=\"background:var(--bg0);border:.5px solid var(--bd2);border-radius:var(--rl);padding:24px;min-width:340px;max-width:480px;width:90%\">" +
"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:16px\">" +
"<div style=\"font-size:15px;font-weight:700\" id=\"pinfo-name\">Player Info</div>" +
"<button class=\"btn\" onclick=\"id('pinfo-modal').style.display='none'\">&#x2715;</button>" +
"</div>" +
"<div id=\"pinfo-body\" style=\"font-family:var(--mono);font-size:12px;line-height:2;color:var(--t2)\"></div>" +
"<div style=\"margin-top:16px;display:flex;gap:8px\" id=\"pinfo-btns\"></div>" +
"</div></div>" +
"<div class=\"grid\">" +
"<div class=\"stat\"><div class=\"stat-label\">CPU</div><div class=\"stat-value\" id=\"cpu\">--</div><div class=\"bar-track\"><div class=\"bar-fill cpu\" id=\"cpub\" style=\"width:0%\"></div></div></div>" +
"<div class=\"stat\"><div class=\"stat-label\">RAM</div><div class=\"stat-value\" id=\"ram\">--</div><div class=\"bar-track\"><div class=\"bar-fill ram\" id=\"ramb\" style=\"width:0%\"></div></div></div>" +
"<div class=\"stat\"><div class=\"stat-label\">Uptime</div><div class=\"stat-value\" id=\"upt\">--</div><div class=\"stat-sub\">since last restart</div></div>" +
"</div>" +
"<div style=\"display:flex;flex-direction:column;gap:10px\">" +
"<div class=\"card\"><div class=\"card-head\"><span class=\"card-title\">players</span><span style=\"font-size:12px;font-family:var(--mono);color:var(--t3)\" id=\"pcnt\">0/20</span></div><div class=\"player-list\" id=\"plist\"><div class=\"empty\">no players connected</div></div></div>" +
"<div class=\"card\"><div class=\"card-head\"><span class=\"card-title\">live log</span><button class=\"btn\" style=\"font-size:11px;padding:3px 10px\" onclick=\"clrLog()\">clear</button></div><div class=\"log-box\" id=\"lbox\"></div></div>" +
"</div>" +
"<div class=\"card\" id=\"banlist-card\" style=\"margin-top:10px;display:none\"><div class=\"card-head\"><span class=\"card-title\">&#128683; ban list</span><button class=\"btn\" style=\"font-size:11px;padding:3px 10px\" onclick=\"refreshBanlist()\">refresh</button></div><div class=\"player-list\" id=\"blist\"><div class=\"empty\">click refresh to load</div></div></div>" +
"</div></div>" +
"<script>\n" +
"const API='http://localhost:" + PORT + "';\n" +
"let logs=[],maxPlayers=20,rconEnabled=false,selectedPlayer=null,lastPlayersJson='',lastPlayers=[],notifyLoaded=false,cfgLoaded=false;\n" +
"async function poll(){try{const d=await(await fetch(API+'/state')).json();apply(d);}catch{}}\n" +
"function apply(d){\n" +
"  setSt(d.status);maxPlayers=d.maxPlayers||20;\n" +
"  const h=Math.floor(d.uptime/3600),m=Math.floor((d.uptime%3600)/60);\n" +
"  set('upt',d.uptime>0?h+'h '+m+'m':'--');\n" +
"  set('cpu',d.cpu+'%');id('cpub').style.width=Math.min(d.cpu,100)+'%';\n" +
"  set('ram',d.ram.toFixed(1)+' GB');\n" +
"  set('lupd',d.lastUpdate||'never');set('nupd',d.nextUpdate||'--');\n" +
"  if(d.restartHour!==undefined&&!cfgLoaded){cfgLoaded=true;\n" +
"    id('cfg-hour').value=d.restartHour;\n" +
"    if(d.rconApiUrl)id('cfg-rcon-url').value=d.rconApiUrl;\n" +
"    if(d.rconPollSec)id('cfg-poll').value=d.rconPollSec;\n" +
"    if(d.updateHours)id('cfg-upd').value=d.updateHours;\n" +
"  }\n" +
"  if(d.notifyOnline!==undefined&&!notifyLoaded){notifyLoaded=true;id('n-online').checked=d.notifyOnline;id('n-offline').checked=d.notifyOffline;id('n-crash').checked=d.notifyCrash;id('n-restart').checked=d.notifyRestart;id('n-update').checked=d.notifyUpdate;id('n-daily').checked=d.notifyDaily;id('n-join').checked=d.notifyJoin;id('n-leave').checked=d.notifyLeave;}\n" +
"  id('cfg-wh-status').textContent=d.webhookSet==='true'?'active':'not set';\n" +
"  id('cfg-wh-status').style.color=d.webhookSet==='true'?'var(--green)':'var(--t3)';\n" +
"  if(d.rconEnabled!==undefined){rconEnabled=d.rconEnabled;}\n" +
"  if(d.maxPlayers)maxPlayers=d.maxPlayers;\n" +
"  lastPlayers=d.players;\n" +
"  if(selectedPlayer&&!d.players.find(p=>p.name===selectedPlayer.name)){selectedPlayer=null;updateInfoBtn();}\n" +
"  else if(selectedPlayer){const up=d.players.find(p=>p.name===selectedPlayer.name);if(up&&up.accountId)selectedPlayer=up;}\n" +
"  renderP(d.players);\n" +
"  renderBanlist(d.banlist);logs=d.logs;renderL();\n" +
"}\n" +
"function setSt(s){\n" +
"  id('dot').className='dot '+s;id('stxt').textContent=s;\n" +
"  id('addr').textContent=s==='online'?'localhost - running':'localhost - '+s;\n" +
"  id('bstart').disabled=s!=='offline';id('bstop').disabled=s!=='online';id('brst').disabled=s!=='online';\n" +
"}\n" +
"function renderP(ps){\n" +
"  id('pcnt').textContent=ps.length+'/'+maxPlayers;\n" +
"  if(!ps.length){id('plist').innerHTML='<div class=\"empty\">no players connected</div>';selectedPlayer=null;updateInfoBtn();return;}\n" +
"  id('plist').innerHTML=ps.map(p=>{\n" +
"    const pc=p.ping<60?'pg':p.ping<120?'pm':'pb';\n" +
"    const btns='<button class=\"kbtn\" style=\"color:var(--blue)\" onclick=\"showPlayerInfo('+JSON.stringify(p)+')\">info</button>'+(rconEnabled?'<button class=\"kbtn\" onclick=\"kickPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\')\" >kick</button><button class=\"kbtn\" style=\"font-weight:700\" onclick=\"banPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\')\" >ban</button>':'');\n" +
"    return '<div class=\"player-row\"><div class=\"avatar\">'+p.name.slice(0,2).toUpperCase()+'</div><span class=\"pname\">'+p.name+'</span><span class=\"tag '+(p.status||'online')+'\">'+( p.status||'online')+'</span><span class=\"pping '+pc+'\">'+(p.ping>0?p.ping+'ms':'--')+'</span>'+btns+'</div>';\n" +
"  }).join('');\n" +
"}\n" +
"function selectPlayer(p){selectedPlayer=p;updateInfoBtn();}\n" +
"function updateInfoBtn(){if(id('binfo'))id('binfo').disabled=!selectedPlayer;}\n" +
"function renderL(){\n" +
"  const el=id('lbox'),ab=el.scrollHeight-el.scrollTop<=el.clientHeight+20;\n" +
"  el.innerHTML=logs.map(l=>'<div class=\"ll\"><span class=\"lt\">'+l.t+'</span><span class=\"l'+l.level[0]+'\">'+l.msg.replace(/</g,'&lt;')+'</span></div>').join('');\n" +
"  if(ab)el.scrollTop=el.scrollHeight;\n" +
"}\n" +
"function clrLog(){logs=[];renderL();}\n" +
"function toggleCfg(){const p=id('cfg');p.style.display=p.style.display==='none'||!p.style.display?'block':'none';}\n" +
"async function saveCfg(){\n" +
"  const body={restartHour:parseInt(id('cfg-hour').value),\n" +
"    notifyOnline:id('n-online').checked,notifyOffline:id('n-offline').checked,notifyCrash:id('n-crash').checked,\n" +
"    notifyRestart:id('n-restart').checked,notifyUpdate:id('n-update').checked,notifyDaily:id('n-daily').checked,\n" +
"    notifyJoin:id('n-join').checked,notifyLeave:id('n-leave').checked,\n" +
"    rconPollSec:Math.max(2,parseInt(id('cfg-poll').value)||5),\n" +
"    updateHours:Math.max(1,parseInt(id('cfg-upd').value)||3)};\n" +
"  const wh=id('cfg-wh').value.trim();if(wh)body.webhookUrl=wh;\n" +
"  const ru=id('cfg-rcon-url').value.trim();if(ru)body.rconApiUrl=ru;\n" +
"  const rk=id('cfg-rcon-key').value.trim();if(rk)body.rconApiKey=rk;\n" +
"  await fetch(API+'/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});\n" +
"  id('cfg-wh').value='';id('cfg-rcon-key').value='';id('cfg-saved').style.display='';setTimeout(()=>id('cfg-saved').style.display='none',2000);\n" +
"}\n" +
"async function clearWh(){await fetch(API+'/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({webhookUrl:''})});}\n" +
"async function act(c){try{await fetch(API+'/'+c,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});}catch{}}\n" +
"async function kickPlayer(n,aid){if(confirm('Kick '+n+'?'))try{await fetch(API+'/kick',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:n,accountId:aid})});}catch{}}\n" +
"async function banPlayer(n,aid){if(confirm('Ban '+n+'? This cannot be undone.'))try{await fetch(API+'/ban',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:n,accountId:aid})});}catch{}}\n" +
"async function unbanPlayer(aid){if(confirm('Unban '+aid+'?'))try{await fetch(API+'/unban',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({accountId:aid})});}catch{}}\n" +
"async function refreshBanlist(){try{await fetch(API+'/banlist',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});}catch{}}\n" +
"function toggleBanlist(){const c=id('banlist-card');c.style.display=c.style.display==='none'||!c.style.display?'block':'none';if(c.style.display==='block')refreshBanlist();}\n" +
"function renderBanlist(bl){\n" +
"  if(!bl||!bl.length){id('blist').innerHTML='<div class=\"empty\">no banned players</div>';return;}\n" +
"  id('blist').innerHTML=bl.map(b=>'<div class=\"player-row\"><span class=\"pname\" style=\"font-size:12px\">'+b.name+'</span><span style=\"font-size:10px;font-family:var(--mono);color:var(--t3);flex:1;padding:0 8px\">'+b.accountId+'</span><button class=\"kbtn\" onclick=\"unbanPlayer(\\''+b.accountId+'\\')\" >unban</button></div>').join('');\n" +
"}\n" +
"function showPlayerInfo(p){\n" +
"  if(!p)p=selectedPlayer;\n" +
"  if(!p)return;\n" +
"  id('pinfo-name').textContent=p.name;\n" +
"  id('pinfo-body').innerHTML=\n" +
"    '<div><span style=\"color:var(--t3)\">Name</span>: '+p.name+'</div>'+\n" +
"    '<div><span style=\"color:var(--t3)\">Account ID</span>: '+(p.accountId||'unknown')+'</div>'+\n" +
"    '<div><span style=\"color:var(--t3)\">Status</span>: '+(p.status||'online')+'</div>'+\n" +
"    '<div><span style=\"color:var(--t3)\">Ping</span>: '+(p.ping>0?p.ping+'ms':'--')+'</div>';\n" +
"  id('pinfo-btns').innerHTML=rconEnabled&&p.accountId?\n" +
"    '<button class=\"btn danger\" onclick=\"kickPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\');id(\\'pinfo-modal\\').style.display=\\'none\\'\">kick</button>'+\n" +
"    '<button class=\"btn danger\" style=\"font-weight:700\" onclick=\"banPlayer(\\''+p.name+'\\',\\''+p.accountId+'\\');id(\\'pinfo-modal\\').style.display=\\'none\\'\">ban</button>':'No RCON configured';\n" +
"  id('pinfo-modal').style.display='flex';\n" +
"}\n" +
"function id(x){return document.getElementById(x)}\n" +
"function set(x,v){id(x).textContent=v}\n" +
"poll();setInterval(poll,2000);\n" +
"</script></body></html>";
        }
    }
}
