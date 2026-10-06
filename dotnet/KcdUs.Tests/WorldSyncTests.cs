// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using System.Net.Sockets;
using KcdUs.Agent;
using KcdUs.Agent.Worlds;
using Xunit;

namespace KcdUs.Tests;

/// <summary>
/// Two players, each with their own copy of a shared world, meet over a real relay on loopback (docs/SHARED-WORLDS.md). The "game" is a fake that answers
/// SAVEWORLD by writing a save and LOAD by remembering it; saves are real files in temporary save folders. (synthetic game, real relay, real files)
/// </summary>
public class WorldSyncTests
{
    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

    private sealed class Rig : IAsyncDisposable
    {
        public readonly FakeGame Game = new();
        public readonly AgentHost Host;
        public readonly SaveStore Store;
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "kcdus-sync-" + Guid.NewGuid().ToString("N")[..8]);
        public readonly string WorldsJson;
        public readonly List<string> Logs = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public Rig(string name, int relayPort, Action<AgentConfig>? tweak = null, Action<Rig>? seed = null)
        {
            Directory.CreateDirectory(Dir);
            Store = new SaveStore(Path.Combine(Dir, "saves"), Path.Combine(Dir, "backups"));
            Directory.CreateDirectory(Store.Root);
            WorldsJson = Path.Combine(Dir, "worlds.json");
            seed?.Invoke(this);
            var cfg = new AgentConfig { Idle = true, RelayPort = relayPort, PlayerName = name, RelayHost = "127.0.0.1" };
            tweak?.Invoke(cfg);
            Host = new AgentHost(cfg, Game, "0.1.0", FreePort(), l => { lock (Logs) Logs.Add(l); }) { SavePath = Path.Combine(Dir, "cfg.json"), OpenUrl = _ => { }, Store = Store, WorldListPath = WorldsJson, AllowLoopbackJoin = true };
            // the fake game: a save request writes a save into the playline in use; a load request is only remembered
            Game.OnSend = r =>
            {
                if (r.StartsWith("SAVEWORLD|"))
                {
                    long unix = 1_799_000_000L + (++_saves);
                    WriteSave(CurrentPlayline, "autosave" + (100 + _saves) + ".whs", unix, CurrentHours + 0.5);
                    CurrentHours += 0.5;
                    Game.Emit("KCDUS|SAVEWORLD|" + r.Split('|')[1] + "|1|");
                }
            };
            Host.StartAsync(_cts.Token).GetAwaiter().GetResult();
            _pump = Task.Run(async () => { while (!_cts.IsCancellationRequested) { try { Host.Tick(); await Task.Delay(100, _cts.Token); } catch { } } });
        }

        private int _saves;
        public int CurrentPlayline = 0;
        public double CurrentHours = 1;

        public void WriteSave(int playline, string file, long unix, double hours)
        {
            var dir = Store.PlaylineDir(playline);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, file), WorldTests.FakeSave(WorldTests.Desc(unix, hours), seed: (int)(unix % 1000) + 1));
        }

        public void Seed(string worldId, int playline, double hours, long unix, string name = "Our world", bool slot = false)
        {
            WriteSave(playline, "permanent001.whs", unix, hours);
            var r = File.Exists(WorldsJson) ? WorldRegistry.Load(WorldsJson) : new WorldRegistry();
            var w = r.Upsert(worldId, name); w.Playline = playline; w.Slot = slot; w.Hours = hours; w.SavedUnix = unix; r.Active = worldId;
            r.Save(WorldsJson);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _pump; } catch { }
            await Host.DisposeAsync();
            try { Directory.Delete(Dir, true); } catch { }
        }
    }

    private static async Task Until(Func<bool> cond, int ms = 8000, string what = "condition", params Rig[] rigs)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { if (cond()) return; await Task.Delay(20); }
        string logs = string.Join("\n", rigs.SelectMany((r, i) => { lock (r.Logs) return r.Logs.Select(l => i + ": " + l).ToList(); }));
        Assert.True(cond(), "timed out waiting for " + what + "\n" + logs);
    }

    [Fact]
    public async Task The_player_who_is_behind_loads_the_other_players_world()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, seed: r => r.Seed("w1", 4, 3, 1_790_000_100, slot: true));
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");

        await Until(() => guest.Game.Has("LOAD|0|4"), what: "the guest's game is told to load its (replaced) slot 4", rigs: new[] { host, guest });
        var now = guest.Store.Newest(4)!;
        Assert.Equal(10, now.Info.Hours, 3);                                           // the host's world is what is there now
        Assert.True(Directory.GetFiles(Path.Combine(guest.Store.BackupRoot), "*.whs", SearchOption.AllDirectories).Length >= 1);   // and his own copy was kept
        Assert.Equal(10, WorldRegistry.Load(guest.WorldsJson).Find("w1")!.Hours, 3);
        Assert.False(host.Game.Has("LOAD|"));                                         // the host, who was ahead, loads nothing
    }
}
