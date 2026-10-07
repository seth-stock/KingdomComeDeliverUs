if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
if tonumber(System.GetCVar('sys_useSteamCloudForPlatformSaving'))~=0 then error('Cloud saving must be disabled') end
local g=KCDUS_ENGINE_REMOTE
if not g or type(ENTITY_FLAG_NO_SAVE)~='number' then error('Owned proxy and nonpersistent flag required') end
g:SetFlags(ENTITY_FLAG_NO_SAVE,0)
if not g:HasFlags(ENTITY_FLAG_NO_SAVE) then error('Proxy is persistable; refusing test save') end
local ok,value=pcall(Game.SaveGameViaResting)
System.LogAlways('KCDUS|ENGINE|probe-save-request|'..tostring(ok)..'|'..tostring(value))
