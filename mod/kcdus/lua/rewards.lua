-- rewards: shared quest consequences (docs/CAPABILITIES.md, "Quest rewards"). KCD1's quest rewards are DATA (Libs/Tables/quest/quest_reward_exp/_item/_money/
-- _perk), paid by the engine itself for a (quest, objective): "immediate" ones when the objective completes, the others when the quest completes. Proved in
-- the private engine: QuestSystem.CompleteObjective (what the guest's quest mirror calls) makes the engine pay that objective's immediate rewards natively,
-- money and XP, the XP with the quest-objective cause (C_QuestObjectiveChangedCause). So:
--   * OBJECTIVE rewards follow causally and natively in every guest's copy through the quest mirror: nothing here pays them again.
--   * QUEST-COMPLETION rewards: a guest's mirror cannot complete a quest (no Lua call does), so the host shares what its completion paid:
--       - XP exactly: the engine adapter observes every native XP application with its cause (XpRead.h); the host forwards the ones caused by a quest state
--         change (C_QuestStateChangedCause), with the native amount and stat/skill (KCDUS|QXP), and each guest applies them once (QXPGIVE);
--       - items and money: what came into the host's pack in the RewardS seconds after its quest completed, minus loot (KCDUS|QREWARD ... done).
-- Each key is paid once per loaded world. NOT shared: XP without the adapter (Linux, or no launcher), a reward missed while a guest was not in the world,
-- rewards of quests the mirror does not cover (DLC, story rails, unknown objectives).
--
-- agent -> game:  QRGIVE|<code>|<key>|class:n:hp;...|done   (guest) the host's quest completion paid this
--                 QXPGIVE|<key>|<kind 0 stat 1 skill>|<index>|<raw>   (guest) the host's quest-caused native XP (raw: 128 per XP point)
-- game -> agent:  KCDUS|QREWARD|<code>|<key>|class:n:hp;...|done   (host)
--                 KCDUS|QXP|<key>|<kind>|<index>|<raw>                (host)
--                 KCDUS|QRGIVEN|<code>|<key>|<given>|<native>  /  KCDUS|QXPGIVEN|<key>|<ok 0/1>   (guest) readback
local K = KCDUS
System.LogAlways("KCDUS|LOAD|rewards")
local R = { windows = {}, applied = {}, given = {}, pending = {}, rewardS = 3, appliedTtl = 60, maxItems = 12, xpSeq = nil, xpGiven = {}, nonce = '' }
do local hex, s = '0123456789abcdef', ''; for _ = 1, 6 do local i = math.random(1, 16); s = s .. string.sub(hex, i, i) end; R.nonce = s end
R.STATS = { [0] = 'str', [1] = 'agi', [2] = 'vit', [3] = 'spc', [9] = 'prg' }       -- native stat indices (read live: AddStatXP -> XpRead)
R.SKILLS = { [0] = 'stealth', 'horse_riding', 'fencing', 'bard', 'lockpicking', 'pickpocketing', 'alchemy', 'cooking', 'repairing', 'smithing', 'fishing',
    'mining', 'first_aid', 'drinking', 'hunter', 'defense', 'weapon_sword', 'weapon_axe', 'weapon_bow', 'weapon_crossbow', 'weapon_shield', 'weapon_mace',
    'weapon_dagger', 'weapon_large', 'weapon_unarmed', 'herbalism', 'reading', 'tailoring', 'armourer', 'weaponsmithing', 'shoemaking', 'gambling', 'houndmaster' }
R.QUEST_STATE_CAUSE = '2306d30'                                                   -- C_QuestStateChangedCause vtable RVA (the exact engine the adapter accepts)
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
    local was, now = string.match(prev, '^%d|(%d)|'), string.match(signature, '^%d|(%d)|')
    if not (was == '0' and now == '1') then return end                            -- objectives pay natively in the guests' mirrors
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
            if #parts > 0 then K.out('QREWARD', w.code, key, table.concat(parts, ';'), 'done') end
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
    if f[5] == 'done' then
        R.applied[code] = R.applied[code] or { at = K.now(), base = counts() or {}, ready = true }
        R.applied[code].ready = true                                             -- no native completion here: nothing was paid natively
        R.applied[code].base = counts() or {}
        R.applied[code].at = K.now()
    end
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

-- (host) the adapter's native XP observations since the last look: forward the quest-completion ones
function R.xpDrain()
    local identity=K.inWorldRaw() and R.myIdentity() or nil
    if identity then K.out('QXPREADY',identity) end
    if not K.Loot or not K.Loot.enabled or not K.inWorldRaw() then
        pcall(function() player.human:PlayAnim('@kcdus/xp-context/','off') end)
        return
    end
    local scope=K.Loot.scope or ''
    pcall(function() player.human:PlayAnim('@kcdus/xp-context/'..scope,K.Loot.host and 'host' or 'guest') end)
    if not K.Loot.host then return end
    local queuedOk,queued=pcall(function() return player.human:PlayAnim('@kcdus/xp-queued/'..tostring(R.nativeCursor or 0),'') end)
    if queuedOk and type(queued)=='table' and queued.available==1 then
        if queued.healthy~=1 then K.out('QXPGAP','native-journal',queued.cursor or 0);return end
        if R.nativeBoot~=queued.boot then
            R.nativeBoot=queued.boot;R.nativeCursor=0
            if (tonumber(queued.cursor) or 0)>0 then return end
        end
        for i=1,tonumber(queued.n) or 0 do
            local row=queued['e'..i] or ''
            local event,kind,index,raw,actual,applied,verified,cause,soul,data,world,flags=
                string.match(row,'^(%d+),(%d),(%d+),(%d+),(%d+),(%d),(%d),(%x+),(%x+),(%x+),([%w_%.%-]*),(%d)$')
            if event and world==scope and flags=='3' and identity==soul and applied=='1' and verified=='1' and cause==R.QUEST_STATE_CAUSE then
                K.out('QXPNATIVE','n'..queued.boot..'.'..event,kind,index,raw,cause,data,world)
            end
        end
        R.nativeCursor=tonumber(queued.cursor) or R.nativeCursor or 0
        return
    end
    local ok, r = pcall(function() return player.human:PlayAnim('@kcdus/xp-since/' .. tostring(R.xpSeq or 0), '') end)
    if not ok or type(r) ~= 'table' or type(r.last) ~= 'number' then return end      -- no adapter: XP is not shared
    if R.xpSeq == nil then R.xpSeq = r.last; return end                            -- start from now: earlier XP is not this session's
    local me = R.myIdentity()
    if (tonumber(r.lost) or 0) > 0 then K.out('QXPGAP', R.xpSeq, r.lost) end
    for i = 1, tonumber(r.n) or 0 do
        local row = r['e' .. i]
        local seq, kind, idx, req, _, applied, verified, cause, soul = string.match(row or '', '^(%d+),(%d),(%d+),(%d+),(%d+),(%d),(%d),(%x+),(%x*),')
        seq = tonumber(seq)
        if seq and seq > R.xpSeq then
            R.xpSeq = seq
            if cause == R.QUEST_STATE_CAUSE and applied == '1' and verified == '1' and me ~= nil and soul == me then
                K.out('QXP', 'x' .. R.nonce .. seq, kind, idx, req)
            end
        end
    end
    -- The adapter returns at most 16 of its 64 retained observations. 'last' is
    -- the ring head, not the last row returned: advancing to it silently loses pages.
end

K.handlers.QXPNATIVEGIVE=function(f)
    local key,kind,index,raw,cause,data,scope,soul=f[2],f[3],tonumber(f[4]),tonumber(f[5]),f[6],f[7],f[8],f[9]
    if not K.inWorldRaw() or not K.Loot or not K.Loot.enabled or K.Loot.host or scope~=K.Loot.scope or soul~=R.myIdentity() then return end
    if type(key)~='string' or #key<35 or #key>41 or not string.match(key,'^n%x+%.%d+$') or
        (kind~='0' and kind~='1') or not index or index~=math.floor(index) or index<0 or index>(kind=='0' and 9 or 32) or
        not raw or raw~=math.floor(raw) or raw<1 or raw>128*100000 or type(data)~='string' or string.find(data,'[^0-9a-f]') or
        not(cause=='2306d30' and #data==32 or cause=='21c8fd0' and #data==48 and string.sub(data,41)=='00000000') then return end
    if R.xpGiven[key] then return end
    R.xpGiven[key]='intent' -- the agent also persists this before sending the command
    local uid=string.match(tostring(player.soul:GetId()),'([%x]+)$')
    if not uid or #uid~=16 then return end
    local ok,result=pcall(function()
        player.human:PlayAnim('@kcdus/xp-context/'..scope,'guest')
        return player.human:PlayAnim('@kcdus/quest-xp/'..uid,kind..','..index..','..raw..','..cause..','..data..','..key..','..scope)
    end)
    if not ok or type(result)~='table' then result={reason=9,applied=0,verified=0,actual=0,before='',after=''} end
    K.out('QXPNATIVEGIVEN',key,result.reason or 9,result.applied or 0,result.verified or 0,result.actual or 0,result.before or '',result.after or '',R.myIdentity() or '')
end

function R.myIdentity()
    local ok, it = pcall(function() return ItemManager.GetItem(player.soul:GetId()) end)
    if ok and type(it) == 'table' and type(it.kcdusPersistent) == 'string' then return it.kcdusPersistent end
    return nil
end

K.handlers['QXPGIVE'] = function(f)
    local key, kind, idx, raw = f[2], f[3], tonumber(f[4]), tonumber(f[5])
    if type(key) ~= 'string' or not string.find(key, '^x%x%x%x%x%x%x%d+$') or #key > 24 or R.xpGiven[key] then return end
    if not K.inWorldRaw() or (K.Loot and K.Loot.host) or not idx or not raw or raw < 1 or raw > 128 * 100000 then return end
    local name = kind == '0' and R.STATS[idx] or kind == '1' and R.SKILLS[idx] or nil
    if not name then return end
    R.xpGiven[key] = true                                                          -- never twice, even if the call below fails
    local before = nil
    pcall(function() before = player.human:PlayAnim('@kcdus/xp-last', '') end)
    local ok = pcall(function()
        if kind == '0' then player.soul:AddStatXP(name, raw / 128) else player.soul:AddSkillXP(name, raw / 128) end
    end)
    local after = nil
    pcall(function() after = player.human:PlayAnim('@kcdus/xp-last', '') end)
    local verified = ok and type(before) == 'table' and type(after) == 'table' and after.sequence > before.sequence and after.stat == idx
    K.out('QXPGIVEN', key, name, ok and 1 or 0, verified and 1 or 0)
end

K.every(0.5, 'quest-rewards', function() K.try('quest-rewards', R.step) end)
K.every(0.25, 'quest-xp', function() K.try('quest-xp', R.xpDrain) end)

function R.onWorldReset() R.windows = {}; R.applied = {}; R.given = {}; R.pending = {}; R.xpSeq = nil; R.xpGiven = {}; R.nativeCursor=0 end
