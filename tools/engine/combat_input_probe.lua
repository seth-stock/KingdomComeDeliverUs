-- Candidate action replay on an ordinary NPC. Private harness only; no Player/channel proxy.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local name=KCDUS_S5_ACTOR or 'rat_pirk_guard4'
local e=System.GetEntityByName(name)
if not e or not e.actor or not e.human then error('Named NPC missing') end
local function report(label)
    local ok,state=pcall(function() return e.actor:GetCurrentAnimationState() end)
    local okw,weapon=pcall(function() return e.human:IsWeaponDrawn() end)
    System.LogAlways('KCDUS|S5|action|'..label..'|'..name..'|state='..tostring(state)..'|read='..tostring(ok)..'|drawn='..tostring(okw and weapon))
end
local action=KCDUS_S5_ACTION or 'attack1'
report('before')
local ok,result=pcall(function() e.actor:SimulateOnAction(action,1,1) end)
System.LogAlways('KCDUS|S5|action|call|'..action..'|'..tostring(ok)..'|'..tostring(result))
Script.SetTimer(150,function()
    pcall(function() e.actor:SimulateOnAction(action,2,0) end)
    report('after-150ms')
end)
Script.SetTimer(1000,function() report('after-1s') end)
