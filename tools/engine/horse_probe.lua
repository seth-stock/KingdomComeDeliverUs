-- Horses and mounting: what a horse entity is, and whether a body can be put on one by script (private disposable world only). KCDUS|HP|...
local function log(...) System.LogAlways('KCDUS|HP|' .. table.concat({ ... }, '|')) end
local function try(label, f) local ok, v = pcall(f); log(label, tostring(ok), tostring(v)); return ok, v end
local classes = {}
for _, e in pairs(System.GetEntities() or {}) do
    local c = e.class
    if c and (string.find(c, 'orse') or string.find(c, 'Animal') or string.find(c, 'Mount')) then classes[c] = (classes[c] or 0) + 1 end
end
for c, n in pairs(classes) do log('class', c, tostring(n)) end
local horse
for _, e in pairs(System.GetEntities() or {}) do
    if e.class and string.find(e.class, 'orse') and e.soul and not e:IsDead() then horse = e; break end
end
if not horse then log('no horse found'); return end
local hp = horse:GetWorldPos()
log('horse', horse:GetName(), horse.class, string.format('%.1f,%.1f,%.1f', hp.x, hp.y, hp.z))
local function names(t) local out = {} for k, v in pairs(t) do if type(k) == 'string' and type(v) == 'function' then out[#out + 1] = k end end local mt = getmetatable(t) if mt and type(mt.__index) == 'table' then for k, v in pairs(mt.__index) do if type(k) == 'string' and type(v) == 'function' then out[#out + 1] = k end end end table.sort(out) return table.concat(out, ',') end
log('horse.human', horse.human and names(horse.human) or 'nil')
log('horse.actor', horse.actor and string.sub(names(horse.actor), 1, 380) or 'nil')
try('horse mount point', function() local p = player.human:GetHorseMountPoint(horse.id) return tostring(p and p.x) end)
try('horse props', function() local o = {} for k, v in pairs(horse.Properties or {}) do if type(v) ~= 'table' then o[#o + 1] = k .. '=' .. tostring(v) end end table.sort(o) return table.concat(o, ';') end)
try('horse soul id', function() return horse.soul:GetId() end)
try('horse archetype', function() return horse.soul:GetArchetype().Name end)
try('player horse', function() local h = player.human:GetHorse() return h and h:GetName() or 'none' end)
