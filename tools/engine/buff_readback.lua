-- Private engine only, read-only: the native buff instances on Henry's soul (definitions and serialized records) after a load. Needs KCDUS_PROBE_STATE=1.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
player.human:PlayAnim('@kcdus/state-capture', '')
local s = ItemManager.GetItem(player.soul:GetId())
if type(s) ~= 'table' or not s.kcdusNativeBuffCount then error('Native state capture refused') end
local list = ''
for i = 0, s.kcdusNativeBuffCount - 1 do
    local g = s['kcdusNativeBuffGuid' .. i]
    if g then list = list .. g .. '=' .. #(s['kcdusNativeBuff' .. i] or '') .. ';' end
end
System.LogAlways('KCDUS|BUFFS|' .. s.kcdusNativeBuffCount .. '|' .. list)
