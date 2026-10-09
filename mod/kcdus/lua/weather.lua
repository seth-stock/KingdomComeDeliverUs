-- SPDX-License-Identifier: GPL-3.0-only
-- Shared open-world presets from the installed rataje mission. This does not read native weather or override scripted story scenes.
local K=KCDUS
local W={enabled=false,scope='',profile=nil}
K.Weather=W
W.profiles={cloudless_sunny=true,semicloudy_clear=true,cloudy_no_rain=true,cloudy_frequent_showers=true,foggy_drizzly=true,foggy_storm=true}
K.handlers.WMODE=function(f)
    local scope=f[3] or ''
    if f[2]~='1' or scope~=W.scope then W.profile=nil end
    W.enabled=f[2]=='1'; W.scope=W.enabled and scope or ''
end
K.handlers.WAPPLY=function(f)
    local profile,blend=f[3],tonumber(f[4])
    if not W.enabled or f[2]~=W.scope or W.scope=='' or not K.inWorldRaw() or not W.profiles[profile] or blend~=30 or W.profile==profile then return end
    if not EnvironmentModule or type(EnvironmentModule.BlendTimeOfDay)~='function' then K.out('WEATHER','unavailable'); return end
    local ok,err=pcall(EnvironmentModule.BlendTimeOfDay,profile,blend,true)
    if not ok then K.out('WEATHER','refused',K.clean(err)); return end
    W.profile=profile
    K.out('WEATHER','candidate',profile) -- API acceptance is not a current-profile readback
end
function W.onWorldReset() W.enabled=false; W.scope=''; W.profile=nil end
