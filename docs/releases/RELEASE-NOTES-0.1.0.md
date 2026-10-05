# Kingdom Come: Deliver Us 0.1.0

> **A modified port, not the official "Kingdom Come: Together".** It carries that project's ideas, some of its code and its licence to
> the first game. Official repository of the project it derives from: https://github.com/DeepFriedDepp/KingdomCome-Together.
> Unofficial and free; not affiliated with or endorsed by Warhorse Studios or Deep Silver. Kingdom Come: Deliverance and its content belong to them.

**First release. Never played by two people.** What is not yet seen is in `docs/KNOWN-LIMITS.md`; the first two-player run is `docs/TWO-PLAYER-CHECKLIST.md`.
Everyone you play with must install this exact version: the relay refuses a different one.

## What it does

* **Presence:** each player appears to the others as a body that follows their position and heading (no walking animation; Henry's base clothing).
* **Chat** from the launcher; **a shared time of day** (a friend who is behind the host skips forward to match; the game cannot turn its clock back).
* **When the host is on rails, you choose:** a question appears, **F11** join your host (be brought beside them; kept within 120 m in a staged stretch), **F12** stay
  in the open world (no answer in 30 s = join; F11 joins at any time). One question covers a whole stretch (the siege is six quests, Theresa's flashback ten).
* **All 287 quests of the game are classified** from the game's own quest data and the wiki: 28 put the host on rails, 62 are mixed, the rest open
  (`docs/quest-gating.md`, `docs/quest-gating-table.md`). Main story, side quests, activities, and the DLC chains: A Woman's Lot, Band of Bastards, From the Ashes, The Amorous Adventures of Bold Sir Hans Capon, Treasures of the Past.
* **A launcher** (host or join, start the game through Steam, live status, the join/stay buttons, chat) and **an installer** that finds the game through Steam.

## How it works without a native plugin

Kingdom Come: Deliverance has no modding build, no REST API and no reflection layer. It does have an open Lua engine and a leftover CryEngine **remote console**:
the mod registers a console command (`kcdus`) for the agent to talk to, and writes events into `kcd.log` for the agent to read (`docs/KCD1-MODDING.md`).

## Checks run before this build

* 108 offline tests: the relay over real sockets, the real mod Lua under a stubbed engine, the story state machine against every one of the game's 90 locked quests, and whole sessions (a real relay, fake games).
* In the real game (one machine, a throwaway new game, 1.9.8): a **real F11 key press** answers the question and brings the player to the host; the clock skips forward to the host's time of day; the mod loads from its pak, the remote console command works without `-devmode`, the player is sampled 10 times a second,
  a second player (a bot) appears as a body, follows, chats, and is asked the join-or-stay question; the quest watcher reads the game's quest state; the menu scene is told from the world.
* A frame-rate soak with and without the mod: see `docs/SOAK.md`.
* The installer compiles; the published relay answers a client and the agent starts; a silent install against a stand-in game folder was run and uninstalled (`docs/INSTALLER.md`).

## Known limits (short)

No shared world (enemies, quests, loot), no walking animation or clothing sync for the other players, no voice, everybody loads their own save. Windows will warn that the installer is unsigned; Windows Defender or a firewall may ask about the agent and the relay. Details: `docs/KNOWN-LIMITS.md`.
