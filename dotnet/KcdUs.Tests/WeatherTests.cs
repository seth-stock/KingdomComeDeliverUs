// SPDX-License-Identifier: GPL-3.0-only
using KcdUs.Agent;
namespace KcdUs.Tests;
public class WeatherTests
{
    [Fact] public void PresetsOnlyRunInSupportedSharedOpenWorldAndAreHeartbeatIdempotent()
    {
        long now=50000;var game=new List<string>();var guests=new List<string>();
        var w=new WeatherCoordinator(()=>now,game.Add,guests.Add,_=>0);
        w.Enabled=true;
        w.Tick("",true,true); Assert.Empty(game);
        w.Tick("w1",true,false); Assert.Empty(game);
        w.Tick("w1",true,true); Assert.Contains("WAPPLY|w1|cloudless_sunny|30",game);
        now+=31000;w.Tick("w1",true,true);
        Assert.Equal(2,guests.Count); Assert.Equal(3,game.Count(x=>x.StartsWith("WAPPLY"))); // Lua is the native idempotence/readiness gate
        w.Tick("w1",true,false);Assert.Equal("WMODE|0",game.Last());
    }
    [Fact] public void InvalidScopesProfilesAndNativeFailuresCannotBecomeAppliedWeather()
    {
        var m=new LuaMod();m.InWorld();m.Do("__weatherCalls=0; EnvironmentModule={BlendTimeOfDay=function(p,b,f) __weatherCalls=__weatherCalls+1 end}");
        m.Send("1~WMODE|1|w1");m.Send("2~WAPPLY|w2|cloudless_sunny|30");m.Send("3~WAPPLY|w1|dream|30");
        Assert.Equal(0,m.Num("__weatherCalls"));
        m.Send("4~WAPPLY|w1|cloudless_sunny|30");m.Send("5~WAPPLY|w1|cloudless_sunny|30");
        Assert.Equal(1,m.Num("__weatherCalls"));
        m.Do("EnvironmentModule.BlendTimeOfDay=function() error('native failed') end");
        m.Send("6~WAPPLY|w1|foggy_storm|30");Assert.Equal("cloudless_sunny",m.Str("KCDUS.Weather.profile"));
    }
}
