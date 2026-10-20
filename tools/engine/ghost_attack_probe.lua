-- Can a ghost (an ordinary NPC body the mod spawns for a friend) attack a real NPC with the adapter's mirror attack, and does its animation state show it?
-- Private disposable world only. KCDUS|GA|...
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|GA|' .. table.concat({ ... }, '|')) end
local function try(label, f) local ok, v = pcall(f); log(label, tostring(ok), tostring(v)); return ok, v end
local p = player:GetWorldPos()
-- the target: the nearest living armed person within 30 m
local best, bd
for _, e in pairs(System.GetEntitiesInSphere(p, 30) or {}) do
    if e ~= player and e.class and string.sub(e.class, 1, 3) == 'NPC' and e.soul and not string.find(e:GetName(), 'kcdus_', 1, true) and not e:IsDead() then
        local q = e:GetWorldPos(); local d = math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2)
        if not bd or d < bd then best, bd = e, d end
    end
end
if not best then log('no target'); return end
log('target', best:GetName(), string.format('%.1f m', bd))
local tstate; try('target combat-state', function() tstate = best.human:PlayAnim('@kcdus/combat-state', ''); return tstate and tstate.self end)
try('target weapon preset', function() return best.actor.GetInitialWeaponPreset and best.actor:GetInitialWeaponPreset() or 'n/a' end)
K.handlers['P']({ 'P', '9', 'Probe', string.format('%.2f,%.2f,%.2f', p.x + 1.5, p.y + 1.5, p.z), '0', '0,0,0', '0', '100', '100', 'x' })
K.Ghosts.update(K.now() + 0.05)
local g = K.Ghosts.list['9']
local ent = g and g.id and System.GetEntity(g.id)
if not ent then log('no ghost'); return end
local q = best:GetWorldPos()
ent:SetWorldPos({ x = q.x + 1.2, y = q.y, z = q.z })
local function state() local ok, s = pcall(function() return ent.actor:GetCurrentAnimationState() end) return tostring(s) .. '|drawn=' .. tostring(pcall(function() return ent.human:IsWeaponDrawn() end) and ent.human:IsWeaponDrawn()) end
try('give weapon preset', function() return ent.actor:EquipWeaponPreset(best.actor:GetInitialWeaponPreset()) end)
Script.SetTimer(400, function() try('draw', function() return ent.human:DrawWeapon() end) log('state after draw call', state()) end)
Script.SetTimer(1500, function() log('state 1.5s', state()) end)
Script.SetTimer(1600, function() try('mirror target', function() return ent.human:PlayAnim('@kcdus/mirror/target', tstate and tstate.self or 0) end) end)
Script.SetTimer(1800, function() try('mirror attack', function() return ent.human:PlayAnim('@kcdus/mirror/attack', '') end) end)
for i = 1, 8 do Script.SetTimer(1800 + i * 250, function() log('attack+' .. i * 0.25, state()) end) end
Script.SetTimer(4500, function() try('mirror clear', function() return ent.human:PlayAnim('@kcdus/mirror/clear', '') end) log('final', state()) end)
