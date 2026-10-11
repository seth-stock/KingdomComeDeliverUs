// SPDX-License-Identifier: GPL-3.0-only
// Shared quest rewards (mod/kcdus/lua/rewards.lua): what a mirrored quest step paid the host is paid to the guest, never twice and never loot.
using KcdUs.Agent;

namespace KcdUs.Tests;

public class RewardsLuaTests
{
    private const string Coin = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private const string Sword = "cccccccc-0000-0000-0000-000000000001";

    private static LuaMod World(bool host)
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~LOOTMODE|1|w1|" + (host ? 1 : 0));
        m.Advance(0.6);
        m.ClearLog();
        return m;
    }

    private static double HenryHas(LuaMod m, string cls) =>
        m.Num("(function() local n = 0 for _, it in ipairs(__henry.items) do if it.class == '" + cls + "' then n = n + it.amount end end return n end)()");

    [Fact]
    public void The_host_measures_what_a_quests_completion_paid_and_says_it_once()
    {
        var m = World(host: true);
        m.Do("KCDUS.Rewards.questChanged('q_test', '1|1|1,2', '1|0|1,2')");
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 150)");                 // the quest's script pays
        m.Advance(3.5);
        var line = Assert.Single(m.Lines("KCDUS|QREWARD|q_test|"));
        Assert.EndsWith("|" + Coin + ":150:1.0000|done", line);
        m.Advance(3.5);
        Assert.Single(m.Lines("KCDUS|QREWARD|"));
    }

    [Fact]
    public void The_first_look_at_a_quest_and_a_step_that_paid_nothing_say_nothing_and_a_guest_never_measures()
    {
        var m = World(host: true);
        m.Do("KCDUS.Rewards.questChanged('q_test', '1|0|1', nil)");
        m.Do("KCDUS.Rewards.questChanged('q_other', '1|0|1,2', '1|0|1')");
        m.Advance(3.5);
        Assert.Empty(m.Lines("KCDUS|QREWARD|"));
        var g = World(host: false);
        g.Do("KCDUS.Rewards.questChanged('q_test', '1|0|1,2', '1|0|1')");
        g.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 150)");
        g.Advance(3.5);
        Assert.Empty(g.Lines("KCDUS|QREWARD|"));
    }

    [Fact]
    public void Loot_taken_during_the_window_is_not_counted_as_the_reward()
    {
        var m = World(host: true);
        m.Do("__mkNpc('bandit_7', 0, 12, 20, 30); __world_ents[1].dead = true; __world_ents[1].items = { __item('" + Coin + "', 40) }");
        m.Advance(0.6);
        m.Do("KCDUS.Rewards.questChanged('q_test', '1|1|1,2', '1|0|1,2')");
        m.Do("__world_ents[1].items = {}; __henry.items[#__henry.items + 1] = __item('" + Coin + "', 40)");   // looted the body meanwhile
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 100)");                                // and the quest paid 100
        m.Advance(3.5);
        Assert.EndsWith("|" + Coin + ":100:1.0000|done", Assert.Single(m.Lines("KCDUS|QREWARD|")));
    }

    [Fact]
    public void A_guest_is_paid_what_its_own_game_did_not_pay_already_and_each_key_once()
    {
        var m = World(host: false);
        m.Do("KCDUS.Rewards.appliedHere('q_test')");
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 50)");                  // the native script paid part of it here
        m.Send("2~QRGIVE|q_test|0a1b2c3d|" + Coin + ":150:1.0000;" + Sword + ":1:0.9000");
        Assert.Equal(150, HenryHas(m, Coin));
        Assert.Equal(1, HenryHas(m, Sword));
        Assert.Single(m.Lines("KCDUS|QRGIVEN|q_test|0a1b2c3d|101|50"));
        m.Send("3~QRGIVE|q_test|0a1b2c3d|" + Coin + ":150:1.0000");
        Assert.Equal(150, HenryHas(m, Coin));
        m.ClearLog(); m.Advance(1.2);
        Assert.Empty(m.Lines("KCDUS|LOOT|"));                                                     // a reward is not a take
    }

    [Fact]
    public void The_host_never_pays_itself_and_bad_lines_are_ignored()
    {
        var m = World(host: true);
        m.Send("2~QRGIVE|q_test|0a1b2c3d|" + Coin + ":150:1.0000");
        Assert.Empty(m.Lines("KCDUS|QRGIVEN|"));
        var g = World(host: false);
        g.Send("2~QRGIVE|q test|0a1b2c3d|" + Coin + ":150:1.0000");
        g.Send("3~QRGIVE|q_test|zz|" + Coin + ":150:1.0000");
        Assert.Empty(g.Lines("KCDUS|QRGIVEN|"));
    }

    [Theory]
    [InlineData("0a1b2c3d", "5ef63059-322e-4e1b-abe8-926e100c770e:150:1.0000", true)]
    [InlineData("0A1B2C3D", "5ef63059-322e-4e1b-abe8-926e100c770e:150:1.0000", false)]
    [InlineData("0a1b2c3d", "5ef63059-322e-4e1b-abe8-926e100c770e:0:1.0000", false)]
    [InlineData("0a1b2c3d", "5ef63059-322e-4e1b-abe8-926e100c770e:150:2", false)]
    [InlineData("0a1b2c3d", "not-a-class:1:1", false)]
    [InlineData("0a1b2c3d", "", false)]
    public void The_agent_checks_a_reward_before_it_goes_anywhere(string key, string items, bool ok)
    {
        var quest = QuestCatalog.All.First(q => q.Tier == "open" && q.Dlc.Length == 0 && q.Kind is "main" or "side" or "activity");
        Assert.Equal(ok, QuestMirrorRules.ValidReward(quest.Code, key, items));
        Assert.False(QuestMirrorRules.ValidReward("no_such_quest", "0a1b2c3d", "5ef63059-322e-4e1b-abe8-926e100c770e:150:1.0000"));
    }

    // ---- causal consequences: objectives pay natively in the guest's mirror; a quest's completion is shared, its XP exactly

    [Fact]
    public void An_objective_change_is_paid_by_the_guests_own_mirror_and_never_measured_here()
    {
        var m = World(host: true);
        m.Do("KCDUS.Rewards.questChanged('q_test', '1|0|1,2', '1|0|1')");
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 150)");
        m.Advance(3.5);
        Assert.Empty(m.Lines("KCDUS|QREWARD|"));
    }

    [Fact]
    public void A_completion_reward_is_given_without_a_mirrored_application()
    {
        var m = World(host: false);
        m.Send("2~QRGIVE|q_test|0a1b2c3d|" + Coin + ":150:1.0000|done");
        Assert.Equal(150, HenryHas(m, Coin));
        Assert.Single(m.Lines("KCDUS|QRGIVEN|q_test|0a1b2c3d|150|0"));
    }

    [Fact]
    public void The_host_forwards_only_quest_state_caused_xp_from_the_adapter_starting_from_now()
    {
        var m = World(host: true);
        m.Do("KCDUS.Rewards.myIdentity=function() return '' end");
        m.Do("__world.xpAdapter = true; __world.xpSince = { last = 5, n = 0, lost = 0 }");
        m.Advance(0.5);                                                                           // the first look only sets where to start
        m.Do(@"__world.xpSince = { last = 8, n = 3, lost = 0,
            e1 = '6,0,9,4096,4096,1,1,2306d30,,0011',
            e2 = '7,1,26,128,128,1,1,2201b10,,',
            e3 = '8,0,3,32,32,1,1,21c8fd0,,aabb' }");
        m.Advance(0.5);
        var xp = Assert.Single(m.Lines("KCDUS|QXP|"));                                           // only the quest-state cause; script and objective XP stay
        var f = xp.Split('|');
        Assert.Matches("^x[0-9a-f]{6}6$", f[2]);
        Assert.Equal(new[] { "0", "9", "4096" }, f[3..6]);
        m.Advance(0.5);
        Assert.Single(m.Lines("KCDUS|QXP|"));                                                     // seen once
    }

    [Fact]
    public void Without_the_adapter_no_xp_is_forwarded_and_the_host_never_applies_its_own()
    {
        var m = World(host: true);
        m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|QXP|"));
        m.Send("2~QXPGIVE|xabcdef1|0|9|4096");
        Assert.Equal(0, m.Num("#__xpAwards"));
    }

    [Fact]
    public void The_xp_cursor_tracks_returned_rows_instead_of_skipping_to_the_ring_head()
    {
        var m = World(host: true);
        m.Do("__world.xpAdapter=true; KCDUS.Rewards.myIdentity=function() return '' end; KCDUS.Rewards.xpSeq=10; __world.xpSince={last=50,n=1,lost=0,e1='11,0,9,128,128,1,1,2306d30,,0011'}");
        m.Do("KCDUS.Rewards.xpDrain()");
        Assert.Equal(11, m.Num("KCDUS.Rewards.xpSeq"));
        m.Do("__world.xpSince.e1='12,0,9,128,128,1,1,2306d30,,0011'; KCDUS.Rewards.xpDrain()");
        Assert.Equal(12, m.Num("KCDUS.Rewards.xpSeq")); Assert.Equal(2, m.Lines("KCDUS|QXP|").Count);
    }

    [Fact]
    public void Unknown_recipient_identity_never_forwards_another_actors_quest_xp()
    {
        var m = World(host: true);
        m.Do("__world.xpAdapter=true; KCDUS.Rewards.myIdentity=function() return nil end; KCDUS.Rewards.xpSeq=10; __world.xpSince={last=11,n=1,lost=2,e1='11,0,9,128,128,1,1,2306d30,other,0011'}; KCDUS.Rewards.xpDrain()");
        Assert.Empty(m.Lines("KCDUS|QXP|")); Assert.Single(m.Lines("KCDUS|QXPGAP|"));
    }

    [Fact]
    public void A_guest_applies_a_quest_xp_once_by_name_and_amount_and_reads_it_back()
    {
        var m = World(host: false);
        m.Do("__world.xpAdapter = true; __world.statIndex = { prg = 9 }; __world.skillIndex = { reading = 26 }");
        m.Send("2~QXPGIVE|xabcdef1|0|9|4096");
        m.Send("3~QXPGIVE|xabcdef1|0|9|4096");                                                  // the same key again: nothing
        m.Send("4~QXPGIVE|xabcdef2|1|26|128");
        Assert.Equal("stat:prg:32", m.Str("__xpAwards[1]"));
        Assert.Equal("skill:reading:1", m.Str("__xpAwards[2]"));
        Assert.Equal(2, m.Num("#__xpAwards"));
        Assert.Single(m.Lines("KCDUS|QXPGIVEN|xabcdef1|prg|1|1"));
    }

    [Theory]
    [InlineData("xabcdef1", "0", "12", "4096")]      // no stat 12
    [InlineData("xabcdef1", "1", "40", "128")]       // no skill 40
    [InlineData("zabcdef1", "0", "9", "4096")]       // not a key
    [InlineData("xabcdef1", "0", "9", "0")]          // nothing
    public void Bad_xp_lines_are_ignored(string key, string kind, string idx, string raw)
    {
        var m = World(host: false);
        m.Send($"2~QXPGIVE|{key}|{kind}|{idx}|{raw}");
        Assert.Equal(0, m.Num("#__xpAwards"));
        Assert.False(KcdUs.Agent.QuestMirrorRules.ValidXp(key, kind, idx, raw));
    }
}
