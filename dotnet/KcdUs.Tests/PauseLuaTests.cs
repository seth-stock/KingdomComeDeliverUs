// SPDX-License-Identifier: GPL-3.0-only
// The game half of the shared pause (mod/kcdus/lua/pause.lua) against the stubbed engine; the engine facts it rests on were proved in the private engine.
namespace KcdUs.Tests;

public class PauseLuaTests
{
    private static LuaMod World()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(0.5);
        m.ClearLog();
        return m;
    }

    [Fact]
    public void A_freeze_slows_the_world_to_a_thousandth_and_the_agent_can_let_go()
    {
        var m = World();
        m.Send("1~FREEZE|1|600");
        Assert.Equal(0.001, m.Num("__cvars.t_scale"), 6);
        Assert.Single(m.Lines("KCDUS|PAUSE|frozen|600"));
        m.Send("2~FREEZE|0");
        Assert.Equal(1, m.Num("__cvars.t_scale"));
        Assert.Single(m.Lines("KCDUS|PAUSE|released|agent"));
    }

    [Fact]
    public void A_freeze_nobody_renews_lets_go_by_itself_after_its_frames()
    {
        var m = World();
        m.Send("1~FREEZE|1|600");                                   // 600 frames = 10 s at the stub's 60 frames a second
        m.Advance(5.0);
        Assert.Equal(0.001, m.Num("__cvars.t_scale"), 6);            // still held: the lease is not up
        m.Advance(6.0);
        Assert.Equal(1, m.Num("__cvars.t_scale"));
        Assert.Single(m.Lines("KCDUS|PAUSE|released|lease"));
    }

    [Fact]
    public void Renewing_the_freeze_restarts_the_lease_and_the_scale_goes_back_to_one_never_to_a_raised_value()
    {
        var m = World();
        m.Do("__cvars.t_scale = 20");                                 // a sleep or a wait has raised the scale
        m.Send("1~FREEZE|1|600");
        m.Advance(8.0);
        m.Send("2~FREEZE|1|600");                                     // the agent asks again
        m.Advance(8.0);
        Assert.Equal(0.001, m.Num("__cvars.t_scale"), 6);
        m.Send("3~FREEZE|0");
        Assert.Equal(1, m.Num("__cvars.t_scale"));                    // not 20
    }

    [Fact]
    public void The_lease_is_clamped_and_a_loaded_world_lets_go_at_once()
    {
        var m = World();
        m.Send("1~FREEZE|1|5");                                       // far too short: raised to 240
        Assert.Single(m.Lines("KCDUS|PAUSE|frozen|240"));
        m.Do("KCDUS.Pause.onWorldReset()");
        Assert.Equal(1, m.Num("__cvars.t_scale"));
        Assert.Single(m.Lines("KCDUS|PAUSE|released|reset"));
        m.ClearLog();
        m.Send("2~FREEZE|1|999999");
        Assert.Single(m.Lines("KCDUS|PAUSE|frozen|6000"));
    }

    [Fact]
    public void The_menu_events_are_passed_on_and_the_watcher_graph_is_started_by_the_agent()
    {
        var m = World();
        m.Do("KCDUS_MenuEvent(1)");
        Assert.True(m.Bool("KCDUS.Pause.menuOpen"));
        Assert.Single(m.Lines("KCDUS|PAUSE|menu|1"));
        m.Do("KCDUS_MenuEvent(0)");
        Assert.False(m.Bool("KCDUS.Pause.menuOpen"));
        m.Send("1~PAUSEWATCH");
        Assert.True(m.Bool("(function() for _, a in ipairs(__actions) do if a == 'MP_MenuWatch' then return true end end return false end)()"));
    }
}
