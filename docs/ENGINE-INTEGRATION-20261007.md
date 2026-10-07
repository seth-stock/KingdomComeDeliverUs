# Native character join and simulation investigation, 2026-10-07

Development branch `codex/coop-reliability`. This is not a completed shared-simulation release.

## Character join implemented and observed

Normal **My own Henry** joins now obtain a fresh native save, validate its version/checksum and canonical Henry, and durably retain the complete source file in a SHA-256 addressed personal snapshot. They prepare the received world before installing it. The additive `CARDSET` path is no longer used for joining or sending Henry home.

Prepared fields are stat/skill XP, perk records, native inventory including equipment/condition/counts/item extension records, and the remaining four native core fields. The resource block contains six floats; health, stamina, exhaustion/energy and nourishment were read back in the game. World-owned position, story XP, quest knowledge and companion references remain in the received world. Field `137D` is a `04B0` record with an actor reference and timer, **not an ability bitset**; only the observed empty state is accepted for portability. Other soul timers and non-core state are not carried. Active buffs/ability effects and arbitrary character states have not passed comprehensive gameplay validation.

Capture refuses a menu/loading scene, drawn weapons, riding, dialogue, death or combat danger. Unsupported character framing, nonempty world-linked core state and item instance IDs owned elsewhere in the destination refuse the transfer before installation. There is no fallback to an old additive card or an unrelated playline's newest save.

The receive path records a durable `Prepared`/`Installed` load journal. It publishes acceptance only after the engine writes a fresh save from the destination playline and the portable progression/perk/inventory state matches native readback. Resource values are allowed to tick during that verification. **Play my shared world** retries an installed pending load after interruption; original saves and personal snapshots remain available. A generic UI/READY event alone is not acceptance. A short delay is necessary: the UI's load-completed event precedes the end of native loading, when `SaveGameViaResting` can return without writing anything.

`tools/engine/LiveWorldSmoke` uses the production agents, relay, save staging and console/log link with one real private game and one synthetic sender. The fresh join and interrupted-load retry both passed native save readback. This is not a two-person/two-computer test. The staged source-character core/inventory file hash was `74e6f3bb673772f483fc2ebfcbf9f14f82ac74efa0c5afe3dbc9b554b89b6a35`.

A subsequent fresh process exposed native inventory linked-list reordering while equipment loaded. Its header, item identity set and each complete item record were byte-identical; only the record sequence differed. Native acknowledgement now compares items by persistent instance identity while still requiring every item's full state/extension bytes and the inventory header to match. Quantity, condition, equipment or ownership differences remain failures.

The same early-load no-op can affect the initial personal capture. An accepted save request that writes nothing gets one bounded retry; no older file is substituted. Explicit refusals (including unsafe character state) are not retried. The full .NET/Lua suite passed 240 tests after these changes.

## Additional bugs fixed

- Relay admission checked ID availability and inserted peers in separate operations. Concurrent joins could both become player #1. ID/name/host/capacity admission and initial roster publication now share one lock; concurrent admission tests cover distinct IDs and a single host.
- Native verification autosaves appeared newer than the received world, causing repeated resynchronization. The verification file has a recorded digest and keeps the received checkpoint's stamp until a subsequent gameplay save advances it.
- Save capture requires the response's request tag and one changed, stable save file. Ambiguous writes and readback from another playline stop the operation.
- Offers must come from the requested peer/world; chunks must come from that offer's sender. Retransmission is restricted to its original recipient. An expired/refused request can be retried.
- Test agents now explicitly use temporary save/registry/archive directories rather than discovering a developer's real profile.

## Shared simulation remains incomplete

The original native adapter now provides read-only persistent soul and item identities, retaining its complete engine-file hash and startup/signature gates. `ItemManager.GetItem` gains a branded type-5 soul lookup (`kcdusSoul`, `kcdusPersistent`) and a persistent identity on normal item results. The identifiers are raw 16-byte RPG identities rendered as 32 lowercase hex characters, not transient entity IDs or WUIDs. `NativeIdentity` checks support, stale handles and ambiguity before resolving actors/items. The loaded private world yielded 517 native NPC identities.

In a disposable world, `soul:SetState('health', value)` changed NPC health and native readback confirmed it; the old value was restored. `soul:DealDamage(3, 0, ..., true)` returned without changing health on the tested NPC or Henry, including a pause-control attempt. The observed call did not enter the wrapped Lua `BasicActor.Server.OnHit`. These are negative capability results, not proof that ordinary weapon damage is impossible. Forwarding those Lua calls would not provide working shared combat.

Native quest mutation candidates (`StartQuest`, `StartObjective`, `CompleteObjective`, `TriggerCondition`, etc.) exist, but safely intercepting ordinary quest transitions, loot transfers and combat outcomes is not implemented. `HasPerk` and `GetPerks` are absent in this retail Lua API. The source perk table lookup is diagnostic only; presence/serialization does not establish gameplay effects.

Required engine work remains: intercept the native RPG damage/event path, enforce guest suppression before mutation, make host NPC AI target remote actors safely, arbitrate transfers of persistent item instances, prevent independent guest quest transitions, and persist/recover authority with checkpoints. Those behaviors are not enabled or represented as complete.

All engine mutations used harness-owned processes and cloned saves with Cloud disabled. All 260 real save hashes stayed unchanged. The tests, tools and this report contain original project code; locally read game assets and private saves are not distributed. Claude's original checkout and KCD2 remain unchanged.
