# How Kingdom Come: Deliverance (the first game) can be modded

Kingdom Come: Deliver Us. A modified port of Kingdom Come: Together (official repository of that project:
https://github.com/DeepFriedDepp/KingdomCome-Together). Unofficial; not affiliated with or endorsed by Warhorse Studios,
Deep Silver or PLAION.

Written 2026-10-05 against the Steam build **1.9.8 (404-504czj4)**. Marks: **(verified)** seen working in the real game on this
machine · **(read)** read in the game's files or a public guide · **(not seen)** built or assumed, not observed.
Everything here was found with a single running game; nothing has been seen with two players.

## 0. The short answer

KCD1 has **no Modding Tools build, no REST API and no reflection layer**, which is everything the KCD2 mod stood on. What it has is
a script engine that is far more open than KCD2's, and a leftover CryEngine **remote console**. Together they are enough for a
co-op mod that needs **no native plugin and no DLL injection**:

| Need | KCD2 mod used | KCD1 mod uses |
|---|---|---|
| Put code in the game | Lua in `Mods\kdcmp` + a native plugin injected into the process | Lua only, in a `.pak` under `Mods\kcdus` **(verified)** |
| Agent to game (commands) | REST API on `localhost:1403` | CryEngine remote console on `localhost:4600`, a console command we register **(verified)** |
| Game to agent (events) | REST polling + a named pipe from the plugin | the game's own log, `kcd.log`, tailed by the agent **(verified)** |
| Change the world (NPCs, health, time) | native hooks (Lua writes were inert) | plain Lua: the API is writable **(verified in part, §5)** |

## 1. How a mod is installed and loaded

* The game reads `<game>\Mods\<id>\`. Each mod folder holds `mod.manifest`, optionally `mod.cfg`, and a `Data\` folder. **(verified)**
* **Only `.pak` files in `Mods\<id>\Data\` are mounted. Loose files in `Data\` are ignored.** A pak is a plain zip (store or deflate) renamed
  `.pak`; entry names use forward slashes. The log says `[Mod] Opening paks in mods/<id>/data/*.pak` and `Pak 'mods\<id>\data\<id>.pak' is opened`. **(verified)**
* **The mod's init script is `Scripts/Mods/<id>.lua` inside the pak**, run once at start (`Loading lua init script for mod <id> ...`).
  There is no `Scripts/Startup/` folder in KCD1 (that is a KCD2 convention). **(verified)** It can `Script.ReloadScript("Scripts/Mods/<id>/other.lua")`
  to load more files from the same pak.
* `mod.cfg` is executed as console configuration: `log_EnableRemoteConsole = 1` there is how the remote console is switched on. **(verified)**
* Mods load in alphabetical order (`mods/mod_order.txt` not found -> `Loading mods in alphabetical order`). **(verified)**
* A game update can disable mods (`mod_status.xml` carries `modFolderWasMoved` / `playerWasNotified`). 1.9.8 is the final build. **(read)**
* `mod.manifest` (this is what the game accepted): `<kcd_mod><info><name/><modid/><description/><author/><version/><created_on/></info></kcd_mod>`;
  with no `<supports>` the log says `has no version restrictions` and the mod is enabled. **(verified)**
* Mods are noted in saves (`wh_mod_ShowUsedModsInSaveTooltip`); saves made with a mod load without it. **(read)**
* The game must be started with **Steam running first**, or it stops at a licence dialog; the Steam entry's Play button works for KCD1
  (it is the retail exe, not a setup tool as it is for KCD2's Modding Tools). The exe is `Bin\Win64\KingdomCome.exe` (working directory: the install root). **(verified)**

## 2. The Lua environment

Lua 5.1 inside CryEngine's script system.

* **Missing:** `io` (nil), `os.clock`, `os.getenv`, `socket`, `jit`, `ffi`. A script cannot open a file or a socket. **(verified)**
* **Present:** `os.time`, `loadstring`, `loadfile`, `dofile`, `require`, `package`, `debug`, `string`, `table`, `math`, `coroutine`;
  `Script.LoadScript/ReloadScript/ReloadScripts/UnloadScript/SetTimer/KillTimer/SetTimerForFunction`; `System.*` (117 functions: `SpawnEntity`,
  `RemoveEntity`, `GetEntityByName`, `GetEntitiesInSphere`, `GetCVar/SetCVar`, `AddCCommand`, `ExecuteCommand`, `LogAlways`, `RayWorldIntersection`,
  `DrawLabel/DrawText/ProjectToScreen`, `GetViewCameraPos/Dir`); `Game.*` (`SendInfoText`, `ShowNotification`, `QuickSave/QuickLoad`,
  `AddSaveLock/RemoveSaveLock`, ...); `Calendar.*`; `QuestSystem.*`; `Utils.*`; `XGenAIModule.*`; `AI.*` (206 functions); `CryAction.*`. **(verified: listed from the running game)**
* `Script.SetTimer(33, f)` re-armed from `f` runs at about 30 Hz: 150 ticks took 5 s. A mod can therefore run its own update loop. **(verified)**
* A Lua error inside a mod is logged as `[Error] Lua error. Please run with -lua_storedebug 1 ...` with **no line number** unless the game is started with
  `-lua_storedebug 1`; the launcher passes it in debug runs.

## 3. Getting commands in: the remote console

`log_EnableRemoteConsole = 1` makes the game listen on **TCP 4600, all interfaces** (`Remote console listening on: 4600`). There is **no allow-list cvar**
in this build, so anyone who can reach the port can run console commands; the installer therefore adds a Windows Firewall rule that blocks the
port from every address except the machine itself (§8). **(verified: the port listens on 0.0.0.0)**

Wire format (found by experiment): a message is a one-character type, the text, and a NUL. **(verified)**

| Direction | Bytes | Meaning |
|---|---|---|
| server to client, on connect | `'1' 00` | hello (a "request" with no text) |
| client to server | `'5' <console line> 00` | run this line in the console |
| server to client | `'6' <text> 00` | autocomplete suggestions (noise: ignore) |

* The server **does not forward log output** to the client, so the game-to-agent direction cannot use this socket (§4).
* A console line starting `#` is Lua, but **only when the game runs with `-devmode`**. Without it `#` lines are refused. **(verified with -devmode; the refusal without it is not seen)**
* **A mod can register its own console command from its init script, and that works without `-devmode`:** `System.AddCCommand("kcdus", "KCDUS_In(%line)", "help")`.
  `%line` is replaced by the **whole argument text as a quoted Lua string**, so the payload must contain no `"` and no `\`. The command takes any number of
  arguments when the code contains `%line`; without a placeholder extra arguments are refused (`Too many arguments for: ...`). **(verified)**
* **Limits (measured):**
  * length: 3000 bytes got through, 6000 did not. The agent keeps every command under **1800 bytes**.
  * burst: 40 commands sent back to back kept 8; 500 kept 133. Sent **at least 5 ms apart** (181 per second) all 40 of 40 arrived. The agent
    paces to one command per 25 ms and packs several messages into each.
  * the console runs on the game's main thread, one command per frame at most.

## 4. Getting events out: the log

`System.LogAlways("KCDUS|...")` writes one line to `kcd.log` in the game folder, immediately. **(verified)** The agent tails that file (a line starting
`KCDUS|` is ours; everything else is ignored). The log is truncated when the game starts, so the agent resets its offset when the file shrinks.
A line is a few hundred bytes at most; the mod writes about 20 a second. `System.Log` (lower verbosity) is not used.

## 5. What Lua can read and write (live, in the new-game save)

| Area | Result |
|---|---|
| Position | `player:GetWorldPos()` -> a vector; `player:GetWorldAngles()`; `player.actor:GetHeadDir()` **(verified)** |
| Movement | `player:GetVelocity(v)` (walk 3.2 m/s, run 5.1 m/s); `player.actor:GetCurrentAnimationState()` -> `MotionIdle` / `MotionMovement`; `AI.GetStance(player.id)` **(verified)** |
| Vitals | `player.soul:GetState('health')` 100, `'stamina'` 105; `soul:SetState`, `soul:DealDamage`, `actor:SetHealth` exist **(read; writes not yet exercised)** |
| Time | `Calendar.GetWorldTime()`, `SetWorldTime` exist **(read)**; `Calendar.GetWorldTime()` read 36000 **(verified)** |
| Weapon, horse | `player.human:IsWeaponDrawn/IsMounted/GetHorse/DrawWeapon/Mount/PlayAnim/SetAnimMotionParam` exist **(read)** |
| Spawn a body | `System.SpawnEntity{class="NPC", name=..., position=...}` returns a table; a human NPC (bald, plain shirt) appears at that spot **(verified)**; `class="DummyPlayer"` returned nil; `InventoryDummyPlayer` spawned **(verified)** |
| Move the body | `entity:SetWorldPos`, `entity:SetWorldAngles` work; moved from a 33 ms timer the body follows. Its animation state stays `MotionIdle` (it glides) **(verified)**. `AI.GoTo` does nothing for these NPCs **(verified)** |
| Quests | `QuestSystem.IsQuestStarted/IsQuestCompleted/IsQuestActivated/IsQuestAvailable(code)` and `GetActiveObjectives(code)` -> `{index=objectiveId}` **(verified)**; `StartQuest`, `CompleteObjective`, `ResetQuest` exist **(read)** |
| Remove | `System.RemoveEntity(id)` **(verified)** |

KCD1's Lua is therefore **writable** where KCD2's was read-mostly: that is why this port needs no native code. What it cannot do is any of what the native
plugin did to the engine's own systems (a custom animation graph for the ghost, a per-hit damage hook). §7 lists what the port keeps, changes and drops.

## 6. Saves and loading

* Saves are `Saved Games\kingdomcome\saves\playline<N>\<name>.whs`. **There is no console command to load one** (`load`, `playline`, `wh_sys_loadGame` are all
  `Unknown command` in the retail build); a save is loaded from the main menu. **(verified)** The mod therefore never loads a save for the player: the host loads
  their own; a joiner loads the co-op save the launcher placed for them (§7). The menu lists playlines by their folders.
* The game writes `exit.whs` and autosaves into the playline in use. A "New Game" asks for a playline and a mode (Normal/Hardcore) and writes only into the chosen
  empty playline. **(verified)**
* Henry's own state in a save is what the player's character is; the mod never edits a save file in place.

## 7. What the port keeps, changes and drops

**Kept** (same design as Kingdom Come: Together, same names where possible): the relay and the agent as separate processes; the launcher with Host and Join; the
version handshake (a different release is refused); presence (each player sees the others as a body that follows them); chat; the shared clock; and the
**join-or-stay choice** with the gating on every quest of the game (`docs/quest-gating.md`).

**Changed:** the transport (§3, §4); the other players are plain NPC bodies moved by script; **everybody loads their own save** (there is no load command, and nothing
is mirrored from the host's world).

**Dropped** (it needed the native plugin or an engine-level hook): per-hit combat replication, NPC claim and drive, the dice minigame replication, the voice layer,
quest mirroring, loading the host's world. `docs/KNOWN-LIMITS.md` lists them.

**Found while building it (all seen in the real game):**
* the main menu is a live scene with a player entity and `IsGameStarted() == true`: "in the world" needs the camera to be near the player (ARCHITECTURE.md, "The game link");
* every script timer is dropped when a save loads: the loop re-arms from `Player.OnLoad` and from any agent line;
* `AI.GoTo`, `human:PlayAnim`, `human:SetAnimMotionParam` and `UseMannequinAGState` do nothing useful on a script-spawned `NPC`: its animation state stays `MotionIdle`;
* the engine can stop serving a remote-console connection that was fine before a load; a second connection works. The agent drops and re-dials a connection the game stopped answering on;
* the engine serves several remote-console clients at once.

## 8. Safety

* The remote console is a local control surface. The installer's optional task adds a Windows Firewall rule (`tools/Harden-Firewall.ps1`) that blocks inbound TCP 4600 from every IPv4 address except 127.0.0.0/8, so
  other computers cannot send the game console commands while the agent on this machine still can. The agent only ever connects to `127.0.0.1`, and the
  mod's `mod.cfg` switches the console on whenever the mod is installed (the game offers no allow-list). **(the rule is not seen on a second machine)**
* The commands the mod adds: `kcdus` (all agent traffic), `kcdus_say`, `kcdus_join`, `kcdus_stay`, `kcdus_status`, `kcdus_on`, `kcdus_off`. None can run arbitrary Lua: the payload is parsed as text, and a record kind the mod does not know is ignored.
* The mod ships none of the game's files.

## 9. Sources

* The game's own files, read on this machine: `Bin\Win64\WHGame.dll` (strings: `mod.manifest`, `mod.cfg`, `mod_order.txt`, `scripts/mods/%s.lua`,
  `log_EnableRemoteConsole`, `Remote console listening on: %d`), `Data\Scripts.pak`, `Data\Tables.pak`, `Data\GameData.pak`, `Localization\English_xml.pak`.
* KCD Coding Guide (benjaminfoo): https://github.com/benjaminfoo/KCD-Coding-Guide (the `Mods\` layout, `#` console Lua, `-devmode`).
* Nexus Mods wiki, "Modding guide for KCD": https://wiki.nexusmods.com/index.php/Modding_guide_for_KCD
* The Kingdom Come: Deliverance wiki (quest pages): https://kingdomcomedeliverance.wiki.gg/
