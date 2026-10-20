-- SPDX-License-Identifier: GPL-3.0-only
-- riding: a friend on a horse. Every player's game holds the same save, so the horse a friend rides (Pebble, a stable's horse) is also in this copy of the world,
-- under the same name. The rider's game says which horse (KCDUS|HRS|name); here that horse is carried to where the friend is (its brain off, as a puppet), and
-- the friend's body (a ghost) is seated on it with the engine's own mount call. Seen in the private engine: ForceMount puts a body on a real horse entity
-- (IsMounted true, the body on the saddle); the body must NOT be moved by the ghost loop while it rides, the horse is what moves. Before this, a rider walked
-- on foot beside nothing, and the horse never showed.
local K = KCDUS
K.Riding = { horses = {}, taken = {}, last = nil, sentAt = 0, carried = 0 }
local R = K.Riding

local function validName(n) return type(n) == 'string' and #n >= 1 and #n <= 80 and not string.find(n, '[^%w_%.%-%:@]') and not string.find(n, 'kcdus_', 1, true) end

local function safe(f, default) local ok, v = pcall(f); if ok and v ~= nil then return v end; return default end

-- human:GetHorse() returns the horse's WUID (userdata), not an entity; the AI module turns it into the entity (seen in the engine)
local function horseEntity(human)
    local w = safe(function() return human:GetHorse() end, nil)
    if type(w) == 'table' then return w end
    if w == nil then return nil end
    return safe(function() return XGenAIModule.GetEntityByWUID(w) end, nil)
end

-- ---------------------------------------------------------------- my own horse, told to the others
function R.myHorseName()
    local human = player.human
    if not human or not safe(function() return human:IsMounted() end, false) then return '' end
    local horse = horseEntity(human)
    local name = horse and safe(function() return horse:GetName() end, '') or ''
    return validName(name) and name or ''
end

function R.emit(now)
    local name = R.myHorseName()
    if name ~= R.last or now - R.sentAt >= 5 then
        R.last, R.sentAt = name, now
        K.out('HRS', name)
    end
end
K.every(0.5, 'riding-report', R.emit)

-- agent -> game: HORSE|<peer key>|<horse name>   ('' = the friend is not riding)
K.handlers['HORSE'] = function(f)
    local key, name = f[2], f[3] or ''
    if not key or not string.match(key, '^%d+$') or #key > 3 then return end
    R.horses[key] = validName(name) and name or nil
end

-- ---------------------------------------------------------------- a friend's horse, in this copy of the world
local function horseOf(g)
    local name = R.horses[g.key]
    if not name then return nil end
    local e = System.GetEntityByName(name)
    if not e or e.class ~= 'Horse' or safe(function() return e:IsDead() end, true) then return nil end
    -- never take the horse this player is sitting on, or one another friend already carries
    if safe(function() return player.human:IsMounted() and horseEntity(player.human) == e end, false) then return nil end
    if R.taken[e.id] and R.taken[e.id] ~= g.key then return nil end
    return e
end

local function release(g, ent)
    local id = g.horseId
    g.horseId = nil
    if id == nil then return end
    if ent then K.try('riding:dismount', function() ent.human:Dismount() end) end
    local h = System.GetEntity(id)
    if h then K.try('riding:brain-on', function() h:EnableBehaviorTreeEvaluation() end) end
    R.taken[id] = nil
    K.out('RIDING', 'released', g.key)
end
R.release = release

-- Called by the ghost loop for every ghost. True: this ghost is carried by its horse, the ghost loop must leave the body (and its animation) alone.
function R.drive(ent, g, now)
    local mounted = K.num(g.flags, 0) % 4 >= 2 and now - (g.stamp or now) <= 1.5
    if not mounted then
        if g.horseId then release(g, ent) end
        return false
    end
    local h = horseOf(g)
    if not h then
        if g.horseId then release(g, ent) end
        return false
    end
    if g.horseId ~= h.id then
        g.horseId = h.id
        R.taken[h.id] = g.key
        K.try('riding:brain-off', function() h:DisableBehaviorTreeEvaluation() end)
        K.try('riding:brain-off2', function() AI.SetBehaviorTreeEvaluationEnabled(h.id, false) end)
        K.out('RIDING', 'taken', g.key, h:GetName())
    end
    K.try('riding:horse', function()
        h:SetWorldPos({ x = g.cx, y = g.cy, z = g.cz })
        h:SetWorldAngles({ x = 0, y = 0, z = g.cyaw })
    end)
    if not safe(function() return ent.human:IsMounted() end, false) and now >= (g.mountRetry or 0) then
        g.mountRetry = now + 2
        K.try('riding:seat', function() ent:SetWorldPos({ x = g.cx, y = g.cy, z = g.cz }); ent.human:ForceMount(h.id) end)
    end
    R.carried = R.carried + 1
    return true
end

function R.onWorldReset() R.horses = {}; R.taken = {}; R.last = nil end
