> Historical incoming handoff. Session 4 work/build/install results supersede it: read SESSION4-RESULTS.md first for current state. KCD2 parity paths are Candidate; human acceptance is pending.

# Session 4 handoff (2026-10-08): KCD1 own-menu pause, shared enemies, items, shops, rewards; KCD2 parity still to do

Written for the next agent (GPT Codex). Nothing from this session is committed or pushed yet. VERSION stays KCD1 0.1.0 / KCD2 0.45.0 (the user's call).

## Repos and rules (unchanged)

* KCD1 worktree `C:\Users\seths\Projects\kcd1-coop\codex-reliability`, branch `codex/coop-reliability`, last pushed tip 88ea0e1, fork https://github.com/seth-stock/KingdomComeDeliverUs.git
* KCD2 worktree `C:\Users\seths\Projects\kcd2-coop\codex-reliability`, same branch, tip ea82e3d, fork https://github.com/seth-stock/KingdomCome-Together.git. Its `origin` is DeepFriedDepp: NEVER push there; push by URL.
* Push: `git -c credential.helper= -c "credential.helper=!\"C:/Program Files/GitHub CLI/gh.exe\" auth git-credential" push <url> HEAD:refs/heads/codex/coop-reliability`, then `git ls-remote`. No force push.
* Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (Codex: use your own attribution as the user wishes).
* Never touch real saves (`C:\Users\seths\Saved Games\kingdomcome\saves`), firewall rules, or kill processes by name (stop only the owned PID: `python tools/engine/stop_owned_session.py`). Never `rm -rf` a folder holding junctions. Don't claim shared simulation or human acceptance.
* **A private KCD1 game may still be running** (engine harness PID 38540, profile KCDUS-engine-b8ae00265df445ee9344d10d7803b88b). Stop it first: `python tools/engine/stop_owned_session.py` (from the KCD1 worktree).
* The real game folder `F:\SteamLibrary\steamapps\common\KingdomComeDeliverance\Mods\kcdus\Data\kcdus.pak` currently holds the **session-4 test build** (sha256 f8e06092...11b1). The installed release pak is backed up in `C:\Users\seths\Projects\kcd1-coop\_work\mod-backup-20261008b\`. Reinstall with the final installer at the end (or copy the backup back).

## What was done (KCD1, uncommitted)

All 375 automated tests pass (`dotnet test dotnet/KcdUs.sln -c Release`; `WorldSyncTests.WrongPlaylineReadback...` is timing-flaky under full load, passes alone).

1. **Own pause menu no longer pauses (option "off")**, proved in a RETAIL-mode game (no -devmode):
   * `native/KcdUs.EngineBridge/EngineBridge.cpp`: inline hook on `CCryAction::PauseGame` at WHGame.dll RVA 0x5ba0e8 (vtable 0x2734e30 slot 0x70; signature `(this, bool pause, uint16 source, bool force, uint32 fadeMs)`, counters at this+8+4*source; 15-byte prologue checked). ESC menu = source 7 (same as KCD2). Gate controlled through named shared memory `Local\KcdUsBridgePause.v1` (struct PauseShared); declines PAUSE of masked sources only while agent heartbeat < 10 s old. Built with MSBuild Release x64 -> `build/engine/KcdUsEngineBridge.dll`.
   * `dotnet/KcdUs.Agent/PauseGate.cs` (BridgePauseGate), `PauseCoordinator.cs` (levers = option off + in world + connected + a friend in world; heartbeat 1 s; sends `OWNPAUSE|0/1`), `Session.cs`/`AgentHost.cs` wiring. `mod/kcdus/lua/pause.lua` OWNPAUSE handler sets `wh_ui_InventoryPauseEnabled` 0 / restores.
   * Facts: `wh_game_unpause` works only in -devmode; the remote console in retail accepts console commands (`kcdus ...`) but not `#` Lua.
   * Tools: `tools/engine/pause_gate.py` (drive/read the gate), `engine_session.py` gained `KCDUS_PROBE_RETAIL=1`.
2. **Shared enemies** (where they walk, whom they fight): `mod/kcdus/lua/npcs.lua` + `dotnet/KcdUs.Agent/NpcCoordinator.cs`. Host gives each NPC near players to the nearest player (4 m margin, 2 s hold); owner's game runs it and reports NPCST (5 Hz); others puppet it (`DisableBehaviorTreeEvaluation`, SetWorldPos/angles, locomotion clips, DrawWeapon/HolsterWeapon with retry). Puppets released when mine/stale 4 s/ownership lapses 3 s/mode off/save/load. Active only while shared outcomes are active.
3. **Items** in `mod/kcdus/lua/loot.lua` (rewritten, same agent journal path): ground items (PickableItem, ids `g@x_y_z`, snapshot keyed per entity), drops (`LOOT|drop` -> peers spawn via SpawnEntity), saddlebags (Horse inventories), shop stock (Stash linked via `Shops.IsLinkedWithShop`, 40 m reach, refund money class `5ef63059-322e-4e1b-abe8-926e100c770e` on gone), puts (`LOOT|put` -> `LOOTPUT`). Takes require the item to appear in Henry's pack (restocks ignored). Agent: `OutcomesCoordinator.PeerPutDrop`, Session routes `loot|put|drop`.
4. **Quest rewards**: `mod/kcdus/lua/rewards.lua`; host measures pack gains for 3 s after a mirrored quest change (minus loot gainLog), `QREWARD` -> HostEvent `qreward` -> guest `QRGIVE` minus natively paid; key once per load. Validation `QuestMirrorRules.ValidReward`. XP not shared (no exact readback).
5. UI text (MenuUi Revision 8, StatusServer, notifications) updated for pause and shared items.
6. Tests added: PauseGateTests, NpcsLuaTests, NpcCoordinatorTests, SharedItemsLuaTests, RewardsLuaTests, more PauseCoordinator/PauseLua tests; LuaStubs extended.

### Live engine evidence (private profile, synthetic peer)

* Retail game + bridge: ESC menu declined while heartbeat on (world clock and status lines kept running), menu paused again after heartbeat lapse, resume always runs; C# BridgePauseGate drove it (`KCDUS_LIVE_GATE=1` test). FREEZE works in retail too.
* `tools/engine/shared_items_live.py`: 24/25 passed on the final pak; only the weapon-drawn check failed for `rat_pirk_guard4`, which cannot draw by script even with its brain on (control added to the script; the re-run with the control `_work/live-items2.txt` ended without summary output - rerun it and look).
* `shared_outcomes_live.py` 14/14, `shared_outcomes_live2.py` 12/12 (parse fix applied) on the same pak.
* Probes kept in `tools/engine/`: npc_ai_probe.lua, npc_target_probe.lua, npc_puppet_probe.lua, ground_probe*.lua, shop_probe.lua. Findings: AI.AddPersonallyHostile sets hostility but KCD1's brain does not start combat; `soul:IsInCombatDanger` stays false on a hit.

## Remaining work, in order

1. KCD1: rerun `shared_items_live.py` once; stop the private game; write docs: CAPABILITIES.md (Pausing, Shared enemies, Loot and items, Quest rewards), HUMAN-ACCEPTANCE-TESTS.md (new T-57+ for each), KNOWN-LIMITS, IMPLEMENTATION-STATUS, PLAYING-TOGETHER, READ-ME-FIRST.txt, FEATURE-PARITY (copy identically to KCD2), SESSION4-RESULTS.md with hashes. Honest limits: puppets don't show sword swings; shop money not shared; XP rewards not shared; own-menu option needs the launcher's adapter (Windows only).
2. **KCD2 parity** (KCD2 lacks): saddlebags, shop stock, quest rewards, live chests (KCD2 chests are a per-world ledger applied at join, `kdcmp/Data/Scripts/Startup/kdcmp.lua` WO-134 ~line 15740; `W134.CHEST_CLASS` ~16660, radius 3 m). KCD2 already has: own-menu levers (wo138 native gate, `mp_pause_mode`), shared pause, native NPC claims/drive, body/loose-item loot, DLC cap. Suggested: add `Horse` and shop-linked stashes (larger radius, require player-pack delta) to W134, port rewards.lua logic onto KCD2's quest mirror, add Lua synthetic suites (tools/Test-WO*Synthetic.lua pattern) and agent tests. Keep KCD2 checkpoint barrier Candidate unless proved.
3. Build: KCD1 `tools/Build-Installer.ps1 -SkipSoakGate` (make sure it picks up the rebuilt `build/engine/KcdUsEngineBridge.dll`); KCD2 `tools/Build-Installer.ps1 -PlaytestBuild`; Linux archives via WSL (`_work/wsl-build-both.sh`, run as `wsl.exe -d Ubuntu -- bash -c "tr -d '\r' < ... > $HOME/x.sh; bash $HOME/x.sh"`). Install both locally with the silent installers.
4. Commit both repos, push branches, publish pre-release `playtest-20261009` (or similar) on both forks with `gh release create --prerelease`, verified SHA-256s in notes; update memory.
