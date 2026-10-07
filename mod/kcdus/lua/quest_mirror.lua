-- SPDX-License-Identifier: GPL-3.0-only
-- Candidate one-way quest adapter. Explicitly opt in; rails, local events and DLC are vetoed.
-- This cannot suppress all native quest rewards/spawns: do not claim shared simulation.
local K = KCDUS
local M = { enabled=false, cursor=1, cache={}, scope='', catalogue={} }
K.QuestMirror = M
for _, q in ipairs(KCDUS_QUEST_MIRROR or {}) do M.catalogue[q.code] = q end
local function allowed(q)
    return q and q.tier == 'open' and q.dlc == '' and (q.kind == 'main' or q.kind == 'side' or q.kind == 'activity')
end
local function read(q)
    local done = {}
    for id, name in pairs(q.objectives) do
        if QuestSystem.IsObjectiveCompleted(q.code, name) then done[#done+1] = id end
    end
    table.sort(done)
    return QuestSystem.IsQuestStarted(q.code) and 1 or 0, QuestSystem.IsQuestCompleted(q.code) and 1 or 0, table.concat(done, ',')
end
function M.step()
    if not M.enabled or not K.inWorldRaw() then return end
    local list = KCDUS_QUEST_MIRROR or {}
    if #list == 0 then return end
    local q = list[M.cursor]
    M.cursor = M.cursor % #list + 1
    if not allowed(q) then return end
    local s, c, done = read(q)
    local signature = s .. '|' .. c .. '|' .. done
    if M.cache[q.code] ~= signature then
        M.cache[q.code] = signature
        K.out('QM', q.code, s, c, done)
    end
end
K.handlers.QMODE = function(f)
    M.enabled = f[2] == 'candidate'
    M.scope = f[3] or ''
    M.cache = {}
    K.out('QMODE', M.enabled and 'candidate' or 'off')
end
K.handlers.QMRESET = function(f)
    M.scope = f[2] or ''
    M.cache = {}
end
K.handlers.QMAPPLY = function(f)
    local scope, code, started, completed, text = f[2], f[3], f[4], f[5], f[6] or ''
    local q = M.catalogue[code]
    if not M.enabled or not K.inWorldRaw() or scope ~= M.scope or scope == '' or not allowed(q) then
        K.out('QMAPPLIED', code or '?', 'refused'); return
    end
    if started ~= '0' and started ~= '1' or completed ~= '0' and completed ~= '1' then return end
    local ids = {}
    for value in string.gmatch(text, '[^,]+') do
        local id = tonumber(value)
        if not id or id ~= math.floor(id) or not q.objectives[id] then K.out('QMAPPLIED', code, 'invalid-objective'); return end
        ids[#ids+1] = id
    end
    -- Native state may not be rolled backwards or canceled from absence in a snapshot.
    if QuestSystem.IsQuestCompleted(code) then K.out('QMAPPLIED', code, 'already'); return end
    if started == '1' and not QuestSystem.IsQuestStarted(code) then
        QuestSystem.ActivateQuest(code)
        if not QuestSystem.IsQuestStarted(code) then K.out('QMAPPLIED', code, 'start-refused'); return end
    end
    for _, id in ipairs(ids) do
        local name = q.objectives[id]
        if not QuestSystem.IsObjectiveCompleted(code, name) then
            if not QuestSystem.IsObjectiveStarted(code, name) then K.out('QMAPPLIED', code, 'objective-inactive'); return end
            QuestSystem.CompleteObjective(code, name, false) -- suppress the optional completion message; native reward effects still need proof
            if not QuestSystem.IsObjectiveCompleted(code, name) then K.out('QMAPPLIED', code, 'objective-refused'); return end
        end
    end
    if completed == '1' and not QuestSystem.IsQuestCompleted(code) then
        K.out('QMAPPLIED', code, 'quest-completion-unverified'); return -- never substitute CancelQuest/DeactivateQuest for successful completion
    end
    K.out('QMAPPLIED', code, 'readback-matched')
end
K.every(0.1, 'quest-mirror-candidate', function() K.try('quest-mirror', M.step) end)
