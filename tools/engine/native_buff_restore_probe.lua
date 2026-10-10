-- Restore a timed record through the real native reader, private profile only.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local guid='c37ab134-a443-433f-92a9-51ff6f08999c'
local soul=player.soul;local uid=tostring(soul:GetId()):match('([0-9A-Fa-f]+)$')
if not uid or #uid~=16 then error('Native soul handle format unsupported') end
local function capture()
    player.human:PlayAnim('@kcdus/state-capture','')
    local s=ItemManager.GetItem(soul:GetId());if not s or not s.kcdusNativeBuffCount then error('Native state capture refused') end
    local selected
    for i=0,s.kcdusNativeBuffCount-1 do
        if s['kcdusNativeBuffGuid'..i]==guid then
            if selected then error('Duplicate test definition') end
            selected=s['kcdusNativeBuff'..i]
        end
    end
    return selected,s.kcdusNativeBuffCount
end
local old,baseline=capture();if old then error('Test buff already present') end
local id=soul:AddBuff(guid);if not id then error('Native buff creation failed') end
local original,count=capture()
if not original or #original~=108 or count~=baseline+1 then soul:RemoveBuff(id);error('Timed schema unavailable') end
local bad='ff'..original:sub(3)
local rejected=player.human:PlayAnim('@kcdus/buff-restore/'..uid,bad)
if type(rejected)~='table' or rejected.applied~=0 then soul:RemoveBuff(id);error('Malformed record not refused') end
if capture()~=original then soul:RemoveBuff(id);error('Rejected input changed native state') end
Script.SetTimer(900,function()
    local progressed=capture()
    if not progressed or progressed==original then soul:RemoveBuff(id);error('Native timer did not progress') end
    local result=player.human:PlayAnim('@kcdus/buff-restore/'..uid,original)
    local restored=capture()
    System.LogAlways('KCDUS|S8|buff-restore|ack='..tostring(result and result.applied)..'|equal='..tostring(restored==original))
    soul:RemoveBuff(id)
    Script.SetTimer(300,function()
        local remaining,final=capture()
        if remaining or final~=baseline then error('Test buff cleanup failed') end
        System.LogAlways('KCDUS|S8|buff-restore|cleanup|PASS')
    end)
end)
