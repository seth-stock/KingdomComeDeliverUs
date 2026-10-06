# Feature parity: Kingdom Come: Deliver Us (first game) and Kingdom Come: Together (second game)

Written 2026-10-06. Both mods are unofficial community mods, not affiliated with or endorsed by Warhorse Studios, Deep Silver or PLAION.
The two games are different engines' builds, so the **backends differ**; the aim is that a player sees *the same features* in both. This table says, feature by feature, what each has,
how it is done, and what is **not seen yet**. Marks: **seen** = observed working in the real game on one computer · **tests** = automated tests only · **built** = written, not run in the game · **no** = not possible, with the reason.
Nothing here has been seen with two real people on two computers.

| Feature | KCD1 (Deliver Us) | KCD2 (Together) | Same? / why they differ |
|---|---|---|---|
| **In-game "Multiplayer" tab** | **seen**: a button in the main menu (and pause menu: built) opening pages (status, host, join, world, story, keys, browser settings). Data-only: flow-graph files built on the player's computer from their own `GameData.pak` | **seen** in the main menu: root button, pages, Back, settings that change the mod's real state (e.g. `mp_join_henry` auto/fresh), all via Lua calls into the game's Scaleform `Menu` element. Pause menu not yet seen | Same pages and options; different mechanism (KCD1 menus are data, KCD2's are compiled) |
| Install needs no Modding Tools | yes (the tab's file is made at install from the player's own game files) | yes (it is Lua inside the mod's pak; nothing is made from game files). KCD2 itself needs the *Modding Tools build of the game* for its console API: that is the KCD2 mod's existing requirement | differs: KCD2's game build requirement |
| Warhorse's modding EULA shown and carried | **built**: installer page, copy in `Mods\kcdus\`, Linux asks for `yes` | **not yet**: the KCD2 installer shows the repo's licence only, and no KCD2 modding EULA text was found on this machine | **gap in KCD2**: needs the text from Warhorse/PLAION |
| Host / join / leave from the game | **seen** (agent idle mode, tab buttons) | **built** in the tab as *Co-op sync start/stop*; host and join are the KCD2 launcher's job (a separate program starts the relay and the agent) | differs: KCD1's agent can start a relay itself |
| Settings in your browser | **tests + seen**: `http://127.0.0.1:1415/settings?t=…` | **no**: the KCD2 launcher (a Blazor app) is the settings UI | differs |
| Keys: F11 join / F12 stay, changeable | **tests**: F11/F12, F9/F10 or none, Windows and Linux | F11/F12 in the game's own Keybinds list (changeable in Settings > Keybinds) | KCD2's keys are the game's own bindings; KCD1 has no key binding list for mods |
| Join-or-stay question for scripted stretches (rails) | **seen** (real F11 press), 287 quests classified | built and tested over 201 quests; not seen with two players | same idea; different quest lists (different games) |
| Presence: seeing each other, chat, clock | **seen** (bodies glide in idle pose; no animations/outfits) | **seen with two players** (animated, dressed, riding...) | differs a lot: KCD2 has a native plugin and the game's REST API; KCD1 only has Lua + a console |
| Combat, NPCs, quests, loot shared | **no**: KCD1 has no hook to do it | yes (see the KCD2 README) | **cannot be equal**: backend |
| Linux | built, untested under Proton (tarball + `kcdus`) | built, published as a pre-release, untested | same |
| **Join a host's existing world** (even 100 h) | **seen** (6 MB world over the relay, loaded by the game) and tests | `mp_join_request` + the joiner's character spliced in (**seen** in the real game against a scripted host) | same result, different method (KCD1 copies the host's save into an empty slot; KCD2 splices a Henry into it) |
| **Start a new world together, new characters** | **built**: host announces, both choose New Game; the first save becomes the world | KCD2: `mp_join_henry fresh` | same |
| **Bring my Henry from another world** | **seen** (card read and applied): stats/skills raised, things/money added, no perks, nothing lowered | **seen**: the real character is spliced into the world | KCD2 carries the whole character; KCD1 only what its Lua API can read and set |
| **Send my Henry back to his own world** | **built + seen in parts** (home playline remembered, card captured, home save backed up before) | **seen** (`mp_henry_home`: the home save loads and matches) | same |
| **Play the shared world alone or with friends; separate saves at other times/places** | **built + tests**: every player has their own copy, saves freely | `mp_world_copy` (a copy of the shared world with the player's character as their own save) | same idea |
| **Reconcile when they reconnect** | **tests + seen** (further-played / host / newest rule; the one behind takes the other's world; backup kept) | **not the same**: KCD2's host is the world; a joiner's world copy is a separate save (`mp_world_copy`); there is no "two copies merge" | **gap in KCD2** (see below) |
| Host saves for the client too | the *Save the world for everyone* button: saves and tells the friends | the host's world saves + each joiner's character paired with every host save | same |
| Prologue / scenes as other characters (Theresa; Godwin) | presence is character-agnostic; Theresa's flashback is a locked period; **not seen** | Godwin world loads (**seen**); a Godwin join not run | partly |
| All quests incl. DLC | 287 classified, all five DLCs | 201 classified, DLC shared (`mp_quest_dlc`) | same |

## KCD2 tab: what is left

* **Seen live:** the *Multiplayer* entry on the main menu (it comes back after the game rebuilds the page), every page (status, game world, saving, story, session rules, keys), *Back* up the tab and then to the game's root page,
  and a setting that changes the mod's own state and re-draws. The mechanism (no timers in the main menu; the cursor-move sound drives it; the game's own Help page is opened by an injected `menu_accept` so that its Back works) is in the KCD2 repo's `docs/MENU.md`.
* **Not seen:** the tab in the pause menu of a running game; the buttons in a session with a partner; controllers.
* The KCD2 tab has no *host/join* buttons: that is the KCD2 launcher's job (it starts the relay and the agent); the KCD1 agent can start its own relay, so its tab can.

## KCD1 gaps that remain (and why)

* No shared simulation (backend), no animations or outfits on the other player's body (backend), no perks in a Henry card (the Lua API cannot list them), levels are only raised.
* The prologue cannot be saved by the game, so a world cannot be shared before it is over.

## Things that exist in one game and are missing from the other

| In | Missing from | Plan |
|---|---|---|
| KCD1: reconcile two copies of a world when players meet | KCD2 | Would need KCD2 world files to be comparable; the KCD2 `HenryStore` already keeps per-world snapshots. Not started |
| KCD1: browser settings page | KCD2 | Not needed: the KCD2 launcher is the settings UI |
| KCD1: Warhorse EULA page in the installer | KCD2 | **To do**: carry the KCD2 modding terms. The KCD2 Modding Tools folder only holds `LICENSE.txt`, a list of third-party licences (MIT, Apache, Codejock...), not a Warhorse/PLAION modding EULA, so there is nothing to copy; the terms have to come from Warhorse/PLAION (or the Modding Tools' own first-run agreement) |
| KCD2: shared combat, NPCs, quests, loot | KCD1 | Cannot be built without a native plugin |
