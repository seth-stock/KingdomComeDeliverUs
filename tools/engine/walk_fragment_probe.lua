-- Disposable-world experiment: PlayAnim requires TWO strings in retail 1.9.8.
local pos=player:GetWorldPos()
local g=System.SpawnEntity({class='NPC',name='kcdus_walk_probe',position=pos})
KCDUS_ENGINE_GHOST=g
g:Event_MakeInvulnerable()
g:DisableBehaviorTreeEvaluation()
local function test(name,fn)
    local ok,value=pcall(fn)
    System.LogAlways('KCDUS|ENGINE|'..name..'|'..tostring(ok)..'|'..tostring(value))
end
test('appearance',function() return g.actor:MakeLookAsActor(player.id,true) end)
test('movement-control',function() return g.actor:SetMovementControlledByAnimation(false) end)
test('walk-fragment',function() return g.human:PlayAnim('MotionMovement','walk') end)
test('motion-speed',function() return g.human:SetAnimMotionParam(0,1.5) end)
test('motion-direction',function() return g.human:SetAnimMotionParam(2,0) end)
test('walk-state',function() return g.actor:GetCurrentAnimationState() end)
KCDUS_ENGINE_GHOST_POS=g:GetWorldPos()
