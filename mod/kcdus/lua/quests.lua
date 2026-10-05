-- quests: watch the quests the plan can gate (docs/quest-gating-plan.csv) and tell the agent when one changes.
-- Q|code|started|completed|objectiveId,objectiveId   (a quest back at "nothing" is sent once, as Q|code|0|0|)
local K = KCDUS
K.Quests = { cache = {}, cursor = 1, perStep = 2, changes = 0 }
local Q = K.Quests

local function bool(v)
    if v then return 1 end
    return 0
end

local function objectiveIds(code)
    local t = QuestSystem.GetActiveObjectives(code)
    local ids = {}
    if type(t) == "table" then
        for _, id in pairs(t) do
            ids[#ids + 1] = tonumber(id) or 0
        end
    end
    table.sort(ids)
    return table.concat(ids, ",")
end

function Q.read(code)
    local started = bool(QuestSystem.IsQuestStarted(code))
    local completed = bool(QuestSystem.IsQuestCompleted(code))
    local objs = ""
    if started == 1 or completed == 0 then
        objs = objectiveIds(code)
    end
    return started, completed, objs
end

function Q.poll(code, force)
    local s, c, o = Q.read(code)
    local sig = s .. ":" .. c .. ":" .. o
    local prev = Q.cache[code]
    if prev == sig then
        return
    end
    Q.cache[code] = sig
    if prev == nil and not force and s == 0 and c == 0 and o == "" then
        return   -- never seen, nothing going on: not news
    end
    Q.changes = Q.changes + 1
    K.out("Q", code, s, c, o)
end

function Q.step(now)
    local list = KCDUS_QUESTS
    if list == nil then
        return
    end
    local n = #list
    if n == 0 then
        return
    end
    for _ = 1, Q.perStep do
        local q = list[Q.cursor]
        Q.cursor = Q.cursor + 1
        if Q.cursor > n then
            Q.cursor = 1
        end
        K.try("quest:" .. q.code, Q.poll, q.code, false)
    end
end

-- QSNAP: forget what was sent, so the next pass sends every quest that has any state (the agent just connected)
K.handlers["QSNAP"] = function(f)
    Q.cache = {}
    Q.snapshot = true
end

-- 2 quests every 0.1 s: the whole watched list is visited about every 5 s. A change is noticed within that; the story logic works in
-- tens of seconds, and polling faster cost measurable frame rate (the soak).
K.every(0.1, "quests", Q.step)

-- Q? code: read one quest right now (used by tests and the agent's resync)
K.handlers["QGET"] = function(f)
    if f[2] then
        Q.poll(f[2], true)
    end
end
