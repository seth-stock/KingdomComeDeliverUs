# Session 10 (2026-10-11): durable economy recovery, causal quest XP, character restoration, shared enemies

Development build. VERSION stays KCD1 0.1.0 / KCD2 0.45.0 (the user's call); KCD1 wire protocol 4, KCD2 wire 17. Nobody has played this with two people: every
"live" line below is ONE private real game with a synthetic second player and a disposable save. No Proton gameplay, no soak gate.

The first half of this file is the first Session 10 pass; **"Continuation"** at the end is the second pass (KCD2 quest XP, the economy audit ledger, the KCD1
damage mirror, the quest-effect census) and supersedes the "Honest limits" below where they differ.

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
* **KCD2:** wire 16 custody is tested (Lua synthetic suites and agent tests) but was NOT run in the live KCD2 game in the first pass. KCD2's character splice carries
  the whole joiner `player_henry` soul record (persistent buffs and states included) as before. (Quest XP: see Continuation.)
* **Linux/Proton:** the native adapters (pause gate, XP, buffs) are Windows launcher-injected DLLs; the Linux archives carry the agents and Lua only. Not run under
  a real Proton.

## Continuation (same day, second pass)

### KCD2 quest XP: measured natively, given as a difference (live in one real game)

* **Observer** (`native/KCDMP/xpread.cpp`, pipe op 9): the plugin wraps vftable slot 3 of `C_StatXPEffect` and `C_SkillXPEffect` (found by RTTI and verified by
  the slot function's own `C_SoulValueEffect<...>::Apply` trace string; any miss leaves it off). The engine's Apply runs unchanged; afterwards the plugin records the
  effect's own fields (soul id, stat/skill id, requested amount, applied amount) and classes the row as a **quest's reward** when the call stack runs through
  `C_RPGQuest::OnStatXPReward` / `OnSkillXPReward` (located by their trace strings; matched by the function's primary entry because the compiler splits them into
  chained unwind fragments). Each row is stamped with the **quest change it ran under** (the outermost recorded State change, `root`) and the **Apply tag** (the
  host's change seq on a joiner).
* **Agent** (`QuestXp.cs`, `GameBridge.QuestXp.cs`, LootHost kind 13, **wire 17**): the host groups quest-reward rows by `root`, waits 1.5 s of quiet, and sends one
  `QuestXp` text per shared change. The joiner measured what **its own engine** paid for the same change (rows carrying that apply's tag, or landing in its
  window) and gives only the **difference**, once, through `KCD2MP_QuestXpGive` -> the engine's own `AddStatXP`/`AddSkillXP`. A change the joiner already had (the
  apply moved nothing) is never settled; storyProgress (stat 8, world-owned) and unknown slots never cross; amounts and slots are bounded.
* **Live** (KCD2 dev build, throwaway slot, `tools/k2xp_live.py`): pulsing the active quest State `hledaniPsaActive` paid houndmaster 23040 (=180 XP), survival 6400
  and storyProgress 6400 through the native reward nodes; the observer classed all three as quest rewards (`cause` 2, 2, 1), attributed them to the detector's change
  (`root=1`) and to the apply (`tag=777`); the receipt `KCD2MP_QuestXpGive` then produced exactly two script rows, 23040 and 6400 (requested == 128 x XP), and the same
  key again produced none. Also learned live: `AddStatXP(name, x)` requests exactly `x * 128`. Unit-tested: KCD2 client 1,381 + relay 74, Lua parity suite 65/65 (P45-P61); KCD1 519.
* **Not proved:** a quest XP receipt between two machines (the host half and the joiner half were each exercised, not connected); a quest reward paid by something
  that is not a State change (a timer, a dialogue effect) has no `root` and is counted, not forwarded; a joiner's own quest completions are not forwarded to the host
  (the host's engine pays its own when the mirrored request is applied there); XP is not durable across a reload before the game saved it, like the game's own.

### Economy audit ledger (both games; observation, not enforcement)

Every change of a player's pack that the existing pack watchers (`kdcmp_containers.lua` pass in KCD2, `loot.lua` scan in KCD1) can attribute is now an event
(`w134_econ` / `KCDUS|ECON`): **take, put, barter-buy, barter-sell, loss, gain, quest reward, crime** (a fine WO-139 judged is claimed by the watcher so it is named, not a
plain loss), as a signed quantity per item class with the world time. Each agent appends them to a durable hash-chained file (`economy-ledger.jsonl`, `EconomyLedger.cs`,
a torn final write dropped, a tampered record refused). `Residual(before, after)` names the classes whose pack changed by a quantity the ledger cannot explain; that is what an
acceptance run compares (unexpected loss fails). Live (KCD2, throwaway slot): five apples created asynchronously produced five gain entries, a deletion produced a loss entry,
and the watcher's own counts matched the pack (6, then 5). Not live: shop barter and a judged crime (Lua synthetic only); consumption by actually eating did not complete in the
test world (`EatItem` returned and the apple stayed), so it is covered as "loss" only.
**Not claimed:** this is an audit, not an authority: it never moves, refuses or restores anything, a gain and a loss of the same class inside one watch interval net out, and the
shared holders' takes/puts stay journalled by the host (`DurableLootLedger`) and the joiner (`JoinerCustody`) as before.

### KCD1 damage mirror: an enemy's blow can hurt every player standing in front of it (an approximation)

`npcs.lua` (`NPCHIT` / `NPCHITIN` / `NPCHITAPPLIED`, agent `NpcCoordinator.GameHit`/`PeerHit`, no wire bump: an agent without it ignores the messages): when a blow of an NPC
this game owns lowers its Henry's health (the NPC was attacking, engaged on him, within 4 m), the owner reports the health actually lost. The other games hear it
(only from the NPC's owner as the host named it, once per blow, at most 8 a second) and, if their copy of that NPC is a current puppet of that owner and their Henry stands
inside its reach (2.8 m, +-75 degrees in front, 1.6 m up/down), take the same loss through the engine's own `soul:DealDamage`, read back.
**Live** (KCD1 private profile, disposable NPC as a synthetic owner's puppet, `tools/engine/damage_mirror_live.py`): `DealDamage(0, 5, nil, true)` lowers HENRY's health by 5;
the engine's forward at yaw z is `(-sin z, cos z)` (`GetDirectionVector(1)`, equal to the player's head direction); a blow of 12 took exactly 12 from Henry 1.5 m in front of
the puppet and logged `NPCHITAPPLIED ... 12.0|12.0|1`; a puppet facing away (`behind`), 6 m away (`out-of-reach`) and the switch off (`NPCDMGMIRROR|0`) took nothing. That run
was 7/8: the one failure (the same blow applied again after the puppet had been released and taken again) is fixed since by deduplicating per owner and NPC rather than per puppet
object, and is unit-tested; it was not re-run live.
**Not claimed:** independent combat. The receiving Henry's block, parry and armour are NOT applied (the owner's measured loss is what travels); a blow that missed the owner reports
nothing; the owner side (health watch with a real attacking NPC through the engine adapter) is unit-tested but was not exercised against a real attack; no two-player run.

### Quest spawn and script effects: what the existing mirror covers (census, no new mechanism)

`tools/Audit-QuestEffects.py` -> `docs/SESSION10-QUEST-EFFECTS.md` (KCD2, read-only census of 21,648 quest graph files, 125,941 nodes). 10.6% of nodes are **consumers** (layers = spawns,
behaviours, contexts, presets, fights, ownership): they run when their State changes, and the quest sync applies the host's State change on the joiner through the engine's own port
trigger, so the same consumers run natively there. 7.3% are **local triggers** (areas, distances, deaths, interactions, dialogue lines): they fire on the machine that observes
them and their State change is then mirrored. 4.2% are **per-player** effects (rewards, quest items, buffs, payments, autosaves, cutscenes) that each machine runs for itself.
That is a reading by class name of what the mirror covers by construction, not an observation of two worlds: no spawn or script effect was verified on two machines, and a
guest-caused step is not fenced. KCD1's quest data was not censused.

### Dev tooling added

`tools/k2live.py`, `k2pipe.py`, `k2xp_live.py`, `k2restart.py` (KCD2 dev build, throwaway slot only; `k2restart` only ever stops the process it started, after checking its command
line), `tools/engine/damage_mirror_live.py` (KCD1 private profile). A KCD2 live run writes diagnostics into the saves folder (`faderprofiler`, `testresults`) and a throwaway slot;
before and after hashes of every real save and profile file matched (backup in `SAVE-BACKUP-20261011-prelive`).
