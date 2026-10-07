-- Read-only private-profile quest/pause discovery. Invoke only through engine_session.py.
assert(player and player.soul, 'a disposable loaded world is required')
local function log(s) System.LogAlways('KCDUS-PROBE|' .. s) end
for _, name in ipairs({'ActivateQuest', 'CompleteObjective', 'IsQuestStarted', 'IsQuestCompleted', 'IsObjectiveStarted', 'IsObjectiveCompleted', 'GetActiveObjectives'}) do
    log('quest-binding|' .. name .. '|' .. type(QuestSystem[name]))
end
for _, quest in ipairs({'q_revenge', 'q_awakeningInRattay', 'q_tutorials'}) do
    local ok, active = pcall(QuestSystem.GetActiveObjectives, quest)
    log('quest-active|' .. quest .. '|' .. tostring(ok) .. '|' .. type(active))
    if ok and type(active) == 'table' then
        for key, value in pairs(active) do log('objective|' .. quest .. '|' .. tostring(key) .. '|' .. type(value) .. '|' .. tostring(value)) end
    end
end
for _, name in ipairs({'t_scale', 'sys_user_folder', 'sys_useSteamCloudForPlatformSaving'}) do
    local ok, value = pcall(System.GetCVar, name)
    log('cvar|' .. name .. '|' .. tostring(ok) .. '|' .. tostring(value))
end
