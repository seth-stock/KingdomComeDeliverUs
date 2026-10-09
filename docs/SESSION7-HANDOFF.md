# Session 7 continuation: native KCD1 integration

Read SESSION7-RESULTS.md first, then FEATURE-PARITY.md and the session-6 results. Do not call the current work full multiplayer. Keep VERSION unchanged, use only seth-stock fork URLs, never force/upstream push, never touch real saves/firewall rules or kill by process name. Native actions/capture remain private Candidate paths. Stop only the harness-owned PID through tools/engine/stop_owned_session.py. The final owned session was stopped. Installed packages remain session 6.

## Local tools

KCD1 worktree: `C:\Users\seths\Projects\kcd1-coop\codex-reliability`.
Ghidra project: `_work/ghidra-session7/projects/KCD1Targets`, program WHGame.dll.
Decompilation outputs: `_work/ghidra-session7/out` (private/ignored; never publish).

Set scoped JAVA_HOME to `C:\Program Files\Eclipse Adoptium\jdk-25.0.4.101-hotspot`, heap 2G and direct memory 256m; run one headless job with `-noanalysis -max-cpu 2`. `prepare_targets.py <local binary> <ignored TSV> name=<VA>...` selects bounded unwind spans and one direct-callee level. Reuse `-process WHGame.dll`, not repeated imports/overwrites. Run `TargetedDecompile.java <TSV> <ignored output>`. Check its finished marker and errors, not just CLI exit code. Ghidra frequently drops floating-point arguments or misidentifies optimized thunk prototypes: check assembly and call sites before calling anything.

Use the engine harness launch/readback/screenshot flow in tools/engine/README.md. Inspect a nonblack initialized menu screenshot before Enter; selection may be Quit. New probes are not in the pak. Load the current native_identity.lua into the private process before the owner probe if the installed pak is older. Never run all old probes as a batch.

## Verified native map (RVAs, exact gated KCD1 binary only)

| Domain | Entry / layout | What it establishes |
|---|---|---|
| NPC animation | Generic IAction ctor 0x2c3ebc; TAction vtable 0x22c4e28; controller vtable 0x2229848; Queue +0x98 / 0x2c3a78 | Generic animation lifecycle, not full fighting |
| Action fields | forced scope mask +0x18, priority +0x24, status +0x28, fragment +0x38, tags +0x3c, refs +0x50 | Status 1 pending, 2 installed, 4 finished; retained ref needed for delayed inspection |
| Native action lifetime | retain 0x21eb18, release 0x222688, stop 0xf541a0 | Uses game heap/refcounts; do not free with CRT or write Player +0xaf0 |
| Controller resolve | 0x3778c0 update, 0x3ef008 pending resolution, 0xb01090 pending rule, 0x222100 priority comparison | Queue expiry can occur without installation; scopes/priority matter |
| Real combat actor | human +0x1a0, vtable 0x2228be8; lazy getter 0x3a107c; factory 0x6edd24 -> ctor 0x4f3068, allocation 0x780 | Actual combat component, distinct from animation expansion at human +0x2f8 |
| Combat target | CombatActor +0x4b8 is its combat soul; target at soul +0xca8; setter 0x4f6cb0, caller 0x4f68f4 | Native target setter emits notifications; not live-proved and not exposed as a writer |
| Actual attack graph | C_CombatActorActionAttack vtable 0x21aeca8; C_CombatActionAttackFactory vtable 0x22157e8 | Do not substitute generic TAction for the native combat-action graph |
| Attack procedural context | clip 0x21ca920; context 0x21b9a30; activation 0x563a44; callbacks through context +0x18/+0x58 | Full attacks need native context/callback registration, contact attribution and cleanup |
| Native inventory transfer | wrapper 0x451c1c -> core 0x451d08; item owner WUID +0x68 | One native AddItem moves existing instance; a merge can return another WUID |
| Item metadata | persistent GUID +0x10; class GUID +0x28; amount +0x38; condition +0x3c; equipped byte +0x48 | Preserving the existing object retains native data; current network class/count snapshots are insufficient |
| Progression | stats soul +0x4b4, ten 8-byte pairs; skills +0x540, 33 pairs | Base level and fixed-point progress, not total XP |
| XP path | Lua stat XP 0x11bbe60 -> 0x11ad080; skill 0x11bbd84 -> 0x11acea8; stat effect vtable 0x21c7a48, apply +0x18 -> 0x6ac280 | Actual stat apply resolves target WUID at effect +0x10; stat id +0x18; raw amount +0x1c; modifier flag +0x34. No hook or quest provenance is implemented |
| Buff ownership | soul vector +0x490/+0x498; instance +0x88 = owner soul; +0x10 original definition; +0x18 optional override | Exact instance enumeration/original factory definition; not an actor-reference/timer bitset |
| Buff serializer | instance vtable +0x38 saves, +0x40 loads; native archive ctor 0xf32c38, write 0xf35a84, child close 0xf33638, final close/free 0xf336e0 | Native polymorphic serialization proved for injury/timed examples; loading is unproved |
| Buff manager | singleton 0x3500f10; vtable 0x22eecf8; Add 0x56998c, remove WUID 0x6c4d64, remove GUID 0x6d6790 | Stock factory/removal APIs work in private test; no arbitrary restore is wired |

## Next implementation gates

1. Trace the true combat action requests and native callbacks, target registration, weapon contact and hit/block/death credit. Prove ordinary no-save NPC creation, actual attacks in both directions, destruction, streaming, save/load and ownership handoff. Native MotionIdle proof does not satisfy these gates; keep the old Player/channel proxy rejected.
2. Wire the verified whole-instance move into scoped host transaction intents and durable receipts, with native pre-mutation stock UI interception, host-local participation and crash reconciliation. Implement lineage/metadata for split/merge, consume/equip, barter and theft/crime. The current helper rejects possible merges/equipped items and has no automatic retry or compensation after uncertainty. Class/count recreation across computers is still insufficient.
3. Hook actual quest/effect provenance and correlate native XP/reward/spawn/script effects with persistent objective executions. Distinguish generated effects from applied effects, and bonuses from base XP. Three-second inventory inference and raw progression differences cannot establish cause or exactly-once outcomes. Journal authority, receipt and checkpoint epoch before mutation and reconcile interrupted outcomes from native state.
4. Implement a validated buff/state capsule and native rehydration. Carry original/override definition identity, derived serialized fields and source clocks; classify every timer/reference policy. The observed timed example stores elapsed time; other effects can store absolute deadlines or references. Prove effect reconstruction/removal, derived stats, effective perks, injury/body parts and saved readback before enabling restoration. Do not feed opaque arbitrary data to native deserializers or copy pointers across worlds. The existing nonempty 137D actor-reference refusal remains.
5. Carry the finished native operations through authenticated room protocols, ancestry/checkpoints, recovery tests and two-/four-computer human acceptance. Until then do not replace the installed packages with claims of complete campaign co-op. Update FEATURE-PARITY identically in both worktrees, build/install/push only authorized forks, and publish a new pre-release only with honest actual feature coverage and verified package hashes.

Verification commands: `dotnet test dotnet/KcdUs.sln -c Release`, `tools/Test-EngineRules.ps1`, `tools/Build-Engine.ps1`, `python tools/Build-Pak.py`. Last results: 392 managed/Lua tests, 14 native stream checks; private live buff/owner proofs passed. No human tests accepted.
