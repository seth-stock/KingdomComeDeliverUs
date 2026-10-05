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
            Console.WriteLine("           [--server-name \"Deliver Us\"] [--status-port 1415] [--pref ask|join|free] [--no-hotkeys]");
            Console.WriteLine("Settings are read from kcdus-agent.json next to this program; the command line overrides them.");
            return 0;
        }

        var cfg = AgentConfig.FromArgs(AgentConfig.Load(), args);
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

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        RelayServer? relayServer = null;
        if (cfg.Serve)
        {
            relayServer = new RelayServer(new RelayOptions { Port = cfg.RelayPort, ServerName = cfg.ServerName, Password = cfg.Password, MaxPlayers = cfg.MaxPlayers, Release = release },
                l => Log("relay: " + l));
            try { relayServer.Start(); }
            catch (Exception e) { Log($"ERROR: the relay cannot listen on port {cfg.RelayPort}: {e.Message}"); return 2; }
            cfg.RelayHost = "127.0.0.1";
        }

        await using var game = new ConsoleGameLink(gameDir, "127.0.0.1", cfg.ConsolePort, null, Log);
        await using var relay = new RelayClient(new RelayEndpoint { Host = cfg.RelayHost, Port = cfg.RelayPort, Name = cfg.PlayerName, Role = cfg.Role, Password = cfg.Password, Release = release });
        var session = new Session(new SessionOptions
        {
            Role = cfg.Role,
            PlayerName = cfg.PlayerName,
            Pref = RailsRules.ParsePref(cfg.RailsPref) ?? RailsPref.Ask,
            TetherMeters = cfg.TetherMeters,
            GameVersion = release,
        }, game, relay, () => Environment.TickCount64, Log);

        await using var status = new StatusServer(session, cfg.StatusPort, () => cts.Cancel());
        try { status.Start(); Log($"status on http://127.0.0.1:{cfg.StatusPort}/status"); }
        catch (Exception e) { Log($"warning: the status port {cfg.StatusPort} is not available ({e.Message}); the launcher cannot see this agent"); }

        game.Start(cts.Token);
        relay.Start(cts.Token);

        if (cfg.Hotkeys)
        {
            var hk = new Hotkeys();
            hk.Join += () => session.Choose(RailsChoice.Join, "F11");
            hk.Stay += () => session.Choose(RailsChoice.Free, "F12");
            _ = Task.Run(() => hk.RunAsync(cts.Token));
        }

        string last = "";
        while (!cts.IsCancellationRequested)
        {
            session.Tick();
            var m = session.GetStatus().Message;
            if (m != last) { Log("status: " + m); last = m; }
            try { await Task.Delay(100, cts.Token); } catch { break; }
        }

        Log("stopping");
        if (relayServer != null) await relayServer.DisposeAsync();
        return 0;
    }
}
