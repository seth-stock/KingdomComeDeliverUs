-- loot: shared loot of corpses and containers (docs/CAPABILITIES.md, "Loot"). Every player's game keeps its own copy of the world (the same save, so the same bodies
-- and stashes). The HOST decides who got an item; nobody else ever holds one it did not confirm:
--   * the host takes something: it tells the guests, and the item leaves their copy of that body or stash (KCDUS|LOOT|took);
--   * a guest takes something: its game already moved it (that is how the loot screen works), so the guest ASKS the host (KCDUS|LOOT|ask). The host answers ok
--     (it removes the item from ITS copy too and tells the other guests) or gone (somebody was first). On "gone" the item is taken back off the guest's Henry;
--   * an ask the host never answers is taken back after 20 s and before a world save: an unconfirmed item is never kept, and never travels into a save.
-- A lost answer therefore loses an item; it can never duplicate one. What is NOT covered: items on the ground (PickableItem), saddlebags, shops, anything an
-- NPC does to a body. Identity: a body's name; a stash has none, so its position (the same in every copy of the save).
--
-- agent -> game:  LOOTMODE|1|<scope>|<host 0/1>      on for this world (only when both players are in the same world); LOOTMODE|0 off
--                 LOOTASK|<scope>|<from>|<nonce>|<tok>|<id>|<class>|<n>      (host only) a guest's take
--                 LOOTRES|<scope>|<tok>|<ok|gone|none>|<id>|<class>|<n>      (guest) the host's answer to its ask
--                 LOOTTOOK|<scope>|<id>|<class>|<n>                          (guest) the host's copy lost it: remove it from this one too
--                 LOOTSETTLE                                                  take back every take the host has not confirmed
-- game -> agent:  KCDUS|LOOT|took|<id>|<class>|<n>|<health>          (host) the host took it
--                 KCDUS|LOOT|ask|<nonce>|<tok>|<id>|<class>|<n>|<health>   (guest) this player took it, host please decide
--                 KCDUS|LOOT|res|<from>|<tok>|<verdict>|<id>|<class>|<n>|<health>   (host) the verdict on a guest's ask
--                 KCDUS|LOOT|unconfirmed|<id>|<class>|<n>             an item was taken back (no answer, or the host said gone)
local K = KCDUS
local L = { enabled = false, scope = '', host = false, snap = {}, pending = {}, tok = 0, reach = 9, settleS = 20, taken = 0, applied = 0, nonce = '' }
K.Loot = L

do   -- a per-world-load nonce so an ask from an earlier load can never be mistaken for a new one
    local hex = '0123456789abcdef'
    local s = ''
    for _ = 1, 8 do local i = math.random(1, 16); s = s .. string.sub(hex, i, i) end
    L.nonce = s
end

local CLASS = '^[%x%-]+$'
local function validClass(c) return type(c) == 'string' and #c >= 8 and #c <= 40 and string.find(c, CLASS) ~= nil end
local function validId(n) return type(n) == 'string' and #n >= 3 and #n <= 80 and not string.find(n, '[^%w_%.%-%:@]') and not string.find(n, 'kcdus_', 1, true) end
local function fmt(n) return string.format('%.4f', n) end

local function posId(p) return string.format('s@%d_%d_%d', math.floor(p.x * 10), math.floor(p.y * 10), math.floor(p.z * 10)) end

local function isDead(e) local ok, v = pcall(function() return e:IsDead() end); return ok and v == true end
local function className(e) local ok, c = pcall(function() return e.class end); return ok and c or '' end

-- something that holds loot: a dead person, or a stash
local function lootable(e)
    if e == player or not e.inventory then return false end
    local c = className(e)
    if string.sub(c, 1, 3) == 'NPC' then return isDead(e) end
    return c == 'Stash'
end

local function idOf(e)
    local ok, n = pcall(function() return e:GetName() end)
    if ok and validId(n) then return n end
    if className(e) == 'Stash' then return posId(e:GetWorldPos()) end
    return nil
end

local function record(v)
    local ok, it = pcall(ItemManager.GetItem, v)
    if ok and type(it) == 'table' then return it end
    if type(v) == 'table' then return v end
    return nil
end

-- what an inventory holds, as classes: how many, and the health of the last one seen
local function read(e)
    local counts, hp = {}, {}
    local ok, t = pcall(function() return e.inventory:GetInventoryTable() end)
    if not ok or type(t) ~= 'table' then return nil end
    for _, v in pairs(t) do
        local it = record(v)
        if it and it.class then
            counts[it.class] = (counts[it.class] or 0) + (tonumber(it.amount) or 1)
            hp[it.class] = tonumber(it.health) or 1
        end
    end
    return counts, hp
end

local function countOf(e, class)
    local c = read(e)
    return c and (c[class] or 0) or 0
end

local function find(id)
    if string.sub(id, 1, 2) == 's@' then
        local x, y, z = string.match(id, '^s@(-?%d+)_(-?%d+)_(-?%d+)$')
        if not x then return nil end
        local pos = { x = tonumber(x) / 10, y = tonumber(y) / 10, z = tonumber(z) / 10 }
        local ok, list = pcall(System.GetEntitiesInSphere, pos, 1.5)
        if not ok or type(list) ~= 'table' then return nil end
        local best
        for _, e in pairs(list) do if className(e) == 'Stash' and idOf(e) == id then best = e end end
        return best
    end
    local ok, e = pcall(System.GetEntityByName, id)
    if ok and e and e.inventory and e ~= player then return e end
    return nil
end

local function removeFrom(e, class, n)
    local have = countOf(e, class)
    local k = math.min(n, have)
    if k <= 0 then return 0 end
    local ok = pcall(function() e.inventory:DeleteItemOfClass(class, k) end)
    return ok and k or 0
end

local function rebaseline(e)
    local id = idOf(e)
    if not id then return end
    local counts, hp = read(e)
    if counts then L.snap[id] = { counts = counts, hp = hp } end
end

local function took(id, class, n, hp)
    L.taken = L.taken + 1
    if L.host then
        K.out('LOOT', 'took', id, class, n, fmt(hp))
    else
        L.tok = L.tok + 1
        L.pending[L.tok] = { id = id, class = class, n = n, hp = hp, at = K.now() }
        K.out('LOOT', 'ask', L.nonce, L.tok, id, class, n, fmt(hp))
    end
end

function L.scan()
    if not L.enabled or not K.inWorldRaw() then return end
    local pp = player:GetWorldPos()
    local ok, list = pcall(System.GetEntitiesInSphere, pp, L.reach)
    if not ok or type(list) ~= 'table' then return end
    local seen = {}
    for _, e in pairs(list) do
        if lootable(e) then
            local id = idOf(e)
            if id then
                seen[id] = true
                local counts, hp = read(e)
                if counts then
                    local prev = L.snap[id]
                    if prev then
                        for class, n in pairs(prev.counts) do
                            local m = counts[class] or 0
                            if m < n and validClass(class) then took(id, class, n - m, prev.hp[class] or 1) end
                        end
                    end
                    L.snap[id] = { counts = counts, hp = hp }
                end
            end
        end
    end
    for id in pairs(L.snap) do if not seen[id] then L.snap[id] = nil end end   -- out of reach: forgotten (a re-approach starts from what is there)
end

-- an unconfirmed take is not kept
function L.settle(maxAgeS)
    local now = K.now()
    local n = 0
    for tok, t in pairs(L.pending) do
        if maxAgeS <= 0 or now - t.at > maxAgeS then
            L.pending[tok] = nil
            local ok = pcall(function() player.inventory:DeleteItemOfClass(t.class, t.n) end)
            n = n + 1
            K.out('LOOT', 'unconfirmed', t.id, t.class, t.n, ok and 1 or 0)
        end
    end
    return n
end

K.handlers['LOOTMODE'] = function(f)
    L.enabled = f[2] == '1'
    L.scope = f[3] or ''
    L.host = f[4] == '1'
    L.snap = {}
    if not L.enabled then L.settle(0) end
    K.out('LOOTMODE', L.enabled and 'on' or 'off', L.scope, L.host and 'host' or 'guest')
end

K.handlers['LOOTSETTLE'] = function(f) L.settle(0) end

-- (host) a guest's take: decide, apply to the host's copy, answer
K.handlers['LOOTASK'] = function(f)
    local scope, from, nonce, tok, id, class, n = f[2], f[3], f[4], f[5], f[6], f[7], tonumber(f[8])
    if not L.enabled or not L.host or scope ~= L.scope or scope == '' or not validId(id) or not validClass(class) or not n or n < 1 or n > 100000 then return end
    local e = find(id)
    local verdict, hp = 'none', 1
    if e then
        local counts, hps = read(e)
        hp = hps and hps[class] or 1
        if counts and (counts[class] or 0) >= n then
            local k = removeFrom(e, class, n)
            if k == n then verdict = 'ok' else verdict = 'gone' end
            rebaseline(e)                                           -- the host's own watcher must not report this removal as a host take
        else
            verdict = 'gone'
        end
    end
    K.out('LOOT', 'res', from, tok, verdict, id, class, n, fmt(hp))
    if verdict == 'ok' then K.out('LOOT', 'took', id, class, n, fmt(hp)) end    -- the other guests lose it too
end

-- (guest) the host's answer to my ask
K.handlers['LOOTRES'] = function(f)
    local scope, tok, verdict, id, class, n = f[2], tonumber(f[3]), f[4], f[5], f[6], tonumber(f[7])
    if scope ~= L.scope or not tok then return end
    local t = L.pending[tok]
    if not t then return end
    L.pending[tok] = nil
    if verdict == 'ok' then
        K.out('LOOT', 'confirmed', t.id, t.class, t.n)
    else
        local ok = pcall(function() player.inventory:DeleteItemOfClass(t.class, t.n) end)
        K.out('LOOT', 'unconfirmed', t.id, t.class, t.n, ok and 1 or 0)    -- somebody else was first (or it was never there): taken back
    end
end

-- (guest) the host's copy lost an item: so does mine
K.handlers['LOOTTOOK'] = function(f)
    local scope, id, class, n = f[2], f[3], f[4], tonumber(f[5])
    if not L.enabled or L.host or scope ~= L.scope or not validId(id) or not validClass(class) or not n or n < 1 then return end
    local e = find(id)
    if not e then K.out('LOOTAPPLIED', id, class, 0, 'no-such-body'); return end
    local k = removeFrom(e, class, n)
    rebaseline(e)
    L.applied = L.applied + 1
    K.out('LOOTAPPLIED', id, class, k, k == n and 'applied' or 'partial')
end

K.every(0.5, 'loot-shared', function() K.try('loot', L.scan) end)
K.every(1.0, 'loot-settle', function() K.try('loot-settle', function() L.settle(L.settleS) end) end)

function L.onWorldReset() L.snap = {}; L.pending = {} end
