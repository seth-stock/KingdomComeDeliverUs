# Playing together: the guide for the host and for the friends

Kingdom Come: Deliver Us, version 0.1.0. A free, unofficial co-op mod for **Kingdom Come: Deliverance** (the first game).
A modified port of Kingdom Come: Together (https://github.com/DeepFriedDepp/KingdomCome-Together); not affiliated with or endorsed by
Warhorse Studios or Deep Silver.

> **Read this first.** Nobody has played this with two people yet. What is true and what is not yet seen is in `KNOWN-LIMITS.md`.
> It is a **presence** mod, not a shared world: each of you plays your own save in your own game; you see each other, talk, share the clock,
> and the host's story can ask the friends whether they want to be brought along. Enemies, quests and loot are not shared.

## 1. What you get

| | |
|---|---|
| See each other | every other player is a body that follows their position and heading (it glides, in an idle pose: no walking animation) |
| Talk | type in the launcher's chat box; messages appear at the top of the game screen |
| One clock | a friend's game time is set to the host's when it has drifted more than two game-minutes |
| **Join or stay** | when the host is in a stretch of the story that locks them in (a battle, the monastery, a duel, a tournament, Theresa's flashback...) each friend is asked: **F11 join** (be brought beside the host and, in a staged stretch, kept within 120 m) or **F12 stay** in the open world. No answer in 30 seconds counts as joining. **F11 joins at any time** |
| Every quest known | the game's 287 quests are classified: 28 put the host on rails, 62 are mixed (scripted and free alternate), the rest are open (`quest-gating-table.md`) |

## 2. What you need (everyone)

1. **Kingdom Come: Deliverance** (the first game) in Steam, up to date (build 1.9.8). DLC are optional.
2. **Steam running** before the game starts.
3. The installer `KingdomComeDeliverUs-Setup-0.1.0.exe` from the same source as your friends. **Everyone must run the same version.**
4. A way to reach each other: the host's address and port **7788**. Friends on other networks should use **Tailscale** (free): both install it and the host
   gives the friend their Tailscale address (it starts with `100.`). Never type `localhost`.

## 3. Installing (everyone)

1. Run the installer. Windows will say it is unsigned: **More info, then Run anyway**. Check the file first if you like:
   `Get-FileHash .\KingdomComeDeliverUs-Setup-0.1.0.exe -Algorithm SHA256` must equal the number in `SHA256.txt`.
2. It finds the game through Steam and shows the folder. Correct it if it is wrong. Leave **"Block the game's remote-console port"** ticked
   (it asks for administrator permission once; the mod works without it, but then another computer on your network could send the game console commands).
3. It puts the mod in `<game>\Mods\kcdus` and the programs in `%LocalAppData%\KCDUS`. It touches none of the game's own files and none of your saves.
4. If Windows Defender or your firewall asks about **KcdUsAgent** or **KcdUsRelay**: the host should **Allow** the relay on private networks. The friends need not.

## 4. The host

1. Open **Kingdom Come Deliver Us** (desktop shortcut). Pick **Host a game**. The launcher lists your addresses (a `100.x` one if you use Tailscale). Optional: a password.
2. **1. START CO-OP**, then **2. START THE GAME** (it starts the game through Steam). Wait for the main menu and **load your own save**: the one you want to play.
3. The launcher shows "Connected as host". Tell your friends your address and port, for example `100.64.12.3:7788`.
4. Play. When your story goes on rails the launcher shows who joined, who is staying and who is still deciding.

## 5. The friends

1. Open the launcher, pick **Join a friend**, type the host's address and port. **START CO-OP**, then **START THE GAME**.
2. At the main menu **load your own save** (any save: you play your own game). Your launcher says "Connected as guest" and the host appears as a body near where they are.
   **Your game and the host's game must be on the same map position to meet:** use the same save point, or walk there, or be brought (F11 when asked).
3. When the host's story locks them in you are asked. **F11** join, **F12** stay. The launcher has the same two buttons. The setting
   **"When my host is on rails"** can answer for you every time (always join / always stay).

## 6. Chat and commands

* Type in the launcher's chat box and press Enter.
* In the game console (`~`, if it opens in your build): `kcdus_say hello`, `kcdus_join`, `kcdus_stay`, `kcdus_status`, `kcdus_off`, `kcdus_on`.

## 7. If something goes wrong

| Symptom | Do this |
|---|---|
| "Waiting for the game" | start the game with the launcher's button (Steam must be running); the game opens its console port a few seconds into the main menu |
| "The game is running but the co-op mod did not answer" | the mod is not installed or was disabled: run the installer again; after a game update the game may have switched mods off |
| "In the main menu or loading" | load a save |
| "The relay refused you: version" | everyone must install the same version |
| "The relay refused you: password / full / host" | check the password, the room holds 4, and only one player can be host |
| Nobody appears | the two games are far apart on the map: walk toward each other, or press F11 when asked; the launcher shows the distance |
| The host is a still figure | that is how it is in this version: bodies glide without walking animation |
| Logs | `%LocalAppData%\KCDUS\logs\agent.log` and the game's `kcd.log` (lines that start `KCDUS|`). Remove your public IP before posting them |

## 8. Safety and privacy

* The mod switches on the engine's **remote console** (TCP 4600). The installer's firewall rule blocks other computers from it; the agent only ever talks to it on `127.0.0.1`.
* The agent starts the relay only for the host. The relay sees names, positions and chat, and forwards them. Nothing is stored.
* Uninstalling asks whether to remove the mod from the game folder. Your saves are never touched; a save made with the mod loads without it.
