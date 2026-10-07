local g = KCDUS_ENGINE_GHOST
local p = g:GetWorldPos()
local function test(label, fn)
    local ok, value = pcall(fn)
    System.LogAlways('KCDUS|ENGINE|'..label..'|'..tostring(ok)..'|'..tostring(value))
end
test('goal-begin', function() AI.BeginGoalPipe('kcdus_engine_walk') end)
test('goal-locate', function() AI.PushGoal('locate', 1, 'refpoint') end)
test('goal-approach', function() AI.PushGoal('approach', 1, 0.5, 0) end)
test('goal-end', function() AI.EndGoalPipe() end)
test('goal-target', function() AI.SetRefPointPosition(g.id,{x=p.x+3,y=p.y,z=p.z}) end)
test('goal-select', function() return g:SelectPipe(0,'kcdus_engine_walk') end)
KCDUS_ENGINE_GHOST_POS = p
for _, name in ipairs({'CreateGoalPipe','BeginGoalPipe','PushGoal','EndGoalPipe','SetRefPointPosition'}) do
    System.LogAlways('KCDUS|ENGINE|goal-binding|'..name..'|'..type(AI[name]))
end
