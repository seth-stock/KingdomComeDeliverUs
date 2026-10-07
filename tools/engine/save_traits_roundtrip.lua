if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
if tonumber(System.GetCVar('sys_useSteamCloudForPlatformSaving'))~=0 then error('Cloud saving must be disabled') end
Game.SaveGameViaResting()
System.LogAlways('KCDUS|ENGINE|traits-roundtrip-save|requested')
