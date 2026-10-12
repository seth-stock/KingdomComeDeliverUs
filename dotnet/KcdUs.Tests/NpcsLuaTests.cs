// SPDX-License-Identifier: GPL-3.0-only
// Shared enemies, the game half (mod/kcdus/lua/npcs.lua), against the stubbed engine. The engine facts (the brain switch stops a walking NPC, SetWorldPos
// then places it, the brain comes back) were proved in the private engine (tools/engine/npc_puppet_probe.lua).
namespace KcdUs.Tests;

public class NpcsLuaTests
{
    private const string Scope = "w1";

    private static LuaMod World(string me = "1")
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__mkNpc('bandit_7', 100, 14, 20, 30); __mkNpc('bandit_8', 100, 18, 20, 30); __mkNpc('guard_far', 100, 90, 20, 30)");   // player at (10,20,30)
        m.Send("1~NPCMODE|1|" + Scope + "|" + me);
        m.Advance(0.5);
        m.ClearLog();
        return m;
    }

    [Fact]
    public void The_npcs_near_the_player_are_reported_nearest_first_and_far_ones_are_not()
    {
        var m = World();
        m.Advance(1.1);
        var near = m.Lines("KCDUS|NPCNEAR|");
        Assert.NotEmpty(near);
        Assert.Equal("KCDUS|NPCNEAR|bandit_7:4.0;bandit_8:8.0", near.Last());
    }

    [Fact]
    public void An_npc_this_game_owns_is_reported_five_times_a_second_and_one_it_does_not_own_is_not()
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_7:1;bandit_8:2");
        m.ClearLog();
        m.Advance(1.0);
        var st = m.Lines("KCDUS|NPCST|");
        Assert.InRange(st.Count, 4, 6);
        Assert.All(st, l => Assert.StartsWith("KCDUS|NPCST|bandit_7,14.00,20.00,30.00,", l));
    }

    [Fact]
    public void A_friends_samples_make_my_copy_a_puppet_that_follows_and_draws_its_weapon()
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("3~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,1.500,1.00,0.00,1");
        Assert.False(m.Bool("__world_ents[2].brain"));                               // the brain is off: it does not fight me on its own
        Assert.Single(m.Lines("KCDUS|NPCPUP|bandit_8|on|2"));
        m.Advance(0.3);                                                               // the owner's samples come every 0.2 s
        Assert.InRange(m.Num("__world_ents[2].pos.x"), 18.9, 19.6);                   // pulled to the owner's sample (and a little ahead with its walk)
        Assert.Equal(1.5, m.Num("__world_ents[2].angles.z"), 3);
        Assert.True(m.Bool("__world_ents[2].weapon"));
        Assert.Equal("relaxed_walk_medium", m.Str("__world_ents[2].anim"));
    }

    [Fact]
    public void Samples_from_anyone_but_the_owner_the_host_named_are_ignored_and_my_own_npcs_are_never_puppets()
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_7:1;bandit_8:2");
        m.Send("3~NPCSET|" + Scope + "|3|bandit_8,0.00,0.00,30.00,0,0,0,0");          // player 3 does not own it
        m.Send("4~NPCSET|" + Scope + "|2|bandit_7,0.00,0.00,30.00,0,0,0,0");          // mine
        m.Send("5~NPCSET|other|2|bandit_8,0.00,0.00,30.00,0,0,0,0");                  // another world
        Assert.True(m.Bool("__world_ents[1].brain"));
        Assert.True(m.Bool("__world_ents[2].brain"));
        Assert.Empty(m.Lines("KCDUS|NPCPUP|"));
    }

    [Fact]
    public void A_puppet_gets_its_brain_back_when_it_becomes_mine_when_samples_stop_and_when_the_mode_ends()
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("3~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,0,0,0,0");
        m.Send("4~NPCOWN|" + Scope + "|bandit_8:1");                                  // I came nearest: mine now
        Assert.True(m.Bool("__world_ents[2].brain"));
        Assert.Single(m.Lines("KCDUS|NPCPUP|bandit_8|off|mine"));

        m.Send("5~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("6~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,0,0,0,0");
        Assert.False(m.Bool("__world_ents[2].brain"));
        for (int i = 0; i < 10; i++) { m.Advance(0.5); m.Send($"{7 + i}~NPCOWN|{Scope}|bandit_8:2"); }   // ownership repeated, but no samples for 5 s
        Assert.True(m.Bool("__world_ents[2].brain"));
        Assert.Single(m.Lines("KCDUS|NPCPUP|bandit_8|off|stale"));

        m.Send("30~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,0,0,0,0");
        Assert.False(m.Bool("__world_ents[2].brain"));
        m.Send("31~NPCMODE|0");
        Assert.True(m.Bool("__world_ents[2].brain"));
    }

    [Fact]
    public void Ownership_lapses_without_the_host_and_a_save_gives_every_brain_back()
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("3~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,0,0,0,0");
        m.Do("KCDUS.Npcs.releaseAll('save')");
        Assert.True(m.Bool("__world_ents[2].brain"));
        m.Send("4~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,0,0,0,0");
        Assert.False(m.Bool("__world_ents[2].brain"));
        m.Advance(3.5);                                                               // the host said nothing for 3.5 s
        Assert.True(m.Bool("__world_ents[2].brain"));
    }

    [Fact]
    public void A_far_jump_snaps_and_a_dead_puppet_is_let_go()
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("3~NPCSET|" + Scope + "|2|bandit_8,40.00,20.00,30.00,0,0,0,0");
        m.Advance(0.2);
        Assert.Equal(40, m.Num("__world_ents[2].pos.x"), 3);
        m.Do("__world_ents[2].dead = true");
        m.Advance(0.2);
        Assert.Single(m.Lines("KCDUS|NPCPUP|bandit_8|off|dead"));
    }

    [Fact]
    public void A_refused_draw_is_tried_again_and_a_holstered_owner_holsters_the_puppet()
    {
        var m = World("1");
        m.Do("__world_ents[2].refuseDraw = true");                                    // mid-animation: the first draw does nothing
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("3~NPCSET|" + Scope + "|2|bandit_8,19.00,21.00,30.00,0,0,0,1");
        m.Advance(0.2);
        Assert.False(m.Bool("__world_ents[2].weapon"));
        m.Do("__world_ents[2].refuseDraw = false");
        for (int i = 0; i < 10; i++) { m.Advance(0.2); m.Send($"{4 + i}~NPCSET|{Scope}|2|bandit_8,19.00,21.00,30.00,0,0,0,1"); }
        Assert.True(m.Bool("__world_ents[2].weapon"));
        for (int i = 0; i < 10; i++) { m.Advance(0.2); m.Send($"{20 + i}~NPCSET|{Scope}|2|bandit_8,19.00,21.00,30.00,0,0,0,0"); m.Send($"{40 + i}~NPCOWN|{Scope}|bandit_8:2"); }
        Assert.False(m.Bool("__world_ents[2].weapon"));
    }

    // ------------------------------------------------------------------ the damage mirror (an approximation: docs/SESSION10-RESULTS.md)
    private static LuaMod Fight(string me = "1")
    {
        var m = World(me);
        m.Do("__world_ents[1].human.PlayAnim = function(self, frag) if frag == '@kcdus/combat-state' then return { present = 1, mode = __attackMode or 4, active = 1, target = 1 } end end");
        return m;
    }

    [Fact]
    public void The_owner_reports_the_health_a_blow_of_its_npc_cost_it_once_and_only_then()
    {
        var m = Fight("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_7:1");
        m.Advance(0.5);                                                  // the owner's report sees the attack (mode 4, engaged on me)
        m.ClearLog();
        m.Do("__world.health = 88");                                     // the blow landed: 12 health
        m.Advance(0.3);
        var hit = m.Lines("KCDUS|NPCHIT|");
        Assert.Single(hit);
        Assert.Equal("KCDUS|NPCHIT|bandit_7|1|12.0", hit[0]);
        m.Do("__world.health = 87.9"); m.Advance(0.5);                    // regen noise and a tiny fall are not blows
        m.Do("__world.health = 88"); m.Advance(0.5);
        Assert.Single(m.Lines("KCDUS|NPCHIT|"));
    }

    [Fact]
    public void A_health_loss_with_no_attack_of_my_own_npc_nearby_is_not_reported()
    {
        var m = Fight("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:1");                      // bandit_8 is 8 m away: out of the 4 m
        m.Advance(0.5);
        m.Do("__world.health = 70"); m.Advance(0.5);
        Assert.Empty(m.Lines("KCDUS|NPCHIT|"));
        var n = Fight("1");
        n.Do("__attackMode = 1");                                         // an NPC that is not attacking
        n.Send("2~NPCOWN|" + Scope + "|bandit_7:1");
        n.Advance(0.5); n.Do("__world.health = 70"); n.Advance(0.5);
        Assert.Empty(n.Lines("KCDUS|NPCHIT|"));
    }

    private static LuaMod Puppet(double x, double y, double yaw)
    {
        var m = World("1");
        m.Send("2~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send(FormattableString.Invariant($"3~NPCSET|{Scope}|2|bandit_8,{x:F2},{y:F2},30.00,{yaw:F3},0.00,0.00,1"));
        m.Advance(0.3);
        m.ClearLog();
        return m;
    }

    [Fact]
    public void A_friends_blow_hurts_my_henry_when_he_stands_in_front_of_the_puppet_and_is_read_back()
    {
        var m = Puppet(11.5, 20, Math.PI / 2);                            // 1.5 m east of Henry, facing west (-x): at him
        m.Send("4~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");
        Assert.Equal(88, m.Num("__world.health"), 3);
        Assert.Equal(1, m.Num("__world.damageCalls"));
        Assert.Equal("KCDUS|NPCHITAPPLIED|bandit_8|1|12.0|12.0|1", m.Lines("KCDUS|NPCHITAPPLIED|").Single());
        Assert.True(m.Bool("__world.lastDamage.suppress"));
        Assert.Equal(0, m.Num("__world.lastDamage.stam"));
        m.Advance(0.5);
        Assert.Empty(m.Lines("KCDUS|NPCHIT|"));                          // my own watch does not report the blow I just took as mine
    }

    [Theory]
    [InlineData(11.5, 20.0, -Math.PI / 2, "behind")]                      // facing away
    [InlineData(15.0, 20.0, Math.PI / 2, "out-of-reach")]                 // 5 m away
    public void A_friends_blow_that_would_not_have_reached_my_henry_does_not_hurt_him(double x, double y, double yaw, string why)
    {
        var m = Puppet(x, y, yaw);
        m.Send("4~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");
        Assert.Equal(100, m.Num("__world.health"), 3);
        Assert.Equal($"KCDUS|NPCHITSKIP|bandit_8|1|{why}", m.Lines("KCDUS|NPCHITSKIP|").Single());
    }

    [Fact]
    public void A_blow_counts_once_only_from_the_npcs_owner_for_a_current_puppet_and_within_bounds()
    {
        var m = Puppet(11.5, 20, Math.PI / 2);
        m.Send("4~NPCHITIN|" + Scope + "|3|bandit_8|1|12.0");              // player 3 does not own it
        m.Send("5~NPCHITIN|other|2|bandit_8|1|12.0");                     // another world
        m.Send("6~NPCHITIN|" + Scope + "|2|bandit_8|1|0.1");              // below a blow
        m.Send("7~NPCHITIN|" + Scope + "|2|bandit_8|1|61");               // above any blow
        m.Send("8~NPCHITIN|" + Scope + "|2|bandit_8|0|12");               // no such sequence
        m.Send("9~NPCHITIN|" + Scope + "|2|bandit_7|1|12");               // not a puppet of mine
        Assert.Equal(100, m.Num("__world.health"), 3);
        m.Send("10~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");
        m.Send("11~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");            // the same blow again
        m.Send("12~NPCHITIN|" + Scope + "|2|bandit_8|2|5.0");
        Assert.Equal(83, m.Num("__world.health"), 3);
        Assert.Equal(2, m.Num("__world.damageCalls"));
    }

    [Fact]
    public void A_blow_is_not_taken_twice_when_the_puppet_was_released_and_taken_again_in_between()
    {
        var m = Puppet(11.5, 20, Math.PI / 2);
        m.Send("4~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");
        Assert.Equal(88, m.Num("__world.health"), 3);
        m.Advance(5);                                                    // the owner went quiet: the puppet is given back
        m.Send("5~NPCOWN|" + Scope + "|bandit_8:2");
        m.Send("6~NPCSET|" + Scope + "|2|bandit_8,11.50,20.00,30.00,1.571,0.00,0.00,1");     // taken again
        m.Send("7~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");            // the same blow, delayed
        Assert.Equal(88, m.Num("__world.health"), 3);
        Assert.Equal(1, m.Num("__world.damageCalls"));
    }

    [Fact]
    public void The_damage_mirror_can_be_switched_off_and_a_released_puppet_takes_nothing()
    {
        var m = Puppet(11.5, 20, Math.PI / 2);
        m.Send("4~NPCDMGMIRROR|0");
        m.Send("5~NPCHITIN|" + Scope + "|2|bandit_8|1|12.0");
        Assert.Equal(100, m.Num("__world.health"), 3);
        m.Send("6~NPCDMGMIRROR|1");
        m.Send("7~NPCOWN|" + Scope + "|bandit_8:1");                       // it is mine now: no longer a puppet
        m.Send("8~NPCHITIN|" + Scope + "|2|bandit_8|2|12.0");
        Assert.Equal(100, m.Num("__world.health"), 3);
        Assert.Equal(0, m.Num("__world.damageCalls or 0"));
    }
}
