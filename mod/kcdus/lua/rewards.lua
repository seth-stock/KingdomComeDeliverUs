-- rewards: shared quest rewards (docs/CAPABILITIES.md, "Quest rewards"). The host's quest progress already follows into the guests' copies of the same shared
-- world (quest_mirror.lua). What a quest step GIVES (money, items) follows too:
--   * HOST: when a quest's state changes here (a new objective done, or the quest done), what came into Henry's pack in the next RewardS seconds is that step's
--     reward, minus whatever came from loot, the ground or a shop meanwhile (loot.lua keeps a log). It is told once: KCDUS|QREWARD|code|key|class:n:hp;...
--   * GUEST: the reward is given to this Henry, minus what this game already gave natively when the same step was applied here (quest_mirror.lua tells this file
--     when it applied it; the native quest scripts may or may not have paid). Each key is given once per loaded world: a step is never paid twice.
-- NOT shared: experience (the engine has no exact readback of a skill's XP total), a reward missed while a guest was not in the world, rewards of quests the
-- mirror does not cover (DLC, story rails, unknown objectives).
--
-- agent -> game:  QRGIVE|<code>|<key>|class:n:hp;...     (guest) the host's step paid this
-- game -> agent:  KCDUS|QREWARD|<code>|<key>|class:n:hp;...   (host) a quest step paid this
--                 KCDUS|QRGIVEN|<code>|<key>|<given>|<native>   (guest) readback: what was given here, and what the native scripts had paid already
local K = KCDUS
System.LogAlways("KCDUS|LOAD|rewards")
local R = { windows = {}, applied = {}, given = {}, pending = {}, rewardS = 3, appliedTtl = 60, maxItems = 12 }
K.Rewards = R

local CLASS = '^[%x%-]+$'
local function validClass(c) return type(c) == 'string' and #c >= 8 and #c <= 40 and string.find(c, CLASS) ~= nil end

local function counts()
    local c, hp = {}, {}
    local ok, t = pcall(function() return player.inventory:GetInventoryTable() end)
    if not ok or type(t) ~= 'table' then return nil end
    for _, v in pairs(t) do
        local okI, it = pcall(ItemManager.GetItem, v)
        if not okI or type(it) ~= 'table' then it = type(v) == 'table' and v or nil end
        if not it or not it.class then return nil end -- incomplete native inventory is not an empty pack
        if it and it.class then
            c[it.class] = (c[it.class] or 0) + (tonumber(it.amount) or 1)
            hp[it.class] = tonumber(it.health) or 1
        end
    end
    return c, hp
end

-- what came into the pack since <base>, without what loot.lua took from bodies, stashes, shops and the ground since <since>
local function gains(base, since)
    local now, hp = counts()
    if not now then return nil end
    local g = {}
    for class, n in pairs(now) do local d = n - (base[class] or 0); if d > 0 then g[class] = d end end
    for _, l in ipairs(K.Loot and K.Loot.gainLog or {}) do
        if l.at >= since and g[l.class] then g[l.class] = g[l.class] - l.n; if g[l.class] <= 0 then g[l.class] = nil end end
    end
    return g, hp
end

local function shortKey(s)
    local h = 5381
    for i = 1, #s do h = (h * 33 + string.byte(s, i)) % 4294967296 end
    return string.format('%08x', h)
end

-- (host) quest_mirror.lua: this quest's state just changed (prev nil: the first look at it, nothing happened)
function R.questChanged(code, signature, prev)
    if prev == nil or not K.Loot or not K.Loot.host then return end
    local base = counts()
    if not base then return end
    local key = shortKey(code .. '|' .. signature)
    R.windows[key] = { code = code, at = K.now(), base = base }
end

-- (guest) quest_mirror.lua applied the host's change of this quest here
function R.beginApply(code)
    local base = counts()
    if not base then R.applied[code] = nil; return false end
    R.applied[code] = { at = K.now(), base = base, ready = false }
    return true
end
function R.appliedHere(code)
    -- Standalone probes may arm here, but production arms BEFORE native mutation.
    if not R.applied[code] and not R.beginApply(code) then return end
    R.applied[code].ready = true
end

function R.step()
    local now = K.now()
    for key, w in pairs(R.windows) do
        if now - w.at >= R.rewardS then
            R.windows[key] = nil
            local g, hp = gains(w.base, w.at)
            local parts = {}
            for class, n in pairs(g or {}) do
                if validClass(class) and #parts < R.maxItems then parts[#parts + 1] = class .. ':' .. n .. ':' .. string.format('%.4f', hp[class] or 1) end
            end
            if #parts > 0 then K.out('QREWARD', w.code, key, table.concat(parts, ';')) end
        end
    end
    for code, a in pairs(R.applied) do if now - a.at > R.appliedTtl then R.applied[code] = nil end end
    for id, p in pairs(R.pending) do
        if now - p.at > R.appliedTtl then R.pending[id] = nil
        elseif R.applied[p.f[2]] and R.applied[p.f[2]].ready then
            R.pending[id] = nil; K.handlers.QRGIVE(p.f)
        end
    end
end

K.handlers['QRGIVE'] = function(f)
    local code, key, text = f[2], f[3], f[4] or ''
    if type(code) ~= 'string' or #code>100 or not string.find(code, '^[%w_]+$') or type(key) ~= 'string' or #key~=8 or not string.find(key, '^[0-9a-f]+$') then return end
    if not K.inWorldRaw() or (K.Loot and K.Loot.host) then return end
    local id = code..'|'..key
    if R.given[id] or #text==0 or #text>1600 then return end
    local rows, seen = {}, {}
    for entry in string.gmatch(text, '[^;]+') do
        local class, n, hp = string.match(entry, '^([%x%-]+):(%d+):([%d%.]+)$')
        n, hp = tonumber(n), tonumber(hp)
        if not class or not validClass(class) or seen[class] or not n or n<1 or n>100000 or not hp or hp<0 or hp>1 or #rows>=R.maxItems then return end
        seen[class]=true; rows[#rows+1]={class,n,hp}
    end
    if #rows==0 then return end
    local a = R.applied[code]
    if not a or not a.ready then
        local size=0; for _ in pairs(R.pending) do size=size+1 end
        if size<32 and not R.pending[id] then R.pending[id]={f=f,at=K.now()} end
        return -- a reordered reward never grants before verified quest application
    end
    local native = gains(a.base, a.at)
    if not native then return end
    R.given[id] = 'intent' -- do not retry an unknown partial native outcome
    local givenN, nativeN = 0, 0
    for _, row in ipairs(rows) do
        local class, n, hp = row[1], row[2], row[3]
            local already = math.min(n, native[class] or 0)
            nativeN = nativeN + already
            local give = n - already
            if give > 0 then
                local before = counts()
                local ok = before and pcall(function()
                    local item=ItemManager.CreateItem(class,hp,give)
                    if not item then error('Native reward creation returned no item') end
                    player.inventory:AddItem(item)
                end)
                local after = counts()
                if not ok or not after or (after[class] or 0)-(before[class] or 0)~=give then
                    R.given[id]='uncertain'; K.out('QRUNVERIFIED',code,key,class); return
                end
                givenN = givenN + give
            end
    end
    R.given[id]='verified'
    if K.Loot then K.Loot.pc = nil end                                         -- the reward is not mistaken for a take
    K.out('QRGIVEN', code, key, givenN, nativeN)
end

K.every(0.5, 'quest-rewards', function() K.try('quest-rewards', R.step) end)

function R.onWorldReset() R.windows = {}; R.applied = {}; R.given = {}; R.pending = {} end
