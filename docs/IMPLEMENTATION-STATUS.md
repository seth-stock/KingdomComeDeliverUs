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
| Stable participant identities / compatibility | Persistent identity integration, verified content profiles, and complete artifact provenance |
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
