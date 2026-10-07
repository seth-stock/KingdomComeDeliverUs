local g=KCDUS_ENGINE_GHOST
local function out(phase)
    System.LogAlways('KCDUS|ENGINE|walk-'..phase..'|'..tostring(g.actor:GetCurrentAnimationState()))
end
out('before')
g.human:PlayAnim('MotionMovement','walk')
g.human:SetAnimMotionParam(0,1.5)
g.human:SetAnimMotionParam(2,0)
out('immediate')
Script.SetTimer(300,function() out('delayed') end)
