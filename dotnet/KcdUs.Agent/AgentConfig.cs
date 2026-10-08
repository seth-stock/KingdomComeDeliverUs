// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.Json;
using System.Text.RegularExpressions;
using KcdUs.Wire;

namespace KcdUs.Agent;

/// <summary>The agent's settings: kcdus-agent.json next to the exe (the installer and the launcher write it), overridden by the command line.</summary>
public sealed class AgentConfig
{
    public string GameDir { get; set; } = "";
    public string PlayerName { get; set; } = "Henry";
    /// <summary>host or guest.</summary>
    public string Role { get; set; } = "guest";
    public string RelayHost { get; set; } = "127.0.0.1";
    public int RelayPort { get; set; } = Proto.DefaultPort;
    public string Password { get; set; } = "";
    /// <summary>The host's agent runs the relay itself (no second program to start).</summary>
    public bool Serve { get; set; }
    public string ServerName { get; set; } = "Deliver Us";
    public int MaxPlayers { get; set; } = 4;
    public int StatusPort { get; set; } = 1415;
    public int ConsolePort { get; set; } = 4600;
    /// <summary>ask | join | free: what to do when the host's story goes on rails.</summary>
    public string RailsPref { get; set; } = "ask";
    public float TetherMeters { get; set; } = 120f;
    public bool Hotkeys { get; set; } = true;
    /// <summary>True (the default): when a friend opens the game's pause menu, this game is held until they are back (a frame-counted lease lets it go by itself if the friend vanishes). False: nobody's menu holds anybody's world.</summary>
    public bool SharedPause { get; set; } = true;
    /// <summary>True (the default): with a friend in the SAME shared world, damage and deaths of NPCs and the loot of bodies and stashes are shared (see docs/CAPABILITIES.md). False: every copy of the world stays its own.</summary>
    public bool SharedOutcomes { get; set; } = true;
    /// <summary>f11f12 | f9f10 | off: the keys that answer the host's join-or-stay question (the Multiplayer tab's Keys page).</summary>
    public string KeyPreset { get; set; } = KcdUs.Agent.KeyPreset.Default;
    /// <summary>Start doing nothing: the player hosts or joins from the game's Multiplayer tab (the installer's setting).</summary>
    public bool Idle { get; set; }
    /// <summary>The name of the shared world this player starts (Game world page).</summary>
    public string WorldName { get; set; } = "Our world";
    /// <summary>host | mine: whose Henry goes into a world that is received (the Which Henry page).</summary>
    public string HenryMode { get; set; } = "host";
    /// <summary>furthest | host | newest: which copy of a world goes on when two have been played apart (the When we reconnect page).</summary>
    public string ResolvePolicy { get; set; } = "furthest";
    /// <summary>Take a friend's further-along world by itself when the player is at a menu (never while they are in the open world).</summary>
    public bool AutoSync { get; set; } = true;
    /// <summary>Where the game keeps its saves; empty: found automatically.</summary>
    public string SavesDir { get; set; } = "";
    /// <summary>Where the list of shared worlds and the backups of replaced copies are kept; empty: under the player's local application data.</summary>
    public string WorldsFile { get; set; } = "";
    public string BackupDir { get; set; } = "";
    /// <summary>Allow "join" to name this very computer (127.0.0.1): only for testing two agents on one machine.</summary>
    public bool AllowLoopbackJoin { get; set; }
    /// <summary>Development only: a room admits a peer whose mod payload cannot be verified. A real room refuses it.</summary>
    public bool DevAllowUnverifiedPayload { get; set; }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "kcdus-agent.json");

    /// <summary>Where a player's own changes go when the program's folder is not writable (a Program Files install).</summary>
    public static string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDUS", "kcdus-agent.json");

    /// <summary>The settings file: the one next to the exe, or the player's own copy when that is newer (the Multiplayer tab saved there).</summary>
    public static AgentConfig Load(string? path = null)
    {
        try
        {
            if (path is null && File.Exists(UserPath) && (!File.Exists(DefaultPath) || File.GetLastWriteTimeUtc(UserPath) > File.GetLastWriteTimeUtc(DefaultPath))) path = UserPath;
            path ??= DefaultPath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), Json) ?? new AgentConfig();
        }
        catch { }
        return new AgentConfig();
    }

    /// <summary>Saves next to the exe, or in the player's own folder when that is not allowed. Returns where it went (null: nowhere).</summary>
    public string? Save(string? path = null)
    {
        string json = JsonSerializer.Serialize(this, Json);
        foreach (var p in path is null ? new[] { DefaultPath, UserPath } : new[] { path })
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, json); return p; }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        }
        return null;
    }

    /// <summary>--role host --relay 100.64.1.2:7788 --name Henry --game-dir X --password p --serve --status-port 1415 --pref free</summary>
    public static AgentConfig FromArgs(AgentConfig c, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--role" when next is "host" or "guest": c.Role = next; i++; break;
                case "--relay" when next != null:
                    var m = Regex.Match(next, @"^(?<h>.+?)(:(?<p>\d+))?$");
                    c.RelayHost = m.Groups["h"].Value;
                    if (m.Groups["p"].Success) c.RelayPort = int.Parse(m.Groups["p"].Value);
                    i++; break;
                case "--name" when next != null: c.PlayerName = Safe.Name(next); i++; break;
                case "--game-dir" when next != null: c.GameDir = next; i++; break;
                case "--password" when next != null: c.Password = next; i++; break;
                case "--serve": c.Serve = true; break;
                case "--server-name" when next != null: c.ServerName = Safe.Clean(next, 40); i++; break;
                case "--status-port" when next != null: c.StatusPort = int.Parse(next); i++; break;
                case "--pref" when next != null: c.RailsPref = next; i++; break;
                case "--no-hotkeys": c.Hotkeys = false; break;
                case "--shared-pause": c.SharedPause = true; break;
                case "--no-shared-pause": c.SharedPause = false; break;
                case "--shared-outcomes": c.SharedOutcomes = true; break;
                case "--no-shared-outcomes": c.SharedOutcomes = false; break;
                case "--idle": c.Idle = true; break;
            }
        }
        return c;
    }
}

/// <summary>Finds the first game's folder: the setting, then Steam's own library list.</summary>
public static class GameLocator
{
    public const string SteamAppId = "379430";
    public const string FolderName = "KingdomComeDeliverance";

    public static bool LooksLikeTheGame(string? dir) =>
        !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Bin", "Win64", "KingdomCome.exe")) && Directory.Exists(Path.Combine(dir, "Data"));


    public static string? Find(string? configured, string? steamPath = null)
    {
        if (LooksLikeTheGame(configured)) return configured;
        var env = Environment.GetEnvironmentVariable("KCDUS_GAME_DIR");
        if (LooksLikeTheGame(env)) return env;
        foreach (var lib in Libraries(steamPath))
        {
            var d = Path.Combine(lib, "steamapps", "common", FolderName);
            if (LooksLikeTheGame(d)) return d;
        }
        return null;
    }

    /// <summary>Steam's install folder and every library in its libraryfolders.vdf.</summary>
    public static IEnumerable<string> Libraries(string? steamPath = null)
    {
        steamPath ??= SteamInstall();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(steamPath) && seen.Add(steamPath)) yield return steamPath;
        if (string.IsNullOrEmpty(steamPath)) yield break;
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) vdf = Path.Combine(steamPath, "config", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (seen.Add(p)) yield return p;
        }
    }

    /// <summary>Where Steam lives on Linux, most likely first (docs/LINUX.md).</summary>
    public static IEnumerable<string> LinuxSteamCandidates(string home)
    {
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "root");
    }

    public static string? SteamInstall()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                var env = Environment.GetEnvironmentVariable("STEAM_ROOT");
                if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
                string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var c in LinuxSteamCandidates(home))
                    if (Directory.Exists(Path.Combine(c, "steamapps"))) return new DirectoryInfo(c).ResolveLinkTarget(true)?.FullName ?? c;
                return null;
            }
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string s && s.Length > 0) return s.Replace('/', '\\');
            using var k2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            return k2?.GetValue("InstallPath") as string;
        }
        catch { return null; }
    }
}
