// SPDX-License-Identifier: GPL-3.0-only
// The agent's half of the shared fights and loot: when it is on, what it forwards, and the host's durable decisions.
using Coop.Contract;
using KcdUs.Agent;
using Xunit;

namespace KcdUs.Tests;

public class OutcomesCoordinatorTests
{
    private const string Coin = "5ef63059-322e-4e1b-abe8-926e100c770e";

    private sealed class Rig : IDisposable
    {
        public long Now = 1000;
        public readonly List<string> ToGame = new(), ToPeers = new(), ToGuests = new(), Notes = new(), Log = new();
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "kcdus-outc-" + Guid.NewGuid().ToString("N")[..8]);
        public OperationJournal? Journal;
        public readonly OutcomesCoordinator O;
        public Rig(bool journal = false)
        {
            Directory.CreateDirectory(Dir);
            if (journal) Journal = new OperationJournal(Path.Combine(Dir, "j.jsonl"));
            O = new OutcomesCoordinator(() => Now, ToGame.Add, ToPeers.Add, ToGuests.Add, Notes.Add, Log.Add, id => "Hans", () => Journal);
        }
        public void Step(string scope = "w1", bool host = false, bool inWorld = true, bool connected = true) { Now += 100; O.Tick(inWorld, connected, host, scope); }
        public void Dispose() { Journal?.Dispose(); try { Directory.Delete(Dir, true); } catch { } }
    }

    [Fact]
    public void It_is_on_only_while_a_friend_is_in_the_same_shared_world()
    {
        using var r = new Rig();
        r.Step();                                                              // alone
        Assert.False(r.O.Active);
        Assert.Contains("scope|w1", r.ToPeers);                                // it tells the friends which world this player plays
        r.O.PeerScope(2, "other");
        r.Step(); Assert.False(r.O.Active);                                    // a friend in ANOTHER world: nothing shared
        r.O.PeerScope(2, "w1");
        r.Step();
        Assert.True(r.O.Active);
        Assert.Contains("CMBMODE|1|w1", r.ToGame);
        Assert.Contains("LOOTMODE|1|w1|0", r.ToGame);
        Assert.Contains(r.Notes, n => n.Contains("Shared fights and loot are on with Hans"));
        r.Step(scope: "");                                                     // left the shared world (a menu)
        Assert.False(r.O.Active);
        Assert.Contains("CMBMODE|0", r.ToGame);
    }

    [Fact]
    public void The_option_off_keeps_every_copy_its_own()
    {
        using var r = new Rig();
        r.O.SetEnabled(false);
        r.O.PeerScope(2, "w1");
        r.Step();
        Assert.False(r.O.Active);
        Assert.DoesNotContain("CMBMODE|1|w1", r.ToGame);
        r.O.SetEnabled(true); r.Step(); Assert.True(r.O.Active);
        r.O.SetEnabled(false); r.Step(); Assert.False(r.O.Active);
    }

    [Fact]
    public void Combat_is_forwarded_validated_and_rate_limited_and_a_friends_is_applied_in_this_scope()
    {
        using var r = new Rig();
        r.O.PeerScope(2, "w1"); r.Step();
        r.O.GameCombat(new[] { "CMB", "bandit_7", "20.00", "80.00", "0" });
        Assert.Contains("cmb|bandit_7|20.00|0", r.ToPeers);
        r.ToPeers.Clear();
        foreach (var bad in new[] { new[] { "CMB", "bad name", "1", "1", "0" }, new[] { "CMB", "bandit_7", "-1", "1", "0" }, new[] { "CMB", "kcdus_ghost_2", "1", "1", "0" }, new[] { "CMB", "bandit_7", "1", "1", "7" } })
            r.O.GameCombat(bad);
        Assert.Empty(r.ToPeers);
        r.Now += 1500;                                                                                     // a new one-second window
        for (int i = 0; i < 100; i++) r.O.GameCombat(new[] { "CMB", "bandit_7", "1", "1", "0" });
        Assert.Equal(OutcomesCoordinator.MaxCombatPerSecond, r.ToPeers.Count);
        r.O.PeerCombat(2, new[] { "cmb", "bandit_7", "12", "0" });
        Assert.Contains("CMBAPPLY|w1|bandit_7|12|0", r.ToGame);
        int n = r.ToGame.Count;
        r.O.PeerCombat(2, new[] { "cmb", "bandit 7", "12", "0" });
        r.O.PeerCombat(2, new[] { "cmb", "bandit_7", "99999", "0" });
        Assert.Equal(n, r.ToGame.Count);
    }

    [Fact]
    public void Nothing_is_forwarded_while_it_is_off()
    {
        using var r = new Rig();
        r.O.GameCombat(new[] { "CMB", "bandit_7", "20.00", "80.00", "0" });
        r.O.PeerCombat(2, new[] { "cmb", "bandit_7", "12", "0" });
        Assert.Empty(r.ToPeers);
        Assert.DoesNotContain(r.ToGame, l => l.StartsWith("CMBAPPLY"));
    }

    [Fact]
    public void A_guest_ask_is_journalled_before_the_game_decides_and_the_answer_completes_it()
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        r.O.PeerAsk(2, new[] { "loot", "ask", "abcd1234", "1", "bandit_7", Coin, "50" });
        Assert.Contains("LOOTASK|w1|2|abcd1234|1|bandit_7|" + Coin + "|50", r.ToGame);
        Assert.Equal(OpState.EngineApplying, r.Journal!.Get("lt:2:abcd1234:1")!.State);                  // durable intent first
        r.O.GameLoot(new[] { "LOOT", "res", "2", "1", "ok", "bandit_7", Coin, "50", "1.0000" }, isHost: true);
        Assert.Contains("loot|res|2|1|ok|bandit_7|" + Coin + "|50", r.ToGuests);
        var rec = r.Journal.Get("lt:2:abcd1234:1")!;
        Assert.Equal(OpState.Complete, rec.State);
        Assert.Equal("ok", rec.Outcome);
    }

    [Fact]
    public void The_same_ask_again_gets_the_same_answer_and_the_game_is_not_asked_twice()
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        var ask = new[] { "loot", "ask", "abcd1234", "7", "bandit_7", Coin, "50" };
        r.O.PeerAsk(2, ask);
        r.O.GameLoot(new[] { "LOOT", "res", "2", "7", "ok", "bandit_7", Coin, "50", "1.0000" }, true);
        int asks = r.ToGame.Count(l => l.StartsWith("LOOTASK"));
        r.ToGuests.Clear();
        r.O.PeerAsk(2, ask);                                                                              // the guest's retry
        Assert.Equal(asks, r.ToGame.Count(l => l.StartsWith("LOOTASK")));
        Assert.Contains("loot|res|2|7|ok|bandit_7|" + Coin + "|50", r.ToGuests);
        r.O.PeerAsk(2, new[] { "loot", "ask", "abcd1234", "7", "bandit_7", Coin, "999" });               // the same id with other contents: refused
        Assert.Equal(asks, r.ToGame.Count(l => l.StartsWith("LOOTASK")));
    }

    [Fact]
    public void An_ask_interrupted_by_a_restart_is_never_granted_again()
    {
        string path;
        using (var first = new Rig(journal: true))
        {
            first.O.PeerScope(2, "w1"); first.Step(host: true);
            first.O.PeerAsk(2, new[] { "loot", "ask", "abcd1234", "9", "bandit_7", Coin, "50" });        // the game was told; the agent died before the answer
            path = first.Journal!.Path;
            first.Journal.Dispose(); first.Journal = null;
            var j2 = new OperationJournal(path);
            Assert.Single(j2.Recover());                                                                  // quarantined
            using var second = new Rig();
            second.Journal = j2;
            second.O.PeerScope(2, "w1"); second.Step(host: true);
            second.O.PeerAsk(2, new[] { "loot", "ask", "abcd1234", "9", "bandit_7", Coin, "50" });
            Assert.DoesNotContain(second.ToGame, l => l.StartsWith("LOOTASK"));
            Assert.Contains("loot|res|2|9|gone|bandit_7|" + Coin + "|50", second.ToGuests);
            j2.Dispose(); second.Journal = null;
        }
    }

    [Fact]
    public void A_guest_forwards_its_ask_and_the_hosts_answer_and_broadcasts_reach_the_game_only_for_that_guest()
    {
        using var r = new Rig();
        r.O.PeerScope(1, "w1"); r.Step(host: false);
        r.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "3", "bandit_7", Coin, "50", "1.0000" }, isHost: false);
        Assert.Contains("loot|ask|abcd1234|3|bandit_7|" + Coin + "|50", r.ToPeers);
        r.O.HostLoot(new[] { "loot", "res", "5", "3", "ok", "bandit_7", Coin, "50" }, myId: 2);          // an answer for another guest
        Assert.DoesNotContain(r.ToGame, l => l.StartsWith("LOOTRES"));
        r.O.HostLoot(new[] { "loot", "res", "2", "3", "gone", "bandit_7", Coin, "50" }, myId: 2);
        Assert.Contains("LOOTRES|w1|3|gone|bandit_7|" + Coin + "|50", r.ToGame);
        r.O.HostLoot(new[] { "loot", "took", "bandit_7", Coin, "20" }, myId: 2);
        Assert.Contains("LOOTTOOK|w1|bandit_7|" + Coin + "|20", r.ToGame);
        r.O.GameLoot(new[] { "LOOT", "unconfirmed", "bandit_7", Coin, "50", "1" }, isHost: false);
        Assert.Contains(r.Notes, n => n.Contains("Someone already took that"));
    }

    [Fact]
    public void A_host_take_is_announced_to_the_guests_only_by_the_host()
    {
        using var r = new Rig();
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        r.O.GameLoot(new[] { "LOOT", "took", "bandit_7", Coin, "201", "1.0000" }, isHost: true);
        Assert.Contains("loot|took|bandit_7|" + Coin + "|201", r.ToGuests);
        r.ToGuests.Clear();
        r.O.GameLoot(new[] { "LOOT", "took", "bandit_7", Coin, "201", "1.0000" }, isHost: false);
        Assert.Empty(r.ToGuests);
    }
}
