if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local g=System.SpawnEntity({class='NPC',name='kcdus_concrete_walk',position=player:GetWorldPos()})
KCDUS_ENGINE_GHOST=g
g:DisableBehaviorTreeEvaluation();g:Event_MakeInvulnerable();g:SetFlags(ENTITY_FLAG_NO_SAVE,0)
local function sample(phase)
    local l=g:GetBonePos('LeftFoot',{});local r=g:GetBonePos('RightFoot',{})
    System.LogAlways('KCDUS|ENGINE|concrete-time|'..phase..'|'..tostring(g:GetAnimationTime(0,0)))
    local k=g:GetBonePos('LeftLeg',{});local root=g:GetWorldPos()
    if k then System.LogAlways('KCDUS|ENGINE|concrete-knee|'..phase..'|'..(k.x-root.x)..','..(k.y-root.y)..','..(k.z-root.z)) end
    if l and r then System.LogAlways('KCDUS|ENGINE|concrete-feet|'..phase..'|'..(l.x-r.x)..','..(l.y-r.y)..','..(l.z-r.z)) end
end
Script.SetTimer(500,function()
    System.LogAlways('KCDUS|ENGINE|concrete-start|'..tostring(g:StartAnimation(0,'relaxed_walk_medium',0,0.2,1,true,true)))
    sample('start')
    Script.SetTimer(400,function() sample('400ms') end)
    Script.SetTimer(1100,function() sample('1100ms') end)
    Script.SetTimer(1600,function() sample('1600ms') end)
end)
