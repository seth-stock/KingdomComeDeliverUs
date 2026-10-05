// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using KcdUs.Agent;
using KcdUs.Relay;
using KcdUs.Wire;

namespace KcdUs.Tests;

internal sealed class FakeClock { public long Ms = 1_000_000; public long Now() => Ms; }

/// <summary>A game that is only a list of what the agent sent it, and a way to say what the game's mod "logged".</summary>
internal sealed class FakeGame : IGameLink
{
    public readonly List<string> Sent = new();
    public bool ConsoleConnected { get; set; } = true;
    public event Action<GameLine>? Line;
    public event Action<bool>? ConsoleStateChanged;
    public void Send(string record) { lock (Sent) Sent.Add(record); }
    public void SendLatest(string key, string record) { lock (Sent) Sent.Add(record); }
    public void Start(CancellationToken ct) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Emit(string line) => Line?.Invoke(GameLine.Parse(line)!.Value);
    public void ConsoleUp(bool up) { ConsoleConnected = up; ConsoleStateChanged?.Invoke(up); }
    public List<string> Snapshot() { lock (Sent) return Sent.ToList(); }
    public bool Has(string prefix) => Snapshot().Any(s => s.StartsWith(prefix));
    public string? Last(string prefix) => Snapshot().LastOrDefault(s => s.StartsWith(prefix));
    public int Count(string prefix) => Snapshot().Count(s => s.StartsWith(prefix));
    public void Clear() { lock (Sent) Sent.Clear(); }
}

/// <summary>One player's whole stack: a fake game, the real relay client, the session.</summary>
internal sealed class Player : IAsyncDisposable
{
    public readonly FakeGame Game = new();
    public readonly RelayClient Relay;
    public readonly Session Session;
    public readonly FakeClock Clock;
    private readonly CancellationTokenSource _cts = new();

    public Player(int port, string name, string role, FakeClock clock, RailsPref pref = RailsPref.Ask, string? release = null, string password = "")
    {
        Clock = clock;
        Relay = new RelayClient(new RelayEndpoint { Port = port, Name = name, Role = role, Release = release ?? "0.1.0", Password = password });
        Session = new Session(new SessionOptions { Role = role, PlayerName = name, Pref = pref, GameVersion = "0.1.0" }, Game, Relay, clock.Now);
        Relay.Start(_cts.Token);
    }

    public static string St(double x, double y, double z, double yaw = 0, double vx = 0, double vy = 0, int flags = 0, double wt = 36000, string anim = "MotionIdle") =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"KCDUS|ST|{x:0.00},{y:0.00},{z:0.00}|{yaw:0.000}|{vx:0.00},{vy:0.00},0.00|{flags}|100|105|{anim}|{wt:0}");

    public void EnterWorld() { Game.Emit("KCDUS|HELLO|0.1.0|1|pak"); Game.Emit("KCDUS|READY|1"); }
    public void Sample(double x, double y, double z, double wt = 36000, int flags = 0) => Game.Emit(St(x, y, z, wt: wt, flags: flags));
    public void Tick() => Session.Tick();

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Relay.DisposeAsync();
    }
}

public class SessionTests
{
    private static RelayServer StartRelay()
    {
        var r = new RelayServer(new RelayOptions { Port = 0, BindAddress = IPAddress.Loopback, ServerName = "Test", Release = "0.1.0", IdleTimeoutSeconds = 30 });
        r.Start();
        return r;
    }

    private static async Task Until(Func<bool> cond, int ms = 3000, string what = "condition")
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            if (cond()) return;
            await Task.Delay(10);
        }
        Assert.True(cond(), "timed out waiting for " + what);
    }

    private static async Task<(RelayServer relay, Player host, Player guest, FakeClock clock)> Pair(RailsPref pref = RailsPref.Ask)
    {
        var relay = StartRelay();
        var clock = new FakeClock();
        var host = new Player(relay.Port, "Henry", "host", clock);
        await Until(() => host.Session.MyId != 0, what: "host welcome");
        var guest = new Player(relay.Port, "Hans", "guest", clock, pref);
        await Until(() => guest.Session.MyId != 0 && host.Session.Peers.Count == 1, what: "guest welcome");
        host.EnterWorld(); guest.EnterWorld();
        return (relay, host, guest, clock);
    }

    [Fact]
    public async Task Players_see_each_other_and_are_told_who_joined()
    {
        var (relay, host, guest, _) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        Assert.Contains("NOTE|Hans joined", host.Game.Snapshot());
        host.Sample(100, 200, 30);
        await Until(() => guest.Game.Has("P|1|Henry|"), what: "the host's position at the guest");
        var rec = guest.Game.Last("P|1|Henry|")!.Split('|');
        Assert.Equal("100.00,200.00,30.00", rec[3]);
        guest.Sample(110, 210, 31);
        await Until(() => host.Game.Has("P|2|Hans|"), what: "the guest's position at the host");
    }

    [Fact]
    public async Task Chat_travels_both_ways_and_is_cleaned()
    {
        var (relay, host, guest, _) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        guest.Game.Emit("KCDUS|CHAT|hello there Henry");
        await Until(() => host.Game.Has("CHAT|Hans|"), what: "chat at the host");
        Assert.Equal("CHAT|Hans|hello there Henry", host.Game.Last("CHAT|")!);
        host.Session.Say("welcome \"friend\"");
        await Until(() => guest.Game.Has("CHAT|Henry|"), what: "chat at the guest");
        Assert.Equal("CHAT|Henry|welcome  friend", guest.Game.Last("CHAT|Henry")!);
        Assert.Contains("CHAT|You|welcome  friend", host.Game.Snapshot());
    }

    [Fact]
    public async Task A_player_who_leaves_the_world_or_the_room_disappears_from_the_others_game()
    {
        var (relay, host, guest, _) = await Pair();
        await using var _r = relay; await using var _h = host;
        guest.Sample(1, 2, 3);
        await Until(() => host.Game.Has("P|2|"), what: "ghost");
        guest.Game.Emit("KCDUS|READY|0");                       // the guest went to the main menu
        await Until(() => host.Game.Has("PD|2"), what: "ghost removed on world|0");
        host.Game.Clear();
        guest.Game.Emit("KCDUS|READY|1");
        guest.Sample(1, 2, 3);
        await Until(() => host.Game.Has("P|2|"), what: "ghost back");
        await guest.DisposeAsync();
        await Until(() => host.Game.Count("PD|2") >= 1 && host.Game.Has("NOTE|Hans left"), what: "left");
        Assert.Empty(host.Session.Peers);
    }

    [Fact]
    public async Task The_hosts_clock_is_applied_to_a_guest_that_has_drifted_and_not_to_one_that_has_not()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(0, 0, 0, wt: 50000);
        guest.Sample(0, 0, 0, wt: 36000);
        clock.Ms += 6000; host.Sample(0, 0, 0, wt: 50000); host.Tick();
        await Until(() => guest.Game.Has("TIME|"), what: "the shared clock");
        Assert.Equal("TIME|50000", guest.Game.Last("TIME|")!);

        guest.Game.Clear();
        guest.Sample(0, 0, 0, wt: 50040);       // 40 game-seconds apart: ordinary drift
        clock.Ms += 25_000; host.Sample(0, 0, 0, wt: 50000); host.Tick();
        await Task.Delay(300);
        Assert.False(guest.Game.Has("TIME|"));
    }

    [Fact]
    public async Task The_host_entering_rails_asks_the_friend_and_a_yes_brings_them()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40, 36000);
        guest.Sample(10, 10, 10);
        clock.Ms += 7000; host.Tick();                                   // the quest-log baseline window is over
        host.Game.Emit("KCDUS|Q|q_escapeToTalmberk|1|0|31");              // Run!: a main rails quest
        await Until(() => guest.Game.Has("PROMPT|"), what: "the question");
        Assert.Contains("F11", guest.Game.Last("PROMPT|")!);
        Assert.Contains("Skalitz", guest.Game.Last("PROMPT|")!);

        guest.Game.Emit("KCDUS|KEY|join");
        await Until(() => guest.Game.Has("TP|"), what: "being brought");
        Assert.StartsWith("TP|503.10,600.00,40.00", guest.Game.Last("TP|")!);   // beside the host: 1.5 m east, and a little apart per player id
        Assert.Contains("NOTE|You joined your host.", guest.Game.Snapshot());
        Assert.Contains("PROMPTCLEAR", guest.Game.Snapshot());
        await Until(() => host.Game.Has("NOTE|Hans joined you"), what: "the host told");
        Assert.Equal(RailsChoice.Join, host.Session.Peers.Single().Choice);
    }

    [Fact]
    public async Task A_friend_who_stays_is_not_brought_and_not_tethered()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40);
        guest.Sample(10, 10, 10);
        clock.Ms += 7000; host.Tick();
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");
        await Until(() => guest.Game.Has("PROMPT|"), what: "the question");
        guest.Game.Emit("KCDUS|KEY|stay");
        await Until(() => host.Game.Has("NOTE|Hans stays in the open world"), what: "the host told");
        Assert.False(guest.Game.Has("TP|"));
        // the host moves on and the friend is a kilometre away: still left alone
        for (int i = 0; i < 5; i++)
        {
            clock.Ms += 1500; host.Sample(500 + i, 600, 40); guest.Sample(1500, 1600, 10); host.Tick(); guest.Tick();
        }
        await Task.Delay(200);
        Assert.False(guest.Game.Has("TP|"));
        // F11 joins at any time
        guest.Game.Emit("KCDUS|KEY|join");
        await Until(() => guest.Game.Has("TP|"), what: "joining late");
    }

    [Fact]
    public async Task A_friend_who_joined_a_rails_section_is_warned_and_then_pulled_back_at_the_tether()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40);
        guest.Sample(500, 600, 40);
        clock.Ms += 7000; host.Tick();
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");
        await Until(() => guest.Game.Has("PROMPT|"), what: "the question");
        guest.Game.Emit("KCDUS|KEY|join");
        await Until(() => guest.Game.Has("TP|"), what: "joined");
        guest.Game.Clear();

        clock.Ms += 1500; host.Sample(500, 600, 40); guest.Sample(500 + 100, 600, 40); guest.Tick();   // 100 m: inside the warning band (90 to 120)
        await Task.Delay(100);
        Assert.Contains("NOTE|You are straying from your host", guest.Game.Snapshot());
        Assert.False(guest.Game.Has("TP|"));
        clock.Ms += 1500; host.Sample(500, 600, 40); guest.Sample(500 + 150, 600, 40); guest.Tick();   // 150 m: past the tether
        await Task.Delay(100);
        Assert.True(guest.Game.Has("TP|"));
        Assert.Contains("NOTE|Your host is on rails: you were brought back to them", guest.Game.Snapshot());
    }

    [Fact]
    public async Task A_mixed_quest_asks_but_never_tethers()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40);
        guest.Sample(500, 600, 40);
        clock.Ms += 7000; host.Tick();
        for (int i = 1; i <= 3; i++) host.Game.Emit($"KCDUS|Q|q_massacre|1|0|{string.Join(',', Enumerable.Range(1, i))}");   // The Hunt Begins: mixed, three objectives
        await Until(() => guest.Game.Has("PROMPT|"), what: "the question for a mixed quest");
        guest.Game.Emit("KCDUS|KEY|join");
        await Until(() => guest.Game.Has("TP|"), what: "joined");
        guest.Game.Clear();
        clock.Ms += 1500; host.Sample(500, 600, 40); guest.Sample(900, 600, 40); guest.Tick();
        await Task.Delay(100);
        Assert.False(guest.Game.Has("TP|"));
    }

    [Fact]
    public async Task No_answer_counts_as_joining_once_and_is_not_repeated_every_second()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40); guest.Sample(10, 10, 10);
        clock.Ms += 7000; host.Tick();
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");
        await Until(() => guest.Game.Has("PROMPT|"), what: "the question");
        for (int i = 0; i < 60; i++) { clock.Ms += 1000; host.Sample(500, 600, 40); await Task.Delay(40); guest.Tick(); }   // a minute of silence (the host keeps reporting, inside the relay's 30 per second)
        await Until(() => guest.Game.Has("TP|"), what: "the default join");
        await Task.Delay(300);
        Assert.Equal(1, guest.Game.Count("TP|"));
        Assert.Equal(1, guest.Game.Snapshot().Count(s => s == "NOTE|You joined your host."));
        Assert.Equal(RailsChoice.Join, guest.Session.GetStatus().Story.MyChoice == "join" ? RailsChoice.Join : RailsChoice.Pending);
    }

    [Fact]
    public async Task A_standing_answer_does_not_ask()
    {
        var (relay, host, guest, clock) = await Pair(RailsPref.Free);
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40); guest.Sample(10, 10, 10);
        clock.Ms += 7000; host.Tick();
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");
        await Until(() => host.Game.Has("NOTE|Hans stays in the open world"), what: "the standing answer reached the host");
        Assert.False(guest.Game.Has("PROMPT|"));
        Assert.False(guest.Game.Has("TP|"));
    }

    [Fact]
    public async Task The_quest_log_the_host_already_had_is_not_news()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40); guest.Sample(10, 10, 10);
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");                // inside the first seconds after the world loaded: the snapshot
        await Task.Delay(200);
        Assert.False(guest.Game.Has("PROMPT|"));
        // ...but a host who loaded INTO a rails stretch of the main story is asked once the baseline is over
        clock.Ms += 7000; host.Tick(); clock.Ms += 1100; host.Tick();
        await Until(() => guest.Game.Has("PROMPT|"), what: "seeded rails section");
    }

    [Fact]
    public async Task A_friends_quests_never_decide_what_the_host_is_asked()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        clock.Ms += 7000; guest.Tick();
        guest.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");
        guest.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7,8");
        await Task.Delay(300);
        Assert.False(host.Game.Has("PROMPT|"));
        Assert.Equal("", guest.Session.GetStatus().Story.Section);
    }

    [Fact]
    public async Task The_period_ends_and_the_friend_is_told_when_the_host_leaves_the_section()
    {
        var (relay, host, guest, clock) = await Pair();
        await using var _r = relay; await using var _h = host; await using var _g = guest;
        host.Sample(500, 600, 40); guest.Sample(10, 10, 10);
        clock.Ms += 7000; host.Tick();
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|0|7");
        await Until(() => guest.Game.Has("PROMPT|"), what: "question");
        guest.Game.Emit("KCDUS|KEY|stay");
        host.Game.Emit("KCDUS|Q|q_pribBattle|1|1|");                    // the battle ends
        await Until(() => guest.Session.GetStatus().Story.Section != "" , 500, "section").ContinueWith(_ => { });
        clock.Ms += 9000; guest.Tick();                                   // past the 8 s grace
        await Until(() => guest.Game.Has("NOTE|Your host's story stretch is over"), what: "the end of the stretch");
    }

    [Fact]
    public async Task A_relay_that_refuses_the_version_is_reported_not_retried()
    {
        var relay = StartRelay();
        await using var _r = relay;
        var clock = new FakeClock();
        await using var p = new Player(relay.Port, "Hans", "guest", clock, release: "9.9.9");
        await Until(() => p.Session.GetStatus().Refused.StartsWith("version"), what: "the refusal");
        Assert.Contains("refused", p.Session.GetStatus().Message);
        Assert.False(p.Relay.Connected);
    }

    [Fact]
    public async Task The_status_says_what_is_wrong_in_the_order_a_player_can_fix_it()
    {
        var relay = StartRelay();
        await using var _r = relay;
        var clock = new FakeClock();
        var p = new Player(relay.Port, "Henry", "host", clock);
        await using var _p = p;
        p.Game.ConsoleUp(false);
        Assert.Contains("Waiting for the game", p.Session.GetStatus().Message);
        p.Game.ConsoleUp(true);
        Assert.Contains("did not answer", p.Session.GetStatus().Message);
        p.Game.Emit("KCDUS|HELLO|0.1.0|1|pak");
        Assert.Contains("main menu", p.Session.GetStatus().Message);
        await Until(() => p.Session.MyId != 0, what: "welcome");
        p.Game.Emit("KCDUS|READY|1");
        Assert.Contains("Connected as host", p.Session.GetStatus().Message);
    }

    [Fact]
    public async Task The_agent_asks_the_game_to_introduce_itself_when_the_console_comes_up_and_keeps_pinging()
    {
        var relay = StartRelay();
        await using var _r = relay;
        var clock = new FakeClock();
        await using var p = new Player(relay.Port, "Henry", "host", clock);
        p.Game.ConsoleUp(true);
        Assert.Contains("HELLO?", p.Game.Snapshot());
        clock.Ms += 2100; p.Tick();
        Assert.True(p.Game.Has("PING|"));
    }

    [Fact]
    public async Task A_mod_of_another_version_is_reported_in_the_log_not_silently_trusted()
    {
        var relay = StartRelay();
        await using var _r = relay;
        var logs = new List<string>();
        var game = new FakeGame();
        var s = new Session(new SessionOptions { GameVersion = "0.1.0" }, game, new RelayClient(new RelayEndpoint { Port = 1 }), () => 0, logs.Add);
        game.Emit("KCDUS|HELLO|0.0.9|1|pak");
        Assert.Contains(logs, l => l.Contains("WARNING") && l.Contains("0.0.9"));
        Assert.Equal("0.0.9", s.GetStatus().GameModVersion);
    }
}

public class CommandPackerTests
{
    [Fact]
    public void Records_are_packed_in_order_under_the_line_limit_with_a_sequence_number()
    {
        var p = new CommandPacker();
        p.Add("NOTE|one"); p.Add("NOTE|two");
        Assert.Equal("kcdus 1~NOTE|one~NOTE|two", p.Next());
        Assert.Null(p.Next());
        for (int i = 0; i < 100; i++) p.Add("CHAT|Hans|" + new string('x', 80));
        int lines = 0, records = 0;
        while (p.Next() is { } cmd)
        {
            Assert.True(cmd.Length <= CommandPacker.MaxCommandChars, "line of " + cmd.Length);
            Assert.StartsWith("kcdus ", cmd);
            records += cmd.Split('~').Length - 1;
            lines++;
        }
        Assert.Equal(100, records);
        Assert.InRange(lines, 5, 8);
        Assert.Equal(lines + 1, p.LastSequence);
    }

    [Fact]
    public void A_players_latest_position_replaces_the_one_waiting_and_keeps_its_place()
    {
        var p = new CommandPacker();
        p.AddLatest("P2", "P|2|Hans|1");
        p.AddLatest("P3", "P|3|Anna|1");
        p.AddLatest("P2", "P|2|Hans|2");
        Assert.Equal("kcdus 1~P|2|Hans|2~P|3|Anna|1", p.Next());
    }

    [Fact]
    public void Events_go_before_positions_and_a_flood_drops_the_oldest_event()
    {
        var p = new CommandPacker();
        p.AddLatest("P2", "P|2|Hans|1");
        p.Add("NOTE|first");
        Assert.Equal("kcdus 1~NOTE|first~P|2|Hans|1", p.Next());
        for (int i = 0; i < CommandPacker.MaxQueued + 25; i++) p.Add("NOTE|" + i);
        Assert.Equal(25, p.Dropped);
        Assert.Equal(CommandPacker.MaxQueued, p.Queued);
    }

    [Fact]
    public void One_record_longer_than_the_limit_is_still_sent_alone()
    {
        var p = new CommandPacker();
        p.Add("NOTE|" + new string('y', 2500));
        Assert.True(p.Next()!.Length > CommandPacker.MaxCommandChars);   // a single record cannot be split; the mod's own limit is 3000
    }
}

public class LogTailTests
{
    [Fact]
    public void Complete_lines_are_delivered_once_and_a_partial_line_waits()
    {
        var path = Path.Combine(Path.GetTempPath(), "kcdus-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, "old line\n");
            var tail = new LogTail(path);                       // starts at the end: history is not replayed
            var got = new List<string>();
            tail.LineRead += got.Add;
            Assert.Equal(0, tail.Poll());
            File.AppendAllText(path, "KCDUS|HB|1|60|10|0\nplain\nKCDUS|ST|1,2,3|0|0,0,0|0|100|1|x|3");   // the last has no newline yet
            Assert.Equal(2, tail.Poll());
            File.AppendAllText(path, "6\n");
            Assert.Equal(1, tail.Poll());
            Assert.Equal(new[] { "KCDUS|HB|1|60|10|0", "plain", "KCDUS|ST|1,2,3|0|0,0,0|0|100|1|x|36" }, got);
            Assert.Equal(0, tail.Poll());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_new_game_start_truncates_the_file_and_the_tail_starts_over()
    {
        var path = Path.Combine(Path.GetTempPath(), "kcdus-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, new string('a', 500) + "\n");
            var tail = new LogTail(path);
            var got = new List<string>();
            tail.LineRead += got.Add;
            tail.Poll();
            File.WriteAllText(path, "KCDUS|HELLO|0.1.0|1|pak\n");   // the game began again, the file is shorter
            Assert.Equal(1, tail.Poll());
            Assert.Equal("KCDUS|HELLO|0.1.0|1|pak", got[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_log_is_not_an_error()
    {
        var tail = new LogTail(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".log"));
        Assert.Equal(0, tail.Poll());
    }

    [Fact]
    public void Lines_are_found_even_with_a_prefix_and_ignored_when_they_are_not_ours()
    {
        Assert.Equal("ST", GameLine.Parse("[12:00:01] KCDUS|ST|1,2,3|0")!.Value.Kind);
        Assert.Null(GameLine.Parse("CryGFxFileOpener::OpenFile(), 'Libs/UI//ico.gfx'"));
        Assert.Null(GameLine.Parse("KCDUS|"));
        var q = GameLine.Parse("KCDUS|Q|q_skalitz|1|0|5,12")!.Value;
        Assert.Equal(new[] { "Q", "q_skalitz", "1", "0", "5,12" }, q.Fields);
    }
}

public class RemoteConsoleClientTests
{
    [Fact]
    public async Task A_line_goes_out_as_5_text_nul_and_the_engines_chatter_is_ignored()
    {
        var server = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource();
        var client = new RemoteConsoleClient("127.0.0.1", port);
        var run = Task.Run(() => client.RunAsync(cts.Token));
        using var conn = await server.AcceptTcpClientAsync();
        var s = conn.GetStream();
        await s.WriteAsync(new byte[] { (byte)'1', 0 });           // the engine's hello
        await s.WriteAsync(System.Text.Encoding.ASCII.GetBytes("6map rataje\0"));
        var end = DateTime.UtcNow.AddSeconds(3);
        while (!client.Connected && DateTime.UtcNow < end) await Task.Delay(10);
        Assert.True(client.Connected);
        Assert.True(client.TrySend("kcdus 1~PING|7"));
        var buf = new byte[64];
        int n = await s.ReadAsync(buf);
        Assert.Equal("5kcdus 1~PING|7\0", System.Text.Encoding.UTF8.GetString(buf, 0, n));
        cts.Cancel();
        server.Stop();
        await run;
        Assert.False(client.Connected);
    }
}
