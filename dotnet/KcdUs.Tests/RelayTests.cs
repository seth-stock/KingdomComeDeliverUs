// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using System.Net.Sockets;
using KcdUs.Relay;
using KcdUs.Wire;

namespace KcdUs.Tests;

/// <summary>A bare protocol client for tests: sends frames, reads the next frame of a type.</summary>
internal sealed class TestClient : IAsyncDisposable
{
    private readonly TcpClient _c = new();
    private NetworkStream _s = null!;
    public readonly List<Frame> Seen = new();

    public static async Task<TestClient> Connect(int port)
    {
        var t = new TestClient();
        await t._c.ConnectAsync(IPAddress.Loopback, port);
        t._s = t._c.GetStream();
        return t;
    }

    public Task Send(MessageType type, string text) => FrameIO.WriteAsync(_s, type, text);

    public Task Hello(string name, string role = "guest", string password = "", string? release = null, int proto = Proto.ProtocolVersion) =>
        Send(MessageType.Hello, $"{proto}|{release ?? Release.Current}|{name}|{role}|{password}");

    public async Task<Frame> Next(MessageType type, int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            var f = await FrameIO.ReadAsync(_s, cts.Token) ?? throw new IOException("closed while waiting for " + type);
            Seen.Add(f);
            if (f.Type == type) return f;
        }
    }

    public async Task<bool> Closed(int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (true)
            {
                var f = await FrameIO.ReadAsync(_s, cts.Token);
                if (f is null) return true;
                Seen.Add(f.Value);
            }
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return true; }
    }

    /// <summary>True when no frame of this type arrives within the window.</summary>
    public async Task<bool> Quiet(MessageType type, int windowMs = 300)
    {
        try { await Next(type, windowMs); return false; }
        catch (OperationCanceledException) { return true; }
    }

    public ValueTask DisposeAsync() { _c.Close(); return ValueTask.CompletedTask; }
}

public class RelayTests
{
    private static RelayServer Start(Action<RelayOptions>? tweak = null)
    {
        var o = new RelayOptions { Port = 0, BindAddress = IPAddress.Loopback, ServerName = "Test", Release = "0.1.0", IdleTimeoutSeconds = 30 };
        tweak?.Invoke(o);
        var r = new RelayServer(o);
        r.Start();
        return r;
    }

    private static string Rel => "0.1.0";

    [Fact]
    public async Task Hello_is_answered_with_a_welcome_and_the_player_list()
    {
        await using var relay = Start();
        await using var host = await TestClient.Connect(relay.Port);
        await host.Hello("Henry", "host", release: Rel);
        var w = await host.Next(MessageType.Welcome);
        Assert.Equal(new[] { "1", "1", "1", "0.1.0", "Test" }, w.Fields);
        Assert.Equal("", (await host.Next(MessageType.PlayerList)).Text);

        await using var guest = await TestClient.Connect(relay.Port);
        await guest.Hello("Hans", release: Rel);
        var gw = (await guest.Next(MessageType.Welcome)).Fields;
        Assert.Equal("2", gw[0]);
        Assert.Equal("1", gw[1]); // the host's id
        Assert.Equal("1:Henry:host", (await guest.Next(MessageType.PlayerList)).Text);

        var joined = await host.Next(MessageType.PlayerJoined);
        Assert.Equal("2|Hans|guest", joined.Text);
    }

    [Fact]
    public async Task A_different_release_is_refused_even_with_the_same_protocol_number()
    {
        await using var relay = Start();
        await using var c = await TestClient.Connect(relay.Port);
        await c.Hello("Henry", release: "0.2.0");
        var r = await c.Next(MessageType.Reject);
        Assert.StartsWith("version|", r.Text);
        Assert.Contains("0.1.0", r.Text);
        Assert.True(await c.Closed());
        Assert.Equal(0, relay.PlayerCount);
    }

    [Fact]
    public async Task A_different_protocol_number_is_refused()
    {
        await using var relay = Start();
        await using var c = await TestClient.Connect(relay.Port);
        await c.Hello("Henry", release: Rel, proto: 99);
        Assert.StartsWith("protocol|", (await c.Next(MessageType.Reject)).Text);
    }

    [Fact]
    public async Task A_patch_suffix_does_not_split_a_release()
    {
        await using var relay = Start();
        await using var c = await TestClient.Connect(relay.Port);
        await c.Hello("Henry", release: "0.1.0+abc");
        await c.Next(MessageType.Welcome);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_the_right_one_is_not()
    {
        await using var relay = Start(o => o.Password = "pie");
        await using var bad = await TestClient.Connect(relay.Port);
        await bad.Hello("A", password: "nope", release: Rel);
        Assert.StartsWith("password|", (await bad.Next(MessageType.Reject)).Text);
        await using var good = await TestClient.Connect(relay.Port);
        await good.Hello("B", password: "pie", release: Rel);
        await good.Next(MessageType.Welcome);
    }

    [Fact]
    public async Task There_is_only_one_host()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("B", "host", release: Rel);
        Assert.StartsWith("host-taken|", (await b.Next(MessageType.Reject)).Text);
    }

    [Fact]
    public async Task The_room_has_a_limit()
    {
        await using var relay = Start(o => o.MaxPlayers = 2);
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("B", release: Rel);
        await b.Next(MessageType.Welcome);
        await using var c = await TestClient.Connect(relay.Port);
        await c.Hello("C", release: Rel);
        Assert.StartsWith("full|", (await c.Next(MessageType.Reject)).Text);
    }

    [Fact]
    public async Task Two_players_with_one_name_are_told_apart()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("Henry", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("Henry", release: Rel);
        await b.Next(MessageType.Welcome);
        Assert.Equal("2|Henry (2)|guest", (await a.Next(MessageType.PlayerJoined)).Text);
    }

    [Fact]
    public async Task State_reaches_the_others_and_not_the_sender()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("B", release: Rel);
        await b.Next(MessageType.Welcome);
        await a.Next(MessageType.PlayerJoined);

        var st = new PlayerState(10, 20, 30, 1.5, 1, 2, 0, 3, 90, 100, "MotionIdle", 36000).Encode();
        await b.Send(MessageType.State, st);
        var got = await a.Next(MessageType.PState);
        Assert.Equal("2|" + st, got.Text);
        Assert.True(await b.Quiet(MessageType.PState));
        var back = PlayerState.TryDecode(got.Fields, 1)!;
        Assert.Equal(20, back.Y);
        Assert.True(back.WeaponDrawn && back.Mounted);
    }

    [Fact]
    public async Task A_player_that_floods_state_is_rate_limited()
    {
        await using var relay = Start(o => o.StateMaxPerSecond = 10);
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("B", release: Rel);
        await b.Next(MessageType.Welcome);
        await a.Next(MessageType.PlayerJoined);
        var st = new PlayerState(1, 2, 3, 0, 0, 0, 0, 0, 100, 100, "x", 0).Encode();
        for (int i = 0; i < 100; i++) await b.Send(MessageType.State, st);
        await Task.Delay(300);
        int n = 0;
        while (!await a.Quiet(MessageType.PState, 100)) n++;
        Assert.InRange(n, 8, 14);
    }

    [Fact]
    public async Task Chat_is_cleaned_and_attributed()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("Bob", release: Rel);
        await b.Next(MessageType.Welcome);
        await a.Next(MessageType.PlayerJoined);
        await b.Send(MessageType.Chat, "hi \"there\" | ~ \\ friend");
        var c = await a.Next(MessageType.PChat);
        Assert.Equal("2|Bob|hi  there" + new string(' ', 8) + "friend", c.Text);
    }

    [Fact]
    public async Task Only_the_host_may_speak_with_the_hosts_voice()
    {
        await using var relay = Start();
        await using var h = await TestClient.Connect(relay.Port);
        await h.Hello("H", "host", release: Rel);
        await h.Next(MessageType.Welcome);
        await using var g = await TestClient.Connect(relay.Port);
        await g.Hello("G", release: Rel);
        await g.Next(MessageType.Welcome);
        await h.Next(MessageType.PlayerJoined);

        await g.Send(MessageType.HostEvent, "time|40000");
        Assert.True(await h.Quiet(MessageType.PHostEvent));
        await h.Send(MessageType.HostEvent, "time|40000");
        Assert.Equal("1|time|40000", (await g.Next(MessageType.PHostEvent)).Text);
    }

    [Fact]
    public async Task An_event_kind_must_be_a_word()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await using var b = await TestClient.Connect(relay.Port);
        await b.Hello("B", release: Rel);
        await b.Next(MessageType.Welcome);
        await a.Next(MessageType.PlayerJoined);
        await b.Send(MessageType.Event, "bad kind!|x");
        Assert.True(await a.Quiet(MessageType.PEvent));
        await b.Send(MessageType.Event, "beat|q_skalitz|1");
        Assert.Equal("2|beat|q_skalitz|1", (await a.Next(MessageType.PEvent)).Text);
    }

    [Fact]
    public async Task Leaving_is_announced()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        var b = await TestClient.Connect(relay.Port);
        await b.Hello("B", release: Rel);
        await b.Next(MessageType.Welcome);
        await a.Next(MessageType.PlayerJoined);
        await b.DisposeAsync();
        Assert.Equal("2|left", (await a.Next(MessageType.PlayerLeft)).Text);
        await Task.Delay(100);
        Assert.Equal(1, relay.PlayerCount);
    }

    [Fact]
    public async Task Ping_is_echoed()
    {
        await using var relay = Start();
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        await a.Send(MessageType.Ping, "12345");
        Assert.Equal("12345", (await a.Next(MessageType.Pong)).Text);
    }

    [Fact]
    public async Task Info_needs_no_hello()
    {
        await using var relay = Start();
        await using var h = await TestClient.Connect(relay.Port);
        await h.Hello("Hostman", "host", release: Rel);
        await h.Next(MessageType.Welcome);
        await using var c = await TestClient.Connect(relay.Port);
        await c.Send(MessageType.InfoRequest, "");
        Assert.Equal("Test|1|4|0.1.0|Hostman", (await c.Next(MessageType.Info)).Text);
    }

    [Fact]
    public async Task Traffic_before_hello_is_dropped()
    {
        await using var relay = Start();
        await using var c = await TestClient.Connect(relay.Port);
        await c.Send(MessageType.Chat, "hello?");
        Assert.True(await c.Closed());
    }

    [Fact]
    public async Task A_silent_player_is_dropped()
    {
        await using var relay = Start(o => o.IdleTimeoutSeconds = 1);
        await using var a = await TestClient.Connect(relay.Port);
        await a.Hello("A", "host", release: Rel);
        await a.Next(MessageType.Welcome);
        Assert.True(await a.Closed(4000));
        Assert.Equal(0, relay.PlayerCount);
    }

    [Fact]
    public async Task Frames_round_trip_and_unknown_text_survives()
    {
        var bytes = FrameIO.Encode(MessageType.Chat, "héllo wörld");
        Assert.Equal((byte)MessageType.Chat, bytes[0]);
        using var ms = new MemoryStream(bytes);
        var f = (await FrameIO.ReadAsync(ms))!.Value;
        Assert.Equal("héllo wörld", f.Text);
    }

    [Fact]
    public void Player_state_text_is_stable()
    {
        var s = new PlayerState(733.884, 3421.9, 63.91, -1.868, 2.99, 1.18, 0, 1, 100, 105, "MotionMovement", 36000.4);
        Assert.Equal("733.88,3421.90,63.91|-1.868|2.99,1.18,0.00|1|100|105|MotionMovement|36000", s.Encode());
        var d = PlayerState.TryDecode(s.Encode().Split('|'))!;
        Assert.Equal(733.88, d.X, 2);
        Assert.Equal("MotionMovement", d.Anim);
        Assert.Null(PlayerState.TryDecode(new[] { "1,2", "3" }));
    }

    [Fact]
    public void Safe_removes_what_would_break_a_console_line()
    {
        Assert.Equal("a  b  c d e", Safe.Clean("a\"|b\\~c\nd\te"));
        Assert.Equal("Henry", Safe.Name("  |~ "));
    }
}
