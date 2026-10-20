-- Does an NPC play its hit reaction when a friend's blow is applied with the reaction NOT suppressed, and does it stay calm (no target, no weapon drawn)?
-- Private disposable world only. KCDUS|HR|...
local function log(...) System.LogAlways('KCDUS|HR|' .. table.concat({ ... }, '|')) end
local g = System.GetEntityByName('rat_guard23')
if not g then log('no guard'); return end
local function state()
    local ok, s = pcall(function() return g.actor:GetCurrentAnimationState() end)
    local cs = g.human:PlayAnim('@kcdus/combat-state', '')
    return tostring(s) .. '|hp=' .. tostring(g.soul:GetState('health')) .. '|drawn=' .. tostring(g.human:IsWeaponDrawn()) .. '|mode=' .. tostring(cs and cs.mode) .. '|target=' .. tostring(cs and cs.target) .. '|active=' .. tostring(cs and cs.active)
end
local suppress = KCDUS_HR_SUPPRESS
if suppress == nil then suppress = false end
log('suppress', tostring(suppress))
log('before', state())
local ok, e = pcall(function() g.soul:DealDamage(0, 8, nil, suppress) end)
log('dealt', tostring(ok), tostring(e))
for i = 1, 12 do Script.SetTimer(i * 400, function() log('t+' .. string.format('%.1f', i * 0.4), state()) end) end
