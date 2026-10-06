# Known limits of Kingdom Come: Deliver Us 0.1.0

Marks: **(seen)** observed working in the real game on one machine · **(tests)** covered by offline tests only · **(not seen)** built, never observed.

## What is NOT in this version (by design: KCD1 has no native-plugin route)

* **No shared world.** Each player plays their own save. Enemies, NPCs, loot, quest state and combat are not shared; a friend does not see the host's fights.
* **No walking animation on the other players' bodies.** They are plain NPC bodies moved by script. They glide in an idle pose. **(seen)**
* **No appearance sync.** Every body wears Henry's base-game clothing preset (a dark tunic with a scarf), whatever the real player wears. **(seen)**
* **No quest mirroring.** The host's quest progress decides what the friend is *asked*; it is not copied onto the friend. A friend who joins a stretch is brought
  beside the host, nothing more.
* **No loading of the host's world.** The retail game has no load command and the mod never edits a save: everybody loads their own save from the menu.
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
