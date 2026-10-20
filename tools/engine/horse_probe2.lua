-- Seat a ghost body on a real horse entity and carry it by moving the horse (private disposable world only). KCDUS|HP2|...
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|HP2|' .. table.concat({ ... }, '|')) end
local function try(label, f) local ok, v = pcall(f); log(label, tostring(ok), tostring(v)); return ok, v end
local p = player:GetWorldPos()
local horse
for _, e in pairs(System.GetEntities() or {}) do
    if e.class == 'Horse' and e.soul and not e:IsDead() then
        local q = e:GetWorldPos()
        if math.abs(q.x - p.x) < 400 and math.abs(q.y - p.y) < 400 then horse = e; break end
    end
end
if not horse then log('no horse near'); return end
log('horse', horse:GetName())
local hp0 = horse:GetWorldPos()
horse:SetWorldPos({ x = p.x + 4, y = p.y + 4, z = p.z })
try('horse brain off', function() horse:DisableBehaviorTreeEvaluation() end)
K.handlers['P']({ 'P', '9', 'Probe', string.format('%.2f,%.2f,%.2f', p.x + 2, p.y + 2, p.z), '0', '0,0,0', '2', '100', '100', 'x' })
K.Ghosts.update(K.now() + 0.05)
local g = K.Ghosts.list['9']
local ent = System.GetEntity(g.id)
local function pos(e) local q = e:GetWorldPos() return string.format('%.1f,%.1f,%.1f', q.x, q.y, q.z) end
local function ds() return 'mounted=' .. tostring(ent.human:IsMounted()) .. '|ghost=' .. pos(ent) .. '|horse=' .. pos(horse) .. '|anim=' .. tostring(ent.actor:GetCurrentAnimationState()) end
log('before', ds())
Script.SetTimer(600, function() try('ForceMount', function() return ent.human:ForceMount(horse.id) end) end)
Script.SetTimer(1800, function() log('after mount', ds()) end)
Script.SetTimer(2200, function() try('Mount', function() return ent.human:Mount(horse.id) end) log('after Mount()', ds()) end)
Script.SetTimer(3500, function() horse:SetWorldPos({ x = p.x + 14, y = p.y + 4, z = p.z }) end)
Script.SetTimer(4500, function() log('after moving the horse 10 m', ds()) end)
Script.SetTimer(5500, function() try('Dismount', function() return ent.human:Dismount() end) end)
Script.SetTimer(7000, function() log('after dismount', ds()) try('horse brain on', function() horse:EnableBehaviorTreeEvaluation() end) end)
