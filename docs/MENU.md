# The Multiplayer tab

Written 2026-10-06. Unofficial community mod; not affiliated with or endorsed by Warhorse Studios or Deep Silver.

Once the mod is installed, the game's main menu and its pause menu have a **Multiplayer** entry (between *Help* and *DLCs*). Everything the mod can do is here;
nothing has to be typed.

```
Multiplayer                       (the line under the heading says what is happening: "Waiting for Henry's game", "Receiving: 40% of 6.1 MB", ...)
  Host: share my game             the host: load your game or start a New Game (before or after pressing this); friends get THAT game
  Join my friend's game           the friend: connects, gets the game the host is playing now and loads it (asks again by itself until it has it)
  Join with my own Henry          the same, with the Henry of the game you have loaded (stats, skills, perks, inventory)
  Save now                        saves your game (the host's save is what a joining friend gets)
  Leave the session
  Settings: host address, name    your name, the host's address and password, the port (in your browser)
  More options                    How to play together · Who is here · Load my copy of the shared game · Send my Henry home ·
                                  Story and keys · Pausing · Shared fights and loot
  Back
```

* **New game together:** the host starts a New Game and presses Host; the friend presses Join. The friend waits ("Waiting for Henry's game") and gets the
  host's new game as soon as it has begun.
* **Joining a game in progress:** the host loads the game and presses Host; the friend presses Join. The first time the friend plays a copy of the host's
  Henry; when they join that world again they keep their own Henry from their last save of it.
* The heading line holds one short line (about 40 characters). Anything longer is a box with OK in the menu, or the centred line in the world.

## The settings page

*Settings in your browser* opens `http://127.0.0.1:1415/settings?t=<one-time token>` in your browser. The address works only on your computer and only with the token
the agent put in the link, so a web page you visit cannot change your settings. Changes are saved in `kcdus-agent.json` next to the agent (or in
`%LocalAppData%\KCDUS` when that folder is not writable). A new host address, port or password is used the next time you host or join.

## How the tab is made (and why nobody needs the Modding Tools)

* The mod's two files in the game (`kcdus.pak`, the Lua) and the tab's file `kcdus-ui.pak` live in `Mods\kcdus\Data`.
* The tab's pages are small flow-graph files in the game's own menu language (the files the game itself builds its menus from). The main and pause menu files must
  be *changed* to hold one more button, and the changed copy has to be made from **your** copy of the game. So the installer makes it **on your computer**, by running
  `KcdUsAgent --build-ui` (it reads your `Data\GameData.pak` and writes `Mods\kcdus\Data\kcdus-ui.pak`). Nothing of Warhorse's is in the installer, and the Modding Tools
  are not needed by anyone who only plays.
* If the game is updated, or the mod is, the agent makes the tab again by itself the next time it starts (restart the game to see it). If the game's menu files are not the
  ones this version knows, there is no tab and the mod otherwise works as before (the launcher still hosts and joins).
* The installer shows **Warhorse's modding EULA** and asks you to accept it: its section 4.7 says a mod that is passed on must carry the EULA and whoever gets it must
  agree. The mod folder carries a copy (`Mods\kcdus\WARHORSE-MODDING-EULA.txt`).

## Troubleshooting

| You see | Do this |
|---|---|
| No Multiplayer entry in the menu | Start the agent once (the launcher, or `KcdUsAgent.exe`): it makes the tab if it is missing. Restart the game. Or run `KcdUsAgent.exe --build-ui`. |
| A button does nothing | The agent is not running. Start it from the launcher (it starts *waiting* and the tab tells it what to do) or `kcdus play` on Linux. |
| "First set the host's address (Settings)" | *Settings: host address, name*, fill in the host's address, save, then *Join my friend's game*. |
| "Waiting for Henry's game" | The host is in the main menu: they load a game or start a New Game; the game arrives by itself. |
| "Missing DLC: cannot join" | The box names the DLC (for example Band of Bastards). Get it on Steam, or the host turns it off in Steam and plays a game without it. |
| "All five playlines are full" | The box names the playline the mod would use (the one played longest ago; its saves go to the backup folder). Press Join again to say yes, or delete a game on the Load screen. |
| The game does not start after something was copied into the saves folder | A folder `playline5` or higher stops the game: remove it. |

The same options, for Linux and for the second game, are in [LINUX.md](LINUX.md) and [FEATURE-PARITY.md](FEATURE-PARITY.md).
