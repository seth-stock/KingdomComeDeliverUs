# The frame-rate soak

Kingdom Come: Deliver Us 0.1.0. Unofficial; not affiliated with Warhorse Studios or Deep Silver.

**Result: PASS.** With the mod running and a second player's body in view, the real game's frame rate is **3.5% below vanilla** (limit 5%), its worst seconds **4.3% below** (limit 10%), with **0 script errors**.

| | mean fps | median | 5th percentile | worst second | seconds measured |
|---|---|---|---|---|---|
| vanilla (2 runs: 96.07, 96.48) | 96.3 | 96.3 | 94.8 | 93.0 | 478 |
| with the mod + a second player (2 runs: 92.98, 92.85) | 92.9 | 93.0 | 90.8 | 86.8 | 478 |

* **Noise floor:** the two vanilla runs differ by 0.4%; the two mod runs by 0.1%. The measured gap (3.5%) is well outside it.
* **Machine:** one PC (20-thread Intel Core Ultra 7 265F class CPU, 1080p, the game's own settings), 2026-10-05, game 1.9.8. One save: the throwaway day-0 new game. Not a benchmark of anyone else's machine.

## How it is measured (`tools/perf/soak.py run`)

* The real game, started with `-devmode` so the same Lua sampler can be injected through the remote console in both configurations. The sampler reads the engine's frame counter once a second (`SOAK|fps|frames|seconds`).
* **Vanilla, mod, vanilla, mod** (alternating), each run a fresh game start, `Continue` into the same save, 1.5 minutes warm-up, then 4 minutes measured. The verdict compares the means of the two vanilla and the two mod runs and prints the spread between the vanilla runs as the noise floor.
* **The mod run carries the real workload:** a relay, a bot host (a second player who orbits Henry and keeps the clock), and the guest agent: Henry's game samples himself 10 times a second, moves the other body at 20 Hz, polls the quests, and receives console lines from the agent.
* The vanilla run moves the mod folder out of `Mods` (and puts it back at the end).
* **The guard:** the soak refuses (kills the game) unless the loaded save is the day-0 throwaway (world time under 200000). It never touches a real save.
* Pass needs: mean within 5%, 5th percentile within 10%, no script error. It prints the numbers either way and never loosens itself. `build\Build-Installer.ps1` refuses to build unless `tools/perf/soak-record.json` says PASS for this version **and for exactly this mod pak** (its sha256 is recorded).

## What happened on the way (kept, because it changed the method)

1. **First soak: FAIL (-6.4% mean).** One vanilla run then one mod run: 98.5 vs 92.2 fps. Not loosened.
2. The mod's work was cut anyway (quests polled 2 at a time instead of 6, the other body moved at 20 Hz instead of 30, the world check cached): 92.8 fps, still -5.9%.
3. **A micro-benchmark in the real game** timed each piece of the mod's loop: a position read 0.5 microseconds, a whole player sample 4.8, a quest read 1 to 5, a log line 1.6, an idle tick 0.4. In total about a tenth of a millisecond per second: the Lua cost is negligible. The 4 to 6% gap could not be the Lua.
4. **The mod with no second player measured 94.4, then 94.8; vanilla re-measured later the same day gave 94.8** (not 98.5). The first vanilla number was a faster session: **the game's frame rate drifts by a few percent between launches**, so one A-then-B run cannot tell an overhead from drift.
5. The soak was rebuilt to alternate (above). Its result is the table at the top. The second player's body costs about 1.5 to 2% of the 3.5% (94.4 without it, 92.8 with it, same session); the rest is the mod's mount, timers and console connection.

## Limits

* One machine, one save, 4 minutes per run: a four-hour session and a crowded town are not measured. The workload had one second player; four players is not measured.
* The measured window is a hut interior at a fixed spot: draw-call-heavy places (Rattay at noon) were not measured.
* The agent, relay and bot ran on the same CPU as the game in the mod runs; on a real play session the agent runs on the same PC, the relay on the host's.
