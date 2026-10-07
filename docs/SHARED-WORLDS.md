# Shared worlds in Kingdom Come: Deliver Us

Written 2026-10-06. Unofficial community mod; not affiliated with or endorsed by Warhorse Studios or Deep Silver.

A shared world is **one world that two (or more) players each hold their own copy of**. Each of you can play it alone, whenever you like, and save it
whenever and wherever you like. When you meet again, the mod looks at both copies, the copy that is *behind* is replaced by the other (the replaced one is
kept as a backup), and each player's own Henry is carried over onto the world he lands in. Then you play together again, and part again, and so on.

This is not a shared simulation: while you play together you still see each other as presence (position, chat, the clock, the join-or-stay question for
scripted stretches) exactly as in [PLAYING-TOGETHER.md](PLAYING-TOGETHER.md). What a shared world adds is *the same world*, and the choice of who you are in it.

Everything is in the game's **Multiplayer tab → Game world** (see [MENU.md](MENU.md)).

## What each button does

| Button | What happens |
|---|---|
| **Join the host's world** | You ask the player you are connected to for their world. Their game is saved if it is running, the save (about 6 MB, a second or two over the relay) is checked and put into a free playline slot of yours, and the game loads it. Works for a world of 100 hours as well as a new one. Your own games are not touched. |
| **Start a new world together** | The host starts a new shared world and tells the other players. You all choose **New Game** in the game's menu (a free playline) and play the prologue with new characters. The first save the game writes becomes the world's copy on each computer. |
| **Play my shared world** | Loads your own copy of the shared world (the newest save in its slot), to play alone or with friends. |
| **Save the world for everyone** | Saves now and tells the friends how far your copy has got. A friend whose copy is behind takes it the next time you meet (or when they choose Join the host's world). |
| **Which Henry → the world's own / my own** | See "Henries" below. |
| **When we reconnect → further-played / host's / newest** | Which copy goes on when two copies were played apart. |
| **Send my Henry home** | Loads your own game (the one you had before the shared world took its slot) and puts the Henry you have now onto it. A copy of that save is kept first. |

## Playing apart, then together

1. You both start from the same world.
2. Each of you loads their copy and plays on, alone or with other friends. Each saves normally; nothing about saving is locked.
3. When you connect again, each computer tells the other how far its copy has got: the world's id, the play time and the time of its newest save.
4. Both computers work out the same answer from the rule you chose:
   * **The further-played world wins** (default): the copy with more hours of play. If the two are within about three minutes of play, the newer save wins.
   * **The host's world wins**.
   * **The newest save wins**.
5. The player whose copy is behind takes the other's: **automatically if they are at a menu** (and *automatic world sync* is on in the browser settings), otherwise
   the game says so and the player chooses **Join the host's world** when it suits them. Nobody is pulled out of a game they are in the middle of.
6. The copy that was replaced is moved to a backup folder, never deleted (see "Where things are").

What this means in practice: the world's story is the winner's. If you played a quest to its end alone and your friend did something else for two hours, the
longer one goes on, and your Henry (his levels and things) is carried over to it. Choose *host's world wins* if you want the host's world to always be the truth.

## Henries

The game keeps one Henry in each save, so "who is Henry in this world" is a choice made when a world is received:

* **The world's own Henry** (default): you play the Henry that is in the world you receive: for a host's 100-hour world, a copy of the host's Henry.
* **My own Henry**: first load your Henry and finish combat, dialogue or riding. Holster his weapon. The mod requests a fresh save and retains an immutable personal snapshot. It stages native stats/skills, perk records, resources and inventory/equipment in the received world before loading. Levels and inventory can decrease to match your snapshot; they are not added to the sender's character.

The destination retains its position, story/quest knowledge and companion references. Active world-linked core state, unsupported save framing and carried item instances owned elsewhere refuse the move. Other soul timers and arbitrary ability/buff states are not fully covered. See [current engine evidence and limits](ENGINE-INTEGRATION-20261007.md).

The engine must write a fresh save from the destination playline and pass progression/perk/inventory readback before the agent announces acceptance. A pending installation can be retried with **Play my shared world**. Your original saves and personal snapshot are retained. The UI's load-completed notification alone does not acknowledge success.

**Sending a Henry home** uses the same native snapshot preparation and readback, with a verified archive of the home playline's saves. An archived home must first be restored offline. The older additive card no longer handles these transfers.

## Where things are

* `%LocalAppData%\KCDUS\worlds.json`: the shared worlds this computer knows: id, name, where your copy is, how far it has got, your latest personal snapshot digest, and any pending load journal.
* `%LocalAppData%\KCDUS\world-backups\transactions\<UUID>\originals\`: verified copies of every replaced save. Personal snapshots are in `world-backups\characters\<SHA256>.whs`.
* The saves themselves are the game's: `Saved Games\kingdomcome\saves\playline0` … `playline4`.

### The five playlines

**The game has exactly five playlines** (the "Playline 1 … 5" of the New Game screen; folders `playline0` … `playline4`). A received world needs one of them
**to be empty**: the mod picks the highest empty one and takes it for that world; later copies of the same world replace what is in *that* slot (with a backup).
It never writes into a playline that holds one of your games, and a folder numbered 5 or more would stop the game from starting, so none is ever made.
If all five are in use the mod says so and does nothing: delete a game you do not need on the game's Load screen and ask again.

## What is verified, and what is not

Verified live in the retail game (1.9.8), on one computer with a stand-in second player:

* a real 52-hour world (6.1 MB) crossed the relay, was checked, installed in an empty slot and **loaded by the game** (also while another world was loaded);
* the Henry card read from that world (4.6 KB: stats, 15 skills, 95 things) and put onto another Henry (levels raised, money and things added);
* the same flows run as automated tests with two agents over a real relay (167 tests): a player behind takes the other's world, the host behind takes the friend's,
  copies that match move nothing, five used playlines refuse, the card is read before and put on after the load.

**Not seen:** two real people on two computers; the Multiplayer tab's *Game world* pages in the running game (the page machinery is proven; the new pages
were checked by tests); **Save the world for everyone** and **Start a new world together** in the running game; Linux (Proton).

**Known limits:** the prologue does not allow saving, so a world cannot be shared before it is over (the game's own rule); a *new world together* needs each
player to choose New Game by hand (the first save the game writes is picked up by itself); a world that was *modded by another mod* is copied as it is;
the received world is a **copy of a save**: if the game's version differs between the two computers the game may refuse it (both must be on 1.9.8).
