-- Private engine only. Can a living NPC be made a puppet (its brain stopped) and driven by position, then given its brain back?
-- KCDUS_PP_STEP: 'scan' | 'stop' | 'move' | 'start' | 'read' | 'provoke'
local step = KCDUS_PP_STEP or 'read'
local name = KCDUS_PP_NAME
local function pos(e) local q = e:GetWorldPos(); return string.format('%.2f,%.2f,%.2f', q.x, q.y, q.z) end
local function anim(e) local ok, a = pcall(function() return e.actor:GetCurrentAnimationState() end); return ok and tostring(a) or 'n/a' end
local function state(e)
    local okc, c = pcall(function() return e.soul:IsInCombatDanger() end)
    local okw, w = pcall(function() return e.human:IsWeaponDrawn() end)
    return string.format('pos=%s anim=%s combat=%s weapon=%s hp=%s', pos(e), anim(e), tostring(okc and c), tostring(okw and w), tostring(e.soul:GetState('health')))
end
if step == 'scan' then
    local p = player:GetWorldPos()
    for _, e in pairs(System.GetEntitiesInSphere(p, 40)) do
        if e ~= player and KCDUS.Combat.isPerson(e) and e:GetName() ~= '' and not e:IsDead() then
            System.LogAlways('KCDUS|PROBE|pp|scan|' .. e:GetName() .. '|' .. state(e))
        end
    end
    return
end
local e = System.GetEntityByName(name)
if not e then System.LogAlways('KCDUS|PROBE|pp|no-such|' .. tostring(name)) return end
if step == 'stop' then
    local ok, err = pcall(function() e:DisableBehaviorTreeEvaluation() end)
    System.LogAlways('KCDUS|PROBE|pp|stop|' .. tostring(ok) .. '|' .. tostring(err))
elseif step == 'start' then
    local ok, err = pcall(function() e:EnableBehaviorTreeEvaluation() end)
    System.LogAlways('KCDUS|PROBE|pp|start|' .. tostring(ok) .. '|' .. tostring(err))
elseif step == 'move' then
    local q = e:GetWorldPos()
    local ok, err = pcall(function() e:SetWorldPos({ x = q.x + (KCDUS_PP_DX or 2), y = q.y, z = q.z }) end)
    System.LogAlways('KCDUS|PROBE|pp|move|' .. tostring(ok) .. '|' .. tostring(err))
elseif step == 'provoke' then
    local ok, err = pcall(function() e.soul:DealDamage(0, 1, player.soul:GetId(), false) end)
    System.LogAlways('KCDUS|PROBE|pp|provoke|' .. tostring(ok) .. '|' .. tostring(err))
end
System.LogAlways('KCDUS|PROBE|pp|state|' .. name .. '|' .. state(e))
