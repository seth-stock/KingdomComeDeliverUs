// SPDX-License-Identifier: GPL-3.0-only
// Shared enemies, the agent half (NpcCoordinator): the host gives every NPC to the nearest player, without flicker, and nothing from a friend is trusted.
using KcdUs.Agent;

namespace KcdUs.Tests;

public class NpcCoordinatorTests
{
    private sealed class Rig
    {
        public long Now = 50_000;
        public readonly List<string> ToGame = new(), ToPeers = new(), ToGuests = new(), Log = new();
        public readonly NpcCoordinator N;
        public Rig() { N = new NpcCoordinator(() => Now, ToGame.Add, ToPeers.Add, ToGuests.Add, Log.Add); }
        public void Step(long ms, string scope = "w1", bool host = true, int me = 1) { Now += ms; N.Tick(scope, host, me); }
    }

    [Fact]
    public void It_is_on_only_with_a_shared_world_and_tells_the_game()
    {
        var r = new Rig();
        r.Step(100, scope: "");
        Assert.False(r.N.Active);
        r.Step(100);
        Assert.True(r.N.Active);
        Assert.Equal("NPCMODE|1|w1|1", r.ToGame.Last());
        r.Step(100, scope: "");
        Assert.Equal("NPCMODE|0", r.ToGame.Last());
    }

    [Fact]
    public void The_host_gives_each_npc_to_the_nearest_player_and_tells_its_game_and_the_guests()
    {
        var r = new Rig();
        r.Step(100);
        r.N.GameNear(new[] { "NPCNEAR", "bandit_7:3.0;bandit_8:20.0" }, isHost: true);
        r.N.PeerNear(2, new[] { "npcnear", "w1", "bandit_7:15.0;bandit_8:2.5" }, isHost: true);
        r.Step(1000);
        Assert.Equal(1, r.N.Owners["bandit_7"]);
        Assert.Equal(2, r.N.Owners["bandit_8"]);
        Assert.Contains(r.ToGame, l => l.StartsWith("NPCOWN2|w1|") && l.Contains("bandit_7:1:") && l.Contains("bandit_8:2:"));
        Assert.Contains(r.ToGuests, l => l.StartsWith("npcown2|w1|"));
    }

    [Fact]
    public void An_npc_between_two_players_does_not_flicker_a_clearly_nearer_player_takes_it_after_two_seconds()
    {
        var r = new Rig();
        r.Step(100);
        void Report(double host, double guest)
        {
            r.N.GameNear(new[] { "NPCNEAR", $"bandit_7:{host:0.0}" }, isHost: true);
            r.N.PeerNear(2, new[] { "npcnear", "w1", $"bandit_7:{guest:0.0}" }, isHost: true);
        }
        Report(5, 7); r.Step(1000);
        Assert.Equal(1, r.N.Owners["bandit_7"]);
        Report(6, 4); r.Step(1000);                          // nearer, but not by the margin
        Assert.Equal(1, r.N.Owners["bandit_7"]);
        Report(12, 3); r.Step(1000);                         // clearly nearer: the challenge starts
        Assert.Equal(1, r.N.Owners["bandit_7"]);
        Report(12, 3); r.Step(1000);
        Report(12, 3); r.Step(1000);                         // held for two seconds
        Assert.Equal(2, r.N.Owners["bandit_7"]);
    }

    [Fact]
    public void A_silent_owner_loses_its_npcs_and_a_leaving_player_too()
    {
        var r = new Rig();
        r.Step(100);
        r.N.GameNear(new[] { "NPCNEAR", "bandit_7:30.0" }, isHost: true);
        r.N.PeerNear(2, new[] { "npcnear", "w1", "bandit_7:2.0;bandit_9:2.0" }, isHost: true);
        r.Step(1000);
        Assert.Equal(2, r.N.Owners["bandit_7"]);
        r.N.PeerLeft(2);
        r.N.GameNear(new[] { "NPCNEAR", "bandit_7:30.0" }, isHost: true);
        r.Step(1000);
        Assert.Equal(1, r.N.Owners["bandit_7"]);
        Assert.False(r.N.Owners.ContainsKey("bandit_9"));
        for (int i = 0; i < 5; i++) r.Step(1000);            // nobody reports bandit_7 any more
        Assert.Empty(r.N.Owners);
    }

    [Fact]
    public void A_guest_sends_its_near_list_and_states_to_the_host_and_applies_the_hosts_owners_and_the_owners_samples()
    {
        var r = new Rig();
        r.Step(100, host: false, me: 2);
        r.N.GameNear(new[] { "NPCNEAR", "bandit_7:3.0" }, isHost: false);
        Assert.Equal("npcnear|w1|bandit_7:3.0", r.ToPeers.Last());
        const string epoch="0123456789abcdef0123456789abcdef";
        r.N.HostOwnersV2(new[] { "npcown2", "w1", epoch, "1", "bandit_7:2:5" });
        r.N.GameStates(new[] { "NPCST", "bandit_7,1.00,2.00,3.00,0.500,0.00,0.00,1" });
        Assert.Equal("npcst2|w1|"+epoch+"|1|bandit_7,5,1.00,2.00,3.00,0.500,0.00,0.00,1", r.ToPeers.Last());
        r.N.HostOwners(new[] { "npcown", "w1", "bandit_7:2" });
        Assert.Equal("NPCOWN|w1|bandit_7:2", r.ToGame.Last());
        r.N.PeerStates(1, new[] { "npcst", "w1", "bandit_8,1.00,2.00,3.00,0.500,0.00,0.00,0" });
        Assert.Equal("NPCSET|w1|1|bandit_8,1.00,2.00,3.00,0.500,0.00,0.00,0", r.ToGame.Last());
    }

    [Theory]
    [InlineData("npcst", "w2", "bandit_8,1.00,2.00,3.00,0.5,0,0,0")]         // another world
    [InlineData("npcst", "w1", "bandit_8,1.00,2.00,3.00,0.5,0,0,2")]         // not a weapon flag
    [InlineData("npcst", "w1", "kcdus_ghost_1,1,2,3,0,0,0,0")]                // our own presence bodies are never NPCs
    [InlineData("npcst", "w1", "bandit_8,1,2,3,0,0,0,0|NPCMODE|0")]           // an injected record
    [InlineData("npcst", "w1", "bandit_8,NaN,2,3,0,0,0,0")]
    public void A_friends_bad_states_never_reach_the_game(string kind, string scope, string body)
    {
        var r = new Rig();
        r.Step(100, host: false, me: 2);
        int before = r.ToGame.Count;
        r.N.PeerStates(1, new[] { kind, scope, body });
        Assert.Equal(before, r.ToGame.Count);
    }

    [Fact]
    public void Owners_from_the_host_are_checked_and_states_are_rate_limited()
    {
        var r = new Rig();
        r.Step(100, host: false, me: 2);
        int before = r.ToGame.Count;
        r.N.HostOwners(new[] { "npcown", "w1", "bandit_7:999" });
        r.N.HostOwners(new[] { "npcown", "w1", "bandit 7:1" });
        Assert.Equal(before, r.ToGame.Count);
        for (int i = 0; i < NpcCoordinator.MaxStatesPerSecond+20; i++) r.N.PeerStates(1, new[] { "npcst", "w1", "bandit_8,1.00,2.00,3.00,0.500,0.00,0.00,0" });
        Assert.Equal(before + NpcCoordinator.MaxStatesPerSecond, r.ToGame.Count);
    }

    [Fact]
    public void A_guest_never_decides_ownership()
    {
        var r = new Rig();
        r.Step(100, host: false, me: 2);
        r.N.PeerNear(3, new[] { "npcnear", "w1", "bandit_7:1.0" }, isHost: false);
        r.Step(2000, host: false, me: 2);
        Assert.Empty(r.N.Owners);
        Assert.DoesNotContain(r.ToGuests, l => l.StartsWith("npcown"));
    }
}
