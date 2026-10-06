# Characters, worlds and saves in Kingdom Come: Deliver Us

Written 2026-10-06. Kingdom Come: Deliver Us is an unofficial community mod; not affiliated with or endorsed by Warhorse Studios or Deep Silver.
Asked for in the same round as the KCD2 mod's WO-157 (its `docs/WO-157-findings.md`): Linux, other characters, new/brought/returned Henrys, every quest, separate saves.

## 1. Where this mod stands on each

| # | Asked | Status in this mod |
|---|---|---|
| 1 | Linux | **Done** ([LINUX.md](LINUX.md)); not run under a real Proton |
| 2 | Multiplayer where the player is not Henry | This mod does not need to know who the player is: everybody is in their **own** game, the others are shown as presence (position, chat, the clock). Theresa's flashback (A Woman's Lot) is already a locked *period* in the gating (ten quests, one question). **Not seen in the game**: nobody has played the Theresa part with this mod |
| 3 | New Henrys, a Henry brought from another world, put back | **There is no shared world here, so this does not arise the way it does in KCD2.** Everyone loads their own save, so everyone *is* a Henry from their own world, and nobody's world is ever overwritten. Two new games started side by side are two new Henrys. Nothing to bring and nothing to put back |
| 4 | Every quest, side quests, tasks, activities, main, DLC | All **287** quest roots in the game's data are classified (28 *rails*, 62 *mixed*, 197 *open*), including the five DLCs ([quest-gating.md](quest-gating.md)). The join-or-stay question works on any of them. **Never seen with two players** |
| 5 | Host saves for both; separate saves at other times and places | In this mod every player saves **their own** game whenever and wherever they like; that is the default and the only mode. A save that holds *both* players needs a shared world |

## 2. What a shared world would take in this game (not built)

KCD2's mod has a shared world: the joiner loads the host's world, with the joiner's own character spliced into it (`WhsSave`), the host's saving locked, and every host save paired with a copy of
the joiner's character. Doing the same here needs four things, and this mod has none of them:

1. **The save format.** KCD1's saves share the **block framing and the `0XBP` MD5 footer** with KCD2's (checked 2026-10-06 on the real saves here: 844 blocks of 32 KB, the footer's MD5 stored
   at -60), but the stream inside is **not** KCD2's tag-length-value tree: it starts with a different structure and carries the save's description (`1|103|@su...`) *inside* the stream, with no
   KCD2 soul GUIDs in it. Finding Henry's record (inventory, stats, skills, perks), the world's own blocks and the integrity of a spliced result is a full reverse-engineering job (KCD2's took WO-109 to WO-112).
2. **A way to load a save from outside.** KCD1 has no console command that loads a save (verified, docs/KCD1-MODDING.md): the player would load the transferred world **by hand from the menu**.
3. **A host-only save lock and a Henry snapshot.** KCD2's come from a native plugin (a hook in the engine); KCD1's mod is a Lua pak plus the remote console. The lock and the "copy of the joiner's
   character at each host save" have no equivalent here.
4. **The in-world protocol**: who owns each NPC, quest and crime in a world two games both simulate (KCD2 needed WO-123 to WO-156).

So a KCD1 shared world is a **new subsystem**, not a port, and without a native plugin it could only be a *manual* one (host saves, sends the file, the guest loads it from the menu).
Until someone chooses to build that, the honest answer to items 3 and 5 for this game is "everybody has their own world and their own saves", and the mod makes no promise about a world two people share.
