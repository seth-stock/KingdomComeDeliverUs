-- ghost look probe 2: what an archetype is, what the entity's own look data says (private disposable world only). KCDUS|GP2|...
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|GP2|' .. table.concat({ ... }, '|')) end
local function dumpt(label, t, depth)
    depth = depth or 0
    if type(t) ~= 'table' or depth > 2 then log(label, tostring(t)); return end
    local keys = {}
    for k in pairs(t) do keys[#keys + 1] = tostring(k) end
    table.sort(keys)
    for _, k in ipairs(keys) do
        local v = t[k]
        if type(v) == 'table' then dumpt(label .. '.' .. k, v, depth + 1)
        elseif type(v) ~= 'function' then log(label .. '.' .. k, tostring(v)) end
    end
end
local g = K.Ghosts.list['9']
local ent = g and g.id and System.GetEntity(g.id)
if not ent then log('no ghost'); return end
dumpt('player.arch', player.soul:GetArchetype())
dumpt('ghost.arch', ent.soul:GetArchetype())
local function try(label, f) local ok, v = pcall(f); log(label, tostring(ok), tostring(v)) end
try('player.Properties.fileModel', function() return tostring(player.Properties and player.Properties.fileModel) end)
try('ghost.Properties.fileModel', function() return tostring(ent.Properties and ent.Properties.fileModel) end)
try('player.Properties.keys', function() local o = {} for k, v in pairs(player.Properties or {}) do o[#o + 1] = tostring(k) .. '=' .. tostring(type(v) == 'table' and 'table' or v) end table.sort(o) return table.concat(o, ';') end)
try('ghost.Properties.keys', function() local o = {} for k, v in pairs(ent.Properties or {}) do o[#o + 1] = tostring(k) .. '=' .. tostring(type(v) == 'table' and 'table' or v) end table.sort(o) return table.concat(o, ';') end)
try('player.soulprop', function() return tostring(player.soul and player.soul:GetNameStringId()) end)
try('player.hairhead', function() return tostring(player.soul.OverrideHair) .. '/' .. tostring(player.soul.OverrideHead) end)
