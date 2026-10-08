# Private KCD1 engine probes

These are developer experiments. They are not loaded by `kcdus.pak` and are not
a multiplayer implementation. Some deliberately retained experiments crashed
the private game process; do not run them in a real save/profile or run all
probes as a batch.

Build `native/ProfileIsolation.vcxproj` with Visual Studio MSBuild, Release/x64.
Its DLL goes into ignored `_work/engine-native`. Use 64-bit Python on Windows.
The read-only disassembly tool additionally uses Capstone.

```
python tools/engine/engine_session.py launch <disposable-save.whs>
python tools/engine/engine_session.py verify
python tools/engine/engine_session.py key Escape
```

Wait until the menu has initialized before sending `key Enter`. The profile has
only the cloned save, so Continue selects that clone. Check
`world_readback.lua` for `world-ready|true` before mutation experiments.

```
python tools/engine/engine_session.py file tools/engine/world_readback.lua
python tools/engine/engine_session.py file tools/engine/bindings.lua
```

The harness refuses probes unless the log confirms the private folder, real
save hashes match, its unique owned process still exists and that process owns
the remote-console listener. Session metadata and game-root junctions live
under `_work`. Never recursively delete a game-root junction tree.

The test-only DLL hooks folder lookup in the owned process and its dynamically
loaded engine. It does not modify the Windows profile, other processes, or game
DLLs/executables on disk. Cloud saving must report zero before any test save.
The shadow root references the user's installed game data; no game data is
redistributed.

For a smoke test through the production C# startup path, build
`NativeStartSmoke/NativeStartSmoke.csproj` and the runtime engine adapter, then
set `KCDUS_PROBE_NATIVE_START=1` before `launch`. The helper refuses any root
outside the harness's UUID-marked private profile. `KCDUS_PROBE_ITEMS=1` is an
optional read-only native-layout experiment for the exact gated engine hash;
do not combine it with the runtime adapter. Its raw metadata stays under `_work`.

`runtime_walk_probe.lua`, `runtime_outfit_probe.lua` and `outfit_probe.lua`
mutate only disposable NPCs in a verified private loaded world. They are not
included in the mod pak. `inventory_readback.lua` is read-only.

Read-only tools: `search_scripts.py`, `animation_catalog.py`,
`binding_disasm.py`, `save_layout.py`, `soul_layout.py`, `bindings.lua`,
`item_fields.lua`, `traits_readback.lua`, `world_readback.lua`.

**Known dangerous/rejected experiments:** `goalpipe_probe.lua`,
`npc_channel_probe.lua`, `remove_proxy.lua`, and negative XP in
`character_probe.lua`. `player_proxy_probe.lua` demonstrates animation but
still lacks safe actor cleanup. `proxy_physics_probe.lua` did not disable
physics. A successful `pcall` does not prove any of these operations worked.

`save_traits_roundtrip.lua` and `proxy_save_probe.lua` write only to the verified
private profile. Their guards also require a loaded world and disabled cloud
saving. Do not substitute an unguarded console client for this harness.

`LiveWorldSmoke` exercises production world transfer/personalization/load/native-save readback with one real disposable game and a synthetic sender. Verify the owned process first, build this .NET project, then pass `_work/engine-current.json` and a cloned source world to its DLL. It requires the exact private folder in the log, matching process/console ownership and unchanged real-save hashes. It also retries an existing pending installation. Run only one smoke agent at a time; wait for it to exit before rebuilding its binaries.

`npc_damage_probe.lua` temporarily changes one private NPC's damage path and restores health; it is not in the mod pak. Native readback, rather than `pcall` success, determines whether damage worked. `perk_portability_probe.py` reads local definitions against cloned serialized perk records and extracts no game assets. Current positive/negative results are in `docs/ENGINE-INTEGRATION-20261007.md`.

**Which mod does the private game run?** The one in the REAL game folder (`<game>\Mods\kcdus`), not the copy under the session's `game-root`
(the engine resolves `Mods` relative to the real install, whatever `-root` says). A build under test therefore has to be installed into the game
folder (back up `kcdus.pak` and `kcdus-ui.pak` first) before `launch`; the harness does not do that for you. `KcdUsAgent --build-ui --game-dir X` writes
into the game folder it FINDS (a folder that is not a game is ignored and the real one is used), so build the UI pak with a junction-only stand-in that
contains `Data` and `Bin\Win64\KingdomCome.exe`, and remove the junctions with `cmd /c rmdir` (never `rm -rf`).
Remote-console lines for the mod need the agent's sequence prefix: `KCDUS_In('1~FREEZE|1|600')`.
