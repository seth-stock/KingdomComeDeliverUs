# Session 8: native combat scheduler, XP observation and bounded buff restoration

2026-10-10. **The full multiplayer request remains unfinished.** These changes do not establish full remote fighting, a complete durable economy, causal quest consequences, arbitrary character restoration, parity or human acceptance. Native experimental paths remain private Candidate paths. VERSION and wire versions are unchanged: KCD1 0.1.0 / wire 3; KCD2 0.45.0 / wire 15.

## KCD1 implementation and actual engine evidence

`NativeFight.h` uses the game's real `C_CombatActorActionAttack` factory and action environment. It resolves actors on each game-thread call, replaces targets through the stock strategy/wrapper lifecycle, submits the real combat action, and releases only its locally retained action reference. It does not use a fabricated Player, the rejected channel proxy, a generic animation as an attack, or a copied KCD2 layout.

Private ordinary NPCs accepted target replacement and a real attack action (constructed=1, submitted=1, active=1). Native combat mode advanced through attack/recovery, and visible NPCs moved and reached combat poses. Target clearing and removal completed without a native fault. **The tests did not establish a damaging hit against the intended independent participant.** Health stayed 100 in the sampled NPCs, native targeting could change after submission, and off-view behavior differed. This is an implemented private native scheduler, not complete fighting. Ownership fencing, independent targeting, hit/block/death credit, remote control, culling/streaming and save/load cleanup still need integration and proof.

`XpRead.h` observes the original native stat and skill XP applications exactly once and preserves their return values. Before and after the original call it resolves the soul afresh, checks persistent identity, and records native level/progress pairs, requested/actual fixed-point amounts, modifier policy and source/cause types. It holds no lock across the native call and retains no soul/cause pointers in its published observation. The latest observation is diagnostic, not a durable event queue.

Actual engine readback passed for strength XP (stat index 0) and reading XP (skill index 26). Each increased its progression by raw `0x80`, matching independent native state captures. Read-only queries did not manufacture an XP event. **Effect +8 is C_EffectSource, not I_Cause.** The verified source vtable is RVA `0x21beb08`; its +8 owns the cause. These Lua awards carried `C_UknownScriptCause` (`0x2201b10`). Dedicated quest-objective/state causes also exist, but their persistent objective execution identity and award provenance are not integrated. Do not infer quest causality from timing, inventory gains, source pointers or this diagnostic sequence.

`TimedBuffRecord.h` and `BuffRestore.h` implement a bounded native reader and an explicit 54-byte timed-buff schema. The private loader accepts only the observed cooldown definition `c37ab134-a443-433f-92a9-51ff6f08999c`, with an existing unique instance of the same schema on the requested current soul. It rejects nonempty overrides/modifier chains, malformed framing, nonfinite/negative/excessive elapsed time, invalid handles, duplicate matches and incompatible current records before mutation. It uses the game's own archive allocation/reader/free path and requires exact serialized readback.

Live proof: a malformed record was refused without changing state; after the native timer advanced, restoring its earlier snapshot returned applied=1 and exact byte equality. Removing the test buff restored the original count. **This does not reconstruct missing effects or restore every injury, timer, cooldown, ability, effective perk or world reference.** Normal profiles cannot arm these experiment entries through the mod UI.

The managed suite exposed a Windows registry publication/read race in `WorldRegistry`. Registry reads and flushed-file publication now serialize across independently constructed in-process registries; readers allow file replacement and read one file incarnation. The new concurrency test reproduces the old failure mode and validates complete snapshots during repeated publication. Corrupt JSON still fails closed; save files are not rewritten by this fix.

## KCD2 recipient delivery verification

`KCD2MP_W134ItemResult` now requires native recipient inventory readback for a host-approved loose-item pickup. It captures the original native item handle/class/count/condition and verifies that same instance in the player's inventory after the callback. A narrow stock merge is also recognized: the source has disappeared, exactly one pre-existing destination of the same class/condition gains the exact source quantity, and no other item of that class changes. A newly minted class-equivalent substitute is refused. A successful `pcall` alone cannot settle delivery; an exception after an observed native move can still establish that the move happened.

An unverified delivery remains unsettled, blocks the Candidate checkpoint barrier, cannot invoke pickup again on duplicate/reordered replies, and cannot be erased by an ordinary timeout or a host-disappearance update. Reward gain accounting occurs after observed delivery, with the observed quantity.

**Limits:** this pending evidence is still volatile; world load/process restart is not durable recipient recovery. Ambiguous, deferred or condition-changing merges remain unresolved; persistent GUIDs and arbitrary metadata/lineage are not covered by these observations. The host ledger still records host decisions rather than verified end-to-end custody. Stock consumption, barter, theft/crime, host-local transactions and arbitrary item metadata are not fully intercepted or reconciled. This fix must not be described as a complete durable economy.

## Verification and publishing

* KCD1 managed/Lua suite: 393 passed after the registry fix; initial suite exposed the registry race, which was fixed rather than waived.
* KCD1 bounded-stream/schema native suite: 25 passed; native adapter compiled with MSVC.
* KCD1 live disposable-profile tests: stat XP, skill XP and timed-buff restore/readback passed. Combat scheduler/target cleanup had native evidence, damaging contact did not.
* KCD2 managed client suite: 1323 passed; world-item synthetic suite: 87 passed; container/reward parity suite: 40 passed.
* Final owned KCD1 process was stopped through `stop_owned_session.py`. Its native fault log was empty. Harness audits preserved 260 KCD1 save files; the broader real-save audit preserved all 401 baseline files.
* Ghidra used one targeted headless job at a time, 2 GiB Java heap cap, 256 MiB direct-memory cap, two CPUs and no whole-game automatic analysis. Binaries, projects, decompilations, stock-script inspection, private saves and screenshots stay under ignored `_work` and are not published.

Build/push results are recorded separately after verification. Existing session-6 releases and installed packages are not evidence of these new integrations.
