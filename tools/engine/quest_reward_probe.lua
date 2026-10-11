-- Private engine only. Which rewarded objectives (KCDUS_QR, from quest_reward_catalog.py) are open in this save, and does completing one by script pay it?
-- KCDUS_QRP_STEP: 'list' | 'snap' | 'complete' (KCDUS_QRP_Q, KCDUS_QRP_O) | 'diff'
local step = KCDUS_QRP_STEP or 'list'
if step == 'list' then
    local n = 0
    for _, e in ipairs(KCDUS_QR or {}) do
        local okS, s = pcall(QuestSystem.IsQuestStarted, e.q)
        local okC, c = pcall(QuestSystem.IsQuestCompleted, e.q)
        if okS and s and okC and not c then
            local okO, os_ = pcall(QuestSystem.IsObjectiveStarted, e.q, e.o)
            local okD, od = pcall(QuestSystem.IsObjectiveCompleted, e.q, e.o)
            if okO and okD and not od then
                n = n + 1
                local kinds = ''
                for _, r in ipairs(e.r) do kinds = kinds .. r[1] .. ':' .. r[2] .. ':' .. r[3] .. ':' .. r[4] .. ';' end
                if n <= 40 then System.LogAlways('KCDUS|QRP|open|' .. e.q .. '|' .. e.o .. '|started=' .. tostring(os_) .. '|' .. kinds) end
            end
        end
    end
    System.LogAlways('KCDUS|QRP|count|' .. n)
    return
end
local function snapshot()
    local s = { money = player.inventory:GetMoney(), items = {}, stats = {}, skills = {}, perks = 0 }
    for _, h in pairs(player.inventory:GetInventoryTable()) do
        local it = ItemManager.GetItem(h)
        if it and it.class then s.items[it.class] = (s.items[it.class] or 0) + (it.amount or 1) end
    end
    for _, n in ipairs({ 'str', 'agi', 'vit', 'spc' }) do
        s.stats[n] = player.soul:GetStatLevel(n) + (player.soul:GetStatProgress(n) or 0)
    end
    for _, n in ipairs({ 'reading', 'alchemy', 'herbalism', 'weapon_sword', 'fencing', 'defense', 'stealth', 'lockpicking', 'horse_riding', 'drinking' }) do
        local okL, l = pcall(function() return player.soul:GetSkillLevel(n) end)
        local okP, p = pcall(function() return player.soul:GetSkillProgress(n) end)
        s.skills[n] = (okL and l or 0) + (okP and p or 0)
    end
    local okX, xp = pcall(function() return player.human:PlayAnim('@kcdus/xp-last', '') end)
    s.xpSeq = okX and type(xp) == 'table' and xp.sequence or -1
    s.xpCause = okX and type(xp) == 'table' and string.format('%x', xp.causeVtableRva or 0) or '?'
    return s
end
if step == 'snap' then
    KCDUS_QRP_BEFORE = snapshot()
    System.LogAlways('KCDUS|QRP|snap|money=' .. tostring(KCDUS_QRP_BEFORE.money) .. '|xpSeq=' .. KCDUS_QRP_BEFORE.xpSeq)
elseif step == 'complete' then
    local q, o = KCDUS_QRP_Q, KCDUS_QRP_O
    local was = QuestSystem.IsObjectiveCompleted(q, o)
    if not QuestSystem.IsObjectiveStarted(q, o) then pcall(QuestSystem.StartObjective, q, o, false) end
    local ok, err = pcall(QuestSystem.CompleteObjective, q, o, false)
    System.LogAlways('KCDUS|QRP|complete|' .. q .. '|' .. o .. '|' .. tostring(ok) .. '|' .. tostring(err) .. '|was=' .. tostring(was) .. '|now=' .. tostring(QuestSystem.IsObjectiveCompleted(q, o)))
elseif step == 'diff' then
    local a, b = KCDUS_QRP_BEFORE, snapshot()
    local d = 'money=' .. tostring(b.money - a.money) .. '|xpSeq ' .. a.xpSeq .. '->' .. b.xpSeq .. ' cause=' .. b.xpCause .. '|'
    for k, v in pairs(b.items) do if v ~= (a.items[k] or 0) then d = d .. 'item ' .. k .. ' ' .. (v - (a.items[k] or 0)) .. ';' end end
    for k, v in pairs(b.stats) do if math.abs(v - a.stats[k]) > 1e-6 then d = d .. 'stat ' .. k .. ' +' .. string.format('%.4f', v - a.stats[k]) .. ';' end end
    for k, v in pairs(b.skills) do if math.abs(v - a.skills[k]) > 1e-6 then d = d .. 'skill ' .. k .. ' +' .. string.format('%.4f', v - a.skills[k]) .. ';' end end
    System.LogAlways('KCDUS|QRP|diff|' .. d)
end
