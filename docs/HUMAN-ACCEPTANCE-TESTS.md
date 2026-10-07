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
| T-07 | Install a build whose `kcdus.pak` differs (the host sends you one) on B, join A. | B's launcher: "different mod payloads … both must install the same build". A shows nothing joined. |
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
| T-19 | A holsters weapons, finishes combat and dialogue, saves. B uses the Multiplayer tab: **Game world → Join the host's world**, with **Which Henry → my own**. | B receives the world, a loading screen follows, B plays in A's world as B's own Henry. The launcher says the native readback was accepted. |
| T-20 | B checks stats, skills, perks and inventory against what B had before. | Levels, XP, selected perks and inventory counts match B's Henry (item order may differ). |
| T-21 | B is on a horse, or in a fight, when asking. | The move is **refused with a reason**, and B's save is untouched. |
| T-22 | Cancel the load halfway (close the game on the loading screen), restart, ask again. | The retry completes or is refused cleanly. B's original saves are untouched (compare the file list before and after). |
| T-23 | **Game world → Send my Henry home.** | B returns to B's own save; the shared one is kept as an archive. |
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
