local g = KCDUS_ENGINE_GHOST
local pos = g:GetWorldPos()
local old = KCDUS_ENGINE_GHOST_POS
System.LogAlways('KCDUS|ENGINE|motion-readback|' .. tostring(g.actor:GetCurrentAnimationState()) .. '|' .. tostring(pos.x-old.x) .. ',' .. tostring(pos.y-old.y))
for key, value in pairs(g) do
    if type(value) == 'table' then System.LogAlways('KCDUS|ENGINE|entity-table|' .. key) end
end
for _, group in ipairs({'AI','Animation','Mannequin','Game','RPG','XGenAIModule'}) do
    if type(_G[group]) == 'table' then
        for key, value in pairs(_G[group]) do
            if type(value) == 'function' and (group ~= 'AI' or string.find(string.lower(key), 'move') or string.find(string.lower(key), 'anim')) then
                System.LogAlways('KCDUS|ENGINE|global-binding|' .. group .. '.' .. key)
            end
        end
    end
end
