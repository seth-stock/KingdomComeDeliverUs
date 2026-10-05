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

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "kcdus-agent.json");

    public static AgentConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), Json) ?? new AgentConfig();
        }
        catch { }
        return new AgentConfig();
    }

    public void Save(string? path = null) => File.WriteAllText(path ?? DefaultPath, JsonSerializer.Serialize(this, Json));

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
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (seen.Add(p)) yield return p;
        }
    }

    public static string? SteamInstall()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string s && s.Length > 0) return s.Replace('/', '\\');
            using var k2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            return k2?.GetValue("InstallPath") as string;
        }
        catch { return null; }
    }
}
