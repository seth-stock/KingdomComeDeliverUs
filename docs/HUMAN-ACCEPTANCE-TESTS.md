# Human acceptance tests: Deliver Us (KCD1)

For two to four people, each on their own computer, each with the **same installer** from the same download. Nobody has run these yet. A pass here moves a capability to
`human-accepted` in [CAPABILITIES.md](CAPABILITIES.md); a failure is a bug report, not a reason to skip the step.

**Rules for every test**

* Use a throwaway save on every machine. Never your only copy of a long game. Steam Cloud paused for the test if you can.
* Write down what you *saw*, with the time ("Hans appeared 4 m away and glided"), not "it worked".
* Each test says what to do, what you should see, and what a failure looks like. If the result is *not* in the "Expect" column it is a failure, even if the game did not crash.
* This build is a **presence** build. Tests T-15 onward check that it says so, and that nothing pretends to be more. Enemies, loot, quests and combat are **not** shared; do not report that as a bug.
* The host machine runs the relay. Friends need the host's address and port (`100.x.x.x:7788` with Tailscale).

**Collecting evidence** (send these after any failure, and after T-30)

* `%LocalAppData%\KCDUS\logs\agent.log` on every machine.
* The game's `kcd.log` in the game folder, lines starting `KCDUS|`.
* A screenshot of the launcher's status area.
* Remove public IP addresses before sending.

## A. Install and identity (each machine, 10 minutes)

| # | Do | Expect | Failure looks like |
|---|---|---|---|
| T-01 | Run the installer. Check `Get-FileHash` of the installer against `SHA256.txt`. | Hashes equal. Installer says it will touch no saves and no game files. | Hash differs: do not install; re-download. |
| T-02 | Start the launcher. Open the game from it. Look at the main menu. | A **Multiplayer** entry between *Help* and *DLCs*. | No entry: run `KcdUsAgent.exe --build-ui`, restart the game; report if still missing. |
| T-03 | Press **Host a game** (machine A). Note the addresses shown. | Launcher says it is listening on port 7788 and lists addresses. | An error, or no address. |
| T-04 | Machine B: **Join a friend**, enter A's address. | B: "Connected as guest: 2 player(s)." A lists B. | Any refusal text is a result: write it down. |
| T-05 | Close B's launcher and agent, start them again, join again with the same name. | B comes back as the **same participant**; A does not show a second "B". | A shows two entries for B, or B is refused as someone else. |
| T-06 | Copy B's identity file (`%LocalAppData%\KCDUS\participant.key`) to machine C, join from C under B's name while B is *not* connected, then again while B *is*. | The first join works (it is the same participant); the second is refused as already present. Delete the copied file afterwards. | C and B are both admitted at the same time. |

## B. Refusals say why (10 minutes)

| # | Do | Expect |
|---|---|---|
| T-07 | Install a build whose `kcdus.pak` differs (the host sends you one) on B, join A. | B's launcher: "different mod payloads â€¦ both must install the same build". A shows nothing joined. |
| T-08 | Host sets a password; B joins without, then with. | Refused with a password message; then admitted. |
| T-09 | B joins A, a third and a fourth machine join, a fifth tries. | The room holds four; the fifth is told it is full. |
| T-10 | Machine D has an extra DLC or mod folder in `Mods\` that A does not. D joins A. | D is **admitted**. Both launchers say D's game has other DLC or mods and that worlds and Henrys will not be moved. |
| T-11 | With T-10 still true, D opens the Multiplayer tab and chooses **Join the host's world**. | Nothing is transferred. D is told, in a sentence, to install the same DLC and mods. A is not paused. |

## C. Presence (two machines, 20 minutes)

| # | Do | Expect |
|---|---|---|
| T-12 | Both load a save. Stand near the same place, or walk to each other. | Each sees a body for the other, facing the right way, walking and idling. |
| T-13 | Walk 300 m apart, then back. Run. Ride a horse. | Bodies follow and are re-created within about a second after going out of range. Riding: note what you see; riders are not claimed to work. |
| T-14 | One player equips plate armour and a helmet. | The other sees the armour on the body within a few seconds. A game on an unsupported engine keeps default clothes and says so. |

## D. Honest status (the point of this build)

| # | Do | Expect | Failure looks like |
|---|---|---|---|
| T-15 | Read the launcher status in a two-player room. | A sentence starting "Room: presence only" that includes "NOT active". | The word "shared" with no "NOT", or silence. |
| T-16 | Open the Multiplayer tab in the game. | The same sentence. | A different claim than the launcher. |
| T-17 | The host fights an enemy. | The friend does **not** see the host's enemy or fight. This is expected. | Not applicable: this is a documented limit. |
| T-18 | The host loots a chest or corpse. | The friend's world is unchanged. Expected. | |

## E. Worlds and your own Henry (two machines, 30 minutes)

These are **personal-Henry and world moves**, not shared simulation. Both machines have the same DLC and mods. Read [SHARED-WORLDS.md](SHARED-WORLDS.md) and [CHARACTERS-AND-SAVES.md](CHARACTERS-AND-SAVES.md) first.

| # | Do | Expect |
|---|---|---|
| T-19 | A holsters weapons, finishes combat and dialogue, saves. B uses the Multiplayer tab: **Game world â†’ Join the host's world**, with **Which Henry â†’ my own**. | B receives the world, a loading screen follows, B plays in A's world as B's own Henry. The launcher says the native readback was accepted. |
| T-20 | B checks stats, skills, perks and inventory against what B had before. | Levels, XP, selected perks and inventory counts match B's Henry (item order may differ). |
| T-21 | B is on a horse, or in a fight, when asking. | The move is **refused with a reason**, and B's save is untouched. |
| T-22 | Cancel the load halfway (close the game on the loading screen), restart, ask again. | The retry completes or is refused cleanly. B's original saves are untouched (compare the file list before and after). |
| T-23 | **Game world â†’ Send my Henry home.** | B returns to B's own save; the shared one is kept as an archive. |
| T-24 | Look at the five playline slots on B before and after. | Still exactly five. Your old saves are still in the archive folder the Multiplayer tab names. |

## F. Reconnect (two machines, 15 minutes)

| # | Do | Expect |
|---|---|---|
| T-25 | A and B play the same world separately for ten game minutes, then reconnect. | The launcher compares the two copies and offers one winner; the other is archived, not deleted. Read the sentence before choosing. |
| T-26 | Kill B's agent mid-session, restart it. | A shows "B left", then B back within seconds as the same participant. |
| T-27 | Kill the host's agent. | Friends say the relay connection went away; they reconnect when it returns (every 3 s). |

## G. Soak and four players (60+ minutes)

| # | Do | Expect |
|---|---|---|
| T-28 | Four machines in one room for an hour. Walk, ride, enter buildings, load a save on one, rejoin. | No crashes. The game's frame rate within a few fps of the game without the mod. `agent.log` grows by a few lines a minute, not thousands. |
| T-29 | Uninstall on one machine from "Apps". | `Mods\kcdus` is gone. Saves are untouched. The five slots are intact. |
| T-30 | Reinstall the same installer over a working install. | Settings (name, password, address) are kept. |

## Result sheet

For each test: ID, pass/fail, what you saw, time, the build number shown in the launcher's title bar, and the installer's SHA-256. Send the sheet with the logs above.
Anything marked failed stays "candidate" in [CAPABILITIES.md](CAPABILITIES.md) until it is fixed and repeated.

## Session 2 tests: pending human acceptance

All cases below are PENDING. Record both machines' release tags, installer hashes, VERSION, engine builds, active DLC, settings and logs. Use disposable save copies. Do not mark overall shared simulation accepted from any individual passing case.

| ID | Test | Required result |
|---|---|---|
| T-31 | Host with no save-affecting DLC, guest with all; restart after disabling extras as instructed. | Guest is admitted; extra DLC is identified and excluded from mirroring; lower-DLC world loads. No automatic Steam entitlement removal is claimed. |
| T-32 | Host with all DLC, guest with none; try moving that host world. Then reverse the host roles. | First transfer is refused before installation. Lower-content host world may be joined. Original saves remain preserved. |
| T-33 | Different other mods or different Lua build. | Other mods cap presence and block world moves; differing Lua payload is refused. |
| T-34 | Host opens ESC, then guest opens ESC; leave it open for 15 minutes. | KCD2: both worlds stop and resume together with shared pause enabled. KCD1: shared pause is unavailable; document independent worlds instead of reporting a pass. |
| T-35 | Set pause off on both KCD2 clients; open ESC on each. | Menu works while both worlds continue. KCD1: unavailable until a native interception path is proved. |
| T-36 | Disconnect/kill an agent during shared pause. | KCD2 hold releases within 20 seconds of the last refresh. Normal offline menu pause remains available. |
| T-37 | Open Multiplayer in the pause menu; use status, Back and settings, then resume. | Correct live-session pages and navigation; main-menu-only evidence is insufficient. |
| T-38 | Two guests take the same body/loose item at once; retry/delay replies; restart host between intent/mutation/result. | One host mutation; retries replay; uncertain mutations quarantine. No stale-scope reply grants an item. Record any lost item separately; no duplication claim from unit tests. |
| T-39 | KCD2: `mp_checkpoint_mode candidate`, save with all clients joined; disconnect/load or withhold one capture. | All participants' native holds, matching world MD5, distinct characters and ledgers, final manifest after ACKs; otherwise abort and release. No partial checkpoint is selectable. Save while held and all native inventory exclusion must be separately demonstrated. |
| T-40 | Reconnect from divergent checkpoints and choose one; stage personalized characters. | Both alternatives preserved, no independent-world merge, no unrelated character substitution. Guest archive transfer/live promotion are still unresolved; do not treat offline preparation as native acceptance. |
| T-41 | KCD1: on disposable matching worlds, enable `kcdus_quest_mode candidate` on both; complete an open base objective, repeat, then try rails/DLC/local events. | Native readback agrees; replay does not repeat mutation; vetoed paths stay untouched. Compare rewards, XP, NPC spawns and save/reload. Full quest completion without native proof must report unverified. |
| T-42 | Extract Linux archive, validate SHA256SUMS, run doctor/install on fake Steam, then real Proton on two computers. | Fake fixtures and real gameplay reported separately; verify Windows/Linux feature negotiation. Missing KCD1 native adapter paths stay unavailable. |

## Session 3 tests: shared fights, loot and pausing (KCD1; pending human acceptance)

Use disposable copies of ONE save on both machines (the host sends its world with **Game world > Join the host's world**), the same installer on both, and a screenshot or log for each result. In a shared world the launcher says "Fights and loot are shared with the friends in your world".

| # | Do | Expect | Failure looks like |
|---|---|---|---|
| T-43 | Both load the SAME shared world. Read the launcher line. Then one of you loads another save. | "Fights and loot are sharedâ€¦" while both are in the shared world; it goes off (a notice) as soon as one is not. | It claims sharing across different saves. |
| T-44 | Stand together near a bandit. Only A attacks him; B watches B's own copy. | B's copy of the bandit loses health as A hits (B sees it stagger-free: no hit reaction, health bar drops) and dies when A kills him. | B's bandit unharmed, or A is credited on B's side with XP, a crime or anger. |
| T-45 | A and B both hit the same enemy. | He dies after roughly half the blows each alone would need, in both copies. | He survives twice the blows. |
| T-46 | A kills a bandit while B is 80 m away. B walks there later. | The corpse is already there in B's copy. | B meets a live bandit. |
| T-47 | A loots a corpse or chest (take everything). B opens the same body or chest afterwards. | It is empty or holds only what A left. | B can take what A took. |
| T-48 | A and B take the same item from the same body at the same moment. | Exactly one of them keeps it; the other sees "Someone already took that" and the item leaves their inventory within about a second. | Both keep it (a duplicate). |
| T-49 | B takes an item, then B kills the agent or the network drops before the answer. B reconnects, saves, loads. | After 20 seconds, or before the save, the item is not in B's inventory. | The unconfirmed item is in B's save. |
| T-50 | The host restarts its agent in the middle of B looting. | B's item is kept or taken back, never both copies; the host log says an interrupted ask will never be granted again. | The item exists twice. |
| T-51 | A opens the pause menu (Esc) and leaves it open for 20 seconds. | B's world holds with a notice; when A closes the menu B runs again within a second. | B keeps running, or stays frozen after A resumes. |
| T-52 | A opens the pause menu, then A's agent is killed. | B's world lets go by itself within about 15 seconds. | B stays frozen. |
| T-53 | B sets **Pausing: Off** (Multiplayer tab, Pausing). A opens the pause menu. | B's game never holds. A's own game is paused by A's own menu as usual. | B holds anyway. |
| T-54 | B opens the inventory, a dialogue, or a loading screen while A plays. | A's game never holds. | A freezes. |
| T-55 | Both players open the pause menu together, then one closes. | Each world holds while the other's menu is open; both run when both are closed. | A world stays frozen with no menu open. |
| T-56 | Set **Shared fights and loot: Off** on B. Repeat T-44 and T-47. | Nothing is shared with B; B's copy stays its own. | B still receives damage or loot changes. |

Report with the installer SHA-256, the commit, `%LocalAppData%\KCDUS\logs\agent.log` from both machines, and the `KCDUS|CMB`, `KCDUS|LOOT` and `KCDUS|PAUSE` lines of each `kcd.log`.

## Session 4 acceptance: all PENDING (two computers, disposable worlds only)

Record release tag/commit, installer and Lua/native hashes, engine build, DLC, settings and logs for both participants. Every item test records counts in each Henry plus all copies of the source before/after; unexpected loss is a failure as well as duplication. Restart/kill only the owned test agent PID. Save/reload both copies to check persistence. Do not infer shared simulation from these cases.

| ID | Procedure | Required outcome / remaining gate |
|---|---|---|
| T-57 | Launch through the Windows adapter on both PCs, same world. Set Pausing Off, open your own ESC and inventory while friend moves; then disconnect agent and wait 12 s. | Own ESC world continues only while the connected gate is active; heartbeat expiry restores offline pause; inventory cvar restores. Unsupported native engine and Linux must not claim this pass. |
| T-58 | A bandit near A walks/chases; B approaches, crosses nearest-owner boundary repeatedly, then leaves/loads/disconnects. | One brain owner; no oscillation before 2 s/4 m margin; positions agree; stale puppets release. Record missing swing/draw/targeting behavior separately. |
| T-59 | Attack that enemy from both PCs; let it attack each Henry; repeat after streaming 100 m away. | Damage/death agree without duplicate rewards; actual attacks target each independent Henry. Invulnerable peer figures or missing native attack targets fail this target. |
| T-60 | Both take the same ground item; drop a separate stack, let friend pick it up, then repeat with delayed/retried replies. | One legitimate owner, matching ground/drop count, no double spawn/regrant; condition and quantity preserved. |
| T-61 | Named horse saddlebags: both take last item, then put/withdraw stacks; ride away and return. | Host-decided stock, each take/put once; no confusion between horses, no duplication or disappearance through movement/rejoin. |
| T-62 | Both open same chest; take/put concurrently. Wait for native restock without touching pack. | Conservation and stock agree; restock never reported as a player put. Guest/host simultaneous native UI ordering must be demonstrated. |
| T-63 | Buy the last shop item concurrently, including a multi-item barter; interrupt one reply. | At most one confirmed purchase; failed goods removed and observed money refunded once. Record shared-wallet/barter/crime limitations; do not mark a full economy accepted. |
| T-64 | Supported base quest pays native items/currency; complete mirrored step/retry. During its 3 s window take a known loot item, then an unrelated unobserved gain. | Native reward subtraction prevents double payment; known loot excluded; replay within load does not pay twice. Heuristic false attribution, XP and restart/backfill limits are failures for full reward parity. |
| T-65 | Disconnect/restart host or guest during take/put/reward; try capture while native rollback is deliberately blocked; disable sharing/quest/pause options, rejoin/load. | Unknown host decisions quarantined; stale asks cannot mutate new world; unresolved capture refuses; no leaked brain/menu holds. Record item loss and undurable reward replay. |

T-53 is superseded on supported Windows installs by T-57: Pausing Off can now gate the player's own menu. Historical T-34/T-35 assumptions about KCD1 absence are likewise superseded; human evidence is still required.

## Session 5 cases: all PENDING

| ID | Test | Required evidence |
|---|---|---|
| T-66 | Delay/reorder NPC samples and ownership snapshots; switch owner away and back; restart host in the same world. | Old term/incarnation/sequence does not move a current actor; proper owner resumes and every puppet releases. Record the remaining missing attack/hit and acknowledged-handoff paths. |
| T-67 | Complete a mirrored quest that natively pays money/items; deliver reward before quest application; force an inert add. | Pre-mutation baseline subtracts native payment, early reward waits, inventory delta proves a grant, uncertain creation is not retried. Repeat after restart to expose still-undurable receipts and attribution gaps. |
| T-68 | Both enter one shared open world; `kcdus_weather on` on both. Try clear/storm presets outside, scene transitions, reload and off. | Native renderer agrees, repeated heartbeat does not restart blend, reload reapplies. No preservation/restoration of native scripted weather is claimed until proved. Candidate defaults off and setting is session-local. |
| T-69 | Stream an NPC out/back so its native handle changes while an old puppet exists; release/save/disable sharing. | Old puppet never moves or enables the replacement handle; new authoritative sample can establish a fresh puppet. |

## Session 6 cases: all PENDING

| ID | Test | Required evidence |
|---|---|---|
| T-70 | In two disposable copies of the same world, damage and kill a named NPC; inspect both health/death and CMBAPPLIED logs. Repeat after streaming/reload. | Only native readback-confirmed damage is labelled applied; partial/failed mutations are not echoed. Record absent independent targeting/attack/block/credit support as failures of full combat, not acceptance. |

## Session 8 cases: all PENDING

| ID | Test | Required evidence |
|---|---|---|
| T-71 | In an owned disposable profile, use the real native combat scheduler against independent participant bodies, in view and behind the camera; change ownership, stream and reload. | Native damaging contact, blocks/reactions/credit and persistent target/term attribution work for each participant. Installed actions/poses or cleanup alone fail full combat acceptance. Current normal profiles cannot enable this experiment. |
| T-72 | Capture/restore supported timed effects on a disposable character, then save/reload and change worlds/clocks; include missing instances, injury/body parts, perks and reference-bearing effects. | Supported records have exact readback and effective behavior; unsupported records refuse before mutation. One existing cooldown roundtrip does not satisfy full character restoration. |
| T-73 | Complete real quests while gaining unrelated XP/items, interrupt/restart effect delivery, and complete the same objective again. | Stable objective execution provenance and durable exactly-once item/XP/spawn/script effects. A generic script cause or timing-based reward guess fails acceptance. Native XP observers currently provide diagnostics only. |
