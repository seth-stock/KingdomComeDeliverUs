# Characters, worlds and saves in Kingdom Come: Deliver Us

Written 2026-10-06, rewritten the same day after the shared-world work. Kingdom Come: Deliver Us is an unofficial community mod; not affiliated with or endorsed by Warhorse Studios or Deep Silver.
Asked for in the same round as the KCD2 mod's WO-157 (its `docs/WO-157-findings.md`): Linux, other characters, new/brought/returned Henrys, every quest, separate saves.

## 1. Where this mod stands on each

| # | Asked | Status in this mod |
|---|---|---|
| 1 | Linux | **Done** ([LINUX.md](LINUX.md)); not run under a real Proton |
| 2 | Multiplayer where the player is not Henry | The presence design does not need to know who the player is: everybody is in their own game, the others are shown as presence (position, chat, the clock). Theresa's flashback (A Woman's Lot) is a locked *period* in the gating (ten quests, one question). **Not seen in the game**: nobody has played the Theresa part with this mod |
| 3 | New Henrys, a Henry brought from another world, put back | **Built** ([SHARED-WORLDS.md](SHARED-WORLDS.md)): *Start a new world together* (new characters), *Which Henry → my own* (your skills/stats/things onto the world you receive: raised, never lowered; perks are not carried), *Send my Henry home*. Proven live on one computer; not with two people |
| 4 | Every quest, side quests, tasks, activities, main, DLC | All **287** quest roots in the game's data are classified (28 *rails*, 62 *mixed*, 197 *open*), including the five DLCs ([quest-gating.md](quest-gating.md)). The join-or-stay question works on any of them. **Never seen with two players** |
| 5 | Host saves for both; separate saves at other times and places | **Built**: each player keeps their own copy of the shared world and saves it whenever and wherever they like; when they meet again the copy that is behind is replaced by the other (backup kept). *Save the world for everyone* tells the friends how far the host's copy has got. See [SHARED-WORLDS.md](SHARED-WORLDS.md) |

## 2. How a shared world works in this game (and why it is not KCD2's design)

KCD2's mod splices the joiner's character into the host's save with a native plugin. This game has no plugin route (retail `WHGame.dll`), and its saves
share the block framing and the `0XBP` footer with KCD2's but not the stream inside (no KCD2 soul GUIDs): the mod **never edits a save**. Instead:

1. **The game can be told to load a save from outside**: a data-only menu graph (`MP_Load`) starts the game's own *LoadSavedGame* node (proven in the retail game, from the menu and from inside a world).
2. **A save is a file the agent can move**: the host's game is saved on request (`Game.SaveGameViaResting`), and the file (about 6 MB) crosses the relay in pieces and is checked before use.
3. **Each player holds their own copy** in an empty one of the game's five playline slots; which copy goes on after two were played apart is decided by comparing the play time and save time inside the saves.
4. **Henry is a card**: the game's Lua API reads stats, skills and things; it raises levels and adds things; it cannot lower a level or list perks.

The in-world protocol (who owns each NPC, quest and crime in a world two games both simulate) is not part of this and not built: while you play together you see each other
as presence; each game simulates its own copy.

## 3. Things that are the game's, not the mod's

* The game has **five playlines**. Saves in a folder `playline5` or higher stop it from starting.
* The prologue **cannot be saved** (the pause menu's *Save Game* is greyed); only *Save & Quit* writes `exit.whs`.
* The playline numbers in the menus are one more than the folders: *Playline 3* is the folder `playline2`.
