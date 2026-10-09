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
local R = { windows = {}, applied = {}, given = {}, rewardS = 3, appliedTtl = 60, maxItems = 12 }
K.Rewards = R

local CLASS = '^[%x%-]+$'
local function validClass(c) return type(c) == 'string' and #c >= 8 and #c <= 40 and string.find(c, CLASS) ~= nil end

local function counts()
    local c, hp = {}, {}
    local ok, t = pcall(function() return player.inventory:GetInventoryTable() end)
    if not ok or type(t) ~= 'table' then return c, hp end
    for _, v in pairs(t) do
        local okI, it = pcall(ItemManager.GetItem, v)
        if not okI or type(it) ~= 'table' then it = type(v) == 'table' and v or nil end
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
    local key = shortKey(code .. '|' .. signature)
    R.windows[key] = { code = code, at = K.now(), base = base }
end

-- (guest) quest_mirror.lua applied the host's change of this quest here
function R.appliedHere(code)
    R.applied[code] = { at = K.now(), base = (counts()) }
end

function R.step()
    local now = K.now()
    for key, w in pairs(R.windows) do
        if now - w.at >= R.rewardS then
            R.windows[key] = nil
            local g, hp = gains(w.base, w.at)
            local parts = {}
            for class, n in pairs(g) do
                if validClass(class) and #parts < R.maxItems then parts[#parts + 1] = class .. ':' .. n .. ':' .. string.format('%.4f', hp[class] or 1) end
            end
            if #parts > 0 then K.out('QREWARD', w.code, key, table.concat(parts, ';')) end
        end
    end
    for code, a in pairs(R.applied) do if now - a.at > R.appliedTtl then R.applied[code] = nil end end
end

K.handlers['QRGIVE'] = function(f)
    local code, key, text = f[2], f[3], f[4] or ''
    if type(code) ~= 'string' or not string.find(code, '^[%w_]+$') or type(key) ~= 'string' or not string.find(key, '^%x+$') or R.given[key] then return end
    if not K.inWorldRaw() or (K.Loot and K.Loot.host) then return end
    R.given[key] = true
    local a = R.applied[code]
    local native = a and gains(a.base, a.at) or {}
    local givenN, nativeN = 0, 0
    for entry in string.gmatch(text, '[^;]+') do
        local class, n, hp = string.match(entry, '^([%x%-]+):(%d+):([%d%.]+)$')
        n = tonumber(n)
        if class and validClass(class) and n and n >= 1 and n <= 100000 then
            local already = math.min(n, native[class] or 0)
            nativeN = nativeN + already
            local give = n - already
            if give > 0 then
                local ok = pcall(function() player.inventory:AddItem(ItemManager.CreateItem(class, math.max(0, math.min(1, tonumber(hp) or 1)), give)) end)
                if ok then givenN = givenN + give end
            end
        end
    end
    if K.Loot then K.Loot.pc = nil end                                         -- the reward is not mistaken for a take
    K.out('QRGIVEN', code, key, givenN, nativeN)
end

K.every(0.5, 'quest-rewards', function() K.try('quest-rewards', R.step) end)

function R.onWorldReset() R.windows = {}; R.applied = {}; R.given = {} end
