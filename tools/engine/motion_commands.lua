local g = KCDUS_ENGINE_GHOST
local function out(label, fn)
    local ok, value = pcall(fn)
    System.LogAlways('KCDUS|ENGINE|' .. label .. '|' .. tostring(ok) .. '|' .. tostring(value))
end
out('ghost-position', function() local p=g:GetWorldPos();return p.x..','..p.y..','..p.z end)
out('ghost-stop', function() return g.actor:SimulateOnAction('moveforward', 2, 0) end)
for key, value in pairs(getmetatable(g).__index) do
    if type(value)=='function' and (string.find(string.lower(key),'anim') or string.find(string.lower(key),'move')) then
        System.LogAlways('KCDUS|ENGINE|entity-binding|'..key)
    end
end
out('play-fragment', function() return g.human:PlayAnim('MotionMovement') end)
out('motion-speed', function() return g.human:SetAnimMotionParam(0, 2) end)
out('fragment-readback', function() return g.actor:GetCurrentAnimationState() end)
out('action-control', function() return Game.GetActionControl() end)
