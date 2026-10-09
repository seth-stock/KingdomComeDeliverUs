-- Candidate native state reader. Owned isolated profile and native adapter.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
player.human:PlayAnim('@kcdus/state-capture','')
local state=ItemManager.GetItem(player.soul:GetId())
if type(state)~='table' or not state.kcdusStatProgressRaw or not state.kcdusSkillProgressRaw or not state.kcdusNativeBuffCount then error('Private native state reader missing or refused') end
System.LogAlways('KCDUS|S7|stat-progress|'..state.kcdusStatProgressRaw)
System.LogAlways('KCDUS|S7|skill-progress|'..state.kcdusSkillProgressRaw)
System.LogAlways('KCDUS|S7|buff-count|'..state.kcdusNativeBuffCount)
local read=0
for i=0,state.kcdusNativeBuffCount-1 do
    local blob=state['kcdusNativeBuff'..i]
    local guid=state['kcdusNativeBuffGuid'..i]
    if blob and guid then read=read+1;System.LogAlways('KCDUS|S7|buff|'..i..'|'..guid..'|'..blob)
    else System.LogAlways('KCDUS|S7|buff-missing|'..i..'|'..tostring(guid)..'|'..tostring(state['kcdusNativeBuffDetail'..i])) end
end
System.LogAlways('KCDUS|S7|buff-serialized|'..read..'/'..state.kcdusNativeBuffCount)
local n=0
for _,id in pairs(player.inventory:GetInventoryTable()) do
    local item=ItemManager.GetItem(id)
    if not item or item.kcdusOwner==nil then error('Native item owner missing') end
    if n<3 then System.LogAlways('KCDUS|S7|item-owner|'..item.class..'|'..tostring(item.kcdusOwner)) end
    n=n+1
end
System.LogAlways('KCDUS|S7|item-owner-count|'..n)
