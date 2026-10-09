// SPDX-License-Identifier: GPL-3.0-only
// The shared pause: a friend's open pause menu holds this world, and nothing can hold it for good.
using KcdUs.Agent;
using Xunit;

namespace KcdUs.Tests;

public class PauseCoordinatorTests
{
    private sealed class Rig
    {
        public long Now = 100_000;
        public readonly List<string> ToGame = new(), ToPeers = new(), Notes = new(), Log = new();
        public readonly PauseCoordinator P;
        public Rig() { P = new PauseCoordinator(() => Now, ToGame.Add, ToPeers.Add, Notes.Add, Log.Add, id => "Hans"); }
        public void Step(long ms, bool inWorld = true, bool connected = true, int fps = 60) { Now += ms; P.Tick(inWorld, connected, fps); }
    }

    [Fact]
    public void A_friends_open_menu_holds_the_world_and_their_closing_it_lets_go()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true);
        r.Step(100);
        Assert.True(r.P.Frozen);
        Assert.Contains("FREEZE|1|720", r.ToGame);                         // 60 fps x 12 s of frames
        Assert.Contains(r.Notes, n => n.Contains("Hans paused"));
        r.P.PeerMenu(2, false);
        r.Step(100);
        Assert.False(r.P.Frozen);
        Assert.Equal("FREEZE|0", r.ToGame.Last());
        Assert.Contains(r.Notes, n => n.Contains("runs again"));
    }

    [Fact]
    public void The_hold_is_asked_for_again_every_two_seconds_so_the_games_own_lease_never_runs_out_while_the_friend_is_there()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true);
        for (int i = 0; i < 6; i++) { r.Step(1000); r.P.PeerMenu(2, true); }    // the friend's heartbeat
        Assert.Equal(3, r.ToGame.Count(l => l.StartsWith("FREEZE|1|")));      // at 0, 2 and 4 s
    }

    [Fact]
    public void A_friend_who_goes_silent_cannot_hold_the_world_for_more_than_six_seconds()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true);
        r.Step(100); Assert.True(r.P.Frozen);
        r.Step(5000); Assert.True(r.P.Frozen);                                // 5.1 s: still counted
        r.Step(1500); Assert.False(r.P.Frozen);                               // 6.6 s with no heartbeat: their link is gone
        Assert.Equal("FREEZE|0", r.ToGame.Last());
    }

    [Fact]
    public void A_menu_left_open_for_a_quarter_of_an_hour_stops_holding_even_with_heartbeats()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true);
        for (int i = 0; i < 930; i++) { r.Step(1000); r.P.PeerMenu(2, true); }
        Assert.False(r.P.Frozen);
    }

    [Fact]
    public void A_friend_who_leaves_the_session_lets_go_at_once()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true); r.Step(100); Assert.True(r.P.Frozen);
        r.P.PeerLeft(2); r.Step(100);
        Assert.False(r.P.Frozen);
    }

    [Fact]
    public void Two_friends_hold_until_both_have_closed_their_menus()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true); r.P.PeerMenu(3, true); r.Step(100);
        r.P.PeerMenu(2, false); r.Step(100);
        Assert.True(r.P.Frozen);
        r.P.PeerMenu(3, false); r.Step(100);
        Assert.False(r.P.Frozen);
    }

    [Fact]
    public void Nothing_holds_a_world_the_player_is_not_in_or_a_relay_that_is_gone()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true);
        r.Step(100, inWorld: false); Assert.False(r.P.Frozen);                 // in a menu or loading: nothing to hold
        r.Step(100); Assert.True(r.P.Frozen);
        r.Step(100, connected: false); Assert.False(r.P.Frozen);               // the relay went away
    }

    [Fact]
    public void With_the_option_off_a_friends_menu_never_holds_and_this_players_menu_is_not_announced()
    {
        var r = new Rig();
        r.P.SetShared(false);
        r.P.PeerMenu(2, true); r.Step(100);
        Assert.False(r.P.Frozen);
        Assert.DoesNotContain(r.ToGame, l => l.StartsWith("FREEZE|1"));
        r.P.LocalMenu(true); r.Step(3000);
        Assert.DoesNotContain("pause|1", r.ToPeers);
    }

    [Fact]
    public void Turning_the_option_off_while_held_lets_go_at_once()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true); r.Step(100); Assert.True(r.P.Frozen);
        r.P.SetShared(false);
        Assert.False(r.P.Frozen);
        Assert.Equal("FREEZE|0", r.ToGame.Last());
    }

    [Fact]
    public void This_players_menu_is_announced_with_a_heartbeat_while_it_is_open_and_when_it_closes()
    {
        var r = new Rig();
        r.P.LocalMenu(true);
        Assert.Equal(new[] { "pause|1" }, r.ToPeers);
        r.Step(2100); r.Step(2100);
        Assert.Equal(3, r.ToPeers.Count(l => l == "pause|1"));
        r.P.LocalMenu(false);
        Assert.Equal("pause|0", r.ToPeers.Last());
    }

    [Fact]
    public void The_lease_follows_the_frame_rate_within_bounds()
    {
        Assert.Equal(240, PauseCoordinator.LeaseFrames(5));
        Assert.Equal(720, PauseCoordinator.LeaseFrames(60));
        Assert.Equal(3600, PauseCoordinator.LeaseFrames(300));
        Assert.Equal(6000, PauseCoordinator.LeaseFrames(9999));
        Assert.Equal(720, PauseCoordinator.LeaseFrames(0));                    // unknown: assume 60
    }

    [Fact]
    public void Reset_lets_go_and_forgets_everyone()
    {
        var r = new Rig();
        r.P.PeerMenu(2, true); r.Step(100); Assert.True(r.P.Frozen);
        r.P.Reset("test");
        Assert.False(r.P.Frozen);
        Assert.Empty(r.P.Holders());
    }

    // ---- the option "off": this player's own ESC menu does not pause their game while a friend is in the world (the engine adapter's gate)

    private sealed class FakeGate : IPauseGate
    {
        public bool Available { get; set; } = true;
        public readonly List<bool> Calls = new();
        public void Apply(bool on) => Calls.Add(on);
    }

    private sealed class GateRig
    {
        public long Now = 100_000;
        public readonly List<string> ToGame = new(), ToPeers = new(), Notes = new(), Log = new();
        public readonly FakeGate Gate = new();
        public readonly PauseCoordinator P;
        public GateRig() { P = new PauseCoordinator(() => Now, ToGame.Add, ToPeers.Add, Notes.Add, Log.Add, id => "Hans", Gate); }
        public void Step(long ms, bool inWorld = true, bool connected = true, int friends = 1) { Now += ms; P.Tick(inWorld, connected, 60, friends); }
    }

    [Fact]
    public void Shared_mode_never_touches_the_gate_the_menu_pauses_as_in_the_unmodded_game()
    {
        var r = new GateRig();
        for (int i = 0; i < 30; i++) r.Step(100);
        Assert.Empty(r.Gate.Calls);
        Assert.False(r.P.OwnMenuRuns);
        Assert.DoesNotContain(r.ToGame, l => l.StartsWith("OWNPAUSE"));
    }

    [Fact]
    public void Off_with_a_friend_in_the_world_keeps_the_gate_on_with_a_heartbeat_every_second()
    {
        var r = new GateRig();
        r.P.SetShared(false);
        r.Step(100);
        Assert.True(r.P.OwnMenuRuns);
        Assert.Equal("OWNPAUSE|0", r.ToGame.Last());                       // the inventory does not slow time either
        for (int i = 0; i < 50; i++) r.Step(100);                           // 5 s
        Assert.InRange(r.Gate.Calls.Count(c => c), 5, 7);
        Assert.DoesNotContain(false, r.Gate.Calls);
    }

    [Fact]
    public void The_gate_turns_off_at_once_when_the_last_friend_leaves_the_world_or_the_option_goes_back_to_shared()
    {
        var r = new GateRig();
        r.P.SetShared(false);
        r.Step(100);
        r.Step(100, friends: 0);
        Assert.False(r.P.OwnMenuRuns);
        Assert.False(r.Gate.Calls.Last());
        Assert.Equal("OWNPAUSE|1", r.ToGame.Last());
        r.Step(100);
        Assert.True(r.P.OwnMenuRuns);
        r.P.SetShared(true);
        r.Step(100);
        Assert.False(r.P.OwnMenuRuns);
        Assert.False(r.Gate.Calls.Last());
    }

    [Theory]
    [InlineData(false, true)]   // in the main menu / loading
    [InlineData(true, false)]   // the relay went away
    public void No_world_or_no_link_means_menus_pause_as_usual(bool inWorld, bool connected)
    {
        var r = new GateRig();
        r.P.SetShared(false);
        r.Step(100);
        r.Step(100, inWorld: inWorld, connected: connected);
        Assert.False(r.P.OwnMenuRuns);
        Assert.False(r.Gate.Calls.Last());
    }

    [Fact]
    public void Reset_turns_the_gate_off()
    {
        var r = new GateRig();
        r.P.SetShared(false);
        r.Step(100);
        r.P.Reset("left");
        Assert.False(r.P.OwnMenuRuns);
        Assert.False(r.Gate.Calls.Last());
    }

    [Fact]
    public void Without_the_adapter_the_option_still_works_for_friends_and_says_that_the_own_menu_still_pauses()
    {
        var r = new GateRig();
        r.Gate.Available = false;
        r.P.SetShared(false);
        r.Step(100);
        Assert.Contains(r.Log, l => l.Contains("still pauses"));
        r.P.PeerMenu(2, true);
        r.Step(100);
        Assert.False(r.P.Frozen);                                           // off: a friend's menu never holds this world
    }

    [Fact]
    public void While_on_the_game_is_told_again_every_ten_seconds_because_a_loaded_world_forgets()
    {
        var r = new GateRig();
        r.P.SetShared(false);
        for (int i = 0; i < 250; i++) r.Step(100);                          // 25 s
        Assert.InRange(r.ToGame.Count(l => l == "OWNPAUSE|0"), 3, 4);
    }
}
