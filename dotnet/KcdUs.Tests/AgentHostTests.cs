// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using KcdUs.Agent;
using Xunit;

namespace KcdUs.Tests;

/// <summary>The agent side of the game's Multiplayer tab (docs/MENU.md): menu lines from the game become host / join / leave / settings. (synthetic: a fake game, real sockets on loopback)</summary>
public class AgentHostTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private sealed class Rig : IAsyncDisposable
    {
        public readonly FakeGame Game = new();
        public readonly AgentHost Host;
        public readonly List<string> Opened = new();
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "kcdus-host-" + Guid.NewGuid().ToString("N")[..8]);
        public readonly int StatusPort = FreePort();
        private readonly CancellationTokenSource _cts = new();

        public Rig(Action<AgentConfig>? tweak = null)
        {
            Directory.CreateDirectory(Dir);
            var cfg = new AgentConfig { Idle = true, RelayPort = FreePort(), PlayerName = "Henry", DevAllowUnverifiedPayload = true };
            tweak?.Invoke(cfg);
            Host = new AgentHost(cfg, Game, "0.1.0", StatusPort, _ => { })
            {
                SavePath = Path.Combine(Dir, "cfg.json"), OpenUrl = u => { lock (Opened) Opened.Add(u); },
                Store = new KcdUs.Agent.Worlds.SaveStore(Path.Combine(Dir, "saves"), Path.Combine(Dir, "backups")),
                WorldListPath = Path.Combine(Dir, "worlds.json"),
                IdentityFile = Path.Combine(Dir, "participant.key"), BindingsFile = Path.Combine(Dir, "room-bindings.txt")
            };
            Host.StartAsync(_cts.Token).GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await Host.DisposeAsync();
            try { Directory.Delete(Dir, true); } catch { }
        }
    }

    private static async Task Until(Func<bool> cond, int ms = 8000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { if (cond()) return; await Task.Delay(10); }
        Assert.True(cond(), "timed out");
    }

    [Fact]
    public async Task An_idle_agent_is_in_no_session_and_says_so()
    {
        await using var r = new Rig();
        Assert.Equal(AgentHost.Idle, r.Host.Mode);
        Assert.Contains("not in a session", r.Host.Describe());
    }

    [Fact]
    public async Task A_menu_line_from_the_game_is_a_command()
    {
        await using var r = new Rig();
        r.Game.Emit("KCDUS|MENU|status|");
        await Until(() => r.Game.Has("NOTE|Multiplayer: not in a session"));
    }

    [Fact]
    public async Task Opening_the_page_puts_the_agents_state_in_its_title()
    {
        await using var r = new Rig();
        r.Game.Emit("KCDUS|MENU|page|");
        await Until(() => r.Game.Has("MENUTEXT|" + (int)KcdUs.Agent.Ui.MenuUi.Title.Idle));
        await r.Host.HandleAsync("host", "");
        Assert.Equal("MENUTEXT|" + (int)KcdUs.Agent.Ui.MenuUi.Title.Hosting, r.Game.Last("MENUTEXT|"));
    }

    [Fact]
    public async Task Host_a_game_listens_and_leave_lets_go_of_the_port()
    {
        await using var r = new Rig();
        await r.Host.HandleAsync("host", "");
        Assert.Equal(AgentHost.HostMode, r.Host.Mode);
        Assert.True(r.Host.Session.IsHost);
        await Until(() => r.Host.Session.GetStatus().RelayConnected);
        Assert.Contains("hosting", r.Host.Describe());
        Assert.True(r.Game.Has("NOTE|Multiplayer: hosting"));

        await r.Host.HandleAsync("leave", "");
        Assert.Equal(AgentHost.Idle, r.Host.Mode);
        using var l = new TcpListener(IPAddress.Loopback, r.Host.Config.RelayPort);
        l.Start();   // the port is free again
        l.Stop();
    }

    [Fact]
    public async Task Join_without_a_host_address_asks_for_one_and_opens_the_settings_page()
    {
        await using var r = new Rig();
        await r.Host.HandleAsync("join", "");
        Assert.Equal(AgentHost.Idle, r.Host.Mode);
        Assert.True(r.Game.Has("NOTE|Enter your host's address"));
        Assert.Single(r.Opened);
        Assert.Contains("/settings?t=" + r.Host.SettingsToken, r.Opened[0]);
    }

    [Fact]
    public async Task A_friend_joins_a_host_started_from_the_tab()
    {
        await using var hostRig = new Rig();
        await hostRig.Host.HandleAsync("host", "");
        await Until(() => hostRig.Host.Session.GetStatus().RelayConnected);

        await using var friend = new Rig(c => { c.RelayHost = "127.0.0.1"; c.RelayPort = hostRig.Host.Config.RelayPort; c.PlayerName = "Hans"; });
        friend.Host.AllowLoopbackJoin = true;
        await friend.Host.HandleAsync("join", "");
        Assert.Equal(AgentHost.GuestMode, friend.Host.Mode);
        await Until(() => friend.Host.Session.GetStatus().RelayConnected);
        await Until(() => hostRig.Host.Session.GetStatus().Players.Count == 1);
        Assert.Equal("Hans", hostRig.Host.Session.GetStatus().Players[0].Name);
    }

    [Fact]
    public async Task The_standing_answer_and_the_keys_are_set_from_the_tab_and_saved()
    {
        await using var r = new Rig();
        r.Host.Keys = new Hotkeys(() => true);
        await r.Host.HandleAsync("pref", "free");
        Assert.Equal("free", r.Host.Config.RailsPref);
        await r.Host.HandleAsync("keys", "f9f10");
        Assert.Equal(0x78, r.Host.Keys.JoinVk);
        Assert.Equal(0x79, r.Host.Keys.StayVk);
        Assert.Equal("F9", r.Host.Keys.JoinName);
        await r.Host.HandleAsync("keys", "off");
        Assert.Equal(0, r.Host.Keys.JoinVk);
        await r.Host.HandleAsync("keys", "bogus");
        Assert.Equal("off", r.Host.Config.KeyPreset);   // an unknown preset changes nothing

        var saved = AgentConfig.Load(Path.Combine(r.Dir, "cfg.json"));
        Assert.Equal("free", saved.RailsPref);
        Assert.Equal("off", saved.KeyPreset);
    }

    [Fact]
    public async Task The_settings_form_changes_what_it_is_given_and_nothing_else()
    {
        await using var r = new Rig();
        var form = new NameValueCollection { ["name"] = "Hans", ["relay"] = "100.64.1.2:7800", ["password"] = "swordfish", ["pref"] = "join", ["keys"] = "nonsense", ["port"] = "" };
        var changed = r.Host.ApplySettings(form);
        var c = r.Host.Config;
        Assert.Equal("Hans", c.PlayerName);
        Assert.Equal("100.64.1.2", c.RelayHost);
        Assert.Equal(7800, c.RelayPort);
        Assert.Equal("swordfish", c.Password);
        Assert.Equal("join", c.RailsPref);
        Assert.Equal(KeyPreset.Default, c.KeyPreset);   // the unknown keys value is ignored
        Assert.Contains("host address", changed);
        Assert.Equal("", r.Host.ApplySettings(new NameValueCollection()));   // an empty form changes nothing
        Assert.Equal("", r.Host.ApplySettings(new NameValueCollection { ["relay"] = "not an address!" }));
    }

    [Fact]
    public async Task The_settings_form_also_sets_the_world_options()
    {
        await using var r = new Rig();
        var c = r.Host.Config;
        var changed = r.Host.ApplySettings(new NameValueCollection { ["worldname"] = "The Skalitz run", ["autosync"] = "0", ["henry"] = "mine", ["resolve"] = "host", ["savesdir"] = Path.Combine(r.Dir, "nope") });
        Assert.Equal("The Skalitz run", c.WorldName);
        Assert.False(c.AutoSync);
        Assert.Equal("mine", c.HenryMode);
        Assert.Equal("host", c.ResolvePolicy);
        Assert.Equal("", c.SavesDir);                                   // a folder that does not exist is not taken
        Assert.DoesNotContain("saves folder", changed);
        r.Host.ApplySettings(new NameValueCollection { ["autosync"] = "maybe", ["henry"] = "everyone", ["resolve"] = "coinflip" });
        Assert.False(c.AutoSync); Assert.Equal("mine", c.HenryMode); Assert.Equal("host", c.ResolvePolicy);   // nonsense changes nothing
        string real = Path.Combine(r.Dir, "saves"); Directory.CreateDirectory(real);
        r.Host.ApplySettings(new NameValueCollection { ["savesdir"] = real });
        Assert.Equal(real, c.SavesDir);
    }

    private static string FakeGame(string pakBytes)
    {
        string d = Path.Combine(Path.GetTempPath(), "kcdus-fakegame-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(d, "Mods", "kcdus", "Data"));
        File.WriteAllText(Path.Combine(d, "Mods", "kcdus", "Data", "kcdus.pak"), pakBytes);
        return d;
    }

    [Fact]
    public async Task Two_installs_with_the_same_mod_payload_share_a_room_that_says_it_is_partly_shared_and_what_is_missing()
    {
        string a = FakeGame("build-1"), b = FakeGame("build-1");
        try
        {
            await using var host = new Rig(c => c.DevAllowUnverifiedPayload = false);
            await using var guest = new Rig(c => { c.DevAllowUnverifiedPayload = false; c.RelayHost = "127.0.0.1"; });
            host.Host.GameDir = a; guest.Host.GameDir = b;
            guest.Host.AllowLoopbackJoin = true; guest.Host.Config.RelayPort = host.Host.Config.RelayPort;
            await host.Host.HandleAsync("host", "");
            await Until(() => host.Host.Session.GetStatus().RelayConnected);
            await guest.Host.HandleAsync("join", "");
            await Until(() => guest.Host.Session.GetStatus().RelayConnected);
            await Until(() => guest.Host.Session.GetStatus().RoomNote.Contains("authority.combat"));    // the Welcome has been read
            var s = guest.Host.Session.GetStatus();
            Assert.Equal("partial", s.RoomMode);                                         // fights and loot are shared outcomes (engine-verified), NPC authority is not
            Assert.Contains("partly shared", s.RoomNote);
            Assert.Contains("authority.npc", s.RoomNote);                                // and it says what is still missing
            guest.Game.Emit("KCDUS|HELLO|0.1.0|1|pak"); guest.Game.Emit("KCDUS|READY|1");   // in a world, the line a player reads says so too
            await Until(() => guest.Host.Session.GetStatus().Message.Contains("partly shared"));
            Assert.True(guest.Game.Has("ADAPTER?"));                                    // and the game was asked whether the engine adapter is loaded
            Assert.Contains("authority.combat", s.RoomNote);                              // engine-verified is not yet integration-verified: still listed
            await Until(() => host.Host.Session.GetStatus().Players.Count == 1);
            Assert.Equal("partial", host.Host.Session.GetStatus().RoomMode);
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public async Task A_friend_with_other_mods_is_admitted_but_no_world_or_henry_is_moved_between_you()
    {
        string a = FakeGame("build-1"), b = FakeGame("build-1");
        Directory.CreateDirectory(Path.Combine(b, "Mods", "someothermod"));          // only the guest has another mod
        try
        {
            await using var host = new Rig(c => c.DevAllowUnverifiedPayload = false);
            await using var guest = new Rig(c => { c.DevAllowUnverifiedPayload = false; c.RelayHost = "127.0.0.1"; });
            host.Host.GameDir = a; guest.Host.GameDir = b;
            guest.Host.AllowLoopbackJoin = true; guest.Host.Config.RelayPort = host.Host.Config.RelayPort;
            await host.Host.HandleAsync("host", "");
            await Until(() => host.Host.Session.GetStatus().RelayConnected);
            await guest.Host.HandleAsync("join", "");
            await Until(() => guest.Host.Session.GetStatus().RelayConnected);
            await Until(() => !guest.Host.Session.ContentMatches);
            await Until(() => !host.Host.Session.ContentMatches);                            // the host is told as well
            await guest.Host.HandleAsync("world", "join");
            await Until(() => guest.Game.Has("NOTE|Your game has other mods"));
            Assert.False(guest.Game.Snapshot().Any(s => s.StartsWith("MENUTEXT|" + (int)KcdUs.Agent.Ui.MenuUi.Title.WorldAsked)));   // no request was sent
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    /// <summary>A game folder whose kcd.log lists the given DLC as active (and affecting saves), the way the game writes it at startup.</summary>
    private static string FakeGameWithDlc(string pakBytes, params string[] active)
    {
        string d = FakeGame(pakBytes);
        var log = new System.Text.StringBuilder("Initializing default materials...\nDLC list:\n");
        int i = 1;
        foreach (var n in new[] { "TreasuresOfThePast", "NewHomes", "ExpeditionaryRides" })
            log.Append($"[{i++} {n}] {i * 1000}, Kingdom Come: Deliverance - {n}, active: {(active.Contains(n) ? 'Y' : 'N')}, save: Y\n");
        log.Append("[9 HDTextures] 836890, HD Texture Pack, active: Y, save: N\nRunning machine spec auto detect (64 bit)...\n");
        File.WriteAllText(Path.Combine(d, "kcd.log"), log.ToString());
        return d;
    }

    [Fact]
    public async Task A_friend_with_more_dlc_than_the_host_plays_the_hosts_game_and_is_told_what_stays_out()
    {
        string a = FakeGameWithDlc("build-1"), b = FakeGameWithDlc("build-1", "NewHomes", "ExpeditionaryRides");   // the host has none; the friend has two
        try
        {
            await using var host = new Rig(c => c.DevAllowUnverifiedPayload = false);
            await using var guest = new Rig(c => { c.DevAllowUnverifiedPayload = false; c.RelayHost = "127.0.0.1"; });
            host.Host.GameDir = a; guest.Host.GameDir = b;
            guest.Host.AllowLoopbackJoin = true; guest.Host.Config.RelayPort = host.Host.Config.RelayPort;
            await host.Host.HandleAsync("host", "");
            await Until(() => host.Host.Session.GetStatus().RelayConnected);
            await guest.Host.HandleAsync("join", "");
            await Until(() => guest.Host.Session.GetStatus().RelayConnected);
            await Until(() => guest.Host.Session.DlcExtra.Count == 2);
            Assert.Equal(new[] { "ExpeditionaryRides", "NewHomes" }, guest.Host.Session.DlcExtra);
            Assert.Empty(guest.Host.Session.DlcLacks);
            Assert.True(guest.Host.Session.ContentMatches);                                // DLC is not a mod difference: the world may move
            Assert.True(guest.Game.Has("NOTE|Your game has DLC your host's does not"));
            Assert.Contains("turn that DLC off in Steam", string.Join("\n", guest.Game.Snapshot()));
            await Until(() => host.Host.Session.GetStatus().RoomNote.Contains("partly shared"));   // honestly partial, never "shared"
            await guest.Host.HandleAsync("world", "join");
            await Until(() => guest.Game.Snapshot().Any(s => s.StartsWith("MENUTEXT|" + (int)KcdUs.Agent.Ui.MenuUi.Title.WorldAsked)));   // the request IS sent
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public async Task A_friend_with_less_dlc_than_the_host_cannot_be_given_the_hosts_dlc_world_and_is_told_why()
    {
        string a = FakeGameWithDlc("build-1", "NewHomes"), b = FakeGameWithDlc("build-1");                   // the host has one; the friend none
        try
        {
            await using var host = new Rig(c => c.DevAllowUnverifiedPayload = false);
            await using var guest = new Rig(c => { c.DevAllowUnverifiedPayload = false; c.RelayHost = "127.0.0.1"; });
            host.Host.GameDir = a; guest.Host.GameDir = b;
            guest.Host.AllowLoopbackJoin = true; guest.Host.Config.RelayPort = host.Host.Config.RelayPort;
            await host.Host.HandleAsync("host", "");
            await Until(() => host.Host.Session.GetStatus().RelayConnected);
            await guest.Host.HandleAsync("join", "");
            await Until(() => guest.Host.Session.GetStatus().RelayConnected);
            await Until(() => guest.Host.Session.DlcLacks.Count == 1);
            Assert.Equal(new[] { "NewHomes" }, guest.Host.Session.DlcLacks);
            Assert.True(guest.Game.Has("NOTE|Your host's game has DLC you do not have (NewHomes)"));
            int guestId = guest.Host.Session.MyId;
            await Until(() => host.Host.Session.PeerLacksDlc(guestId).Count == 1);                  // the host knows too, and will not serve a world to them
            await guest.Host.HandleAsync("world", "join");
            await Until(() => guest.Game.Has("NOTE|Your host's game has DLC you do not have"));
            Assert.False(guest.Game.Snapshot().Any(s => s.StartsWith("MENUTEXT|" + (int)KcdUs.Agent.Ui.MenuUi.Title.WorldAsked)));
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public async Task A_guest_with_another_mod_payload_is_refused_and_the_status_says_why()
    {
        string a = FakeGame("build-1"), b = FakeGame("build-2");
        try
        {
            await using var host = new Rig(c => c.DevAllowUnverifiedPayload = false);
            await using var guest = new Rig(c => { c.DevAllowUnverifiedPayload = false; c.RelayHost = "127.0.0.1"; });
            host.Host.GameDir = a; guest.Host.GameDir = b;
            guest.Host.AllowLoopbackJoin = true; guest.Host.Config.RelayPort = host.Host.Config.RelayPort;
            await host.Host.HandleAsync("host", "");
            await Until(() => host.Host.Session.GetStatus().RelayConnected);
            await guest.Host.HandleAsync("join", "");
            await Until(() => guest.Host.Session.GetStatus().Refused.Length > 0);
            Assert.Contains("different mod payloads", guest.Host.Session.GetStatus().Message);
            Assert.False(guest.Host.Session.GetStatus().RelayConnected && guest.Host.Session.GetStatus().MyId != 0);
            Assert.Empty(host.Host.Session.GetStatus().Players);                          // nobody was admitted
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public async Task A_player_with_no_mod_installed_cannot_join_a_real_room()
    {
        string a = FakeGame("build-1");
        try
        {
            await using var host = new Rig(c => c.DevAllowUnverifiedPayload = false);
            await using var guest = new Rig(c => { c.DevAllowUnverifiedPayload = false; c.RelayHost = "127.0.0.1"; });
            host.Host.GameDir = a;                                                       // the guest has no game folder at all: nothing to verify
            guest.Host.AllowLoopbackJoin = true; guest.Host.Config.RelayPort = host.Host.Config.RelayPort;
            await host.Host.HandleAsync("host", "");
            await Until(() => host.Host.Session.GetStatus().RelayConnected);
            await guest.Host.HandleAsync("join", "");
            await Until(() => guest.Host.Session.GetStatus().Refused.Length > 0);
            Assert.Contains("could not be verified", guest.Host.Session.GetStatus().Message);
        }
        finally { Directory.Delete(a, true); }
    }

    [Fact]
    public async Task The_settings_page_needs_the_agents_own_link()
    {
        await using var r = new Rig();
        await using var srv = new StatusServer(() => r.Host.Session, r.StatusPort, () => { }, r.Host);
        srv.Start();
        using var http = new HttpClient();
        var bad = await http.GetAsync($"http://127.0.0.1:{r.StatusPort}/settings");
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        var ok = await http.GetAsync(r.Host.SettingsUrl);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("name=\"relay\"", await ok.Content.ReadAsStringAsync());

        var post = await http.PostAsync($"http://127.0.0.1:{r.StatusPort}/settings", new FormUrlEncodedContent(new Dictionary<string, string> { ["t"] = "wrong", ["name"] = "Mallory" }));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal("Henry", r.Host.Config.PlayerName);
        var post2 = await http.PostAsync($"http://127.0.0.1:{r.StatusPort}/settings", new FormUrlEncodedContent(new Dictionary<string, string> { ["t"] = r.Host.SettingsToken, ["name"] = "Hans" }));
        Assert.Equal(HttpStatusCode.OK, post2.StatusCode);
        Assert.Equal("Hans", r.Host.Config.PlayerName);
    }

    [Fact]
    public void The_key_presets_name_their_keys()
    {
        Assert.Equal((0x7A, 0x7B), KeyPreset.Parse("f11f12"));
        Assert.Equal((0x78, 0x79), KeyPreset.Parse(" F9F10 "));
        Assert.Equal((0, 0), KeyPreset.Parse("off"));
        Assert.Equal((0x7A, 0x7B), KeyPreset.Parse("garbage"));
        Assert.Equal("join F9, stay F10", KeyPreset.Describe("f9f10"));
        Assert.Equal(59, LinuxKeys.CodeForVk(0x70));   // F1
        Assert.Equal(68, LinuxKeys.CodeForVk(0x79));   // F10
        Assert.Equal(87, LinuxKeys.CodeForVk(0x7A));   // F11
        Assert.Equal(0, LinuxKeys.CodeForVk(0x41));    // 'A' is not an F-key
    }
}
