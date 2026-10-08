# Session 3 results (2026-10-08): KCD1 shared fights, loot and pausing

Playtest build. VERSION stays 0.1.0 (the user's call); tell downloads apart by release tag, commit and SHA-256. Nothing here has human acceptance; nothing was played by two people.

## What was wrong, what is now true

* **"Damage has no effect" was a probe error.** `soul:DealDamage(stamina, health, attackerId, suppressHitreaction, bodyPart)` was called as `(3, 0, ...)`: 3 stamina and 0 health. With health damage the ordinary path lowers health (readback −5 for 5) and a lethal blow kills (health 0, `IsDead()`, corpse inventory readable). Probes: `tools/engine/damage_probe2.lua`, `damage_probe3.lua`.
* **Shared fights (outcomes).** `mod/kcdus/lua/combat.lua` + `OutcomesCoordinator`: each machine reports health lost and deaths of the NPCs near its own player; the others apply them to their own copy of that NPC, dealt the ordinary way with no attacker (no kill credit, no crime, no AI provocation). Damage is additive and order-free. NOT shared: where NPCs walk, whom they target, whether they notice a player.
* **Shared loot (corpses and stashes).** `loot.lua`: a guest's take (the loot screen already moved the item) is an ask; the host decides against its own copy and answers ok/gone; on gone, or no answer in 20 s, or before a world save, the item is taken back off the guest. The host's decision is written to the hash-chained `OperationJournal` before the answer; a repeated ask gets the same answer; an ask interrupted by a restart is never granted again. Stashes have no names, so they are known by position. NOT covered: items on the ground, saddlebags, shops, anything an NPC does to a body.
* **Safe shared pause.** The pause (ESC) menu raises UI events to Lua (`MP_MenuWatch` flow graph, UI pak revision 7). A friend's open menu holds MY world: `t_scale` 0.001 (not 0: at 0 no Lua timer fires and `os.time()` stands still, so no lease could exist) plus a frame-counted lease inside the game that lets go by itself if the agent stops renewing it. The agent renews every two seconds; silence for six seconds, 15 minutes, leaving, or the option off lets go at once. A player's OWN menu still pauses their own game natively: the UI ResumeGame node does not undo it (tested live), so the option is "may a friend's menu hold my world", default shared.
* **Switches:** Multiplayer tab (Shared fights and loot; Pausing), browser settings, `kcdus_shared on|off`, `kcdus_pause_mode shared|off`, `--no-shared-outcomes`, `--no-shared-pause`.
* **When it is on:** only while a friend is in the SAME shared world (each machine announces the id of the world it has loaded). The room is "partly shared": `authority.combat` and `authority.loot` engine-verified, `authority.quest` candidate (the host's quest progress follows one way while shared outcomes are on), `authority.npc` candidate/absent. Never "shared simulation".

## Evidence

* Private real engine, synthetic second player, the installed final bytes (`kcdus.pak` sha256 `b56f742bae54977ab9dffcbe985f60f5373a770ee20ef0406377c446fef8e8de`): `tools/engine/shared_outcomes_live.py` (14 checks) and `shared_outcomes_live2.py` (12 checks). One run's corpse check failed only because the ragdoll had thrown the body more than 9 m away; with the body at the player's feet the ask and the take-back worked (the script now puts the body back). Real saves: 260/260 original .whs hashes unchanged by the harness and no save of the user's changed or missing (profile and shader files only).
* Automated: 322 tests (agent, relay, Lua modules against the stubbed engine, the freeze lease, the outcomes coordinator, the pause coordinator, two-player sessions through the real relay).
* Harness fact: the private game runs the mod from the REAL game folder (`Mods\kcdus`), not the copy under the session root; the build under test must be installed there first (`tools/engine/README.md`).

## Packages (compiled 2026-10-08)

| Package | SHA-256 |
|---|---|
| `KingdomComeDeliverUs-Setup-0.1.0.exe` | `FD1B31710A26CF4B87E5FC18FF9142ECB683F2E163EF44E6B0099B26F4FF2935` |
| `KingdomComeDeliverUs-0.1.0-for-friends.zip` | `41BC1AAD095DC46862160574586361539398193EFB5C09AE34B2223EB7E1492F` |
| `KingdomComeDeliverUs-Linux-0.1.0.tar.gz` (experimental) | `92A50FB7BDEB91460DE43465DD6474691728BA4BBBDB1628E777B8A55202B2BC` |

The frame-rate soak gate was skipped (`-SkipSoakGate`): not release-grade. The Linux archive built in WSL and its internal SHA256SUMS validated; nothing was run under Proton, and the Windows startup adapter paths are not proved under the native Linux agent.

## Still not done

Shared NPC AI (movement, targeting, noticing); items on the ground, saddlebags, shops; quest rewards and spawns; stopping a player's own pause menu from pausing their own game (needs a native hook for the retail engine); the live checkpoint barrier for KCD1; two-computer, four-player and multi-hour acceptance (`HUMAN-ACCEPTANCE-TESTS.md`, T-43 to T-56).
