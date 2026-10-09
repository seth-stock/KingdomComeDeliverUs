# Session 7: KCD1 native integration research (2026-10-09)

**The requested full combat, economy, quest/XP consequences and complete character restoration are still unfinished.** This session implements verified native building blocks and private probes, not a complete multiplayer engine backend. No human acceptance is claimed. KCD2 gameplay source is unchanged. VERSION remains KCD1 0.1.0 / KCD2 0.45.0; protocols remain 3 / 15.

## Implemented and observed

* An ordinary-NPC action factory uses the game's allocator, generic animation-action constructor and controller queue, without the Player scripting storage. A real NPC reached native action status 2 and `MotionIdle` at 200, 700 and 1400 ms. The private diagnostic lease retains one native reference and releases it explicitly. A disposable spawned NPC instead remained pending and expired; queue acknowledgment does not mean playback. Body removal/identity/world readback survived the disposable test without a native fault. Attack/block/hit reactions, independent combat targets and streaming/load lifecycle are NOT proved.
* The supported Windows adapter exposes an item's actual inventory owner as `kcdusOwner`. A native whole-item transfer changed owner, removed the instance from the source, added it to the destination, and preserved persistent identity, count and condition. Both disposable inventory holders were removed. `NativeIdentity.transferInstance` implements this one-call primitive with prechecks and readback; equipped items and possible merges are refused. Inert, ambiguous or faulted outcomes are uncertain and never automatically refunded/retried. This helper is not wired into the room transaction journal or stock UI and is not a complete economy.
* Explicit private state capture reads all ten stat and 33 skill progression records. These are native `(level, fixed-point progress)` pairs, NOT total earned XP or causal quest rewards. No new XP writer, cause hook, shared XP grant or durable quest receipt is implemented.
* Explicit private capture invokes each buff's native polymorphic serializer through a bounded memory stream. It captured the active injury buff and a separately added timed buff. The timed record changed after 750 ms; native removal restored the baseline count. The original definition at +0x10 differs from the optional override at +0x18. This captures serialized state, not native pointers/object bytes. Deserialization, clock rebasing, effect reconstruction, reference remapping, abilities and complete character restoration remain unimplemented.

Private action writes require `KCDUS_PROBE_ACTIONS=1`, the harness's UUID-marked command line and save root. State capture additionally requires `KCDUS_PROBE_STATE=1` and explicit arming through `human:PlayAnim('@kcdus/state-capture','')` immediately before `ItemManager.GetItem(soulWuid)`. Native state faults disable further captures in that process. No normal-profile setting arms these paths. The adapter still refuses every engine except the previously verified WHGame.dll SHA-256 `cf9f6dc384edcf35c20647a912745ddb8adb5ba65953e329c24e89dd9c4381aa`.

## Ghidra findings and resource bounds

Targeted headless Ghidra at E:\Ghidra ran with JDK 25, `GHIDRA_HEADLESS_MAXMEM=2G`, direct-memory maximum 256 MB, CPU limit 2, `-noanalysis -max-cpu 2`, one job at a time. Observed Java heap was approximately 66–164 MiB. This is a heap cap, not a claim that all Java/native subprocess memory is capped at 2 GB. No whole-game automatic analysis ran. Imported binary, project, decompilations, local game-script/asset inspection and logs remain under ignored `_work/ghidra-session7`; they are not distributed.

The NPC constructor allocates 0x9f0 bytes; the Player PlayAnim/cancel path accesses +0xaf0. That explains the session-6 bypass fault. Do not revive it, zero that field on an NPC, fabricate a player channel, or copy KCD2 layouts.

The verified generic action uses a zero forced-scope mask, allowing the fragment definition to choose scopes. Actual combat also has a distinct `C_CombatActorActionAttack` graph and procedural contexts/callbacks. The generic animation factory is not that combat graph. See [SESSION7-HANDOFF.md](SESSION7-HANDOFF.md) for exact native entry points and remaining integration gates.

## Validation and distribution

* 392 .NET/Lua tests pass, including four new whole-instance move cases; 14 native bounded-stream checks pass.
* Windows adapter and deterministic Lua pak compile. The new pak SHA-256 is `a9636dd0dafd531a8de06a731b5a4580aac82ecaf07014511d9bda82f2abfc42`.
* Private real-engine proofs: active ordinary-NPC action; injury/timed-buff serialization and test-buff removal; exact whole-instance ownership transfer using the new Lua helper; disposable-holder cleanup. Native fault log size was zero in the final private session.
* The owned private game was stopped. All 260 original KCD1 .whs hashes and all 401 original-save files across both titles remain unchanged. No firewall edits, process-name kills, real save mutation, VERSION edits or junction-tree deletion.
* Existing installed Windows mods and the session-6 pre-releases remain the installed/downloadable builds. This session does not publish a new installer/pre-release or substitute private Candidate integrations into the installed games. Source/build success is not full multiplayer readiness.

Local evidence: `_work/ghidra-session7/state-live3.txt`, `buff-capture-live.txt`, `owner-helper-live.txt`, `combat-read-live.txt`, `all-tests.txt`, targeted Ghidra logs and the owned-profile engine logs. Evidence is local and not public gameplay acceptance.
