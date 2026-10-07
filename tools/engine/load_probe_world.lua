-- The harness proves the profile contains only a clone before invoking this.
local cloud=System.GetCVar('sys_useSteamCloudForPlatformSaving')
System.LogAlways('KCDUS|ENGINE|cloud|'..tostring(cloud))
if tonumber(cloud)~=0 then error('Cloud saving is enabled; refusing probe load') end
for _,ent in pairs({KCDUS_ENGINE_REMOTE,KCDUS_ENGINE_GHOST}) do
    if ent and ent.id and (not player or ent.id~=player.id) then System.RemoveEntity(ent.id) end
end
KCDUS_ENGINE_REMOTE=nil
KCDUS_ENGINE_GHOST=nil
KCDUS.handlers.LOAD({'LOAD','0','0'})
