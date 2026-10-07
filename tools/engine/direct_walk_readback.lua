local g=KCDUS_ENGINE_GHOST
if not KCDUS.inWorldRaw() or not g then error('Loaded private world and owned NPC required') end
local function sample(phase)
    local l=g:GetBonePos('LeftFoot',{});local r=g:GetBonePos('RightFoot',{})
    if not l or not r then System.LogAlways('KCDUS|ENGINE|feet-unavailable|'..phase);return end
    System.LogAlways('KCDUS|ENGINE|feet-'..phase..'|'..(l.x-r.x)..','..(l.y-r.y)..','..(l.z-r.z))
    System.LogAlways('KCDUS|ENGINE|animation-'..phase..'|'..tostring(g:GetAnimationTime(0,1)))
end
sample('before')
Script.SetTimer(250,function() sample('after') end)
Script.SetTimer(600,function()
    g:StartAnimation(0,'relaxed_idle_both',1,0.2,1,true)
    System.LogAlways('KCDUS|ENGINE|direct-idle|'..tostring(g:IsAnimationRunning(0,1)))
end)
Script.SetTimer(1100,function()
    System.RemoveEntity(g.id)
    KCDUS_ENGINE_GHOST=nil
    System.LogAlways('KCDUS|ENGINE|direct-remove|'..tostring(System.GetEntity(g.id)==nil))
    Script.SetTimer(500,function()
        System.LogAlways('KCDUS|ENGINE|direct-lifecycle|'..tostring(KCDUS.inWorldRaw()))
        System.LogAlways('KCDUS|ENGINE|direct-removed-after-frame|'..tostring(System.GetEntity(g.id)))
    end)
end)
