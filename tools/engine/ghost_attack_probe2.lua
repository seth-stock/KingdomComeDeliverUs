-- Ghost armed with a guard's kit attacks the guard through the adapter's mirror attack (private disposable world only). KCDUS|GA2|...
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|GA2|' .. table.concat({ ... }, '|')) end
local function try(label, f) local ok, v = pcall(f); log(label, tostring(ok), tostring(v)); return ok, v end
local guard = System.GetEntityByName('rat_guard23')
if not guard then log('no guard'); return end
local gp = guard:GetWorldPos()
player:SetWorldPos({ x = gp.x - 4, y = gp.y, z = gp.z })
K.handlers['P']({ 'P', '9', 'Probe', string.format('%.2f,%.2f,%.2f', gp.x - 3, gp.y, gp.z), '0', '0,0,0', '0', '100', '100', 'x' })
K.Ghosts.update(K.now() + 0.05)
local ent = System.GetEntity(K.Ghosts.list['9'].id)
ent:SetWorldPos({ x = gp.x + 1.1, y = gp.y, z = gp.z })
local function state() local ok, s = pcall(function() return ent.actor:GetCurrentAnimationState() end) return tostring(s) .. '|drawn=' .. tostring(ent.human:IsWeaponDrawn()) end
local made = 0
try('clear ghost inventory', function() ent.inventory:RemoveAllItems() end)
for _, id in pairs(guard.inventory:GetInventoryTable()) do
    local it = ItemManager.GetItem(id)
    if it and it.class and not string.find(it.class, '0000000', 1, true) then
        local ok, nid = pcall(ItemManager.CreateItem, it.class, it.health or 1, 1)
        if ok and nid then pcall(function() ent.inventory:AddItem(nid) ent.actor:EquipInventoryItem(nid) end) made = made + 1 end
    end
end
log('items copied', tostring(made))
local cs; try('guard combat-state', function() cs = guard.human:PlayAnim('@kcdus/combat-state', ''); return cs and cs.self end)
Script.SetTimer(500, function() log('state before draw', state()) try('draw', function() return ent.human:DrawWeapon() end) end)
Script.SetTimer(2500, function() log('state after draw', state()) end)
local function entityNumber(e) if type(e.id) == 'number' then return e.id end local hex = string.match(tostring(e.id), '([0-9A-Fa-f]+)$') return hex and tonumber(hex, 16) end
local function dump(r) local o = {} for k, v in pairs(r or {}) do o[#o + 1] = tostring(k) .. '=' .. tostring(v) end table.sort(o) return table.concat(o, ',') end
Script.SetTimer(2600, function() try('mirror target', function() return dump(ent.human:PlayAnim('@kcdus/mirror/target', string.format('%08x', entityNumber(guard)))) end) end)
Script.SetTimer(2800, function() try('mirror attack', function() local r = ent.human:PlayAnim('@kcdus/mirror/attack', '') local o = {} for k, v in pairs(r or {}) do o[#o + 1] = tostring(k) .. '=' .. tostring(v) end return table.concat(o, ',') end) end)
for i = 1, 10 do Script.SetTimer(2800 + i * 250, function() log('attack+' .. i * 0.25, state()) end) end
Script.SetTimer(6000, function() try('mirror clear', function() return ent.human:PlayAnim('@kcdus/mirror/clear', '') end) log('final', state()) end)
