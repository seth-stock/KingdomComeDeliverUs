# The gating on every quest of Kingdom Come: Deliverance

Kingdom Come: Deliver Us. Unofficial; not affiliated with Warhorse Studios or Deep Silver. Written 2026-10-05.
Marks: **(data)** read in the game's own files · **(online)** read on the Kingdom Come: Deliverance wiki (kingdomcomedeliverance.wiki.gg) · **(tests)** offline tests · **(not seen)** never played with two people.

## 0. The ask and the answer

> Work out the main quests, side quests and activities of the first game, so the gating logic works on all of them: the friend joins the host
> (or not) in the missions that lock the player in, for one mission, for several, or for a period of the game.

| Asked | Done |
|---|---|
| Know every mission | The game's data lists **287 quest roots** (`Tables.pak`, `quest.xml`): 37 main-type, 115 side, 30 activities, 3 events, 22 system graphs, 80 developer tests. Titles come from `English_xml.pak`. |
| The gating works on all of them | Every quest has a **tier**: *rails* (28), *mixed* (62) or *open* (197). The state machine handles main quests, side quests, activities and the DLC chains, in the story's real order. |
| One mission, several, a period | A **period** is the set of quests that share one question: the Skalitz opening (3 quests), the monastery (2), the Vranik to Talmberg night (4), the siege (6), Theresa's flashback (10), Band of Bastards (7), a fight-club bout (1)... 60 periods in all. |

## 1. How the game is known

* **The quest table** (data): `Libs/Tables/quest/quest.xml` has `quest_name` (the code the engine's `QuestSystem` takes), a group (`Main`, `Side-Rat`, `DLC`...) and an id; `quest2skald_subchapter` + `skald_quest_string` + `text_ui_quest.xml` give the English title. 9,197 objective rows say how big each quest is.
* **The guides** (online): the wiki's page for each main quest and for about 60 side quests, DLC quests and activities, read with one rubric: *free*, *partly* (scripted scenes alternate with free time) or *forced* (a locked-in sequence), and whether there is a point of no return, a time limit, a forced fight or an area you cannot leave.
* **The plan** (`docs/quest-gating-plan.csv`): the reviewed answer for every row: kind, order, tier, period, why, DLC, source and a note quoting the guide. `tools/Build-QuestCatalog.py` refuses to build if a quest has no row, a row has no quest, a period is not named by one of its own members, or two members of a period disagree about its name.
* To change an answer: edit the plan CSV, run `python tools\Build-QuestCatalog.py`, run the tests. `--check` fails when the generated files drift (the installer's gate).

## 2. The tiers

| Tier | Means | Entered by | Tether | Idle time |
|---|---|---|---|---|
| **rails** | staged for its whole run (a battle, the monastery, a tournament, a flashback) | a main quest: its first objective; a side quest or activity: two distinct objectives | 120 m | main 40 min, other 15 min |
| **mixed** | scripted stretches alternate with free ones | three distinct objectives within 90 s | none (the ordinary freedom) | main 12 min, other 8 min |
| **open** | go where you like | never; an open main quest only says "the story moved on" | none | none |

The state machine (`StoryLock`, tests in `StoryLockTests.cs`):
* A **later main quest** that shows evidence ends the earlier section, and an earlier quest's leftover background changes are ignored (they would make the section flap).
* A side quest or activity does not interrupt a **main rails** section (its world is locked), and replaces another section only after 30 s of quiet.
* A **completed** quest leaves its section; a side quest is not re-entered for 90 s after it ended (its cleanup keeps changing things) or 10 min after it idled out.
* **Seeding:** a host who loads into a main rails quest in progress is asked once the quest-log snapshot (the first 6 s after the world loads) is over, because nothing changes to be noticed.
* **What counts as a change:** the game side polls the watched quests (six a tick, a cursor through the list: every watched quest is visited about once a second) and reports `Q|code|started|completed|objectiveIds`; a new objective id is a "state". Gating uses only the **host's** quests.

## 3. The main story (the game's own order)

| # | Quest | Title | Tier | Period |
|---|---|---|---|---|
| 1 | q_skalitz | Unexpected Visit | mixed | the Skalitz opening |
| 2 | q_escapeToTalmberk | Run! | rails | the Skalitz opening |
| 3 | q_returnToSkalitz | Homecoming | rails | the Skalitz opening |
| 4-6 | q_awakeningInRattay, q_basicTraining, q_nightsWatch | Awakening, Train Hard Fight Easy, Keeping the Peace | mixed | Rattay |
| 7 | q_huntPtacek | The Prey | mixed | its own |
| 8 | q_massacre | The Hunt Begins | mixed | its own |
| 9 | q_neuhof_colliers | Ginger in a Pickle | open | |
| 10 | q_auschitz | Mysterious Ways | mixed | its own |
| 11 | q_ledecko | On the Scent | mixed | its own |
| 12 | q_samopesh | My Friend Timmy | open | |
| 13-14 | q_pribyslav, q_pribBattle | Nest of Vipers, Baptism of Fire | mixed, rails | Pribyslavitz |
| 15 | q_captiveInMerhojed | Questions and Answers | mixed | its own |
| 16 | q_counterfeiters | All that Glisters | mixed | its own |
| 17 | q_recruiters | If You Can't Beat 'em | mixed | its own |
| 18-19 | q_enteringTheMonastery, q_searchForSaint | Poverty, Chastity and Obedience; A Needle in a Haystack | rails | the monastery |
| 20-23 | q_infiltrationAndCapture, q_counterOffensive, q_talmberkBarbican, q_night_rescue | The Die is Cast, Payback, Out of the Frying Pan, Night Raid | mixed/rails | Vranik to the Talmberg night |
| 24-29 | q_talmberkPrepare, q_konradKyeser, q_istvans_reinforcements, q_defence, q_conquest, q_epilogue | Siege, Rocketeer, Cold Steel Hot Blood, Family Values, An Oath is an Oath, Epilogue | mixed/rails | the siege |
| 28 | q_revenge | Vengeance | open | the umbrella quest (never completes in the first game) |

The full 287 are in `quest-gating-table.md` (generated) and `quest-catalog.csv`.

## 4. The side quests, activities and DLC

* **Locked-in or timed side quests** (rails or mixed: 45 side quests and 11 activities): Next to Godliness (a night that must end by morning), In the Cloister (a daily monastic schedule), the Talmberg Horse Race and the Rattay Tourney (events you cannot leave), the three fight clubs, The King's Silver (the mine), Playing with the Devil (the ritual), In God's Hands, Money for Old Rope, Pestilence, Courtship (dates at fixed hours), Hare Hunt / Sheep in Wolf's Clothing (mutually exclusive) and the like. **(online)**
* **Activities:** archery contests and the tournaments are staged; the rest (hunting, thievery, camps) are open. **(data, online)**
* **A Woman's Lot (Theresa):** the player is locked in as Theresa until the flashback ends: *rails*, one question for all ten quests. **(online)**
* **Band of Bastards:** patrols, an ambush, a mill attack and a parley with a locked area: *mixed* with the parley *rails*, one question. **(online)**
* **From the Ashes, Treasures of the Past:** open (management and a treasure hunt). **(online)**
* **The Amorous Adventures of Bold Sir Hans Capon:** mostly *mixed* (timed night scenes, a tournament). **(online)**
* The 80 developer tests and the 22 system graphs (trackers, quest-giver hubs, tutorials) are open and never ask anything.

## 5. Verification

* `QuestCatalogTests` and `StoryLockTests` (tests): the catalog holds 287 quests; the main story is the wiki's 29 titles plus Rocketeer, in order, never going backwards; every locked quest belongs to a period named by one of its members; **every locked quest of the game enters its own section when fed enough objectives** (a loop over all 90); the flashback and band chains share one question.
* The live quest reads (seen): in the throwaway new game `QuestSystem.IsQuestStarted('q_skalitz')` was true and `GetActiveObjectives` returned its objective ids; the mod's watcher reported the real state of the quests that already had one (a dozen at the start of the new game).
* Not seen: a host playing through a rails quest with a friend. The classification is a judgement; the checklist (`TWO-PLAYER-CHECKLIST.md`) lists the first things to look at.

## 6. Limits

* "On rails" is not a flag in the game; it is read from the guides and the data. A mixed quest asks once for the whole quest.
* The wiki pages for a few quests returned 404 (Divine Retribution, Sick Bastard, Do Me a Favour - Punch Me!, At Your Service My Lady, Foreign Steel...); those were classified from the quest data and the group they sit in, and are marked `data` in the plan.
* The game's data has quests that are cut content (the `Beta`, `Tech Alpha` and `Code Test` groups) and a few that are DLC-only; the plan keeps them as rows so nothing in the game is unknown.
