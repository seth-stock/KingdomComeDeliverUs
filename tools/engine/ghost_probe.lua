-- Spawns a ghost the way the mod does (a fake peer sample) and reads back what it is, next to Henry (private disposable world only). KCDUS|GP|...
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|GP|' .. table.concat({ ... }, '|')) end
local function names(t, label)
    local out = {}
    for k, v in pairs(t) do if type(k) == 'string' and type(v) == 'function' then out[#out + 1] = k end end
    local mt = getmetatable(t)
    if mt and type(mt.__index) == 'table' then for k, v in pairs(mt.__index) do if type(k) == 'string' and type(v) == 'function' then out[#out + 1] = k end end end
    table.sort(out)
    local line = ''
    for _, k in ipairs(out) do
        if #line + #k > 380 then log(label, line); line = '' end
        line = line .. k .. ','
    end
    log(label, line)
end
names(player, 'player-entity')
local p = player:GetWorldPos()
K.handlers['P']({ 'P', '9', 'Probe', string.format('%.2f,%.2f,%.2f', p.x + 3, p.y, p.z), '0', '0,0,0', '0', '100', '100', 'x' })
K.Ghosts.update(K.now() + 0.05)
local g = K.Ghosts.list['9']
local ent = g and g.id and System.GetEntity(g.id)
log('spawned', tostring(ent ~= nil), tostring(ent and ent:GetName()), tostring(ent and ent.class))
if ent then
    names(ent, 'ghost-entity')
    local function try(label, f) local ok, v = pcall(f); log(label, tostring(ok), tostring(v)) end
    try('player.archetype', function() return player.soul:GetArchetype() end)
    try('ghost.archetype', function() return ent.soul:GetArchetype() end)
    try('player.clothpreset', function() return player.actor:GetInitialClothingPreset() end)
    try('ghost.clothpreset', function() return ent.actor:GetInitialClothingPreset() end)
    try('player.gender', function() return player.soul:GetGender() end)
    try('ghost.gender', function() return ent.soul:GetGender() end)
    try('player.model', function() return player.GetModel and player:GetModel() or 'n/a' end)
    try('ghost.model', function() return ent.GetModel and ent:GetModel() or 'n/a' end)
    try('player.slots', function() return player.GetSlotCount and player:GetSlotCount() or 'n/a' end)
    try('ghost.slots', function() return ent.GetSlotCount and ent:GetSlotCount() or 'n/a' end)
    try('player.faction', function() return player.soul:GetFactionID() end)
    try('ghost.faction', function() return ent.soul:GetFactionID() end)
    try('look-as-actor', function() return ent.actor:MakeLookAsActor(player.id, true) end)
    try('ghost.clothpreset.after', function() return ent.actor:GetInitialClothingPreset() end)
    try('ghost.archetype.after', function() return ent.soul:GetArchetype() end)
end
