# Capabilities and what a room really is

Deliver Us (KCD1) development build. Every claim here has a level, and the level is what the room handshake sends to the other computer. A friend's computer and yours
agree on the **lower** of the two levels for each capability, so neither side can promise more than the other can do.

| Level | Meaning |
|---|---|
| `absent` | Not implemented, or not possible on this install (for example the native adapter is not loaded). |
| `candidate` | Code exists. The engine entry path is not proved. Never counted as working. |
| `engine-verified` | Seen working in one real game process (a disposable profile, sometimes with a synthetic second player). |
| `integration-verified` | Exercised end to end between real agent and relay processes by automated tests. |
| `human-accepted` | Two or more people played it and signed it off using [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md). **Nothing is at this level yet.** |

## The table for this build

| Capability | Level | Evidence / limit |
|---|---|---|
| `presence.bodies` (you see each other) | engine-verified | Ordinary NPC bodies follow peers. [ENGINE-INTEGRATION-20261006.md](ENGINE-INTEGRATION-20261006.md) |
| `presence.locomotion` (walk/idle/run) | engine-verified | Full-body looping clips on ordinary NPCs. One real game; no two-computer run. |
| `presence.outfits` | engine-verified **only** with the startup adapter loaded on the supported Windows engine, otherwise absent | Equipped classes and condition matched native readback; plate armour and helmet rendered. |
| `character.personal` (bring my own Henry) | engine-verified on the supported engine, otherwise candidate | Native core and inventory replacement, readback accepted. Timers, perk effects, mounted and world-linked state are refused or not restored. [ENGINE-INTEGRATION-20261007.md](ENGINE-INTEGRATION-20261007.md) |
| `identity.participant` | integration-verified | Each install keeps a P-256 key; the relay binds the participant id to the first key that proves it, across restarts. Tested against hijack, replay and wrong-key attempts. |
| `checkpoint.barrier` | candidate | The immutable checkpoint store exists and is tested. It is **not** wired into a live consistent capture. |
| `authority.npc` | candidate when the adapter is loaded, otherwise absent | Persistent soul and item identity can be read. No NPC is driven from the host. |
| `authority.combat` | absent | Damage calls had no observed effect; no interception. |
| `authority.loot` | absent | No shared loot or economy. |
| `authority.quest` | absent | Quests are polled for the join-or-stay question; they are not mirrored. |
| `character.timers` | absent | Timed effects, cooldowns and injuries are not carried across. |

## Room modes, in plain words

The relay (and each agent) computes one of these and shows it to the player. It is never worded as "shared" unless every capability required for a shared simulation is at least
`integration-verified` on both sides.

| Mode | What you are told | When |
|---|---|---|
| **Refused** | The reason, in a sentence, and what to do. | Another game, another contract or wire version, a different or unverifiable mod payload (Lua pak), or no handshake at all. |
| **Presence** | "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active (…)." | No authority capability is engine-verified on both sides; **or** the two installs have different DLC or other mods. |
| **Partial** | "Room: partly shared (not every authority capability is verified: …)." | At least one authority capability is engine-verified on both sides and the rest are not. |
| **Shared simulation** | "Room: shared simulation." | Every capability in `SharedSimulationRequired` is integration-verified on both sides. **This build can never reach it.** |

This build is always **Presence**. That is the honest answer, not a bug.

### Different DLC or mods

Two installs whose DLC or other mods differ are still allowed to play together as presence, but: the room is capped at Presence, both are told ("<name>'s game has other DLC
or mods: worlds and characters will not be moved between you"), and **no world or Henry is requested or served between them**. Install the same DLC and mods on both to share a world.

## What decides a payload match

Both computers must have the same Lua pak (`kcdus.pak`), compared by SHA-256 in the handshake. The agent, the native adapter and the game engine hashes are also sent and shown in the
log so a report can say exactly what was running; only the Lua pak and the content profile are enforced. A build with no pak is refused ("could not be verified"). Two development builds with a
different pak are refused ("different mod payloads"). `--dev-allow-unverified` on the relay exists for development only.

## Not done, on purpose

These are recorded rather than faked. Full list with reasons: [KNOWN-LIMITS.md](KNOWN-LIMITS.md) and [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md).

* Shared NPC, combat, loot, quest and economy authority (the whole point of a "shared simulation").
* A live, consistent world and character capture barrier, and promotion of a winning checkpoint.
* Non-core character state: timers, selected-perk effects, mounted and world-linked state.
* Linux and Proton: the runtime adapter is not proved there; no Linux package is built from this branch.
* Pause-menu tab in a live session (the main-menu tab is engine-observed).
* Two-computer, four-player and multi-hour soak testing.
