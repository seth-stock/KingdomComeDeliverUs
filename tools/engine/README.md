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
