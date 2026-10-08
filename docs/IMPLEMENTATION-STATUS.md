# Co-op reliability implementation

Baseline: `bd17e95`. Work is isolated on `codex/coop-reliability`.

This is a development branch. It must not be distributed as a complete co-op release until the engine capability and human acceptance gates pass.

## Implemented

- Received-world replacement copies and hashes originals before changing the destination. Unique transaction archives prevent timestamp collisions. Recovery completes an installed replacement or cancels an operation that never installed it; unexpected external saves stop recovery without deleting them. `DiskCommitted` is not an engine-load acknowledgement.
- Transfers reject wrong-sized chunks and conflicting duplicates. Save inflation is bounded by declared block sizes and a total size ceiling.
- Damaged world registries disable shared-world operations instead of silently discarding slot/home bindings. New worlds use UUIDs; legacy world IDs remain readable.
- `Coop.Persistence.CheckpointStore` stores immutable SHA-256 artifacts and manifests, validates all character/ledger references before publication, and compares checkpoint ancestry. The same source and tests are in the KCD2 branch. It is not yet wired into KCD1 live session checkpoints.
- Offline playline leasing archives whole directories, verifies them, stages switches on the save volume, journals both renames, and retains displaced trees. The game still sees exactly five playlines. Logical archive IDs prevent home-world references following a reused slot.
- Read-only engine investigation: `tools/Inspect-EngineCapabilities.py` and the in-game `kcdus_capabilities` command. The committed report identifies 35 API candidates from this installed game's scripts and hashes the relevant modules. Candidate presence is deliberately **unproven**, never equivalent to working engine support.
- The in-game Henry tooltip describes native saved-character replacement and refusal conditions. Legacy card commands are not used by normal joins or sending Henry home.
- Added `--prepare-exact-traits` progression preparation, now also used in live portable character staging. A disposable prepared world loaded in retail KCD1 with lower stat/skill levels; its skill XP and perk-state bytes survived native saving. See [initial engine evidence](ENGINE-INTEGRATION-20261006.md).
- Enabled walking on ordinary NPCs using full-body looping animation clips. No native Player/channel proxy is created. Idle transitions, removal and rendered foot/knee movement were observed in the retail engine. Entity-name checks protect against stale IDs after a load.
- Implemented equipment snapshots, validation, relay transport and application to peer NPCs. A small original C++ adapter exposes the equipped bit on the verified Windows retail engine only. Launch through the rebuilt launcher to load it during suspended startup. Item classes/conditions matched native readback and replicated plate armor/helmet rendered in game.
- Added `--prepare-exact-inventory [--with-traits]`, now also used in live portable character staging. It replaces Henry's native inventory record, preserves instance metadata/extensions, and refuses carried instance IDs already owned by other inventories. An early six-item Henry replaced the destination's 127-item inventory; the engine loaded and saved that inventory record byte for byte.
- Normal **My own Henry** joins and **Send Henry home** now stage native core/inventory snapshots before loading and use native-save readback for acceptance. Fresh capture, immutable personal snapshots, load journals, recovery, source-playline checks and refusal paths are implemented. Fresh join and interrupted-load retry passed in one disposable real engine connected to a synthetic sender. See [2026-10-07 evidence and remaining gates](ENGINE-INTEGRATION-20261007.md).
- Persistent native soul/item identity lookup is implemented on the supported Windows engine. Read-only Lua resolution rejects unsupported adapters, stale handles and duplicate identities. Native weapon damage interception and authoritative simulation are still missing.
- Concurrent relay admissions cannot reserve the same player ID or multiple hosts. Verification autosaves do not advance the shared checkpoint stamp and cause resynchronization loops.

## Room contract and participant identity (2026-10-07, protocol 2)

* One shared contract (`Coop.Contract`, the same source and test vectors as the KCD2 branch) now decides who may share a room. The first thing a client says is a handshake: game id, release, wire and contract version, agent/Lua/native/engine/content hashes and an honest capability table. The relay refuses another game, another contract or wire version, and a different or unverifiable Lua pak, in a sentence the player can read. See [CAPABILITIES.md](CAPABILITIES.md).
* Participants are bound to a P-256 key by challenge/response; the relay persists the bindings (`--bindings`). Another computer cannot take a name's participant id, and replayed proofs are refused.
* Every room has a mode, **Presence**, **Partial** or **Shared simulation**, announced to every player and shown in the launcher and the game. This build can only be Presence: authority over NPCs, combat, loot and quests is not implemented. The sentence says "NOT active".
* Installs with different DLC or other mods are admitted as presence only, and a world or a Henry is neither requested nor served between them.
* The durable operation journal (`OperationJournal`) and the authority epoch guard (`AuthorityGuard`) exist with tests but are **not yet attached to live gameplay mutations** in KCD1, because there is no host-authority mutation to protect yet.
* `--dev-allow-unverified` (relay) and `DevAllowUnverifiedPayload` (agent) exist for development only.

Validation on 2026-10-07: 268 .NET tests pass (up from 224), including shared contract vectors, relay handshake/identity/content tests and agent-level room-mode tests. The native engine bridge (`tools/Build-Engine.ps1`) builds; the installed game's `WHGame.dll` hash equals the supported hash. A live engine smoke test was **not** rerun for this stage. No two-computer test was performed. See [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md).

## Shared fights, loot and pausing (2026-10-08)

* The "damage has no effect" finding was a probe mistake: `soul:DealDamage(stamina, health, ...)` was called as (3, 0). With health damage it lowers health through the ordinary path and kills; `tools/engine/damage_probe2.lua` / `damage_probe3.lua`.
* `mod/kcdus/lua/combat.lua`, `loot.lua`, `pause.lua`; agent side `OutcomesCoordinator.cs` and `PauseCoordinator.cs`; the pause menu watcher is a flow graph in the UI pak (`MenuUi.cs`, revision 6). Live checks: `tools/engine/shared_outcomes_live.py` (14) and `shared_outcomes_live2.py` (12), 317 automated tests.
* Harness fact: the private game runs the mod in the REAL game folder (`Mods\kcdus`), not the copy under the session root: install the build under test there first (see `tools/engine/README.md`).
* Still not done: shared NPC AI (movement, targeting, noticing), items on the ground, saddlebags, shops, quest rewards and spawns, stopping a player's own pause menu from pausing their own game, two-computer and four-player acceptance.

## Offline slot commands

Run the built `KcdUsAgent` with the game closed. Configure `savesDir`, `worldsFile`, and `backupDir` if using a non-default profile. Displayed slot numbers are 1 through 5.

```text
KcdUsAgent --slot-list
KcdUsAgent --slot-archive 5 --cloud-sync-paused
KcdUsAgent --slot-restore <lease UUID printed by archive> --cloud-sync-paused
KcdUsAgent --slot-recover --cloud-sync-paused
```

Pause Steam Cloud synchronization before changing slots. The acknowledgement flag does not disable Cloud for you. Operations refuse a running KingdomCome process. Archives are outside the game saves directory. Restore also archives outgoing co-op progress as its own restorable lease. To send Henry to an archived home, first restore that home offline.

For a disposable profile, configure all three paths explicitly; never use your real saves as automated test fixtures.

## Gates not completed

| Plan part | Remaining work |
|---|---|
| Stable participant identities / compatibility | Identity binding, handshake and content-profile gating are implemented and tested (2026-10-07). Remaining: artifact provenance for agent/native hashes (reported, not enforced) and human acceptance |
| Checkpoint publication | Consistent live world/character/inventory capture barrier; engine-load acknowledgement; registry/install transaction coordination |
| Walking and outfits | Implemented on ordinary NPCs; native outfit adapter supports the verified Windows engine only. Two-computer, load/reconnect soak, additional poses, dirt/hair and content compatibility acceptance remain |
| Exact characters and perks | Live portable core/inventory staging and native acknowledgement are implemented; remaining gates include non-core timers, active/world-linked state, selected-perk gameplay effects and content compatibility |
| Shared simulation | Real damage/AI/inventory/quest interception, persistent entity identity, host authority, interest management, transitions and checkpoint recovery |
| Slot UI/lifecycle | Launcher/menu UI and uninstall restoration; automatic startup recovery for directory switches while the game is stopped |
| Human acceptance / release | All live capability gates, multiplayer testing, soak, installer upgrade/rollback, matching packages and deployment |

The installed scripts call SetStatLevel/SetSkillLevel (TalkPersuade.lua), debug setters and AddPerk/RemovePerk (DebugPlayerFightSkills.lua), and animation-graph/equipment APIs. The game's own RemovePerk example questions whether a GUID is accepted. No mutating candidate was invoked on a player's real character. Do not assume the signature or persistence from a script reference.

Claude's original checkout remains on `main` at bd17e95. This worktree is `../codex-reliability`, branch `codex/coop-reliability`. During engine investigation the development Lua mod and locally generated menu pak were installed in KCD1's `Mods/kcdus`. Game executables/DLLs, original installers and firewall rules were not changed. Mutations used private cloned saves; all 260 real save hashes remained unchanged.

## Verification on 2026-10-06

The .NET/Lua-stub suite passed 224 tests (baseline 167), including progression/inventory staging, locomotion, stale-handle protection and outfit transport tests. Engine probes ran in private processes/profiles with cloned saves; positive and negative results are recorded in the linked engine report. Native Player proxies and negative-XP/goal-pipe routes remain excluded. No two-computer multiplayer test was performed. No complete co-op release was published.

The 2026-10-07 work supersedes the earlier offline-only character status. Read [the current engine report](ENGINE-INTEGRATION-20261007.md) before distributing or testing the development build. Full shared simulation is not completed.

Current validation: 240 passing tests, successful production joins and pending-load retry in the disposable retail engine, unchanged hashes for all 260 real saves. The rebuilt local launcher/agent, own Lua pak and locally generated menu revision 5 are installed for development testing. No complete co-op release/installer package has been published.

## Session 2 continuation: 2026-10-07

This remains a playtest build. No capability has human acceptance, and the two games do **not** yet have equivalent shared simulation. VERSION remains KCD1 0.1.0 / KCD2 0.45.0; distinguish downloads by release tag, commit and SHA-256, not VERSION alone.

* KCD2 protocol 13 refuses older agents/relays. Durable host body take/put and loose-item decisions are journaled before the Lua mutation and before the reply. Interrupted unknown mutations are quarantined, never retried automatically. `HostDecisionComplete` means the host decision is durable, not that the recipient received/kept an item. Loose-item replies now carry the same connection/load scope as body replies. Unknown loose items are refused; successful removal requires entity readback.
* KCD2 checkpoint barrier is **Candidate, default off**. Host console: `mp_checkpoint_mode candidate` (off reverses it for this agent session). It uses a fixed relay-verified participant roster, authority incarnation and checkpoint UUID, acknowledged native holds, loot settlement, a verified host save, bounded/acknowledged guest character uploads, matching save MD5s, chest ledgers, cross-participant item-instance checks, durable prepared artifacts, commit acknowledgements, and manifest publication before release. Disconnect, roster/load change, missing data or the 90-second deadline aborts. The native hold expires 20 seconds after the agent's last refresh. A guest keeps its paired personal artifacts and receipt; it does not yet receive a full host checkpoint archive for offline reconciliation.
* Checkpoints still need proof that native saves work while held and that **all** inventory/quest/combat mutations are excluded. The Lua pickup gate is not complete native inventory interception. Ordinary autosaves still follow the existing path; interrupted capture is not automatically promoted. Offline checkpoint selection/preparation remains separate from live reconnect promotion.
* KCD1 quest mirror is **Candidate, default off**. Console `kcdus_quest_mode candidate` on both test clients enables one-way host snapshots; `off` disables it. Identifiers come from the installed quest tables. Only open, base-game main/side/activity quests are eligible. DLC, rails/mixed scenes, local system/random events and unknown objectives are vetoed. It reads native state before applying and after mutation, replays completed objectives without repeating the call, refuses inactive objectives and unverified quest completion, and never treats cancel/deactivate as success. Objective reward/spawn side effects and full quest/variable coverage remain unproved.
* Private KCD1 engine evidence: `q_revenge/findVonAulitz` changed from started/not-completed to completed through the real adapter; repeated application read back completed. This proves one objective binding, not full quest authority. `t_scale=0` stopped observed calendar progression; it was restored to 1. Production shared pause remains **Absent**: there is no proved crash-safe engine lease or menu-pause interception. A Lua timer cannot safely release a freeze that stops its timers.
* KCD1 enemies/combat decision: **do not enable shared combat/NPC drive**. Existing damage probes did not prove the ordinary hit path; presence NPCs are not host-authoritative combatants. Damage interception, AI suppression/drive, targeting all players, one death/reward revision and a shared inventory ledger remain implementation gates. Do not replace this with cosmetic attacks or health setters.
* KCD2 shared pause and its off option remain Candidate until the two-computer tests pass. DLC uses the host's lower-content world: richer guests are allowed and extra DLC quest mirroring is gated; poorer guests cannot receive an existing richer host save. Disable extra DLC in Steam and restart as instructed. The mod does not rewrite a DLC save or deactivate Steam entitlements. Different other mods still cap the room at presence and block world/character moves.
* Linux packages are experimental. A successful WSL build/fake Steam test is not proof of Proton gameplay. KCD1's Windows startup adapter paths are not proved under the native Linux agent. No Linux engine feature is upgraded by rebuilding a tarball.

Human acceptance is pending. KCD1 ESC-menu Multiplayer entry and its Status/Host/Join/Leave/Game world/Story/Keys/Settings/Back page were visually observed in the disposable loaded game. That does not prove buttons in a connected session. KCD2 pause-menu behavior still needs verification. Run the new test cases on disposable copies, record release/commit hashes and logs, and leave failed capabilities Candidate. No real saves or firewall rules were changed by this continuation's guarded probes.
