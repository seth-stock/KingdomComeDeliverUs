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
- Corrected the in-game Henry tooltip: the existing additive card does not transfer perks or exactly replace a character.
- Added offline `--prepare-exact-traits` progression preparation. A disposable prepared world loaded in retail KCD1 with lower stat/skill levels; its skill XP and perk-state bytes survived native saving. This is not full character/inventory restoration or the normal live join flow. See [engine evidence and unresolved gates](ENGINE-INTEGRATION-20261006.md).

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
| Walking and outfits | Player proxy animation reaches walking/idle, but removal crashes; establish safe lifecycle, outfit capture and replication |
| Exact characters and perks | Wire verified progression staging into capture/install/acknowledgement; cover ability flags, inventory, equipment, perk accounting and gameplay effects; replace additive cards |
| Shared simulation | Real damage/AI/inventory/quest interception, persistent entity identity, host authority, interest management, transitions and checkpoint recovery |
| Slot UI/lifecycle | Launcher/menu UI and uninstall restoration; automatic startup recovery for directory switches while the game is stopped |
| Human acceptance / release | All live capability gates, multiplayer testing, soak, installer upgrade/rollback, matching packages and deployment |

The installed scripts call SetStatLevel/SetSkillLevel (TalkPersuade.lua), debug setters and AddPerk/RemovePerk (DebugPlayerFightSkills.lua), and animation-graph/equipment APIs. The game's own RemovePerk example questions whether a GUID is accepted. No mutating candidate was invoked on a player's real character. Do not assume the signature or persistence from a script reference.

Claude's original checkout remains on `main` at bd17e95. This worktree is `../codex-reliability`, branch `codex/coop-reliability`. During engine investigation the development Lua mod and locally generated menu pak were installed in KCD1's `Mods/kcdus`. Game executables/DLLs, original installers and firewall rules were not changed. Mutations used private cloned saves; all 260 real save hashes remained unchanged.

## Verification on 2026-10-06

The .NET/Lua-stub suite passed 200 tests (baseline 167), including five progression-staging tests. Engine probes ran in a private process/profile with cloned saves; positive and negative results are recorded in the linked engine report. Walking proxies and negative-XP/goal-pipe routes remain excluded from the normal mod. No two-computer multiplayer test was performed.
