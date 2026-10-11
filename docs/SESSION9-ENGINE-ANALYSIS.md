# Session 9: full engine exports and private integration work

Updated 2026-10-11. The requested combat, durable economy recovery, causal quest/XP
consequences and arbitrary character restoration are **still unfinished**.
This session starts the requested whole-engine decompilation approach before
further adapter changes. No gameplay capability is promoted. The common journal
and KCD1 private combat probe were improved below; full online integration is
unfinished. No installer, local installation or release has been made.
VERSION remains 0.1.0 / 0.45.0. No human acceptance is recorded.

## Actual work and evidence

The same research tools are in both worktrees at `tools/engine/ghidra`.
Read that directory's README for the exact command and limits. The active
controller was launched in the **KCD1** reliability worktree:

```powershell
python tools/engine/ghidra/full_engine_analysis.py --timeout 8
```

Its private artifacts live in that worktree's ignored
`_work/ghidra-full-session9`. `controller.json` is the live process/module/log
record. Each module has a binary SHA-256, analysis options, saved project,
function inventory, symbols, decoded strings, references, atomic C export
shards, progress and an export audit. Never publish these artifacts or binaries.

The matrix has 37 images / 305 MiB: KCD1 WHGame, KCD2 retail WHGame, KCD2
Modding Tools gameplay DLLs, and that split build's Cry support DLLs. Third-party
libraries, editors, tests and ARM binaries are excluded. Retail KCD2 is a
monolithic image; the current native adapter expects the different split
Modding Tools layout. Do not reuse its RVAs against retail.

KCD1 image SHA-256:
`cf9f6dc384edcf35c20647a912745ddb8adb5ba65953e329c24e89dd9c4381aa`.
Retail KCD2 image SHA-256:
`bdf8f9e4a11257a72b64c84700e284c29e4c4ccaf5b8d4bfa7d0b2a7294479f7`.

KCD1 whole-image analysis succeeded and saved. Export attempted **140,165**
discovered internal functions: **140,158** successful, **7** failed. The audit
flags **24,886** successful functions containing decompiler warnings. Function
bodies cover 34,207,591 of 35,147,776 executable bytes (about 97.3%); gaps may
contain padding, data or undiscovered code. This is not original source recovery,
proof of all code, native ABI correctness or functioning multiplayer.

Failed addresses: 0x18002cdc0, 0x18002ecf0, 0x180030cf0, 0x180032eb0,
0x180034e90, 0x180036de0 (flow instruction limit), 0x180be2773 (input varnodes).
No failed function has been silently marked successful. Retail KCD2 whole-image
analysis also succeeded and saved: 315,064 attempted / 315,060 successful / 4
failed, with 41,764 warning functions. Completed split exports include Combat,
RPG, Quest, Entity, Concept, Shop, Player, Dialog, Animation, Framework and
XBehavior. The whole **37-image** queue is now finished: **910,489** functions
attempted, **16** failed exports and **160,023** warning functions. Every image
has an all-discovered-attempted audit. The controller was subsequently rerun
with its resume-audit change and revalidated every completed export. Original
failures remain explicit; this does not recover every original function or
prove native semantics. XGenAI has 115,750 functions, one failure and 23,020
warning functions.

The first KCD1 default analysis exhausted its bounded 6 GiB Java heap in
`AddressTableAnalyzer.removeNonSearchableMemory`. The retry disables only the
optional whole-image Create Address Tables heuristic, plus offline PDB analysis.
Function discovery, RTTI, references, stack and switch analysis remain enabled.
The failed log is preserved. Import, analysis and export now use separate CLI
invocations so each completed phase is saved before the next.

Default bounds: two CPUs, 6 GiB Java heap, 512 MiB direct memory, 768 MiB
metaspace, 10 GiB aggregate committed memory for the owned Windows job and
children. Launch requires 12 GiB free physical RAM; below 4 GiB the controller
stops only its owned job. A named mutex excludes overlapping controllers.
Both the Java scripts and Python tooling compiled. The export audit passed
checks for incomplete exports, failed functions, duplicate/foreign addresses,
changed bodies, empty successful output, malformed JSON and warning reporting.
A second controller was correctly refused.

## Analysis-quality follow-up

`prepare_unwind.py` validates and exports x64 PE primary/chained unwind ranges
without modifying code or automatically merging Ghidra functions. KCD1 has
138,923 runtime ranges / 118,592 primary groups; retail KCD2 has 291,812 ranges /
255,535 groups. Both inventories have zero unsupported unwind-version records.
These counts are not equivalent to all functions: leaf functions and compiler
fragments require separate interpretation.

Actual KCD1 metadata binds range 0x180be048e..0x180be3814 to primary entry
0x1802f27cc. Failed entry 0x180be2773 is inside it and its machine code uses
inherited registers without a new entry prologue. Review that cold fragment as
part of the primary procedure before interpreting its decompiled arguments.
The early decoder failures have two-stage byte-remap/relative jump tables;
review the machine-code table bounds before allowing decompiler flow to spill
into adjacent tables. Windows permits noncontiguous
procedures and chained unwind records:
[Microsoft x64 exception handling](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170).

Use `full_engine_query.py --audit` to check committed exports, `--address` or
`--match` to extract private review text under `_work`. Check signatures against
assembly and independent callers; decompiler warnings and guesses are not ABI
proof. Preserve the original failed export when doing a repaired follow-up.

## Remaining work and protections

### Follow-up analysis and combat evidence

`BoundedSwitchRepair.java` runs against a **copy** of the saved Ghidra project,
with SHA-256 gating, eight-row/20 KiB address budgets and 30-second decompile
timeouts. Locally verified table targets corrected six decoder exports: five
256-byte remaps into 86 relative DWORD destinations, and two tables in the
sixth decoder (eight direct entries plus another 256/86 remap). All six now
produce C in the review copy. The unmodified full exports still report their
original failures. Manual switch-override warnings remain; ABI/signature and
case semantics still need assembly review. No live binary was changed. Private
results: `_work/ghidra-full-session9/switch-repair`; copied project:
`projects/kcd1-switch-review`. Its job used a 2 GiB Java heap / 6 GiB job cap.
The failed cold fragment and other ten raw failed exports remain to investigate.

KCD1's private `guard`/`unguard` commands use the verified native setter at
vtable+0x278 / RVA 0xf4b3c4, which calls 0x688d94 and schedules the normal
guard/combat action. Native caller 0xa561d0 explicitly releases scope 4; the
request byte is CombatActor+0x565. This is **shared with native AI**, not an
ownership lease. Scope 4 is used only on the disposable bodies in these probes.
The adapter reads the flag back and the probe releases it before target/body
cleanup. Do not reuse KCD2's vtable+0x360 here: KCD1's entry at that offset
(0xf4a7d8) clears combat state when its boolean argument is false.

The 1200 ms delayed indoor probe in profile
`KCDUS-engine-4d3de054d620468b859592bf80b0b99d` still returned factory reason 7,
with combat active=0 / mode=1 / canAttack=1 at the request. Later AI targeted
Henry. Health stayed 100; both-target clearing and body cleanup passed.

Guard-enabled profile `KCDUS-engine-c665c4cbcea947fa9246998332d15f59` did create
and submit the real attack (reason 0, constructed/submitted/scheduled=1, slot 0,
mode 4) against the second NPC. Native attack/hit-sync clips were seen in the
log, but both health values remained 100. An outdoor repeat at (3000,2000),
terrain elevation 159.3, kept template equipment, sampled stamina and weapon
readback, and refreshed the requested target every 150 ms. The attack again
created/submitted; attacker weapon readback was true, health stayed 100 and
stamina recovered through the samples. This does not attribute damage, establish
kill credit or prove independent fighting. Both guard releases, both-target
clears and body removals passed; no native fault was recorded. The process was
stopped with the owned-session helper. The original 401 real-save hashes still
match. The private screenshots show the original furnished room; none are
release assets.

### Implemented reliability changes

Both `OperationJournal.cs` copies now parse bounded 64 KiB records rather than
allocating the entire journal in RAM. Only an unterminated final write may be
truncated. A corrupt newline-terminated record, including the final one, is
preserved and refused; previously this could erase EngineApplying evidence and
recover the preceding IntentRecorded state as a rejection. Journal records also
validate field bounds and enum values on loading. Any write/flush exception
permanently blocks further mutations through that instance until it is closed
and recovered. Sequence publication remains after durable flush. This prevents
unsafe journal reuse; it does **not** implement missing recipient receipts or
restore items after arbitrary engine/checkpoint crashes.

Six new cases run in each common contract suite: corrupted complete tail (JSON
or hash), oversized terminated/unterminated records, escaped Unicode across
stream blocks, and a backing-file write failure. All current suites passed:
KCD1 399; KCD2 client 1,329; KCD2 relay 74. Targeted contract/loot counts were
27 / 32. Existing build/analyzer warnings remain.

KCD1 `NativeFight.h` now uses the native requested-target setter, RVA 0x302cc0,
in addition to the current-target setter 0x303720. Assembly confirms 0x303684
owns soul+0xca0; 0x5d9db8 can restore that requested target later. Clearing a
probe now requires both requested and current targets to be empty before body
removal. The requested wrapper vtable is 0x22075a0, getter+8 -> 0x6f8130 (reads
wrapper+8). No target pointers are retained across Lua calls. Diagnostic version
2 reports requested/current agreement and native state before a factory refusal.
The adapter compiles; it remains guarded by the owned private action/state gates.

First live test profile: `KCDUS-engine-c96586582d7e457eb138ecaafc97d78c`.
The stock menu load graph was invoked through the guarded console because
Windows refused window focus. No Enter or other keys were sent. Native readback
confirmed the disposable cloned world was loaded. Both target setters/readbacks
and both-target clearing succeeded; body cleanup passed and no native fault was
recorded. The attack factory refused with reason 7 before active combat, AI
later retargeted Henry, and both NPCs retained health 100. This is **negative**
independent combat evidence. A delayed follow-up must be recorded separately;
neither result establishes participant ownership, streaming, hits or blocks.

The full output also corrects an earlier research lead: collision-process
vtable+0x18 / RVA 0x6fbe50 is a diagnostic name getter, not a hit callback.
`C_CombatActorCollisions` secondary-interface method at 0x5cc61c instead builds
and filters collision details before dispatch. Its ABI, effect ownership and
multiplayer interception are not established merely by decompilation.

Investigate relevant failed/gap/warning
regions. Then trace and implement the four requested engine integrations in
both game-specific adapters: independent damaging combat/target lifetimes;
native inventory custody, receipts and recovery across host-local/guest use;
quest execution provenance and exactly-once item/XP/spawn/script effects;
complete validated personal state with native construction, clocks, effective
abilities and explicit world-reference handling. Existing SESSION8-HANDOFF
integration gates still apply. No invented engine method or raw unknown field
write may substitute for proof.

Only then run appropriate suites and guarded private-engine tests, build/package
and push the completed work to the explicit seth-stock fork URLs. Do not enable
unproven paths, claim shared simulation or human acceptance, force-push, modify
VERSION, touch real saves/firewall rules, terminate games by name, or recursively
remove game-root junctions. All 401 baseline real-save files remained unchanged
at the latest audit. Private game tests use cloned profiles only. Normal local
installations remain Session 6. Changes are uncommitted and nothing has been
pushed or published in this session; do not publish local decompiled code.
