-- Disposable profile only, verified by engine_session.py; one real quest objective mutation with readback.
assert(KCDUS.inWorldRaw() and player and player.soul, 'a disposable loaded world is required')
assert(tonumber(System.GetCVar('sys_useSteamCloudForPlatformSaving')) == 0, 'cloud saving must be disabled')
local K = KCDUS
KCDUS_QUEST_MIRROR = {{code='q_revenge',tier='open',dlc='',kind='main',objectives={[11]='findVonAulitz'}}}
K.QuestMirror.catalogue.q_revenge = KCDUS_QUEST_MIRROR[1]
System.LogAlways('KCDUS-PROBE|quest-before|' .. tostring(QuestSystem.IsObjectiveStarted('q_revenge','findVonAulitz')) .. '|' .. tostring(QuestSystem.IsObjectiveCompleted('q_revenge','findVonAulitz')))
K.handlers.QMODE({'QMODE','candidate','0123456789abcdef0123456789abcdef'})
K.handlers.QMAPPLY({'QMAPPLY','0123456789abcdef0123456789abcdef','q_revenge','1','0','11'})
System.LogAlways('KCDUS-PROBE|quest-after|' .. tostring(QuestSystem.IsObjectiveCompleted('q_revenge','findVonAulitz')))
K.handlers.QMAPPLY({'QMAPPLY','0123456789abcdef0123456789abcdef','q_revenge','1','0','11'})
K.handlers.QMODE({'QMODE','off',''})
