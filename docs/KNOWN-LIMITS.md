# Known limits of Kingdom Come: Deliver Us 0.1.0

Marks: **(seen)** observed working in the real game on one machine · **(tests)** covered by offline tests only · **(not seen)** built, never observed.

## Development branch status

Walking and peer equipment replication have been implemented on
`codex/coop-reliability`. Equipment requires the rebuilt Windows launcher and
its adapter for the verified retail engine. Offline inventory/progression
preparation is available; regular personal-Henry joins now stage native saved core/inventory state and require native readback. Active world-linked state, item ownership conflicts and unsafe capture states refuse the move. These changes
are not a complete shared-simulation release. See
[the current implementation status](IMPLEMENTATION-STATUS.md).

## What the room tells you (2026-10-07)

Every room announces what it is. This build is **presence only**: the launcher and the game say so, with the words "NOT active" for shared NPC, combat, loot and quest authority.
Two computers with a different `kcdus.pak` are refused with a reason; two with different DLC or other mods are admitted as presence and **no world or Henry moves between them**.
Details: [CAPABILITIES.md](CAPABILITIES.md). What people must test: [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md).

## Remaining limits

* **No shared simulation.** Each player's game simulates its own copy. Enemies, NPCs, loot, quest state and combat are not shared; a friend does not see the host's fights. What *is* shared is the world itself: a friend can take the host's save (even a 100-hour one) and each player keeps their own copy that they play alone or together, reconciled when they meet again: [SHARED-WORLDS.md](SHARED-WORLDS.md).
* Walking/idle on ordinary peer NPCs has been observed. Other poses and two-computer behavior require playtesting.
* Equipment classes and condition synchronize with the Windows adapter. Unsupported engines retain default clothing; hair/dirt synchronization is incomplete.
* **No quest mirroring.** The host's quest progress decides what the friend is *asked*; it is not copied onto the friend. A friend who joins a stretch is brought
  beside the host, nothing more.
* **Loading a received world is done by the mod**: a menu graph starts the game's own load. Offline character preparation creates a separate edited save; regular received-world transfers retain the five-playline requirement.
* **No voice, no dice minigame sync, no horse sync, no NPC drive, no damage between players.**
* **Time:** only the time of day is shared, and only **forward**: the game ignores a request to set an earlier time, so a friend whose clock is ahead of the host's keeps their own, and one who would need to skip more than 12 hours does not sync. Time does nothing while the game has paused it (the prologue). Weather and the sky follow each game's own state.

## What has not been observed

* **Anything with two real players on two machines (not seen).** The relay, the agent and the mod were exercised with a stand-in second player (`KcdUsBot`) on one machine, and with 103 offline tests (tests).
* **The join-or-stay question (seen):** the agent's own text rendered centred on the screen, a **real F11 press** in the game was read by the agent, the answer reached the (bot) host, and the player was brought 100 m to stand beside the host's body. F12, the 45-second default and the tether were exercised by tests only.
* **The firewall rule on a second machine (not seen).** It is written to exclude loopback so it cannot block the agent; a computer elsewhere on the network being refused has not been tried.
* **The installer's interactive wizard (not seen).** It compiled and its payload was smoke-tested; a silent install against a stand-in game folder was run (see `INSTALLER.md`).
* **Long sessions.** The soak is 12 minutes on one save (`SOAK.md`); a four-hour session is not observed.
* **Other game builds.** Written and tested against 1.9.8 (404-504czj4), the last retail build. Older builds may differ.

## Things that behave in a way you may not expect

* **The menu is a live scene.** The game starts a world behind its main menu; the mod tells the menu from a world by where the camera is (the menu's camera is hundreds
  of metres from the player). A cutscene whose camera stays more than 200 m from Henry for more than 1.5 s is read as "not in the world" until it ends; positions stop being sent then.
* **Script timers die on every load.** The mod re-arms its loop from the player's load hooks and from the agent's traffic; the first second after a load may be quiet.
* **The remote console is fiddly.** The engine drops bursts and serves only 21 commands per connection; the agent paces its lines, packs records and opens a new connection every 16 commands. A very busy room (4 players) is about 40 lines a second and was not stressed beyond the bot test.
* **The game must run with Steam up first,** or it stops at a licence dialog (this is the game's own behaviour).
* **Windows Firewall may show a prompt for `KcdUsAgent` / `KcdUsRelay` the first time.** Only the host's relay needs inbound access.
* **Mods and achievements:** the game tags saves made with mods (`This save has been modded`); they load without the mod. Whether the mod affects Steam achievements was not checked.
* **Quest gating is a judgement** from the game's data and published guides; the game has no flag for "on rails" (`quest-gating.md`). A mixed quest asks once for the whole quest, not once per scripted stretch.
* **DLC quest chains** are classified from the wiki (A Woman's Lot as locked-in, From the Ashes as open management). Someone without the DLC is never asked about it: a quest they cannot start does not show in the host's log either.

## Developer tools that touch your game (not part of the installer)

`tools/perf/soak.py` and `tools/perf/gamedrive.py` start the game, drive its menu with synthetic key presses, and need a **throwaway day-0 save** as the newest save;
the soak refuses (kills the game) if the loaded save is not day-0. Do not run them against a real game.

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

Human acceptance is pending. Main-menu evidence does not establish pause-menu behavior in both games. Run the new test cases on disposable copies, record release/commit hashes and logs, and leave failed capabilities Candidate. No real saves or firewall rules were changed by this continuation's guarded probes.
