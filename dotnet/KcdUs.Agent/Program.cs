// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using KcdUs.Relay;
using KcdUs.Wire;

namespace KcdUs.Agent;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("KcdUsAgent [--role host|guest] [--relay host[:port]] [--name Henry] [--game-dir <path>] [--password p] [--serve]");
            Console.WriteLine("           [--server-name \"Deliver Us\"] [--status-port 1415] [--pref ask|join|free] [--no-hotkeys] [--idle]");
            Console.WriteLine("KcdUsAgent --build-ui [--game-dir <path>]   (writes the game's Multiplayer tab: Mods/kcdus/Data/kcdus-ui.pak)");
            Console.WriteLine("--idle: start doing nothing; the player hosts or joins from the Multiplayer tab in the game.");
            Console.WriteLine("Settings are read from kcdus-agent.json next to this program; the command line overrides them.");
            return 0;
        }

        var cfg = AgentConfig.FromArgs(AgentConfig.Load(), args);
        if (args.Contains("--build-ui")) return BuildUi(cfg);
        string release = Release.Current;
        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDUS", "logs", "agent.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var logLock = new object();
        void Log(string line)
        {
            var s = $"{DateTime.Now:HH:mm:ss} {line}";
            Console.WriteLine(s);
            lock (logLock) { try { File.AppendAllText(logPath, s + Environment.NewLine); } catch { } }
        }
        try { if (File.Exists(logPath) && new FileInfo(logPath).Length > 2_000_000) File.Delete(logPath); } catch { }

        Log($"Kingdom Come: Deliver Us agent {release} (protocol {Proto.ProtocolVersion}), role {cfg.Role}");

        string? gameDir = GameLocator.Find(cfg.GameDir);
        if (gameDir is null)
        {
            Log("ERROR: Kingdom Come: Deliverance was not found. Set gameDir in kcdus-agent.json or pass --game-dir.");
            return 2;
        }
        Log("game folder: " + gameDir);
        EnsureMenuTab(gameDir, Log);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var game = new ConsoleGameLink(gameDir, "127.0.0.1", cfg.ConsolePort, null, Log);
        await using var host = new AgentHost(cfg, game, release, cfg.StatusPort, Log, () => cts.Cancel()) { GameDir = gameDir };

        await using var status = new StatusServer(() => host.Session, cfg.StatusPort, () => cts.Cancel(), host);
        try { status.Start(); Log($"status on http://127.0.0.1:{cfg.StatusPort}/status"); }
        catch (Exception e) { Log($"warning: the status port {cfg.StatusPort} is not available ({e.Message}); the launcher and the settings page cannot see this agent"); }

        game.Start(cts.Token);
        await host.StartAsync(cts.Token);

        if (cfg.Hotkeys)
        {
            // Windows: GetAsyncKeyState while the game window is in front. Linux: the kernel's input devices (LinuxKeys).
            LinuxKeys? lk = null;
            Hotkeys hk;
            if (OperatingSystem.IsWindows()) hk = new Hotkeys();
            else
            {
                lk = new LinuxKeys();
                lk.Start(Log);
                hk = new Hotkeys(() => true) { IsDown = lk.IsDownVk };
            }
            hk.Join += () => host.Session.Choose(RailsChoice.Join, hk.JoinName);
            hk.Stay += () => host.Session.Choose(RailsChoice.Free, hk.StayName);
            host.Keys = hk;
            host.ApplyKeys();
            _ = Task.Run(() => hk.RunAsync(cts.Token));
        }

        string last = "";
        while (!cts.IsCancellationRequested)
        {
            host.Tick();
            var m = host.GetStatus().Message;
            if (m != last) { Log("status: " + m); last = m; }
            try { await Task.Delay(100, cts.Token); } catch { break; }
        }

        Log("stopping");
        return 0;
    }

    /// <summary>The tab is made from the player's own menu files: after a game update or a new mod version it is made again, by itself (the game must be restarted to show it).</summary>
    private static void EnsureMenuTab(string gameDir, Action<string> log)
    {
        string src = Path.Combine(gameDir, "Data", "GameData.pak"), dst = Path.Combine(gameDir, "Mods", "kcdus", "Data", Ui.MenuUi.PakName);
        if (!Directory.Exists(Path.Combine(gameDir, "Mods", "kcdus")) || Ui.MenuUi.IsCurrent(src, dst)) return;
        var r = Ui.MenuUi.Build(src, dst);
        log("menu tab: " + r.Message + (r.Ok ? " (restart the game to see it)" : ""));
    }

    /// <summary>--build-ui: the game's Multiplayer tab is a patch of the player's own menu files, made here at install time (docs/MENU.md).</summary>
    private static int BuildUi(AgentConfig cfg)
    {
        string? gameDir = GameLocator.Find(cfg.GameDir);
        if (gameDir is null) { Console.Error.WriteLine("Kingdom Come: Deliverance was not found. Pass --game-dir <path>."); return 2; }
        var r = Ui.MenuUi.Build(Path.Combine(gameDir, "Data", "GameData.pak"), Path.Combine(gameDir, "Mods", "kcdus", "Data", Ui.MenuUi.PakName));
        Console.WriteLine(r.Message);
        return r.Ok ? 0 : 1;
    }
}
