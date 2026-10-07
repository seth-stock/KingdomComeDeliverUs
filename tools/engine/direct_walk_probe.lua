if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local pos=player:GetWorldPos()
local g=System.SpawnEntity({class='NPC',name='kcdus_direct_walk',position=pos})
KCDUS_ENGINE_GHOST=g
g:Event_MakeInvulnerable()
g:DisableBehaviorTreeEvaluation()
g:SetFlags(ENTITY_FLAG_NO_SAVE,0)
local function log(label,fn)
    local ok,v=pcall(fn);System.LogAlways('KCDUS|ENGINE|'..label..'|'..tostring(ok)..'|'..tostring(v))
end
Script.SetTimer(500,function()
    log('direct-walk',function() return g:StartAnimation(0,'3d_relaxed_walk_turn_strafe',1,0.2,1,true) end)
    log('direct-running',function() return g:IsAnimationRunning(0,1) end)
    log('direct-time',function() return g:GetAnimationTime(0,1) end)
    Script.SetTimer(400,function() log('direct-time-delayed',function() return g:GetAnimationTime(0,1) end) end)
end)
