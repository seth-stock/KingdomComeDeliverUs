# Architecture of Kingdom Come: Deliver Us

Unofficial; modified port of Kingdom Come: Together (https://github.com/DeepFriedDepp/KingdomCome-Together). Not affiliated with Warhorse Studios or Deep Silver.

```
   player A's PC                                                   player B's PC
 ┌──────────────────────────────────────┐                      ┌──────────────────────────────────────┐
 │ KingdomCome.exe  + Mods\kcdus (Lua)  │                      │ KingdomCome.exe  + Mods\kcdus (Lua)  │
 │   ▲ commands: TCP 4600 (console)     │                      │   ▲ commands: TCP 4600 (console)     │
 │   │ events:   kcd.log lines KCDUS|…  │                      │   │ events:   kcd.log lines KCDUS|…  │
 │ KcdUsAgent  (session, story, hotkeys)│◄── TCP 7788 ────────►│ KcdUsAgent                           │
 │   + KcdUsRelay (host only)           │   frames [type][len] │                                      │
 │ KcdUsLauncher (status, buttons)      │                      │ KcdUsLauncher                        │
 └──────────────────────────────────────┘                      └──────────────────────────────────────┘
```

| Part | Where | Job |
|---|---|---|
| **Mod** | `mod/kcdus/lua/*.lua`, built into `Mods\kcdus\Data\kcdus.pak` by `tools/Build-Pak.py` | samples the player 10 times a second, spawns and moves the other players' bodies, watches the quests, shows text, executes the agent's records |
| **Agent** | `dotnet/KcdUs.Agent` (`KcdUsAgent.exe`) | one per player. Owns every decision: presence, chat, the shared clock, the host's story, the friend's question, the tether, hotkeys. Takes its two links and its clock from outside, so tests drive it with a fake game and a real relay |
| **Relay** | `dotnet/KcdUs.Relay` (`KcdUsRelay.exe`, or in-process in the host's agent) | forwards. Checks the release string, numbers the players, rate-limits state (30/s), lets only the host speak with the host's voice. Holds no world state |
| **Wire** | `dotnet/KcdUs.Protocol` | frames `[type:1][len:2 LE][utf-8 text]`, text fields split by `|`; `PlayerState`; `Safe.Clean`; `Release` |
| **Launcher** | `dotnet/KcdUs.Launcher` (WinForms) | starts the agent and the game, shows the agent's `/status`, sends the join/stay answer and chat |
| **Bot** | `dotnet/KcdUs.Bot` | a stand-in second player for testing on one machine (not shipped) |

## The game link (docs/KCD1-MODDING.md has the evidence)

* **Agent to game:** the engine's remote console on `127.0.0.1:4600` (switched on by the mod's `mod.cfg`). One console command, `kcdus "<seq>~REC|f|f~REC|f"`, registered by the mod with `System.AddCCommand`, carries a packed batch of records. `CommandPacker` keeps a line under 1800 bytes and the sender spaces lines 25 ms apart (the engine drops bursts). A player's position replaces an older waiting one with the same key.
* **Game to agent:** `System.LogAlways("KCDUS|KIND|…")` into `kcd.log`, tailed every 40 ms. The log is truncated when the game starts, which resets the tail.
* **Liveness:** the agent pings every 2 s; the game logs a heartbeat every second. A connection the game once answered on and stopped answering is dropped and re-dialled after 8 s (observed: the engine stopped serving a connection after the player loaded a save).
* **The menu is a world too:** the engine runs a scene behind the main menu. The mod calls it "in the world" only when a game is started, the player exists, no load is running and **the camera is within 200 m of the player** (the menu's camera was 280 m away).
* **Timers die on every load:** the 30 Hz loop is a chain of one-shot timers that `K.kick()` re-arms from the player's load hooks and from any agent line.

## Records (agent to game)

`HELLO?` `QSNAP` `PING|n` `P|id|name|state` `PD|id` `PA` `NOTE|text` `CHAT|name|text` `PROMPT|text|seconds` `PROMPTCLEAR` `TIME|worldTime` `TP|x,y,z|yaw`

## Events (game to agent)

`HELLO|version|proto|build` `READY|0/1` `ST|pos|yaw|vel|flags|hp|stam|anim|worldTime` `HB|seq|fps|ticks|errors` `PONG|n|seq` `Q|code|started|completed|objectives` `CHAT|text` `KEY|join/stay` `GHOST|…` `TELEPORTED|…` `ERR|where|text`

## The story (host and friend)

1. The game side reports each watched quest as `Q|…`; after the world loads, 6 s of reports are a **baseline** (what the quest log already held).
2. The **host's** agent turns changes into `StoryLock.Note(code, state, end)`. On an *Enter* it starts the **period** in the `RailsRoster`, asks the relay to tell the friends (`HostEvent beat|enter|code|tier|why`, repeated every 30 s) and says it in the log.
3. Each **friend's** `RailsJoiner` turns the beat into a question (`PROMPT`), a standing answer, or the 45 s backstop (no answer = join). The answer goes to the host (`Event choice|code join/free`), repeated every 20 s while the period lasts.
4. A joined friend is **brought** (`TP` beside the host) and, in a rails section, **tethered** (warning at 90 m, pulled back at 120 m); a friend who stays is left entirely alone. A period ends 8 s after the host's last section of it ends, or when the host's beat has been silent for 90 s.

Decisions that came from tests, not the first design: an unanswered question is answered **once** (a bug sent it every second); the quest-log snapshot at load is not news; a friend's quests never decide what the host is asked.

## Tests

`dotnet test dotnet/KcdUs.sln`: relay over real loopback sockets, the Lua mod under MoonSharp with a stubbed engine (`LuaStubs.lua`), the story state machine, the whole session (a real relay, fake games and a fake clock). `tools/perf/soak.py` runs the real game.
