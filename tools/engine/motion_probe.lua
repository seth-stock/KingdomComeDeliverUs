-- Mutates only the disposable probe world. Invoke through guarded engine_session.py.
local pos = player:GetWorldPos()
KCDUS_ENGINE_GHOST = System.SpawnEntity({class='NPC', name='kcdus_engine_probe', position={x=pos.x+2,y=pos.y,z=pos.z}})
local g = KCDUS_ENGINE_GHOST
if not g then error('Probe NPC did not spawn') end
g:Event_MakeInvulnerable()
g:DisableBehaviorTreeEvaluation()
System.LogAlways('KCDUS|ENGINE|ghost-state|' .. tostring(g.actor:GetCurrentAnimationState()))
for _, call in ipairs({
    {'MakeLookAsActor', function() return g.actor:MakeLookAsActor(player.id, true) end},
    {'SimulateOnAction', function() return g.actor:SimulateOnAction('moveforward', 1, 1) end},
}) do
    local ok, result = pcall(call[2])
    System.LogAlways('KCDUS|ENGINE|call|' .. call[1] .. '|' .. tostring(ok) .. '|' .. tostring(result))
end
KCDUS_ENGINE_GHOST_POS = g:GetWorldPos()
