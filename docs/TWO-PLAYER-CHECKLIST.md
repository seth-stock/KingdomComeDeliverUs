# The first two-player run: a checklist

Nothing in this project has been played by two people. This is what to look at first, in order, and what each check proves. Do it with a throwaway save on each machine
(or a new game: the prologue's first save is enough). Write down what you see; "it worked" is not a result, "Hans appeared 4 m away and glided" is.

**Setup:** both machines have the installer's mod (`Mods\kcdus`, mod.cfg, kcdus.pak) and the same version in the launcher's title bar. The host started with **Host a game**; the friend with **Join a friend** and the host's `100.x` address.

## A. Connection (5 minutes)

1. Host: START CO-OP, START THE GAME, load a save. The launcher says "Connected as host: 1 player(s)." and the game's top bar shows nothing alarming.
2. Friend: START CO-OP, START THE GAME, load a save. Friend's launcher: "Connected as guest: 2 player(s)." Host's launcher lists the friend. Host sees "<name> joined" at the top of the game.
3. **Version refusal:** install a build with another VERSION on the friend, try to join: the friend's launcher says "The relay refused you: relay is X, you are Y". Reinstall.
4. **Password:** set one on the host; join without it: refused; with it: fine.
5. A third player and a fifth: the room holds four; the fifth is told "full".

## B. Presence (10 minutes)

6. Stand the two games at the same spot (same save point, or walk). Each sees a body beside the other. Does the body face the right way and follow when the player walks, runs, rides a horse (a horse rider's body floats? note it)?
7. Walk 300 m apart: do the bodies stay in place and update, or vanish (the agent removes none; the engine may unload the entity: it should be spawned again within 1 s)?
8. Enter a building, a cutscene, a dialogue, a loading screen on one side: the other side's body of that player disappears at "world left" and returns after.
9. Save, quit to the menu and load again on one side: the other side's launcher goes "In the main menu or loading", then the player reappears.
10. Frame rate: the launcher shows the game's fps; compare with the game without the mod.

## C. Chat and clock (5 minutes)

11. Type in the launcher's chat box on each side: the other side sees `Name: text` at the top of the game screen. A message with quotes, a pipe or a backslash arrives with those turned into spaces.
12. Set the host's game time well apart from the friend's (sleep in a bed on the host). Within about 20 s the friend's clock jumps to the host's. A dialogue in progress on the friend delays it.

## D. Join or stay (15 minutes) the point of the project

13. Host starts a **rails** quest (the Skalitz opening is the first: Unexpected Visit then Run!, see `quest-gating-table.md`; or a fight club bout; or the Rattay Tourney enrolment). The host's launcher story line names it.
14. Friend sees the question centred on the screen: "Your host entered ... Join them, or stay in the open world? F11 join, F12 stay." **Press F11 with the game in front** (the agent reads the key: a game that has F11 bound to something else does both: note it). Without pressing: after 45 s the friend is brought anyway, once.
15. F11: the friend is teleported beside the host (1.5 m east, a little apart per player), told "You joined your host.", and the host sees "<name> joined you". In a rails section: walk 100 m away: "You are straying from your host"; 150 m: pulled back.
16. Repeat with **F12**: the friend is left alone, can walk anywhere, and "F11 joins at any time" works.
17. The host leaves the section (quest completes): a friend who stayed is told "Your host's story stretch is over" about 8 s later.
18. A **mixed** quest (The Hunt Begins): asked once, never tethered.
19. **No false alarms:** play 10 minutes of ordinary open world on the host (hunt, trade, ride): the friend is never asked. If they are, note the quest code from the agent log (`the host entered q_...`): the classification in `docs/quest-gating-plan.csv` is wrong for it.
20. **Load into a rails quest:** the host loads a save made in the middle of a battle: after about 8 s the friend is asked.

## E. Robustness (10 minutes)

21. Kill the friend's agent: the host sees "<name> left"; start it again: the friend is back within seconds.
22. Kill the host's agent (the relay with it): friends say "The relay connection went away"; restart: they reconnect (every 3 s).
23. Start the game, the launcher and the agent in different orders: "Waiting for the game" then "did not answer" then "main menu" then "Connected" in the launcher, never a stuck state.
24. Leave it for an hour: the agent log (`%LocalAppData%\KCDUS\logs\agent.log`) should not be growing by more than a few lines a minute.

## F. Security (5 minutes)

25. From a **third** computer on the same network: `Test-NetConnection <friend's LAN address> -Port 4600` should fail when the installer's firewall task was ticked, and succeed when it was not (that is the hole the rule closes).
26. On the machine itself the agent must still connect (the status says "Connected").

Report: version, what failed, `agent.log`, the `KCDUS|` lines of `kcd.log` around it. Remove public IPs first.
