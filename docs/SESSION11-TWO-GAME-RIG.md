# Session 11 (2026-10-12): two real KCD1 games on one computer

Two private KCD1 games now run side by side (disposable profiles, never a real save), each with its own agent, joined through a real relay into one
shared world: `tools/engine/rig.py all <clone.whs>` (launch both, load the clone, start both agents, A hosts, B joins, A shares its world, B loads it).
`rig_combat.py`, `rig_bandit.py`, `rig_provoke.py` drive shared-enemy scenarios; `riglog.py` reads either game's log.

What made two games possible: the launcher's single-instance mutex `WhKcdGameMutex` is renamed per private profile (isolation DLL); the second game
logs to `kcd(1).log` and keeps its saves in `KingdomCome(1)` (CryEngine's own second-instance naming), which the harness and agent now follow
(`KCDUS_GAME_LOG`); each agent has its own data folder (`KCDUS_DATA_DIR`), console port (`--console-port`, the second game listens on 4601) and saves
(`--saves-dir`).

## Bugs the rig found and fixed (never visible with one game and a synthetic peer)

* **Only one NPC was ever shared.** The engine's console ends a command at `;`, so every multi-NPC ownership/sample line from the agent arrived as
  its first entry plus "Unknown command" errors. Records now carry `;` as `^` (`CommandPacker.ConsoleSafe`), restored by `KCDUS_In`. After the fix:
  all 11-12 nearby NPCs owned and mirrored (host 8-10, guest 2-3).
* **"Engaged on me" never matched.** The adapter names a fight's target by the actor's own id (`human+0x30`), not the Lua entity id; the player's
  actor is its own class (vtable +0x26c3e30) that the adapter refused. The adapter now reports each actor's `self` id (players too) and Lua compares
  targets with it. The attack "mode" is a bit field, so engagement is "in combat AND targeting me", not "mode == 4".
* **Ownership flapped between the games** because any health drop near a player counted as that player's attack (guards fighting a bandit).
  Combat reports now carry `mine` (the NPC is fighting this player, per the adapter); only those make the host turn an NPC on its attacker.

## New behaviour

* **Aggro:** the host gives an NPC to the player who is fighting it (newest attacker within 25 m), unless its owner is fighting it too; it lapses after 6 s.
* **Ghost targets:** if an NPC's running copy is fighting a friend's ghost (invulnerable there), the host hands it to that friend, whose game runs it
  against the real Henry (NPCST field 12).

## Proved with two real games (rig_combat.py, 6/8 on two runs)

B's blow (15) lowered a guard in B's game and in A's (one shared health: 85.0 / 85.2); the host then gave the guard to B (B's copy ran it, A's became
B's puppet); A's blow (10) landed in both games; a bandit hurt by both died in both games.

## Not proved

An NPC actually attacking a player could not be driven from the console: scripted `DealDamage` with a player attacker does not provoke KCD1's AI, a
spawned event bandit did not seek a fight, and the adapter's native attack commands are gated (and earlier produced no damage). So the damage mirror and
the ghost-target handover are unit-tested and wired, but were not seen firing in a real fight. A person playing game A (mouse and keyboard) on the
rig would settle it.
