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
| `authority.quest` | candidate | Opt-in, one-way open/base quest adapter. One objective native readback proved; full quest effects/rewards/variables are not authoritative. |
| `character.timers` | absent | Timed effects, cooldowns and injuries are not carried across. |

## Room modes, in plain words

The relay (and each agent) computes one of these and shows it to the player. It is never worded as "shared" unless every capability required for a shared simulation is at least
`integration-verified` on both sides.

| Mode | What you are told | When |
|---|---|---|
| **Refused** | The reason, in a sentence, and what to do. | Another game, another contract or wire version, a different or unverifiable mod payload (Lua pak), or no handshake at all. |
| **Presence** | "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active (…)." | No authority capability is engine-verified on both sides; **or** the two installs have different other mods. |
| **Partial** | "Room: partly shared (not every authority capability is verified: …)." | At least one authority capability is engine-verified on both sides and the rest are not. |
| **Shared simulation** | "Room: shared simulation." | Every capability in `SharedSimulationRequired` is integration-verified on both sides. **This build can never reach it.** |

This build is always **Presence**. That is the honest answer, not a bug.

### Different DLC or mods

Other mod differences cap the room at Presence. DLC differences use the least-DLC rule described below. Both players are told ("<name>'s game has other DLC
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

## Session 2 continuation: 2026-10-07

This remains a playtest build. No capability has human acceptance, and the two games do **not** yet have equivalent shared simulation. VERSION remains KCD1 0.1.0 / KCD2 0.45.0; distinguish downloads by release tag, commit and SHA-256, not VERSION alone.

* KCD2 protocol 13 refuses older agents/relays. Durable host body take/put and loose-item decisions are journaled before the Lua mutation and before the reply. Interrupted unknown mutations are quarantined, never retried automatically. `HostDecisionComplete` means the host decision is durable, not that the recipient received/kept an item. Loose-item replies now carry the same connection/load scope as body replies. Unknown loose items are refused; successful removal requires entity readback.
* KCD2 checkpoint barrier is **Candidate, default off**. Host console: `mp_checkpoint_mode candidate` (off reverses it for this agent session). It uses a fixed relay-verified participant roster, authority incarnation and checkpoint UUID, acknowledged native holds, loot settlement, a verified host save, bounded/acknowledged guest character uploads, matching save MD5s, chest ledgers, cross-participant item-instance checks, durable prepared artifacts, commit acknowledgements, and manifest publication before release. Disconnect, roster/load change, missing data or the 90-second deadline aborts. The native hold expires 20 seconds after the agent's last refresh. A guest keeps its paired personal artifacts and receipt; it does not yet receive a full host checkpoint archive for offline reconciliation.
* Checkpoints still need proof that native saves work while held and that **all** inventory/quest/combat mutations are excluded. The Lua pickup gate is not complete native inventory interception. Ordinary autosaves still follow the existing path; interrupted capture is not automatically promoted. Offline checkpoint selection/preparation remains separate from live reconnect promotion.
* KCD1 quest mirror is **Candidate, default off**. Console `kcdus_quest_mode candidate` on both test clients enables one-way host snapshots; `off` disables it. Identifiers come from the installed quest tables. Only open, base-game main/side/activity quests are eligible. DLC, rails/mixed scenes, local system/random events and unknown objectives are vetoed. It reads native state before applying and after mutation, replays completed objectives without repeating the call, refuses inactive objectives and unverified quest completion, and never treats cancel/deactivate as success. Objective reward/spawn side effects and full quest/variable coverage remain unproved.
* Private KCD1 engine evidence: `q_revenge/findVonAulitz` changed from started/not-completed to completed through the real adapter; repeated application read back completed. This proves one objective binding, not full quest authority. `t_scale=0` stopped observed calendar progression; it was restored to 1. Production shared pause remains **Absent**: there is no proved crash-safe engine lease or menu-pause interception. A Lua timer cannot safely release a freeze that stops its timers.
* KCD1 enemies/combat decision: **do not enable shared combat/NPC drive**. Existing damage probes did not prove the ordinary hit path; presence NPCs are not host-authoritative combatants. Damage interception, AI suppression/drive, targeting all players, one death/reward revision and a shared inventory ledger remain implementation gates. Do not replace this with cosmetic attacks or health setters.
* KCD2 shared pause and its off option remain Candidate until the two-computer tests pass. DLC uses the host's lower-content world: richer guests are allowed and extra DLC quest mirroring is gated; poorer guests cannot receive an existing richer host save. Disable extra DLC in Steam and restart as instructed. The mod does not rewrite a DLC save or deactivate Steam entitlements. Different other mods still cap the room at presence and block world/character moves.
* Linux packages are experimental. A successful WSL build/fake Steam test is not proof of Proton gameplay. KCD1's Windows startup adapter paths are not proved under the native Linux agent. No Linux engine feature is upgraded by rebuilding a tarball.

Human acceptance is pending. KCD1 ESC-menu Multiplayer entry and its Status/Host/Join/Leave/Game world/Story/Keys/Settings/Back page were visually observed in the disposable loaded game. That does not prove buttons in a connected session. KCD2 pause-menu behavior still needs verification. Run the new test cases on disposable copies, record release/commit hashes and logs, and leave failed capabilities Candidate. No real saves or firewall rules were changed by this continuation's guarded probes.
