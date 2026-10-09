-- Native polymorphic serialization proof in an owned disposable world.
-- This does not restore buffs or assert portable character completeness.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local guid='c37ab134-a443-433f-92a9-51ff6f08999c'
local function snapshot(phase)
    player.human:PlayAnim('@kcdus/state-capture','')
    local state=ItemManager.GetItem(player.soul:GetId())
    if not state or state.kcdusNativeBuffCount==nil then error('Private state capture unavailable') end
    local count=0;local added={}
    for i=0,state.kcdusNativeBuffCount-1 do
        local code=state['kcdusNativeBuffGuid'..i];local blob=state['kcdusNativeBuff'..i]
        if not code or not blob then error('Incomplete native buff capture at '..i) end
        count=count+1
        if code==guid then added[#added+1]=blob end
    end
    System.LogAlways('KCDUS|S7|buff-probe|'..phase..'|complete='..count..'|selected='..#added)
    for i,blob in ipairs(added) do System.LogAlways('KCDUS|S7|buff-probe-data|'..phase..'|'..i..'|'..blob) end
    return count,#added
end
local baseline,prior=snapshot('before')
if prior~=0 then error('Selected buff already exists; do not modify the baseline') end
local id=player.soul:AddBuff(guid)
if not id then error('Native buff factory refused the test definition') end
local count,n=snapshot('added')
if count~=baseline+1 or n~=1 then player.soul:RemoveBuff(id);error('Added buff not captured exactly once') end
Script.SetTimer(750,function()
    snapshot('750ms');player.soul:RemoveBuff(id)
    Script.SetTimer(300,function()
        local final,remaining=snapshot('removed')
        if final~=baseline or remaining~=0 then error('Native test buff removal not confirmed') end
        System.LogAlways('KCDUS|S7|buff-probe|PASS')
    end)
end)
