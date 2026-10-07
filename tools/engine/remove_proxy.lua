if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local original=player
local g=KCDUS_ENGINE_REMOTE
if not g or g.id==original.id or g.actor:GetChannel()~=77 then error('Owned channel 77 proxy required') end
System.RemoveEntity(g.id)
KCDUS_ENGINE_REMOTE=nil
System.LogAlways('KCDUS|ENGINE|proxy-removed|'..tostring(g_gameRules.game:GetPlayerByChannelId(77)==nil))
Script.SetTimer(300,function()
    System.LogAlways('KCDUS|ENGINE|proxy-remove-world|'..tostring(player==original)..'|'..tostring(KCDUS.inWorldRaw()))
end)
