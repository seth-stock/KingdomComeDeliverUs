// SPDX-License-Identifier: GPL-3.0-only
// The shared fights and loot, as the mod's real Lua runs them against the stubbed engine (the engine facts they rest on were proved in the private engine).
namespace KcdUs.Tests;

public class SharedOutcomesLuaTests
{
    private const string Scope = "w1";

    private static LuaMod World()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(0.5);
        m.ClearLog();
        return m;
    }

    private static LuaMod Combat()
    {
        var m = World();
        m.Do("__mkNpc('bandit_7', 100, 12, 20, 30); __mkNpc('guard_far', 100, 60, 20, 30)");     // 2 m and 50 m from the player (10,20,30)
        m.Send("1~CMBMODE|1|" + Scope);
        m.Advance(0.5);                                                                           // the baseline
        m.ClearLog();
        return m;
    }

    // ------------------------------------------------------------------ combat

    [Fact]
    public void Damage_to_an_npc_near_the_player_is_reported_with_the_amount_and_the_health_left()
    {
        var m = Combat();
        m.Do("__world_ents[1].hp = 80");
        m.Advance(0.5);
        Assert.Equal(new[] { "KCDUS|CMB|bandit_7|20.00|80.00|0" }, m.Lines("KCDUS|CMB|"));
        m.ClearLog(); m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|CMB|"));                                                      // reported once
    }

    [Fact]
    public void A_death_is_reported_once_as_dead_even_when_the_last_blow_was_small()
    {
        var m = Combat();
        m.Do("__world_ents[1].hp = 0; __world_ents[1].dead = true");
        m.Advance(1.0);
        Assert.Equal(new[] { "KCDUS|CMB|bandit_7|100.00|0.00|1" }, m.Lines("KCDUS|CMB|"));
    }

    [Fact]
    public void An_npc_the_player_is_not_near_is_not_reported_the_npcs_own_fights_are_nobodys_to_share()
    {
        var m = Combat();
        m.Do("__world_ents[2].hp = 10");                                                          // 50 m away
        m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|CMB|"));
    }

    [Fact]
    public void An_npc_that_left_the_watched_radius_starts_from_what_it_has_when_it_comes_back()
    {
        var m = Combat();
        m.Do("__world_ents[1].pos = { x = 400, y = 20, z = 30 }");                                // out of the 70 m radius
        m.Advance(0.5);
        m.Do("__world_ents[1].hp = 30");                                                          // hurt out of sight (an NPC's own fight)
        m.Advance(0.5);
        m.Do("__world_ents[1].pos = { x = 12, y = 20, z = 30 }");
        m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|CMB|"));                                                      // not mistaken for the player's blow
    }

    [Fact]
    public void A_friends_damage_is_dealt_the_ordinary_way_with_no_attacker_and_is_not_reported_back()
    {
        var m = Combat();
        m.Send("2~CMBAPPLY|" + Scope + "|bandit_7|12|0");
        Assert.Equal(12, m.Num("__world_ents[1].dealt"));
        Assert.True(m.Bool("__world_ents[1].calls[1].attacker == nil and __world_ents[1].calls[1].suppress == true and __world_ents[1].calls[1].stam == 0"));
        Assert.Single(m.Lines("KCDUS|CMBAPPLIED|bandit_7|100.00|88.00|applied"));
        m.ClearLog(); m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|CMB|"));                                                      // the local watcher does not echo it
    }

    [Fact]
    public void A_friends_kill_kills_this_copy_too_and_a_second_report_changes_nothing()
    {
        var m = Combat();
        m.Send("2~CMBAPPLY|" + Scope + "|bandit_7|3|1");
        Assert.True(m.Bool("__world_ents[1].dead"));
        m.Send("3~CMBAPPLY|" + Scope + "|bandit_7|50|0");
        Assert.Single(m.Lines("KCDUS|CMBAPPLIED|bandit_7|0|0|already-dead"));
        Assert.Equal(1, m.Num("#__world_ents[1].calls"));
    }

    [Fact]
    public void Nothing_is_applied_off_in_another_world_to_a_ghost_or_with_nonsense_numbers()
    {
        var m = Combat();
        m.Send("2~CMBAPPLY|other-world|bandit_7|12|0");
        m.Send("3~CMBAPPLY|" + Scope + "|kcdus_ghost_2|12|0");
        m.Send("4~CMBAPPLY|" + Scope + "|bandit_7|-5|0");
        m.Send("5~CMBAPPLY|" + Scope + "|bandit_7|99999|0");
        m.Send("6~CMBAPPLY|" + Scope + "|bad name!|1|0");
        Assert.Equal(0, m.Num("__world_ents[1].dealt"));
        m.Send("7~CMBMODE|0");
        m.Send("8~CMBAPPLY|" + Scope + "|bandit_7|12|0");
        Assert.Equal(0, m.Num("__world_ents[1].dealt"));                                          // off: nothing
    }

    [Fact]
    public void A_horse_is_not_a_combat_target_and_a_woman_is_a_person()
    {
        var m = Combat();
        m.Do("__mkNpc('rat_horse_1', 100, 12, 22, 30, 'Horse')");
        m.Send("2~CMBAPPLY|" + Scope + "|rat_horse_1|12|0");
        Assert.Equal(0, m.Num("__world_ents[3].dealt"));
        m.Do("__mkNpc('rat_maid', 100, 14, 22, 30, 'NPC_Female')");
        m.Send("3~CMBAPPLY|" + Scope + "|rat_maid|12|0");
        Assert.Equal(12, m.Num("__world_ents[4].dealt"));
    }

    // ------------------------------------------------------------------ loot

    [Fact]
    public void Accepted_but_inert_native_damage_is_not_counted_as_applied()
    {
        var m = Combat();
        m.Do("__world_ents[1].soul.DealDamage=function() end");
        m.Send("2~CMBAPPLY|" + Scope + "|bandit_7|12|0");
        Assert.Equal(0, m.Num("KCDUS.Combat.applied"));
        Assert.Single(m.Lines("KCDUS|CMBAPPLIED|bandit_7|100.00|100.00|readback-unverified"));
    }

    [Fact]
    public void Partial_native_damage_before_an_error_does_not_echo_or_claim_success()
    {
        var m = Combat();
        m.Do("__world_ents[1].soul.DealDamage=function() __world_ents[1].hp=94; error('native partial') end");
        m.Send("2~CMBAPPLY|" + Scope + "|bandit_7|12|0");
        Assert.Equal(0, m.Num("KCDUS.Combat.applied"));
        Assert.Single(m.Lines("KCDUS|CMBAPPLIED|bandit_7|100.00|94.00|call-failed"));
        m.ClearLog();m.Advance(1);
        Assert.Empty(m.Lines("KCDUS|CMB|"));
    }

    [Fact]
    public void Missing_native_health_readback_is_uncertain()
    {
        var m = Combat();
        m.Do("__world_ents[1].soul.DealDamage=function() __world_ents[1].soul.GetState=function() return nil end end");
        m.Send("2~CMBAPPLY|" + Scope + "|bandit_7|12|0");
        Assert.Equal(0, m.Num("KCDUS.Combat.applied"));
        Assert.Single(m.Lines("KCDUS|CMBAPPLIED|bandit_7|100.00|?|readback-unverified"));
    }

    private const string Coin = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private const string Coat = "a856e87a-8065-4338-919d-0aff7a63341d";

    private static LuaMod Loot(bool host)
    {
        var m = World();
        m.Do("__mkNpc('bandit_7', 0, 12, 20, 30); __world_ents[1].dead = true; __world_ents[1].items = { __item('" + Coin + "', 201), __item('" + Coat + "', 1, 0.7) }; __mkStash(11, 20, 30, { __item('" + Coin + "', 40) })");
        m.Send("1~LOOTMODE|1|" + Scope + "|" + (host ? 1 : 0));
        m.Advance(0.6);                                                                           // the baseline
        m.ClearLog();
        return m;
    }

    private static void Take(LuaMod m, int ent, string cls, int n)
    {
        // what the loot screen does: the stack's amount goes down on the body and up in Henry's pack
        m.Do("local e = __world_ents[" + ent + "]; for _, it in ipairs(e.items) do if it.class == '" + cls + "' then it.amount = it.amount - " + n + " end end; __henry.items[#__henry.items + 1] = __item('" + cls + "', " + n + ")");
        m.Advance(0.6);
    }

    private static double HenryHas(LuaMod m, string cls) =>
        m.Num("(function() local n = 0 for _, it in ipairs(__henry.items) do if it.class == '" + cls + "' then n = n + it.amount end end return n end)()");

    [Fact]
    public void A_host_take_from_a_body_is_announced_for_the_guests()
    {
        var m = Loot(host: true);
        Take(m, 1, Coin, 201);
        Assert.Equal(new[] { "KCDUS|LOOT|took|bandit_7|" + Coin + "|201|1.0000" }, m.Lines("KCDUS|LOOT|"));
    }

    [Fact]
    public void A_guest_take_asks_the_host_and_keeps_the_item_only_when_the_host_says_ok()
    {
        var m = Loot(host: false);
        double before = HenryHas(m, Coin);
        Take(m, 1, Coin, 50);
        var ask = m.Lines("KCDUS|LOOT|ask|").Single().Split('|');
        Assert.Equal(new[] { "bandit_7", Coin, "50" }, new[] { ask[5], ask[6], ask[7] });
        string tok = ask[4];
        m.Send("2~LOOTRES|" + Scope + "|" + tok + "|ok|bandit_7|" + Coin + "|50");
        Assert.Equal(1, m.Lines("KCDUS|LOOT|confirmed|").Count);
        Assert.Equal(before + 50, HenryHas(m, Coin));                                              // still Henry's
    }

    [Fact]
    public void When_the_host_says_gone_the_item_is_taken_back_off_henry()
    {
        var m = Loot(host: false);
        double before = HenryHas(m, Coin);
        Take(m, 1, Coin, 50);
        string tok = m.Lines("KCDUS|LOOT|ask|").Single().Split('|')[4];
        m.Send("2~LOOTRES|" + Scope + "|" + tok + "|gone|bandit_7|" + Coin + "|50");
        Assert.Equal(before, HenryHas(m, Coin));
        Assert.Single(m.Lines("KCDUS|LOOT|unconfirmed|bandit_7"));
    }

    [Fact]
    public void An_ask_the_host_never_answers_is_taken_back_after_twenty_seconds_and_before_a_save()
    {
        var m = Loot(host: false);
        double before = HenryHas(m, Coin);
        Take(m, 1, Coin, 50);
        m.Advance(10.2);
        Assert.Empty(m.Lines("KCDUS|LOOT|unconfirmed|"));                                         // 10 s: the host may just be slow
        Assert.Equal(before + 50, HenryHas(m, Coin));
        m.Advance(15.2);
        Assert.Single(m.Lines("KCDUS|LOOT|unconfirmed|"));
        Assert.Equal(before, HenryHas(m, Coin));
        Take(m, 2, Coin, 5);                                                                       // a second one, then a world save settles it at once
        Assert.Equal(before + 5, HenryHas(m, Coin));
        m.Send("3~LOOTSETTLE");
        Assert.Equal(2, m.Lines("KCDUS|LOOT|unconfirmed|").Count);
        Assert.Equal(before, HenryHas(m, Coin));
    }

    [Fact]
    public void The_host_decides_a_guests_ask_first_come_first_served_and_tells_the_other_guests()
    {
        var m = Loot(host: true);
        m.Send("2~LOOTASK|" + Scope + "|2|abcd1234|1|bandit_7|" + Coin + "|100");
        Assert.Single(m.Lines("KCDUS|LOOT|res|2|1|ok|bandit_7|" + Coin + "|100|"));
        Assert.Single(m.Lines("KCDUS|LOOT|took|bandit_7|" + Coin + "|100|"));                      // the other guests lose it too
        Assert.Equal(101, m.Num("__world_ents[1].items[1].amount"));                               // the host's copy lost it
        m.ClearLog(); m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|LOOT|took|"));                                                 // the host's own watcher does not call that a host take
        m.Send("3~LOOTASK|" + Scope + "|3|ffff0000|1|bandit_7|" + Coin + "|150");                  // only 101 are left: somebody was first
        Assert.Single(m.Lines("KCDUS|LOOT|res|3|1|gone|bandit_7|" + Coin + "|150|"));
        Assert.Equal(101, m.Num("__world_ents[1].items[1].amount"));
    }

    [Fact]
    public void A_guest_loses_what_the_host_took_from_its_copy_and_does_not_ask_about_it()
    {
        var m = Loot(host: false);
        m.Send("2~LOOTTOOK|" + Scope + "|bandit_7|" + Coin + "|201");
        Assert.Equal(1, m.Num("#__world_ents[1].items"));                                          // only the coat is left on the body
        Assert.Single(m.Lines("KCDUS|LOOTAPPLIED|bandit_7|"));
        m.ClearLog(); m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|LOOT|ask|"));
    }

    [Fact]
    public void A_stash_has_no_name_so_it_is_known_by_where_it_is()
    {
        var m = Loot(host: true);
        Take(m, 2, Coin, 40);
        var l = m.Lines("KCDUS|LOOT|took|").Single().Split('|');
        Assert.Equal("s@110_200_300", l[3]);
        // and a friend's copy finds the same stash by that id
        var g = Loot(host: false);
        g.Send("2~LOOTTOOK|" + Scope + "|s@110_200_300|" + Coin + "|40");
        Assert.Equal(0, g.Num("#__world_ents[2].items"));
    }

    [Fact]
    public void Nothing_is_taken_off_in_another_world_or_with_nonsense()
    {
        var m = Loot(host: false);
        m.Send("2~LOOTTOOK|other|bandit_7|" + Coin + "|201");
        m.Send("3~LOOTTOOK|" + Scope + "|bandit_7|not a class|201");
        m.Send("4~LOOTTOOK|" + Scope + "|bandit_7|" + Coin + "|-3");
        Assert.Equal(201, m.Num("__world_ents[1].items[1].amount"));
        m.Send("5~LOOTMODE|0");
        m.Send("6~LOOTTOOK|" + Scope + "|bandit_7|" + Coin + "|201");
        Assert.Equal(201, m.Num("__world_ents[1].items[1].amount"));
    }

    [Fact]
    public void A_body_out_of_reach_is_not_watched()
    {
        var m = Loot(host: true);
        m.Do("__world_ents[1].pos = { x = 300, y = 20, z = 30 }");
        m.Advance(0.6);
        Take(m, 1, Coin, 10);                                                                      // somebody far away took it: not this player's take
        Assert.Empty(m.Lines("KCDUS|LOOT|took|bandit_7"));
    }
}
