# The frame-rate soak

Kingdom Come: Deliver Us 0.1.0. Unofficial; not affiliated with Warhorse Studios or Deep Silver.

**Result: PASS.** Inside one game session the mod's loop costs **0.6% of the frame rate** (limit 3%) and **1.4% in the worst seconds** (limit 5%), with **0 script errors**.

| the mod's loop | mean fps | median | 5th percentile | seconds measured |
|---|---|---|---|---|
| ON (sampling, the other body, quest polling, agent traffic) | 93.8 | 94.0 | 91.6 | 311 |
| OFF (`kcdus_off`) | 94.4 | 94.5 | 92.9 | 311 |

12 windows of 60 s, alternating on and off (the first 8 s of each window not counted), after 2 minutes of warm-up, on 2026-10-05, game 1.9.8, one PC (1080p), the throwaway day-0 save.
Window means: on 95.0, 93.2, 94.8, 94.4, 92.1, 93.3; off 95.4, 94.4, 94.2, 94.3, 94.5, 93.9.
It agrees with a micro-benchmark taken in the real game: a position read 0.5 microseconds, a whole player sample 4.8, a quest read 1 to 5, a log line 1.6, an idle tick 0.4: in total about a tenth of a millisecond per second.

## How it is measured (`tools/perf/soak.py run --onoff`)

* The real game with `-devmode` (so a Lua frame counter can be injected through the remote console), `Continue` into the day-0 throwaway save, then a relay, a **bot host** (a second player orbiting Henry, its clock held constant) and the guest agent run beside it: the real workload.
* The mod's own `kcdus_off` and `kcdus_on` commands stop and start its loop **in the same session**, alternating every minute. Nothing about the world differs between the windows, and slow drift of the machine hits both states equally.
* The other player's body stays spawned in both states, so this measures the loop, not the body (a feature of the mod).
* **The guard:** the soak refuses (kills the game) unless the loaded save is the day-0 throwaway (world time under 200000). It never touches a real save.
* Pass needs: mean within 3%, 5th percentile within 5%, no script error, enough samples, and no stalled second (a frame window at more than 1.5 times the median means the game stalled and caught up: the soak calls itself **inconclusive** and does not pass).
* `tools\Build-Installer.ps1` refuses to build unless `tools/perf/soak-record.json` says PASS for this version **and for exactly this mod pak** (its sha256 is recorded).

## What happened on the way (kept, because it changed the method)

1. **First soak, one vanilla launch then one mod launch: FAIL, -6.4% mean** (98.5 against 92.2 fps). Not loosened.
2. The mod's work was cut anyway (quests polled 2 at a time instead of 6, the other body moved at 20 Hz instead of 30, the world check cached): still -5.9%.
3. **A micro-benchmark** (above) showed the Lua cost is negligible, so the gap could not be the Lua.
4. **Comparing launches is unreliable on this machine.** The same vanilla game measured 98.5, 94.8, 96.1, 96.5 and later 105.9 and 96.6 fps; the mod without a second player measured 94.4 and 94.8. A launch-to-launch comparison has a noise floor of several percent (sometimes 0.4%, sometimes 9%), larger than the effect being looked for.
5. An alternating vanilla / mod / vanilla / mod soak passed once (-3.5% with a noise floor of 0.4%). **That pass is withdrawn:** the agent in it was not being heard by the game (the 21-commands-per-connection bug, see `KCD1-MODDING.md`), so the workload was not the real one. Later alternating runs were **inconclusive** (noise floors of 9% and 12%, a mod "faster" than vanilla by 19% to 52%): once the agent worked, it moved the game's clock to the host's daytime in the mod runs while vanilla stayed at night, and the game stalled in two runs when the machine was in use. The soak now refuses such runs.
6. The method was changed to the in-session on/off comparison above, which cancels both the launch-to-launch drift and the time-of-day difference.

## Limits

* **The mod's loop is measured, not its mere presence.** What a mounted pak, the open remote-console port and one idle script timer cost when no one is playing together was measured only roughly (the mod without a second player: 94.4 and 94.8 fps against a vanilla 94.8 in the same hour: within the noise).
* **The other player's body costs frame rate** (about 1.5 to 2% in an early same-session comparison: a second animated character on screen). It is the point of the mod.
* One machine, one save, 12 minutes: not a four-hour session, not a crowded town, not four players. The window is a village outdoor spot at night.
* The agent, relay and bot ran on the same CPU as the game; in a real session the relay runs only on the host's PC.
