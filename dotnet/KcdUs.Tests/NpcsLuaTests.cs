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
}
