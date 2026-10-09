-- loot: shared items (docs/CAPABILITIES.md, "Loot and items"). Every player's game keeps its own copy of the world (the same save, so the same bodies, stashes,
-- shops, horses and things lying about). The HOST decides who got an item; nobody else ever keeps one it did not confirm:
--   * the host takes something: it tells the guests, and the item leaves their copy of that body, stash, saddlebag, shop or patch of ground (KCDUS|LOOT|took);
--   * a guest takes something: its game already moved it (that is how the loot, trade and pick-up screens work), so the guest ASKS the host (KCDUS|LOOT|ask).
--     The host answers ok (it removes the item from ITS copy too and tells the other guests) or gone (somebody was first). On "gone" the item is taken back off
--     the guest's Henry, and for a shop the money the guest paid is given back;
--   * an ask the host never answers is taken back after 20 s and before a world save: an unconfirmed item is never kept, and never travels into a save;
--   * a PUT (into a stash, a saddlebag, a body, or sold to a shop) and a DROP (onto the ground) cannot duplicate anything (the item left this Henry), so they are
--     simply told to the others, whose copy of that stash, saddlebag or shop gains it, or whose ground gets it (KCDUS|LOOT|put / drop).
-- A take must show up in this Henry's pack (and a put leave it): a shop restocking, an NPC or a friend's change applied here is never mistaken for this player's.
-- A lost answer therefore loses an item; it can never duplicate one.
--
-- What holds items, and how it is known in every copy:
--   * a dead person: its name;  * a horse (saddlebags): its name;  * a stash (chests, and the shops' stock: Stash linked with a shop): its position, s@x_y_z
--     (stashes have no names; the same in every copy of the save); a shop's stash is watched from 40 m (the trade screen is at the counter), the rest from 9 m;
--   * a thing lying on the ground (PickableItem, not one only NPCs use, not a shop's display piece, not a body's): its position, g@x_y_z, plus its class.
-- NOT covered: the shop's own money (the trade screen's price is not readable; the stock is shared), anything an NPC does to a body, quest rewards (rewards.lua).
--
-- agent -> game:  LOOTMODE|1|<scope>|<host 0/1>      on for this world (only when both players are in the same world); LOOTMODE|0 off
--                 LOOTASK|<scope>|<from>|<nonce>|<tok>|<id>|<class>|<n>      (host only) a guest's take
--                 LOOTRES|<scope>|<tok>|<ok|gone|none>|<id>|<class>|<n>      (guest) the host's answer to its ask
--                 LOOTTOOK|<scope>|<id>|<class>|<n>                          (guest) the host's copy lost it: remove it from this one too
--                 LOOTPUT|<scope>|<id>|<class>|<n>|<health>                  a friend put it there: this copy gains it
--                 LOOTDROP|<scope>|<id>|<class>|<n>|<health>                 a friend dropped it on the ground there: it lies here too
--                 LOOTSETTLE                                                  take back every take the host has not confirmed
-- game -> agent:  KCDUS|LOOT|took|<id>|<class>|<n>|<health>          (host) the host took it
--                 KCDUS|LOOT|ask|<nonce>|<tok>|<id>|<class>|<n>|<health>   (guest) this player took it, host please decide
--                 KCDUS|LOOT|res|<from>|<tok>|<verdict>|<id>|<class>|<n>|<health>   (host) the verdict on a guest's ask
--                 KCDUS|LOOT|put|<id>|<class>|<n>|<health>  /  KCDUS|LOOT|drop|<id>|<class>|<n>|<health>
--                 KCDUS|LOOT|unconfirmed|<id>|<class>|<n>             an item was taken back (no answer, or the host said gone)
local K = KCDUS
System.LogAlways("KCDUS|LOAD|loot")
local L = { enabled = false, scope = '', host = false, snap = {}, pending = {}, tok = 0, reach = 9, shopReach = 40, dropReach = 3, settleS = 20, taken = 0,
    applied = 0, put = 0, dropped = 0, nonce = '', gid = {}, pc = nil, money = nil, spawned = {} }
K.Loot = L
L.MONEY = '5ef63059-322e-4e1b-abe8-926e100c770e'

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

local function posKey(prefix, p) return string.format('%s@%d_%d_%d', prefix, math.floor(p.x * 10), math.floor(p.y * 10), math.floor(p.z * 10)) end
local function keyPos(id)
    local x, y, z = string.match(id, '^%a@(-?%d+)_(-?%d+)_(-?%d+)$')
    if not x then return nil end
    return { x = tonumber(x) / 10, y = tonumber(y) / 10, z = tonumber(z) / 10 }
end
local function dist(a, b) return math.sqrt((a.x - b.x) ^ 2 + (a.y - b.y) ^ 2 + (a.z - b.z) ^ 2) end

local function isDead(e) local ok, v = pcall(function() return e:IsDead() end); return ok and v == true end
local function className(e) local ok, c = pcall(function() return e.class end); return ok and c or '' end

-- the item a thing on the ground is: its record, or nil when it is not one the player can simply pick up
local function groundItem(e)
    local ok, it = pcall(function()
        if e.npcOnly or e.item:IsFromShop() or e.item:BelongsToDeadBody() then return nil end
        return ItemManager.GetItem(e.item:GetId())
    end)
    if ok and type(it) == 'table' and validClass(it.class) then return it end
    return nil
end

local function isShopStash(e)
    local ok, link = pcall(Shops.IsLinkedWithShop, e.id)
    if not ok or link == nil then return false end
    local okV, v = pcall(Framework.IsValidWUID, link)
    return okV and v == true
end

-- what kind of item holder this is: 'body' | 'stash' | 'shop' | 'horse' | 'ground' | nil
local function kindOf(e)
    if e == player then return nil end
    local c = className(e)
    if c == 'PickableItem' then return groundItem(e) and 'ground' or nil end
    if not e.inventory then return nil end
    if string.sub(c, 1, 3) == 'NPC' then return isDead(e) and 'body' or nil end
    if c == 'Horse' then return 'horse' end
    if c == 'Stash' then return isShopStash(e) and 'shop' or 'stash' end
    return nil
end

local function idOf(e, kind)
    kind = kind or kindOf(e)
    if kind == 'ground' then
        local k = tostring(e.id)
        if not L.gid[k] then L.gid[k] = posKey('g', e:GetWorldPos()) end         -- named where it was first seen: a rolling thing keeps its name
        return L.gid[k]
    end
    if kind == 'stash' or kind == 'shop' then return posKey('s', e:GetWorldPos()) end
    local ok, n = pcall(function() return e:GetName() end)
    if ok and validId(n) then return n end
    return nil
end

local function record(v)
    local ok, it = pcall(ItemManager.GetItem, v)
    if ok and type(it) == 'table' then return it end
    if type(v) == 'table' then return v end
    return nil
end

-- what a holder holds, as classes: how many, and the health of the last one seen
local function read(e, kind)
    if kind == 'ground' then
        local it = groundItem(e)
        if not it then return nil end
        return { [it.class] = tonumber(it.amount) or 1 }, { [it.class] = tonumber(it.health) or 1 }
    end
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

local function playerCounts() local c = read(player, 'player') return c or {} end

-- the holder with this id in this copy of the world (a ground id also needs the class)
local function find(id, class)
    local prefix = string.sub(id, 1, 2)
    if prefix == 's@' or prefix == 'g@' then
        local pos = keyPos(id)
        if not pos then return nil end
        local ok, list = pcall(System.GetEntitiesInSphere, pos, prefix == 'g@' and 2.5 or 1.5)
        if not ok or type(list) ~= 'table' then return nil end
        local best, bd
        for _, e in pairs(list) do
            local kind = kindOf(e)
            if prefix == 's@' and (kind == 'stash' or kind == 'shop') and idOf(e, kind) == id then return e end
            if prefix == 'g@' and kind == 'ground' then
                local c = read(e, 'ground')
                if c and (not class or c[class]) then
                    local d = dist(e:GetWorldPos(), pos)
                    if idOf(e, kind) == id then return e end
                    if not bd or d < bd then best, bd = e, d end
                end
            end
        end
        return best
    end
    local ok, e = pcall(System.GetEntityByName, id)
    if ok and e and e.inventory and e ~= player then return e end
    return nil
end

local function countOf(e, class, kind)
    local c = read(e, kind or kindOf(e))
    return c and (c[class] or 0) or 0
end

local function removeFrom(e, class, n)
    local kind = kindOf(e)
    local have = countOf(e, class, kind)
    local k = math.min(n, have)
    if k <= 0 then return 0 end
    if kind == 'ground' then
        if k < have then return 0 end                                            -- a thing on the ground is taken whole
        local ok = pcall(System.RemoveEntity, e.id)
        return ok and k or 0
    end
    local ok = pcall(function() e.inventory:DeleteItemOfClass(class, k) end)
    return ok and k or 0
end

local function giveTo(inv, class, n, hp)
    return pcall(function()
        local id = ItemManager.CreateItem(class, hp or 1, n)
        inv:AddItem(id)
    end)
end

-- the snapshot's key: a holder's id, but a thing on the ground is known by its entity here (two things dropped on one spot share a position id)
local function snapKey(e, kind, id) if kind == 'ground' then return 'e:' .. tostring(e.id) end return id end

local function rebaseline(e)
    local kind = kindOf(e)
    local id = kind and idOf(e, kind)
    if not id then return end
    local counts, hp = read(e, kind)
    if counts then L.snap[snapKey(e, kind, id)] = { id = id, counts = counts, hp = hp, kind = kind, pos = e:GetWorldPos() } end
end

L.gainLog = {}
local function took(id, class, n, hp, price)
    L.taken = L.taken + 1
    local now = K.now()
    L.gainLog[#L.gainLog + 1] = { class = class, n = n, at = now }            -- rewards.lua: a take is never mistaken for a quest's reward
    while #L.gainLog > 0 and now - L.gainLog[1].at > 120 do table.remove(L.gainLog, 1) end
    if L.host then
        K.out('LOOT', 'took', id, class, n, fmt(hp))
    else
        L.tok = L.tok + 1
        L.pending[L.tok] = { id = id, class = class, n = n, hp = hp, at = K.now(), price = price or 0 }
        K.out('LOOT', 'ask', L.nonce, L.tok, id, class, n, fmt(hp))
    end
end

local function pendingClass(class)
    for _, t in pairs(L.pending) do if t.class == class then return true end end
    return false
end

function L.scan()
    if not L.enabled or not K.inWorldRaw() then return end
    local pp = player:GetWorldPos()
    local ok, list = pcall(System.GetEntitiesInSphere, pp, L.shopReach)
    if not ok or type(list) ~= 'table' then return end
    local pc = playerCounts()
    local prevPc = L.pc or pc
    local gained, lost = {}, {}
    for class, n in pairs(pc) do local d = n - (prevPc[class] or 0); if d > 0 then gained[class] = d end end
    for class, n in pairs(prevPc) do local d = n - (pc[class] or 0); if d > 0 then lost[class] = d end end
    local spent = (lost[L.MONEY] or 0)
    local seen = {}
    for _, e in pairs(list) do
        local kind = kindOf(e)
        if kind and (kind == 'shop' or dist(pp, e:GetWorldPos()) <= L.reach) then
            local id = idOf(e, kind)
            if id then
                local key = snapKey(e, kind, id)
                seen[key] = true
                local counts, hp = read(e, kind)
                if counts then
                    local prev = L.snap[key]
                    if prev then
                        for class, n in pairs(prev.counts) do
                            local m = counts[class] or 0
                            local g = math.min(n - m, gained[class] or 0)                     -- it left the holder AND came into this Henry's pack
                            if g > 0 and validClass(class) then
                                gained[class] = gained[class] - g
                                local price = 0
                                if kind == 'shop' and class ~= L.MONEY then price = spent; spent = 0 end
                                took(id, class, g, prev.hp[class] or 1, price)
                            end
                        end
                        for class, m in pairs(counts) do
                            local n = prev.counts[class] or 0
                            local p = math.min(m - n, lost[class] or 0)                        -- it came into the holder AND left this Henry's pack
                            if p > 0 and validClass(class) and not pendingClass(class) then
                                lost[class] = lost[class] - p
                                L.put = L.put + 1
                                K.out('LOOT', 'put', id, class, p, fmt(hp[class] or 1))
                            end
                        end
                    elseif kind == 'ground' and not L.spawned[tostring(e.id)] and dist(pp, e:GetWorldPos()) <= L.dropReach then
                        for class, m in pairs(counts) do                                       -- a new thing at Henry's feet that just left his pack: a drop
                            local p = math.min(m, lost[class] or 0)
                            if p > 0 and validClass(class) and not pendingClass(class) then
                                lost[class] = lost[class] - p
                                L.dropped = L.dropped + 1
                                K.out('LOOT', 'drop', id, class, p, fmt(hp[class] or 1))
                            end
                        end
                    end
                    L.snap[key] = { id = id, counts = counts, hp = hp, kind = kind, pos = e:GetWorldPos() }
                end
            end
        end
    end
    -- a thing on the ground that was in reach and is gone, while the same class came into this Henry's pack: picked up
    for key, s in pairs(L.snap) do
        if not seen[key] then
            if s.kind == 'ground' and dist(pp, s.pos) <= L.reach - 1 then
                for class, n in pairs(s.counts) do
                    local g = math.min(n, gained[class] or 0)
                    if g > 0 and validClass(class) then gained[class] = gained[class] - g; took(s.id, class, g, s.hp[class] or 1, 0) end
                end
            end
            L.snap[key] = nil                                                               -- out of reach (or gone): a re-approach starts from what is there
        end
    end
    L.pc = pc
end

-- an unconfirmed take is not kept (and a shop's price comes back)
local function takeBack(t)
    local ok = pcall(function() player.inventory:DeleteItemOfClass(t.class, t.n) end)
    if t.price and t.price > 0 then giveTo(player.inventory, L.MONEY, t.price, 1) end
    L.pc = playerCounts()
    return ok
end

function L.settle(maxAgeS)
    local now = K.now()
    local n = 0
    for tok, t in pairs(L.pending) do
        if maxAgeS <= 0 or now - t.at > maxAgeS then
            L.pending[tok] = nil
            local ok = takeBack(t)
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
    L.pc = nil
    if not L.enabled then L.settle(0) end
    K.out('LOOTMODE', L.enabled and 'on' or 'off', L.scope, L.host and 'host' or 'guest')
end

K.handlers['LOOTSETTLE'] = function(f) L.settle(0) end

-- (host) a guest's take: decide, apply to the host's copy, answer
K.handlers['LOOTASK'] = function(f)
    local scope, from, nonce, tok, id, class, n = f[2], f[3], f[4], f[5], f[6], f[7], tonumber(f[8])
    if not L.enabled or not L.host or scope ~= L.scope or scope == '' or not validId(id) or not validClass(class) or not n or n < 1 or n > 100000 then return end
    local e = find(id, class)
    local verdict, hp = 'none', 1
    if e then
        local kind = kindOf(e)
        local counts, hps = read(e, kind)
        hp = hps and hps[class] or 1
        if counts and (counts[class] or 0) >= n then
            local k = removeFrom(e, class, n)
            if k == n then verdict = 'ok' else verdict = 'gone' end
            if kind == 'ground' then L.snap[snapKey(e, kind)] = nil else rebaseline(e) end   -- the host's own watcher must not report this removal as a host take
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
        local ok = takeBack(t)
        K.out('LOOT', 'unconfirmed', t.id, t.class, t.n, ok and 1 or 0)    -- somebody else was first (or it was never there): taken back
    end
end

-- (guest) the host's copy lost an item: so does mine
K.handlers['LOOTTOOK'] = function(f)
    local scope, id, class, n = f[2], f[3], f[4], tonumber(f[5])
    if not L.enabled or L.host or scope ~= L.scope or not validId(id) or not validClass(class) or not n or n < 1 then return end
    local e = find(id, class)
    if not e then K.out('LOOTAPPLIED', id, class, 0, 'no-such-holder'); return end
    local kind = kindOf(e)
    local k = removeFrom(e, class, n)
    if kind == 'ground' then L.snap[snapKey(e, kind)] = nil else rebaseline(e) end
    L.applied = L.applied + 1
    K.out('LOOTAPPLIED', id, class, k, k == n and 'applied' or 'partial')
end

-- a friend's put: my copy of that holder gains it
K.handlers['LOOTPUT'] = function(f)
    local scope, id, class, n, hp = f[2], f[3], f[4], tonumber(f[5]), tonumber(f[6]) or 1
    if not L.enabled or scope ~= L.scope or not validId(id) or not validClass(class) or not n or n < 1 or n > 100000 then return end
    local e = find(id, nil)
    if not e or kindOf(e) == 'ground' then K.out('LOOTAPPLIED', id, class, 0, 'no-such-holder'); return end
    local ok = giveTo(e.inventory, class, n, math.max(0, math.min(1, hp)))
    rebaseline(e)
    L.applied = L.applied + 1
    K.out('LOOTAPPLIED', id, class, ok and n or 0, ok and 'put' or 'put-failed')
end

-- a friend's drop: it lies on my ground too
K.handlers['LOOTDROP'] = function(f)
    local scope, id, class, n, hp = f[2], f[3], f[4], tonumber(f[5]), tonumber(f[6]) or 1
    if not L.enabled or scope ~= L.scope or string.sub(id or '', 1, 2) ~= 'g@' or not validId(id) or not validClass(class) or not n or n < 1 or n > 100000 then return end
    local pos = keyPos(id)
    if not pos then return end
    local ok, ent = pcall(System.SpawnEntity, { class = 'PickableItem', name = 'drop_' .. string.sub(class, 1, 8) .. '_' .. L.dropped .. '_' .. math.random(1000, 9999),
        position = { x = pos.x, y = pos.y, z = pos.z + 0.2 }, properties = { guidItemClassId = class, fHealth = math.max(0, math.min(1, hp)), nAmount = n, bOnlyNPC = 0 } })
    if ok and ent then
        L.spawned[tostring(ent.id)] = true
        L.gid[tostring(ent.id)] = id                                                   -- the same name as in the dropper's world
    end
    L.applied = L.applied + 1
    K.out('LOOTAPPLIED', id, class, ok and ent and n or 0, ok and ent and 'dropped' or 'drop-failed')
end

K.every(0.5, 'loot-shared', function() K.try('loot', L.scan) end)
K.every(1.0, 'loot-settle', function() K.try('loot-settle', function() L.settle(L.settleS) end) end)

function L.onWorldReset() L.snap = {}; L.pending = {}; L.gid = {}; L.spawned = {}; L.pc = nil end
