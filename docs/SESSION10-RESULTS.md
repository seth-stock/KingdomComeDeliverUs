# Session 10 (2026-10-11): durable economy recovery, causal quest XP, character restoration, shared enemies

Development build. VERSION stays KCD1 0.1.0 / KCD2 0.45.0 (the user's call); KCD1 wire protocol 4, KCD2 wire 16. Nobody has played this with two people: every
"live" line below is ONE private real game with a synthetic second player and a disposable save. No Proton gameplay, no soak gate.

## Decompilation (done once, kept)

All 37 engine images (KCD1 `WHGame.dll`, retail KCD2 `WHGame.dll`, the Modding Tools gameplay modules and the Cry support modules) were exported with Ghidra
(`E:\Ghidra`, bounded RAM: 6 GiB Java heap, 10 GiB job cap): 910,489 functions attempted, 16 failed exports, 160,023 functions with decompiler warnings. The
databases, C shards, symbols, strings and cross-references live in the ignored `_work/ghidra-full-session9` (17 GB); nothing was re-decompiled. Tools:
`tools/engine/ghidra/` (controller, bounded switch repair, unwind preparation, a streaming query, and `flat_index.py`, which writes a greppable text index per
shard). Decompilation is evidence for investigation, never proof: every adapter entry below was checked in the live engine.

## What is proved in the live engine (KCD1, private profile, synthetic peer)

| Capability | Evidence |
|---|---|
| **Own pause menu does not pause** (option "off", a friend in the world) | Native gate on `CCryAction::PauseGame` (RVA 0x5ba0e8, source 7 = ESC menu). Retail-mode game (no `-devmode`): the menu opened and the world clock/status lines kept running; the pause came back 10 s after the agent's last heartbeat; a resume always runs. |
| **Quest XP, exact and causal** | The adapter observes every native stat/skill XP application with its cause. `QuestSystem.CompleteObjective` (what a guest's quest mirror calls) pays the objective's immediate rewards natively with `C_QuestObjectiveChangedCause` (money and XP): so objective rewards need no second payment. A quest's COMPLETION cannot be performed by a guest's mirror: its XP (cause `C_QuestStateChangedCause`) is forwarded by the host and a guest applies it ONCE through the engine's own quest-event factory and dispatcher, keyed durably, verified by native readback (`tools/engine/native_quest_xp_live.py`, 7/7: applied once, repeat changes nothing, a non-quest cause and another soul's identity are refused, a cause naming no real quest is refused). |
| **Character restoration** | Henry's whole buff list (injuries, bleeding, poison, drink, alcoholism, hunger, perk/place effects; engine-serialized records, elapsed timers) and the eight personal accumulators move with him; world-owned live state stays. Five buffs added and saved in one private world were spliced into another save and read back natively: 5/5 (`tools/engine/buff_readback.lua`). Every record is validated (framing, unique ids, finite non-negative timers, bounds) before it can enter a save. |
| **Shared enemies** | One owner per NPC (nearest player, 4 m margin, 2 s hold; an NPC in a fight with its owner stays with it); the others' copies are puppets (brain off, position, facing, walk/run clips, weapon draw/holster, brain back on release/mine/stale/mode off/save/load). Live: 24/25 on the first run; the weapon check failed only for a guard who cannot draw by script even with his own brain on. |
| **Items** | Ground items, drops, saddlebags, shop stock (with the price refunded on a refused take), puts: `tools/engine/shared_items_live.py`. |

## Durable economy recovery (both games)

A guest's take has already moved the item into its Henry when the host is asked; the game forgets its probation list on every load and crash. The guest's agent
therefore journals every take on disk (KCD1 `GuestCustody`, KCD2 `JoinerCustody`; hash-chained `OperationJournal`) BEFORE the host hears of it, with the take's
world time and any shop price; the host journals its decision by the same id; the guest's receipt (kept / returned / reverted / partial) completes it.
After a reload or crash the guest re-asks unanswered takes with the SAME id (the host replays its recorded decision, or answers "gone" when interrupted before
deciding: never granted twice) and settles answered ones: a take newer than the loaded save is not in it; otherwise "ok" keeps it and "gone" takes it back off Henry
(refunding a shop price). A granted take the guest did not keep is put back, once, into the host's and the other guests' copies. Nothing unanswered is ever guessed.

## Honest limits (not claimed)

* **Independent damaging combat:** every fight is real in the world of the player the NPC fights; the others see the NPC move, face and hold its weapon, and their
  blows hurt it everywhere (health/death are shared). An NPC does not damage two players in two different worlds at once. The adapter's mirrored attack at the
  owner's presence body (cosmetic, never damaging) stays behind the owned-profile switch. Hit/block reactions per participant, independent targeting of both players
  and kill credit are not implemented.
* **Economy:** world time and class counts cannot prove item custody; there is no persistent item-lineage ledger, no consistent crash checkpoint, and consumption,
  barter and crime are not one authoritative ledger. Receipts have no acknowledgement protocol (they are retried and idempotent).
* **Quest effects:** spawn and script effects of a quest are not mirrored; a guest-caused quest step is not fenced; XP receipts are keyed per load/world.
* **Characters:** a Henry with an active world-referenced core soul state (0x137D/0x04B0: a world actor reference and a timer, e.g. a companion) now MOVES: the reference
  names an actor of the source world, so it is classified as world-owned and the destination's own value stays (unit-tested; no source save with an active one was
  available to load live). Other world-linked state (companions, quest knowledge, position) was never carried. Clocks are carried as elapsed time, not converted.
* **KCD2:** wire 16 custody is tested (Lua synthetic suites and agent tests) but NOT run in the live KCD2 game this session; KCD2 quest XP has no equivalent of the
  KCD1 observer yet (KCD2 reward windows measure inventory only). KCD2's character splice carries the whole joiner `player_henry` soul record (persistent buffs and
  states included) as before.
* **Linux/Proton:** the native adapters (pause gate, XP, buffs) are Windows launcher-injected DLLs; the Linux archives carry the agents and Lua only. Not run under
  a real Proton.
