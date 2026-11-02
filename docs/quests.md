# Quests

The classic quests end to end: the rules of every quest family (`Shared/PristonTale.Simulation/Quests`), the server
authority that runs them on the world tick and stores them before they apply (`QuestTransactions`, `QuestAppService`,
the field's kill credit, timers and arena duels), the quest messages, the quest journal in the character record, the
quest data the importer reads, and the client's quest dialog, quest window, class-change button and quest timer icon.

## Read this when

- Changing a quest step, requirement, hunt, item, reward or dialog document.
- Changing how the class tier advances: the tier moves only through the rank-up quests described here.
- Debugging a quest that is not offered, refuses a follow-up (`Stale`), loses a kill count or never hands out its item.
- Changing the quest arena, a duel opponent, or travel to and from the arena.
- Changing the quest window, the quest dialog, the confirmation boxes or the quest timer icon.
- Adding a quest-related column, monster key or item variant.

## Code map

| Path | Responsibility |
|---|---|
| [QuestCode.cs](../Shared/PristonTale.Simulation/Quests/QuestCode.cs) | `QuestCode` (persisted and on the wire), `LevelQuestLog` and `ElementaryQuestLog` bit logs, `QuestCodes` helpers |
| [QuestJournal.cs](../Shared/PristonTale.Simulation/Quests/QuestJournal.cs) | `ActiveQuest` (the single running quest), `QuestTimerKind`, `QuestJournal` (logs, finished history, applied operations, revision, commit merge) |
| [QuestRequests.cs](../Shared/PristonTale.Simulation/Quests/QuestRequests.cs) | `QuestAction`, `QuestStatus`, `QuestFollowup`, `QuestNoticeKind`, `QuestCommand`, `QuestPrompt`, `QuestTravel`, `QuestArenaDuel`, `QuestOutcome` |
| [QuestContext.cs](../Shared/PristonTale.Simulation/Quests/QuestContext.cs) | `QuestNpc` (what a clicked NPC does for quests), `IQuestItemSource`, `QuestContext` (one request against one character) |
| [QuestEngine.cs](../Shared/PristonTale.Simulation/Quests/QuestEngine.cs) | Dispatch of `Talk`, `Proceed`, `Accept`, `ChangeClass`, `ChooseReward`, `ResetSkills`, `DeclineSkillReset` to the families |
| `Shared/PristonTale.Simulation/Quests/LevelQuests.cs` | Level 30, 55 (two branches), 70, 80, 85 and 90 milestone hunts |
| `Shared/PristonTale.Simulation/Quests/LevelBonusQuests.cs` | Level 80 and level 90 bonus chains (extra stat points per level) |
| `Shared/PristonTale.Simulation/Quests/ElementaryQuests.cs` | The seven beginner quests (A..G) up to level 39 |
| `Shared/PristonTale.Simulation/Quests/FuryOfPhantomQuest.cs` | Fury of Phantom (level 100): two field hunts, three arena battles, a reward list |
| `Shared/PristonTale.Simulation/Quests/RankUpRules.cs` | Class-change offer (level, learned skills, home master, group), rank-up, skill redistribution |
| `Shared/PristonTale.Simulation/Quests/RankUpByTrophies.cs`, `RankUpByPotion.cs` | First rank-up: Tempskron trophies; Morion royal jelly or hair tonic by name branch |
| `Shared/PristonTale.Simulation/Quests/RankUpByQuestWeapon.cs` | Second rank-up: vamps buy a quest weapon that grows by hunting stages, ring, expiry |
| `Shared/PristonTale.Simulation/Quests/RankUpByMasterTrial.cs` | Third rank-up: two timed hunts, the royal amulet, the duel with the class master |
| `Shared/PristonTale.Simulation/Quests/WingQuest.cs` | Wing exchange: held wing, two crystals and gold for the next wing |
| `Shared/PristonTale.Simulation/Quests/QuestDrops.cs` | `QuestDropSource`, `PendingQuestDrop`, which drops a player receives, the quest upkeep, `QuestTick` (kills, deaths, timers) |
| `Shared/PristonTale.Simulation/Quests/QuestRewards.cs` | Lasting rewards and `QuestEffects` (permanent system effects synced in `PlayerCharacter.Recompute`) |
| `Shared/PristonTale.Simulation/Quests/QuestArena.cs`, `World/Fields/QuestArenaField.cs` | Arena field 32, entry and duel spots, opponent scaling, potions, helpers, blink |
| `Shared/PristonTale.Simulation/Quests/QuestItems.cs`, `QuestMonsters.cs`, `QuestClasses.cs`, `QuestInventory.cs` | Item and monster codes, class facts (tribe, home town; the quest weapon, hunt and ring come from the class's `ClassProfile`), pack helpers and `QuestGrant` |
| `Shared/PristonTale.Simulation/Quests/QuestSteps.cs`, `QuestTexts.cs`, `QuestBoard.cs`, `QuestView.cs` | Dialog helpers and the one-quest rule, English texts, quest window lines, the journal view sent to the owner |
| [QuestTransactions.cs](../Shared/PristonTale.Application/World/QuestTransactions.cs) | Prepare a request or an upkeep on the tick against a candidate copy; follow-up validation; travel destination |
| `Shared/PristonTale.Application/World/WorldServer.Quests.cs`, `PreparedQuestOperation.cs` | Prepare and commit on the tick, upkeep requests channel, travel, opening the duel |
| `Shared/PristonTale.Application/World/FieldInstance.Quests.cs`, `PlayerQuestState.cs` | Kill credit and quest drops, deaths, timers, the periodic upkeep check, duels; per-player quest state and the snapshot `QuestUpdate` |
| [QuestAppService.cs](../Shared/PristonTale.Application/Services/QuestAppService.cs) | Requests and upkeep off the tick: save gate, prepare, commit, then the save queue |
| `Shared/PristonTale.Protocol/Packets/QuestPackets.cs` | `QuestRequestPacket`, `QuestResultPacket`, `QuestJournalPacket`, `QuestNoticesPacket` |
| `Server/PristonTale.Server/Net/QuestWireMapping.cs`, `ClientSession.cs` (`RequestQuestAsync`), `PresenceEncoder.cs` | Wire edge; journal and notices from the snapshot; the arena's solo view |
| `Server/PristonTale.Server/World/QuestUpkeepService.cs` | Hosted service running the quest upkeep the tick asks for |
| `Persistence/PristonTale.Persistence.Abstractions/Characters/QuestStateCodec.cs` | The `characters.quests` JSON (`SavedQuests` version 1) |
| `Persistence/PristonTale.Persistence/Migrations/M016QuestData.cs` | Monster columns `quest_code`, `event_item`, `potion_count`, `potion_percent` |
| `Client/PristonTale.Client.Net/Online/QuestWireReader.cs` | Quest messages into the shared quest types |
| `Client/PristonTale.Gameplay/UI/Quests/` | `HudQuestController` (requests, dialog, boxes, notices), `HudQuestDialog`, `HudQuestWindow`, `HudQuestDocuments`, `HudQuestArt` |

## Journal and rules

- One quest runs at a time (`QuestJournal.Active`). Every family refuses to start while another quest runs; a talk that
  a running quest blocks posts "You have undergoing Quest" when the NPC has nothing else to say (`QuestContext.Block`).
  The level 55 masters stay silent while a rank-up runs.
- `ActiveQuest` carries the state number, the hunted monster's quest code (`Target`), `Kills`/`KillsNeeded`, a
  `Variant` (level 90 prey family or Fury reward), the timer and the quest weapon fields. Finished level quests and
  bonus chains set bits in `LevelLog`; beginner quests and Fury of Phantom in `ElementaryLog`. The last 32 finished
  codes and the last 64 applied operations are kept. Every change bumps `Revision`, which the published view follows.
- Quest monsters are named by the monster's quest code (the authored classification key, `MonsterDefinition.QuestCode`);
  Fury's hunts count every kill in their field instead.
- Quest items reach the pack directly, never the ground: codes of the quest, bottle and ingredient categories
  (`QuestItems.IsQuestDrop`) are taken out of the kill's loot and queued as `PendingQuestDrop`s for the credited
  player. The main drop table gives quest items only to first-tier characters (and the vamp to the second tier); bonus
  drops and the monster's event item pass. A drop is stored only while a running quest wants it, one copy at a time
  (`QuestDrops.Accepts`). The royal amulet event item drops 21 times in 100 and only below the final tier. Granted
  items reach the character through `PlayerCharacter.TryReceive`, so quest items land in the special item box
  (`QuestItemBox`); quests count and remove them there and on both bag pages.
- Dialogs name a document; the client shows `image/Sinimage/Quest/<document>.sin`. `QuestFollowup` says what OK does:
  close, send `Proceed`, open the yes/no box whose yes sends `Accept`, list rewards (`ChooseReward`), or show a reward
  whose arrows browse the list before `Accept` claims the browsed one.

## Quest families

- Sides: the quests split the classes by class number, not by tribe. The first four classes (Fighter, Mechanician,
  Archer, Pikeman) take the Tempskron side (Ricarten, the `_T` dialogs and prey); every later class takes the Morion
  side (Pillai), the assassin included. The assassin's first rank-up still gathers trophies.
- Level milestones (`LevelQuests`): quest giver codes 1001-1006 (1001 the eight classic classes, 1002 and 1004 the
  Morion side, 1003 the Tempskron side) offer the quest from its level once the previous milestone is done. Accept starts the hunt (20 Bargon at 30, 20 Muffin or
  Dire Bee at 55, 10 Typhoon at 70, 5 Ratoo at 80, 10 Grotesque at 85, 10 Iron Fist at 90). Rewards: +5 stat points,
  +1 skill point, +5 and +1, +5 and +2, a lower death penalty from level 85, +40 life from level 90 (with the level 90
  token). Level 85 hands out the Tear of Khalia; a death takes it back and the giver restarts the hunt.
- Level 80 bonus chain (`LevelBonusQuests`, 1010 or 1012, then 1011, from level 80): the second NPC wants three bonus
  vamps for a recommendation; the first turns it into +2 stat points per level from 80, paid back for the levels
  already reached.
- Level 90 bonus chain (1012, then 1013, from level 90): the second NPC draws Omega, D-Machine or Mountain; fifty kills
  under a curse of -30% damage; a death restarts the hunt; +3 more stat points per level from 90.
- Beginner quests A..G (`ElementaryQuests`): the instructor (1014) offers them in order up to level 39; finishers
  1015-1022 serve one tribe or both. Cake to the warehouse keeper, 5 Hopy or Rabie, candy to the smith, 5 Hobgoblin or
  North Goblin, 10 Skeleton or Corrupt, 10 Cyclops, the lost hammer dropped by Bargons while the quest runs. Rewards are
  class-specialised items and potions. Past level 39 the instructor drops an unfinished one.
- Fury of Phantom (`FuryOfPhantomQuest`, 1013 from level 100): 50 kills in the Heart of Perum (field 28), a duel with
  Fury's level 118 form, 100 kills in the Galluvia valley (field 31), duels with the 119 and 120 forms; each win sends
  the player to Pillai; the reward is one of eight weapons (their quest variants).
- Wing exchange (`WingQuest`, an NPC with a warp gate service of value 0 or 1): the held wing (none, then metal up to
  chaos), two crystals and 3,000 to 80,000 gold for the next wing.
- Rank-ups: the skill master of the side's home town (Ricarten, field 3, or Pillai, field 21).

### Rank-ups and the class tier

The class tier (0..3) advances only in `RankUpRules.RankUp`. The class-change button of the training box a home-town
skill master opens with the skill window (`QuestAction.ChangeClass`), opens the next rank-up quest when the master is in the class's home town, its class-change
group is 1 (first rank-up only) or 2 and above (all three), the level is reached (20, 40, 60) and every skill the
current tier opened has a rank.

1. Tempskron side and assassin (`RankUpByTrophies`): one silver, bronze and golden wolverine trophy, brought to the
   master.
2. The other classes (`RankUpByPotion`): the character name's byte sum picks royal jelly (bottle + 9 honey) or hair tonic (bottle +
   7 oil). The branch's villager hands out the bottle, the aging master brews, the villager takes the brew and ends the
   quest; the master then promotes.
3. Second rank-up (`RankUpByQuestWeapon`): three vamps buy the class's quest weapon (the item's quest variant). Wielded,
   it grows three times by hunting (30/40/50 Bargon, Skeleton Warrior, Head Cutter for melee; 25/35/45 Armored Beetle,
   Skeleton Ranger, Titan for archers and Atalanta; 50/70/100 kills in fields 8, 10 and 13 for casters); each growth
   adds 1-2 damage, attack rating (mana regeneration for staves) and 4 life. Grown, it earns the rank-up and the class
   ring. Both are bound (no sale, no warehouse) and vanish five days after the weapon was made; the master then ends the
   quest and +15 life applies for good. Levelling more than two levels past the weapon without finishing slows it for
   the session. While it grows, its item box names the stage's hunt under the name, set off by blank lines and in teal,
   with the kills made and needed ("Bargon:12/30", "Forbidden Land's Monster:5/50": `RankUpByQuestWeapon.HuntLine`,
   from the journal's stage and kills left).
4. Third rank-up (`RankUpByMasterTrial`): two hunts of 4,900 seconds of play time each (50 kills below level 80, 30
   from 80; prey by level bracket); a lost hunt goes back a step. Then the royal amulet from the bracket's named
   monster, then a duel in the arena with the skill master (Tempskron side) or the magic master (Morion side). The master yields
   at a sixteenth of its life; the victory dialog promotes to the final tier and sends the player home. Dying in the
   duel goes back to the amulet step.

After each rank-up the master offers one free redistribution of the learned skill ranks (`QuestAction.ResetSkills`).
The original asks once: No (or Escape) sends `DeclineSkillReset`, which ends the saved offer, so it does not open
again at the next login.

## Server authority

- `QuestAppService.RequestAsync` takes the session's save gate, `WorldServer.PrepareQuestAsync` runs the request on the
  tick against `CloneForInventoryOperation()`, the snapshot is stored with `CancellationToken.None`, and
  `CommitQuestAsync` applies the candidate's journal and items, the progression deltas, travel and the duel. A request
  that changes nothing (a talk) answers without a write. The operation id is recorded with the journal; a retried id
  answers "already committed", the same id with another command `Conflict`.
- A follow-up (`Proceed`, `Accept`, `ChooseReward`) must answer a step the NPC still offers: the server re-runs the talk
  (or the class-change button for the start of a rank-up) on a scratch copy and compares code and state. The victory
  dialog of a duel is the only step answered without an NPC.
- Kill credit, deaths and hunt timers run on the tick against the live journal (`QuestTick`). Timers move in steps of
  one second so a running timer republishes the journal once per second. `PlayerCharacter.CommitQuestOperation` merges
  kills counted while a request was being stored (`QuestJournal.AdoptCommitted`).
- Item changes the tick cannot make (quest drops, weapon growth and expiry, leftovers of finished steps) set
  `UpkeepDue`; the field signals `WorldServer.QuestUpkeepRequests`, and `QuestUpkeepService` runs
  `QuestAppService.RunUpkeepAsync` (prepare and commit, then the save queue; a busy save gate defers it).
- Each snapshot carries the owner's `QuestUpdate`: the journal view when the revision moved, quest notices and a dialog
  the world opened. `WorldOutbox` keeps notices in order (at most 256) and the newest view and dialog.

## Quest arena

Field 32 (`QuestArenaField`). Entering it travels the player to (22497, 9605); a character saved there starts in its
home town. `FieldInstance.OpenDuel` spawns the opponent at (22091, 9673) from the monster catalog by sound code (and
level for Fury), with life and damage +1.5% per player level above 60 (life capped at 30,000), three potions on top of
its own (one under 30% life heals half, a quarter for the magic master), 3% helpers (metal golem or wolverine; fire
elemental for the magic master) and the magic master's 3% blink. The opponent and its helper fight only their player,
and only that player can hit them. The presence encoder hides other players and other players' duel monsters in the
arena. Fury falls once its potions are gone; a master yields.

## Wire and storage

Messages are in [protocol.md](protocol.md#quests). The journal is the `characters.quests` JSON column
([persistence.md](persistence.md)); quest weapons and rings are saved with their unique marker and restore as the
quest variant, which the importer stores as the code's quest definition (`item_definitions.variant` 2,
[world-import.md](world-import.md)).

## Client

- Clicking an NPC that gives, runs or ends quests (quest giver or script, skill master, wing exchange, aging master)
  sends `Talk` after the NPC dialog arrives. Quest roles get no button in the NPC dialog.
- The quest dialog takes the NPC help box (50,45, 9x10 tiles): the quest title, the document text (Windows-1252,
  reflowed to the box: blank lines and list marks start paragraphs, column lines and rules keep their own line), OK,
  reward buttons or reward arrows. The yes/no box replaces it for confirmations.
- The training box's class-change button (at (240, 380) of the screen) lights when the talk offered a rank-up and
  sends `ChangeClass`; it shows gray while the tier's skills are learned but the level or a running quest holds it back.
- The quest window (`Q` or the bottom bar's quest button) is the party window's quest page at (539, 155) of the 800x600
  frame (right and bottom anchored, so it keeps its place over the bottom bar on a larger screen): five titles per page, page arrows, running/finished tabs, close; a title opens its
  description (`image/party/party_DATA/<file>`) on the panel to the left.
- A running hunt shows the quest timer icon (`QuestTimer.tga`) after the effect icons, with a gauge for a timed hunt;
  its tooltip shows the quest, the kills and the time left. Chat notices go to the chat's assistant tab, events and
  messages to the notice strip, the fanfare plays `drink2.wav`. A rank-up's redistribution offer is a yes/no box; Enter answers
  Yes, as the original's box takes it.

## How to

### Add a step to a quest family

1. Add the state to the family's switch in `Talk` (the dialog), `Proceed`/`Accept` (the change) and, for hunts,
   `OnKill`; keep follow-ups consistent with what `Talk` shows, since the server validates against it.
2. New items: add the code to `QuestItems`, decide in `QuestDrops.Accepts` when a drop is wanted and in
   `QuestDrops.RemoveLeftovers` when it goes.
3. Add a case to `tests/PristonTale.Tests/QuestTests.cs`; for world behavior, to `QuestServiceTests.cs`.

### Add a quest family

Add a `QuestCode` value (never renumber), a static class named for its mechanic, dispatch in `QuestEngine`, the window
lines in `QuestBoard`, and kill credit in `QuestTick.OnKill` when it hunts.

## Pitfalls

- Never change the class tier outside `RankUpRules.RankUp`; progression commands and saves do not move it.
- `QuestCode`, `QuestAction`, `QuestStatus`, `QuestFollowup` and `QuestNoticeKind` values are persisted or on the wire.
- Quest items queued by a kill are not in the pack until the upkeep commits; checks that need them must run in the
  upkeep or a request, not on the tick.
- The level 85 death-penalty reward has no consumer while Etervale has no death experience penalty.
- A quest grant places all its items or none: they must fit the pack and, together, the carried-weight limit
  (`QuestInventory.Check`). A refusal answers `NoSpace` or `OverWeight` and leaves the quest where it was; a quest
  drop that does not fit is lost. Class rewards are made out for the receiver's class (`ItemRoll.SpecJob`).
- Dagger and phantom quest weapons need their item codes; until the item categories exist they resolve to none and the
  second rank-up refuses to forge for those classes.

## Related

- [application.md](application.md) — transactions, the save gate and the tick.
- [world.md](world.md) — loot rolls, monsters and NPC services.
- [client.md](client.md) — HUD windows and input routing.
- [testing.md](testing.md) — quest regression cases and HUD captures.
