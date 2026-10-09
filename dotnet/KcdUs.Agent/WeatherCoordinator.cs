// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Agent;

/// <summary>Shared open-world weather presets. No native current-profile reader exists in the Lua API.</summary>
public sealed class WeatherCoordinator
{
    public static readonly string[] Profiles={"cloudless_sunny","semicloudy_clear","cloudy_no_rain","cloudy_frequent_showers","foggy_drizzly","foggy_storm"};
    private readonly Func<long> _now;
    private readonly Func<int,int> _pick;
    private readonly Action<string> _game,_guests;
    private string _scope="",_profile="";
    private long _repick,_heartbeat;
    public bool Enabled {get;set;} // Candidate preset arbitration is opt-in until scripted-weather interaction is proved
    public WeatherCoordinator(Func<long> now,Action<string> game,Action<string> guests,Func<int,int>? pick=null)
    { _now=now; _game=game; _guests=guests; _pick=pick??Random.Shared.Next; }
    public void Tick(string scope,bool host,bool open)
    {
        string desired=Enabled && open?scope:"";
        if(desired!=_scope)
        {
            _scope=desired; _profile=""; _repick=_heartbeat=0;
            _game(desired.Length>0?"WMODE|1|"+desired:"WMODE|0");
        }
        if(_scope.Length==0 || !host) return;
        long now=_now();
        if(now>=_repick)
        {
            _repick=now+1200000; // slow preset changes; never enter quest-specific 'dream'
            string profile=Profiles[_pick(Profiles.Length)];
            if(profile!=_profile) { _profile=profile; _game($"WAPPLY|{_scope}|{profile}|30"); _heartbeat=0; }
        }
        if(now>=_heartbeat)
        {
            _heartbeat=now+30000;
            _game("WMODE|1|"+_scope); _game($"WAPPLY|{_scope}|{_profile}|30");
            _guests($"weather|{_scope}|{_profile}|30");
        }
    }
    public void HostWeather(string[] f)
    {
        if(Enabled && _scope.Length>0 && f.Length==4 && f[1]==_scope && Profiles.Contains(f[2]) && f[3]=="30")
        { _game("WMODE|1|"+_scope); _game($"WAPPLY|{_scope}|{f[2]}|30"); }
    }
    public void Reset() { if(_scope.Length>0) _game("WMODE|0"); _scope=""; _profile=""; _repick=_heartbeat=0; }
}
