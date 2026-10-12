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
        Assert.Contains("cmb|bandit_7|20.00|0|0", r.ToPeers);       // the last field: was it the reporter's own fight (aggro)
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

    private const string Op = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void A_guest_ask_is_journalled_before_the_game_decides_and_the_guests_receipt_completes_it()
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        r.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "50" });
        Assert.Contains("LOOTASK|w1|2|" + Op + "|1|bandit_7|" + Coin + "|50", r.ToGame);
        Assert.Equal(OpState.EngineApplying, r.Journal!.Get("lt:" + Op)!.State);                         // durable intent first
        r.O.GameLoot(new[] { "LOOT", "res", "2", "1", "ok", "bandit_7", Coin, "50", "1.0000", Op }, isHost: true);
        Assert.Contains("loot|res|2|" + Op + "|ok|bandit_7|" + Coin + "|50", r.ToGuests);
        Assert.Equal(OpState.Delivered, r.Journal.Get("lt:" + Op)!.State);                               // not complete until the guest says it kept it
        r.O.PeerReceipt(2, new[] { "loot", "receipt", Op, "kept" });
        var rec = r.Journal.Get("lt:" + Op)!;
        Assert.Equal(OpState.Complete, rec.State);
        Assert.Equal("ok", rec.Outcome);
        Assert.DoesNotContain(r.ToGame, l => l.StartsWith("LOOTPUT"));
    }

    [Fact]
    public void The_same_ask_again_gets_the_same_answer_and_the_game_is_not_asked_twice()
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        var ask = new[] { "loot", "ask", Op, "bandit_7", Coin, "50" };
        r.O.PeerAsk(2, ask);
        r.O.GameLoot(new[] { "LOOT", "res", "2", "1", "ok", "bandit_7", Coin, "50", "1.0000", Op }, true);
        int asks = r.ToGame.Count(l => l.StartsWith("LOOTASK"));
        r.ToGuests.Clear();
        r.O.PeerAsk(2, ask);                                                                              // the guest's retry (it restarted)
        Assert.Equal(asks, r.ToGame.Count(l => l.StartsWith("LOOTASK")));
        Assert.Contains("loot|res|2|" + Op + "|ok|bandit_7|" + Coin + "|50", r.ToGuests);
        r.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "999" });                            // the same id with other contents: refused
        Assert.Equal(asks, r.ToGame.Count(l => l.StartsWith("LOOTASK")));
    }

    [Fact]
    public void An_ask_interrupted_by_a_restart_before_the_decision_is_never_granted_and_a_recorded_decision_stands()
    {
        string path;
        const string Op2 = "fedcba9876543210fedcba9876543210";
        using (var first = new Rig(journal: true))
        {
            first.O.PeerScope(2, "w1"); first.Step(host: true);
            first.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "50" });                      // the game was told; the agent died before the answer
            first.O.PeerAsk(2, new[] { "loot", "ask", Op2, "bandit_7", Coin, "5" });
            first.O.GameLoot(new[] { "LOOT", "res", "2", "1", "ok", "bandit_7", Coin, "5", "1.0000", Op2 }, true);   // decided, receipt never came
            path = first.Journal!.Path;
            first.Journal.Dispose(); first.Journal = null;
            var j2 = new OperationJournal(path);
            Assert.Equal(2, j2.Recover().Count);                                                          // both quarantined
            using var second = new Rig();
            second.Journal = j2;
            second.O.PeerScope(2, "w1"); second.Step(host: true);
            second.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "50" });
            second.O.PeerAsk(2, new[] { "loot", "ask", Op2, "bandit_7", Coin, "5" });
            Assert.DoesNotContain(second.ToGame, l => l.StartsWith("LOOTASK"));
            Assert.Contains("loot|res|2|" + Op + "|gone|bandit_7|" + Coin + "|50", second.ToGuests);
            Assert.Contains("loot|res|2|" + Op2 + "|ok|bandit_7|" + Coin + "|5", second.ToGuests);        // the durable decision is replayed
            second.O.PeerReceipt(2, new[] { "loot", "receipt", Op2, "kept" });
            Assert.Equal(OpState.Complete, j2.Get("lt:" + Op2)!.State);
            j2.Dispose(); second.Journal = null;
        }
    }

    [Theory]
    [InlineData("returned")]
    [InlineData("reverted")]
    public void A_granted_item_the_guest_did_not_keep_is_put_back_in_the_hosts_copy_and_the_guests(string how)
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        r.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "50" });
        r.O.GameLoot(new[] { "LOOT", "res", "2", "1", "ok", "bandit_7", Coin, "50", "0.5000", Op }, true);
        r.O.PeerReceipt(2, new[] { "loot", "receipt", Op, how });
        Assert.Contains("LOOTRESTORE|w1|"+Op+"|bandit_7|"+Coin+"|50|0.5000|",r.ToGame);
        Assert.DoesNotContain(r.ToGuests,l=>l.StartsWith("loot|put|"));
        var ack=new[]{"LOOT","restored",Op,"w1","bandit_7",Coin,"50","0.5000",new string('a',32),"verified",""};
        r.O.GameLoot(ack,true);
        Assert.Contains("loot|put|bandit_7|" + Coin + "|50|0.5000", r.ToGuests);
        Assert.Equal(OpState.Complete,r.Journal!.Get("rs:"+Op)!.State);
        Assert.Contains("native-instance="+new string('a',32),r.Journal.Get("rs:"+Op)!.Evidence);
        int puts = r.ToGame.Count(l => l.StartsWith("LOOTRESTORE"));
        r.O.PeerReceipt(2, new[] { "loot", "receipt", Op, how });                                        // a repeated receipt puts nothing back twice
        r.O.GameLoot(ack,true);
        Assert.Equal(puts, r.ToGame.Count(l => l.StartsWith("LOOTRESTORE")));
        Assert.Single(r.ToGuests.Where(l=>l.StartsWith("loot|put|")));
    }

    [Fact]
    public void A_gone_answer_returned_by_the_guest_needs_no_compensation()
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        r.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "50" });
        r.O.GameLoot(new[] { "LOOT", "res", "2", "1", "gone", "bandit_7", Coin, "50", "1.0000", Op }, true);
        r.O.PeerReceipt(2, new[] { "loot", "receipt", Op, "returned" });
        Assert.DoesNotContain(r.ToGame, l => l.StartsWith("LOOTPUT"));
        Assert.Equal(OpState.Complete, r.Journal!.Get("lt:" + Op)!.State);
    }

    private sealed class GuestRig : IDisposable
    {
        public long Now = 1000;
        public readonly List<string> ToGame = new(), ToPeers = new(), ToGuests = new(), Notes = new(), Log = new();
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "kcdus-cust-" + Guid.NewGuid().ToString("N")[..8]);
        public GuestCustody Custody;
        public OutcomesCoordinator O;
        public GuestRig() { Directory.CreateDirectory(Dir); Custody = new GuestCustody(Path.Combine(Dir, "c.jsonl")); O = Make(); }
        public OutcomesCoordinator Make() => new(() => Now, ToGame.Add, ToPeers.Add, ToGuests.Add, Notes.Add, Log.Add, id => "Hans", () => null, Custody);
        /// <summary>The agent restarts: the same file, a new journal and coordinator.</summary>
        public void Restart() { Custody.Dispose(); Custody = new GuestCustody(Path.Combine(Dir, "c.jsonl")); O = Make(); }
        public void Step() { Now += 100; O.Tick(true, true, false, "w1"); }
        public void Dispose() { Custody.Dispose(); try { Directory.Delete(Dir, true); } catch { } }
    }

    private static string AskOp(GuestRig g) => g.ToPeers.Last(l => l.StartsWith("loot|ask|")).Split('|')[2];

    [Fact]
    public void A_completed_receipt_is_retried_after_restart_without_repeating_the_game_settlement()
    {
        using var g = new GuestRig();
        g.O.PeerScope(1, "w1"); g.Step();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "abcd1234", "5000" }, false);
        g.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "3", "bandit_7", Coin, "50", "1.0000", "0", "6000" }, false);
        string op = AskOp(g);
        g.O.HostLoot(new[] { "loot", "res", "2", op, "ok", "bandit_7", Coin, "50" }, 2);
        g.O.GameLoot(new[] { "LOOT", "kept", "abcd1234", "3" }, false);
        g.Restart(); g.ToPeers.Clear(); g.ToGame.Clear();
        g.O.PeerScope(1, "w1"); g.Step();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "dcba4321", "7000" }, false);
        Assert.Contains("loot|receipt|" + op + "|kept", g.ToPeers);
        Assert.DoesNotContain(g.ToGame, l => l.StartsWith("LOOTRECOVER|"));
    }

    [Fact]
    public void Host_compensation_is_journalled_before_native_put_and_survives_restart_as_unknown()
    {
        using var r = new Rig(journal: true);
        r.O.PeerScope(2, "w1"); r.Step(host: true);
        r.O.PeerAsk(2, new[] { "loot", "ask", Op, "bandit_7", Coin, "50" });
        r.O.GameLoot(new[] { "LOOT", "res", "2", "1", "ok", "bandit_7", Coin, "50", "0.5000", Op }, true);
        r.O.PeerReceipt(2, new[] { "loot", "receipt", Op, "returned" });
        var intent = r.Journal!.Get("rs:" + Op)!;
        Assert.Equal(OpState.EngineApplying, intent.State);
        Assert.StartsWith("bandit_7|" + Coin + "|50|0.5000", intent.Evidence);
        r.Journal.Recover();
        Assert.Equal(OpState.RecoveryRequired, r.Journal.Get(intent.OperationId)!.State);
        int puts = r.ToGame.Count(l => l.StartsWith("LOOTRESTORE|"));
        r.O.PeerReceipt(2, new[] { "loot", "receipt", Op, "returned" });
        Assert.Equal(puts, r.ToGame.Count(l => l.StartsWith("LOOTRESTORE|")));
    }

    [Theory]
    [InlineData("w2","abcd1234","bandit_7")]
    [InlineData("w1","deadbeef","bandit_7")]
    [InlineData("w1","abcd1234","bandit_8")]
    public void A_restore_ack_from_another_world_load_or_holder_cannot_complete_compensation(string scope,string nonce,string holder)
    {
        using var r=new Rig(journal:true);r.O.PeerScope(2,"w1");r.Step(host:true);
        r.O.GameLootMode(new[]{"LOOTMODE","on","w1","host","abcd1234","5000"},true);
        r.O.PeerAsk(2,new[]{"loot","ask",Op,"bandit_7",Coin,"50"});
        r.O.GameLoot(new[]{"LOOT","res","2","1","ok","bandit_7",Coin,"50","0.5000",Op},true);
        r.O.PeerReceipt(2,new[]{"loot","receipt",Op,"returned"});
        r.O.GameLoot(new[]{"LOOT","restored",Op,scope,holder,Coin,"50","0.5000",new string('a',32),"verified",nonce},true);
        Assert.Equal(OpState.EngineApplying,r.Journal!.Get("rs:"+Op)!.State);
        Assert.DoesNotContain(r.ToGuests,l=>l.StartsWith("loot|put|"));
    }

    [Fact]
    public void An_uncertain_native_restore_keeps_the_compensation_in_recovery_and_is_not_announced()
    {
        using var r=new Rig(journal:true);r.O.PeerScope(2,"w1");r.Step(host:true);
        r.O.PeerAsk(2,new[]{"loot","ask",Op,"bandit_7",Coin,"50"});
        r.O.GameLoot(new[]{"LOOT","res","2","1","ok","bandit_7",Coin,"50","0.5000",Op},true);
        r.O.PeerReceipt(2,new[]{"loot","receipt",Op,"returned"});
        r.O.GameLoot(new[]{"LOOT","restored",Op,"w1","bandit_7",Coin,"50","0.5000","","uncertain",""},true);
        Assert.Equal(OpState.RecoveryRequired,r.Journal!.Get("rs:"+Op)!.State);
        Assert.DoesNotContain(r.ToGuests,l=>l.StartsWith("loot|put|"));
    }

    [Fact]
    public void Another_guests_receipt_cannot_compensate_or_complete_the_original_guests_take()
    {
        using var r=new Rig(journal:true);r.O.PeerScope(2,"w1");r.Step(host:true);
        r.O.PeerAsk(2,new[]{"loot","ask",Op,"bandit_7",Coin,"50"});
        r.O.GameLoot(new[]{"LOOT","res","2","1","ok","bandit_7",Coin,"50","0.5000",Op},true);
        r.O.PeerReceipt(3,new[]{"loot","receipt",Op,"returned"});
        Assert.Null(r.Journal!.Get("rs:"+Op));Assert.Equal(OpState.Delivered,r.Journal.Get("lt:"+Op)!.State);
        Assert.DoesNotContain(r.ToGame,l=>l.StartsWith("LOOTRESTORE"));
    }

    [Fact]
    public void A_guests_take_is_journalled_on_disk_before_the_host_hears_of_it_and_the_hosts_answer_reaches_the_game()
    {
        using var g = new GuestRig();
        g.O.PeerScope(1, "w1"); g.Step();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "abcd1234", "5000" }, false);
        g.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "3", "bandit_7", Coin, "50", "1.0000", "0", "6000" }, isHost: false);
        string op = AskOp(g);
        Assert.True(OutcomesRules.ValidOp(op));
        Assert.Equal("loot|ask|" + op + "|bandit_7|" + Coin + "|50", g.ToPeers.Last());
        Assert.Single(g.Custody.Open());
        g.O.HostLoot(new[] { "loot", "res", "2", op, "ok", "bandit_7", Coin, "50" }, myId: 2);
        Assert.Contains("LOOTRES|w1|3|ok|bandit_7|" + Coin + "|50", g.ToGame);
        g.O.GameLoot(new[] { "LOOT", "kept", "abcd1234", "3" }, isHost: false);
        Assert.Contains("loot|receipt|" + op + "|kept", g.ToPeers);
        Assert.Empty(g.Custody.Open());
    }

    [Fact]
    public void After_a_restart_and_a_reload_an_unanswered_take_is_asked_again_and_settled_by_recovery_not_guessed()
    {
        using var g = new GuestRig();
        g.O.PeerScope(1, "w1"); g.Step();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "abcd1234", "5000" }, false);
        g.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "3", "bandit_7", Coin, "50", "1.0000", "0", "6000" }, isHost: false);
        string op = AskOp(g);
        g.Restart();                                                                                     // the agent and the game went down before any answer
        g.O.PeerScope(1, "w1"); g.Step();
        g.ToPeers.Clear();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "99998888", "7000" }, false);         // a new load
        Assert.Contains("loot|ask|" + op + "|bandit_7|" + Coin + "|50", g.ToPeers);                     // asked again with the SAME id
        Assert.DoesNotContain(g.ToGame, l => l.StartsWith("LOOTRECOVER"));                               // no answer: nothing guessed
        g.O.HostLoot(new[] { "loot", "res", "2", op, "gone", "bandit_7", Coin, "50" }, myId: 2);
        Assert.Contains("LOOTRECOVER|w1|" + op + "|" + Coin + "|50|0|6000|gone", g.ToGame);             // the take of an earlier load: recovery decides
        g.O.GameLoot(new[] { "LOOT", "recovered", op, "50", "0", "returned" }, isHost: false);
        Assert.Contains("loot|receipt|" + op + "|returned", g.ToPeers);
        Assert.Empty(g.Custody.Open());
    }

    [Fact]
    public void An_answered_take_of_an_earlier_load_is_recovered_at_once_and_a_take_the_game_already_returned_completes_as_returned()
    {
        using var g = new GuestRig();
        g.O.PeerScope(1, "w1"); g.Step();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "abcd1234", "5000" }, false);
        g.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "3", "bandit_7", Coin, "50", "1.0000", "120", "6000" }, isHost: false);
        string op1 = AskOp(g);
        g.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "4", "chest", Coin, "5", "1.0000", "0", "6100" }, isHost: false);
        string op2 = AskOp(g);
        g.O.HostLoot(new[] { "loot", "res", "2", op1, "ok", "bandit_7", Coin, "50" }, myId: 2);         // answered, but the game reloaded before settling
        g.O.GameLoot(new[] { "LOOT", "returned", "abcd1234", "4", "1" }, isHost: false);                 // the game's own 20 s timeout gave op2 back
        Assert.Contains("loot|receipt|" + op2 + "|returned", g.ToPeers);
        g.ToGame.Clear();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "77776666", "5500" }, false);
        Assert.Equal(new[] { "LOOTRECOVER|w1|" + op1 + "|" + Coin + "|50|120|6000|ok" }, g.ToGame.Where(l => l.StartsWith("LOOTRECOVER")));
        g.O.GameLoot(new[] { "LOOT", "recovered", op1, "0", "0", "reverted" }, isHost: false);          // the save was older than the take
        Assert.Contains("loot|receipt|" + op1 + "|reverted", g.ToPeers);                                 // the host will put it back
        Assert.Empty(g.Custody.Open());
    }

    [Fact]
    public void A_take_is_never_settled_as_kept_without_an_ok()
    {
        using var g = new GuestRig();
        g.O.PeerScope(1, "w1"); g.Step();
        g.O.GameLootMode(new[] { "LOOTMODE", "on", "w1", "guest", "abcd1234", "5000" }, false);
        g.O.GameLoot(new[] { "LOOT", "ask", "abcd1234", "3", "bandit_7", Coin, "50", "1.0000", "0", "6000" }, isHost: false);
        g.O.GameLoot(new[] { "LOOT", "kept", "abcd1234", "3" }, isHost: false);
        Assert.Single(g.Custody.Open());
        Assert.DoesNotContain(g.ToPeers, l => l.StartsWith("loot|receipt|"));
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
