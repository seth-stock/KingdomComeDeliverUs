if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local g=KCDUS_ENGINE_REMOTE
if not g then error('Owned proxy required') end
g.actor:SetPhysicalizationProfile('none')
System.LogAlways('KCDUS|ENGINE|proxy-physics|'..tostring(g.actor:GetPhysicalizationProfile()))
g.human:PlayAnim('MotionMovement','walk')
g.human:SetAnimMotionParam(0,1.5)
Script.SetTimer(300,function()
    System.LogAlways('KCDUS|ENGINE|proxy-physics-animation|'..tostring(g.actor:GetCurrentAnimationState()))
end)
