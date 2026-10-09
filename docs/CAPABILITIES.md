# Capabilities and what a room really is

## Session 7 status (2026-10-09)

Full multiplayer remains unfinished. A private ordinary-NPC animation factory reached an installed MotionIdle action; spawned presence bodies still have a scope conflict. Exact whole-item native transfers and injury/timed-buff serialization were observed, and a guarded host-local transfer helper is implemented. The helper is not integrated into room durability/UI interception; buff restoration and causal quest/XP authority remain missing. No new gameplay capability or human acceptance is promoted. See [SESSION7-RESULTS.md](SESSION7-RESULTS.md) and [SESSION7-HANDOFF.md](SESSION7-HANDOFF.md).

## Session 6 status (2026-10-09)

Full multiplayer remains unfinished. KCD1 native input on ordinary NPCs is a confirmed no-op; a direct animation bypass faulted and was removed. A safe native diagnostic reader and verified health/death application remain. KCD2 hit watches now check avatar lifetimes and native health/stamina restoration. These reliability changes do not establish complete combat/economy/quests/personal state. See [SESSION6-RESULTS.md](SESSION6-RESULTS.md).

## Session 5 status (2026-10-09)

Full campaign multiplayer and parity remain unfinished. KCD1 now has sequenced, incarnation/term-checked NPC movement, stale-handle protection, pre-mutation reward baselines and verified item grants. Weather presets are Candidate, default off (`kcdus_weather on` on both clients), with no current-profile or scripted-weather preservation proof. KCD2 whole-item put compensation preserves the observed native instance on definitive refusal; unknown outcomes remain unresolved and cannot trigger a speculative refund. KCD2 source wire is now **15**. Neither game has complete native economy interception, universal quest/XP/effect authority or arbitrary complete character restoration. See [SESSION5-RESULTS.md](SESSION5-RESULTS.md).

## Session 4 current status (2026-10-08)

These notes supersede older unavailable/absent statements below. This remains a **partly shared playtest**, not shared simulation or human-accepted multiplayer. VERSION remains 0.1.0. Use the same release tag and payload on every KCD1 computer.

* **Own-menu pause:** Pausing Off also prevents your own ESC menu from stopping your world, while connected with a friend in a world. Requires the supported Windows startup adapter and launcher. Inventory pause is restored on exit. A 10-second native heartbeat expiry restores normal offline menu behavior; do not expect this adapter on Linux/Proton. Shared pause remains available. One retail-process proof, no two-computer acceptance.
* **Shared enemies:** nearest-player ownership uses a 4 m switching margin and 2 s hold. The owner runs native AI and sends movement/weapon state; other copies suppress their brains and puppet movement. Stale state (4 s), ownership expiry (3 s), save/load and disabling sharing release puppets. Movement/suppression/release were read back in one private engine. Sword swings and hit reactions are not mirrored; some guards cannot draw weapons through the script API even in the control. Peer figures are not proven independent native combat targets. NPC/AI authority remains Candidate.
* **Items:** host-decided body and stash takes now include ground PickableItem pickup, replicated drops, named horse saddlebags, shop-linked stashes (40 m), and pack-to-container puts. Takes require a measured Henry-pack gain; puts require a loss. A refused shop take refunds the observed personal debit; wallets, crime, barter and NPC transfers are not a shared economy. Class/count/condition snapshots do not establish exact instance/provenance conservation. Unconfirmed takes are revoked after 20 s or before supported capture, subject to successful native removal. No complete pre-transfer escrow prevents immediate use, and interrupted/rejected operations can lose items.
* **Quest rewards:** a three-second inventory-gain window around supported host quest changes excludes observed loot and subtracts native guest payment. Keys prevent repeat payment within a load. Arbitrary concurrent gains may be misattributed; no durable reward ledger, offline reward backfill, XP sharing or universal scripted reward coverage. Quest mirroring and rewards stay Candidate.

Fresh private-engine rerun: `tools/engine/shared_items_live.py`, **25 passed / 0 failed**, synthetic peer. Screenshot verified Continue before Enter. The owned game was stopped afterwards; **260 original KCD1 .whs hashes unchanged**. See [SESSION4-RESULTS.md](SESSION4-RESULTS.md) and T-57 through T-65 in [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md). All human cases remain pending.

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
| `authority.npc` | candidate when the adapter is loaded, otherwise absent | Persistent soul identity, nearest-player ownership, brain suppression and movement replay exist. Single-engine puppet proof; full two-player targeting/combat unproved. |
| `authority.combat` | engine-verified (**shared outcomes only**) | `soul:DealDamage(stamina, HEALTH, ...)` lowers an NPC's health through the ordinary path and a lethal one kills it, read back in the private engine (the earlier "no effect" came from a probe that passed health 0). Health and death of the NPCs near each player are shared between copies of the same shared world. Session 4 adds Candidate owner/puppet movement; full remote targeting, swings, noticing and side effects remain unproved. [Details](#shared-fights-and-loot) |
| `authority.loot` | engine-verified (**single-engine item paths**) | A take is the loot screen moving an item (the engine does it); the host decides, the item is taken back off a guest on "gone" or silence, the decision is journalled first. Session 4 also observed ground pickup/drop, saddlebags, shop stock and puts. NPC transfers and complete economic authority remain uncovered. |
| `authority.quest` | candidate | One-way open/base quest adapter, switched on with shared fights and loot. One objective native readback proved; full quest effects/rewards/variables are not authoritative. |
| pausing (not a handshake capability) | engine-verified (**a friend's pause holds my world**) | The pause menu's open/close events reach Lua; `t_scale` 0.001 holds the world and a frame-counted in-game lease lets go by itself. A player's OWN menu still pauses their own game natively (see [Pausing](#pausing)). |
| `character.timers` | absent | Timed effects, cooldowns and injuries are not carried across. |

## Room modes, in plain words

The relay (and each agent) computes one of these and shows it to the player. It is never worded as "shared" unless every capability required for a shared simulation is at least
`integration-verified` on both sides.

| Mode | What you are told | When |
|---|---|---|
| **Refused** | The reason, in a sentence, and what to do. | Another game, another contract or wire version, a different or unverifiable mod payload (Lua pak), or no handshake at all. |
| **Presence** | "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active (â€¦)." | No authority capability is engine-verified on both sides (an old build, or no shared-outcome support); **or** the two installs have different other mods. |
| **Partial** | "Room: partly shared (not every authority capability is verified: â€¦)." | At least one authority capability is engine-verified on both sides and the rest are not. |
| **Shared simulation** | "Room: shared simulation." | Every capability in `SharedSimulationRequired` is integration-verified on both sides. **This build can never reach it.** |

Two copies of this build are **Partial**: fights and loot are shared outcomes (engine-verified), NPC authority is not, and nothing is integration-verified (no two-computer run yet). It can never say "shared simulation". With different other mods the room is still **Presence**.

### Different DLC or mods

Other mod differences cap the room at Presence. DLC differences use the least-DLC rule described below. Both players are told ("<name>'s game has other DLC
or mods: worlds and characters will not be moved between you"), and **no world or Henry is requested or served between them**. Install the same DLC and mods on both to share a world.

## What decides a payload match

Both computers must have the same Lua pak (`kcdus.pak`), compared by SHA-256 in the handshake. The agent, the native adapter and the game engine hashes are also sent and shown in the
log so a report can say exactly what was running; only the Lua pak and the content profile are enforced. A build with no pak is refused ("could not be verified"). Two development builds with a
different pak are refused ("different mod payloads"). `--dev-allow-unverified` on the relay exists for development only.

## Not done, on purpose

These are recorded rather than faked. Full list with reasons: [KNOWN-LIMITS.md](KNOWN-LIMITS.md) and [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md).

* Shared NPC AI: where NPCs walk, whom they target, whether they notice a player; items on the ground, saddlebags and shops; quest rewards and spawns (the whole point of a "shared simulation").
* A live, consistent world and character capture barrier, and promotion of a winning checkpoint.
* Non-core character state: timers, selected-perk effects, mounted and world-linked state.
* Linux and Proton: the runtime adapter is not proved there; no Linux package is built from this branch.
* Stopping a player's OWN pause menu from pausing their own game (the engine pauses natively and the UI resume node does not undo it; see Pausing).
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

## Shared fights and loot

Proved in a private real engine with a synthetic second player (`tools/engine/shared_outcomes_live.py`, 14 checks, and `shared_outcomes_live2.py`, 12 checks): the ordinary damage path with readback, a friend's kill killing this copy, no echo of applied damage, corpse and stash inventories read, a guest's take reported by position for a stash (stashes have no names), the host deciding an ask against its own copy, and "gone" taking the item back. Automated: 317 tests of the agent, the relay and the Lua modules against the stubbed engine.

* **Active only while it is honest.** The option is on (default), both players are in a world, and a friend is in the SAME shared world (each machine announces the id of the world it has loaded; two copies of one save have the same named NPCs, bodies and stashes, two different saves do not). Otherwise every copy of the world stays its own and the launcher says nothing is shared.
* **Fights.** Each machine watches the NPCs near its own player (18 m) and reports health they lost or a death. The others apply it to their own copy of that NPC through the ordinary damage path with **no attacker**: nobody is credited with a kill they did not make, no crime is raised, the NPC's AI is not provoked. Damage is additive and order-free, so two players fighting one enemy cost it both players' blows in both copies. Fights between NPCs that no player is near are nobody's to share.
* **Loot.** The host decides. A guest's take is already done by the loot screen (that is how it works), so the guest asks; the host answers ok (it removes the item from its copy too and tells the other guests) or gone (somebody was first), and on gone the item is taken back off the guest. An ask the host never answers is taken back after 20 s and before a world save: **an unconfirmed item is never kept, and never travels into a save**. A lost answer loses an item; it cannot duplicate one. The host's decision is written to a hash-chained journal before the answer is sent; a repeated ask gets the same answer, and an ask interrupted by a restart is never granted a second time.
* **Unproved or uncovered:** full NPC targeting/attack animation, crime/theft and NPC transfers, full quest spawns/XP, durable cross-player economic conservation. Session 4 adds Candidate movement and item/reward paths, described above.
* **Switch:** the Multiplayer tab (Shared fights and loot), the browser settings, `kcdus_shared on|off`, `--no-shared-outcomes`.

## Pausing

Proved in the private engine: the game's pause (ESC) menu raises UI events that reach Lua (`MP_MenuWatch` flow graph); the menu pauses the world natively (world time stands, frames go on); `t_scale` 0.001 holds a world while Lua timers still fire (about once a real second), so a frame-counted lease inside the game lets go by itself when the agent stops renewing it (a crash, a lost link or a vanished friend can never leave a world frozen for good). At `t_scale` 0 no timer fires and `os.time()` stands still, so there could be no such lease: the mod never uses 0.

* **Shared (default):** a friend's open pause menu holds MY world until they are back (their agent repeats "my menu is open" every two seconds; silence for six seconds, a menu open for 15 minutes, leaving the session, or this option off lets go at once). Only the pause menu counts: a friend's inventory, dialogue or loading screen never holds anyone.
* **Off:** a friend's menu never holds my game. Choose it in the Multiplayer tab (Pausing), the browser settings, `kcdus_pause_mode shared|off`, or `--no-shared-pause`.
* **Historical Session 3 limit (superseded by the Session 4 Windows adapter):** stopping a player's OWN menu from pausing their OWN game was unavailable. The game pauses natively when the menu opens and the UI's ResumeGame node does not undo it (tested live); undoing it would need a native hook that has not been written for the retail engine. In KCD1 each player's world is a separate copy, so a player's own pause never affects anyone else's game unless the shared pause option makes a friend's menu hold it.

## Session 3 (2026-10-08): what changed against the Session 2 notes above

The Session 2 paragraphs below say shared combat, shared loot and a safe shared pause were unavailable. That was true when they were written; this section supersedes them for KCD1: the "no effect" damage result was a probe error (stamina 3, health 0), health damage and death work through the ordinary path, a crash-safe pause lease exists (`t_scale` 0.001 plus a frame-counted lease), and corpse/stash loot is host-decided. What is still unavailable is listed under "Not shared" above. No capability has human acceptance and nothing was played by two real people.
