# Kingdom Come: Deliver Us

A free, unofficial co-op mod for **Kingdom Come: Deliverance** (the first game): see your friends in the world, talk to them, share the world's clock, and, when the host's
story puts them "on rails", **choose whether to join them or to stay in the open world**.

> **This is a modified version, not the official "Kingdom Come: Together".** It is a port of that project to the first game.
> The official repository of the project it derives from: https://github.com/DeepFriedDepp/KingdomCome-Together
> Not affiliated with or endorsed by Warhorse Studios, Deep Silver or PLAION. See `NOTICE` and `AUTHORS`. GPL-3.0.

**Status: 0.1.0, never played by two people.** Read `docs/KNOWN-LIMITS.md` before you promise anything to a friend.

| | |
|---|---|
| Play it | `docs/PLAYING-TOGETHER.md` (host and friends); the friends' one-pager is `docs/READ-ME-FIRST.txt` |
| How the first game can be modded, and what was verified | `docs/KCD1-MODDING.md` |
| The gating on every quest of the game (287) | `docs/quest-gating.md`, `docs/quest-gating-table.md`, `docs/quest-gating-plan.csv` |
| How it is built | `docs/ARCHITECTURE.md` |
| What is not there, and what has not been seen | `docs/KNOWN-LIMITS.md`, `docs/TWO-PLAYER-CHECKLIST.md` |
| Frame-rate soak, installer | `docs/SOAK.md`, `docs/INSTALLER.md` |
| Linux (experimental, untested under a real Proton) | `docs/LINUX.md`; the launcher is `linux/kcdus` |
| Release notes | `docs/releases/RELEASE-NOTES-0.1.0.md` |

## Build

```
dotnet test dotnet\KcdUs.sln                       # 108 tests
python tools\Build-QuestCatalog.py --check         # the quest catalog equals the game data and the plan (needs the game installed)
python tools\Build-Pak.py --install-to "<game>\Mods"   # the mod, into your game (a developer's install; the installer is the normal way)
powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1   # every gate, then release\KingdomComeDeliverUs-Setup-<version>.exe
```

The version is the content of `VERSION`; only its owner changes it.
