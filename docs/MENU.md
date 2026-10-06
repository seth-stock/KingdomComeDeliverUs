# The Multiplayer tab

Written 2026-10-06. Unofficial community mod; not affiliated with or endorsed by Warhorse Studios or Deep Silver.

Once the mod is installed, the game's main menu and its pause menu have a **Multiplayer** entry (between *Help* and *DLCs*). Everything the mod can do is here;
nothing has to be typed.

```
Multiplayer
  Status                    what the agent sees (shown on screen in a world; the line above the buttons in a menu)
  Host a game               start a session others can join
  Join a game               join the host saved in the settings
  Leave the session
  Game world                  Play my shared world / Join the host's world / Start a new world together / Save the world for everyone
                              Which Henry (the world's own / my own)  ·  When we reconnect (further-played / host's / newest)  ·  Send my Henry home
  Story: join or stay       ask me / always join the host / always stay in the open world   (what to do when the host's story goes on rails)
  Keys                      join F11 + stay F12 (default) / F9 + F10 / none
  Settings in your browser  your name, the host's address and password, the port, the world options, the keys
  Back
```

The line at the top of a page tells you what happened ("hosting a game", "joined the host", "receiving the host's world...", "Keys: join F9, stay F10").

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
| "Enter your host's address in the browser settings first" | *Settings in your browser*, fill in the host's address, save, then *Join a game*. |
| "All five playlines are in use" | The game has five. Delete a game you do not need on the Load screen, then *Join the host's world* again. |
| The game does not start after something was copied into the saves folder | A folder `playline5` or higher stops the game: remove it. |

The same options, for Linux and for the second game, are in [LINUX.md](LINUX.md) and [FEATURE-PARITY.md](FEATURE-PARITY.md).
