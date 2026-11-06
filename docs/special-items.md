# Special items

Items a player uses from the inventory with a right click: premium timers (EXP potions, Third Eyes, packages,
vampiric and mana items, cost reductions, Might of Awell, pets), stones that reset stats or skills, hair tints, the
big-head potion, scrolls, force orbs, skill and aging masters, the Gravity Scroll, the Teleport Core and the ether
cores, firecrackers, the mobile shop, monster crystals and soul stones, Bless Castle items, boosters, event foods,
gift boxes, the soccer-ball potion, chocolates and puzzles. The server decides every outcome; the client sends the item
and draws the result.

## Read this when

- You add a usable item, change what one does, or change a duration or percentage.
- You touch experience, extra drop rolls, per-attack life or mana, skill costs or carrying capacity from items.
- You work on the premium timer column, the Teleport Core window, item tooltips or the item effects in the world.
- You change the `premium` character column or the item-use messages.
- You work on crystal pets, castle items, gift box or puzzle tables, or the item events that replace drop rolls.

## Code map

| Path | Responsibility |
| --- | --- |
| `Shared/PristonTale.Simulation/Items/Usable/UsableItemCatalog.cs` | Catalog key to `UsableItemRule` (action, argument, premium grants, conflict, level, village ban) |
| `.../Usable/PremiumTimers.cs` | `PremiumEffectKind`, `PremiumTimer`, the character's running timers, Gravity Scroll count and big-head look |
| `.../Usable/PremiumRules.cs` | Experience, extra drop chance, vampiric and mana-recharging amounts, cost reductions, weight bonus |
| `.../Usable/PremiumEffects.cs` | Mana Reduce, Terry and Mute as permanent system modifiers |
| `.../Usable/PremiumPets.cs` | Pet damage, elements, helper chance, strike timings, Nepsys slow |
| `.../Usable/UsableItemEffects.cs` | The character part of a use (timers, resets, mastery, aging, Gravity Scroll, big head) |
| `.../Usable/ItemScrolls.cs`, `ForceOrbs.cs`, `TeleportCoreDestinations.cs` | Scroll effects, force orb tables, the Teleport Core fields |
| `.../Usable/ItemUseCommand.cs`, `ItemUseMessages.cs`, `ItemEffectKind.cs` | Command, statuses, outcome and history; the game text of each status (the client words it); the effect kinds sent to clients |
| `Shared/PristonTale.Application/Services/ItemUseAppService.cs` | Prepare and commit under the session's `SaveGate`, then the save queue |
| `Shared/PristonTale.Application/World/ItemUseTransactions.cs` | Tick-side shared checks and the candidate of a use |
| `Shared/PristonTale.Application/World/ItemUses/ItemUseAction.cs` | `ItemUseAction`, one per `UsableItemAction` (`ItemUseActions.For`): `ForTheDead`, `Consumes`, `ReappliesAtCommit`, `Check`, `Arrival`, `Extend`, `Message`, `Store`, `Commit` (session), `Apply` (world, returns the effect viewers see) |
| `Shared/PristonTale.Application/World/WorldServer.Items.cs` | `PrepareItemUseAsync`, `CommitItemUseAsync` (calls the kind's `Commit` and `Apply`) |
| `Shared/PristonTale.Application/World/FieldPremium.cs`, `FieldInstance.Premium.cs` | Timer expiry, per-attack life and mana, pets |
| `Shared/PristonTale.Application/World/FieldItemEvents.cs`, `FieldSkills.Items.cs` | Item effects published per tick; item status effects |
| `Shared/PristonTale.Application/World/MobileShopTransactions.cs` | The NPC-less shop a mobile shop item opens |
| `Persistence/PristonTale.Persistence.Abstractions/Characters/PremiumStateCodec.cs` | `SavedPremium` in the `premium` column |
| `Shared/PristonTale.Protocol/Packets/ItemUsePackets.cs` | `ItemUseCommand`, `ItemUseResult`, `PremiumTimers`, `ItemEffects` |
| `Client/PristonTale.Gameplay/UI/Inventory/HudItemUseController.cs`, `HudTeleportCore.cs`, `HudUsableTooltip.cs` | Right-click use, Teleport Core window, confirmations, tooltips |
| `Client/PristonTale.Gameplay/UI/Widgets/HudPremiumTimers.cs` | The premium timer column |
| `Client/PristonTale.Gameplay/Effects/ItemUseEffects.cs` | Firecrackers (converted burst scripts and `ItemVfx` launch sparks and flash), item sounds, and a potion taking effect (`PotionApplied`, for every drinker in view): its drops and `drink1.wav` once |
| `Client/PristonTale.Gameplay/Effects/ItemVfx.cs`, `ForceOrbPresentation.cs` | Effects built in code: skill-learn light, force orb weapon glow and hit spark, potion drops, firecracker launch sparks and flash, the return sparkle (`ItemVfx.Return`) |
| `Client/PristonTale.Gameplay/World/Remote/PremiumPetPresentation.cs` | Pet actors, their animation and strikes |
| `Client/PristonTale.Gameplay/World/GameScene.Items.cs` | Item use, timers and item effects in the scene |
| `.../Usable/MonsterCrystals.cs` | Crystal and soul stone tables, pet lifetime and life, the shared delay, the crystal event |
| `.../Usable/CastleItems.cs`, `ItemStatuses.cs` | Castle scrolls and stones; keep icons, names and lines of item effects in the effects bar |
| `.../Usable/EventFoods.cs`, `GiftBoxes.cs`, `Chocolates.cs` | Event foods and the soccer-ball head, prize tables, Virtual Life levels |
| `.../Usable/Puzzles.cs`, `PuzzleExchange.cs` | Pieces, board cells, reveal cost, drop weights, reward table, the collector's exchange |
| `Shared/PristonTale.Simulation/World/Loot/ItemEventDrops.cs` | Item events that replace drop rolls (`ItemEventRates`) |
| `Shared/PristonTale.Application/World/ItemUses/*Uses.cs` | The kinds by family: `CharacterUses` (timers, resets, masteries, gravity), `ScrollUses` (scrolls, force orbs), `TravelUses` (cores, return, mobile shop, union core), `LookUses` (hair, firecracker, soccer head), `CastleUses` (castle items, Restoration), `CrystalUses`, `EventUses` (foods, boxes, chocolates, puzzle bags) |
| `Shared/PristonTale.Application/World/FieldSkills.Crystals.cs`, `FieldInstance.Items.cs` | Crystal pets, soul monsters, castle items on the field |
| `Client/PristonTale.Gameplay/UI/Widgets/HudPremiumKeeps.cs` | Boosters and the skill-delay item in the effects bar |
| `Client/PristonTale.Gameplay/UI/Inventory/HudPuzzleExchange.cs` | The collector's question; the board is drawn by `HudInventoryPanel` |
| `Client/PristonTale.Gameplay/UI/Widgets/HudCombat.NamePlates.cs` | Name boxes of crystal pets and summons (with the owner's name) |

## Use flow

1. Right click on a carried item whose catalog key has a rule (`HudInventoryController.UseRequested`). The Teleport
   Core opens its field list first and asks to confirm the field; the mobile shop asks to confirm the use.
2. `ItemUseCommand {OperationId, ExpectedRevision, ItemId, Argument, Text}` goes to the server
   (`ItemUseAppService.UseAsync`). `Argument` is the Teleport Core destination index. A dead player's client sends its
   Restoration by itself ten seconds after death in a castle field.
3. Tick (`ItemUseTransactions.Prepare`): replay check (the same id answers its recorded outcome), inventory revision,
   carried item with a rule, then the rule's checks (dead, village ban, level, conflicts, field, target). A refusal is
   final and consumes nothing. Otherwise a candidate consumes one item (a puzzle bag stays and is unsealed), adds what
   the family adds (a box's prize, the crystal's drawn monster, the gold a reveal takes), applies the character part and
   records the use.
4. The candidate's save snapshot is stored (hair tints also store `hair_index`), then `CommitItemUseAsync` applies the
   inventory, re-applies the character part to the live character with the same clock, and the world part: scroll and
   force orb effects, hair, the mobile shop flag, crystal monsters, castle items, the Restoration revive, the eat motion
   of foods and boxes, Virtual Life, item effects and the field change of travel items.
5. The result (`ItemUseResult`) carries `ItemUseStatus`; a field change also sends a forced `FieldWarp`.

| Status | Meaning |
| --- | --- |
| `AlreadyRunning` | The same timer, scroll or force orb runs, or a package's timers run |
| `SameAttribute` | Vampiric Cuspid and its Super version exclude each other; one helper pet at a time |
| `NotHere` | Village for scrolls and force orbs, a room field, Bellatra or the quest arena for travel, towns and the castle for the mobile shop, Fatal Edge against a running Avert Scroll and the reverse |
| `LevelTooLow` | Helper pets need level 10; Teleport Core destinations and force orbs have their own levels |
| `NothingToChange` | Nothing to reset or train, the same hair, no aged item worn, five Gravity Scrolls used, a revealed piece |
| `Delay` | A billing crystal or soul stone within three seconds of the last crystal; eating while eating; Restoration early |
| `ClanOnly` | Clan crystals and the Union Core (Etervale has no clans, so they always refuse) |
| `NoSpace`, `OverWeight` | A gift box without room for a 2 x 2 item or within ten weight (five for the capsule) of the limit |
| `MaleCannotUse`, `FemaleCannotUse` | A chocolate meant for the other gender |
| `NotEnoughGold` | A lucky bag without 1000 gold |
| `CrystalRefused` | Castle mercenaries outside a siege of the castle-master clan (Etervale runs no siege) |
| `SummonLimit` | A pet crystal while the user keeps ten pets ("You can only have 10 crystal summons at a time") |

## Items and effects

Durations run on the real clock (Unix seconds) and keep running while the character is offline. A new timer of a
running kind is refused, so timers never stack.

| Items | Effect |
| --- | --- |
| Third Eyes BI112/114/178 (1 d, 7 d, 1 h) | 50% chance of one extra main drop roll from a monster that drops at all |
| EXP potions BI113/115 (30%), BI124/125/179 (50%), BI143/144/150/180 (100%) | Kill experience + the percent |
| Bronze/Silver/Gold/Superior and Ultra packages (BI126/127, 130/131, 135, 201-212, 223-226, 245-247) | Third Eyes 20/30/40/50% and EXP 10/20/30/40% together for 3 h, 1 d, 7 d or 30 d |
| Vampiric Cuspid BI119/120/181, Super Vampiric Cuspid BI186-188 | Life per attack: `int((STR + P/10)/20 * (1 + H/10) * H + 1)`, Super `/15` and `H/7`; H monsters hit, P average damage |
| Mana Recharging BI121/122/182 | Mana per attack: `int((SPI + P/10)/13 * (1 + H/10) * H + 1)` |
| Mana Reduce BI128/129/183/258, Stamina Reduce BI191/192/238/259 | Skill mana and stamina costs -30% |
| Phoenix BI132-134/151/177 | EXP +20%, extra drop chance +50; strikes the owner's target within 300 units once a second for `L/2+1..L*3/2+1` |
| Terry, Nepsys, Io, Mute BI152-155/165-172 | Join owner attacks (21% to level 5, 24% to 9, 100% to 120): Terry fire, Nepsys ice `L/2..L*95/100` (slow 10 s), Io `L*4/5+1..L*9/5+2`, Mute `L/2..L*39/20`; Terry -20% skill mana, Mute +10% dmg |
| Blue/Red/Green stones BI101-103, (E) stones BI319/320 | Stats back to the class start values, skill ranks refunded, or both |
| Strength..Health stones BI145-149 | One attribute back to its start value |
| Eternal Life BI105 | 30 s no damage taken, damage dealt -50% |
| Fatal Edge BI106, Avert Scroll BI107 | 1 h critical chance +15, evasion +15 |
| Force orbs FO101-112, magic FO121-132 | Added power on single-target hits (magic: on spell power), with +10/15/20% of the average attack for the strongest orbs |
| Teleport Core BI108 | One of 37 fields by level; ether cores EC101/102/104 return to Ricarten, Navisko, Pillai |
| Hair tints BI116-118, BI213-222 | Hair 0..12, refused for the current hair |
| Skill masters BI139-141 | Learned skills of class tier 1..3 fully trained |
| Aging masters BI136-138, BI162-164 | The worn aged item (weapon, shield or orb, armor, armlet, gloves, boots) gains one level |
| Gravity Scroll BI184 | Carrying capacity +50, at most five |
| Big-head potion BI109 | Big head for 600 s |
| Firecracker BI123 | Four rockets half a second apart; the roll picks each burst's shape; the player cheers (Yahoo) after the motion in progress |
| Mobile shop BI142 | A shop that buys and repairs until closed (NPC id 0) |

Aging and mixing materials (BI110, 111, 160, 161, 185, 189, 190) and the passive resurrection scroll are not used
from the inventory. Items without a rule keep the generic tooltip.

### Monster crystals and soul stones

| Items | Monster |
| --- | --- |
| GP101-108, GP111-113 | Hopy, Hobgoblin, Decoy, Bargon, Head Cutter, Figon, King Hopy, Hulk, Web, Dark Specter, Iron Guard (crystal event: Zzangpy, Wild Goblin, Maximum Decoy, Bauton, Vega Demir, HellSing, Horropion, Typhoon, CurseWeb, Dark Phantom, Imitir Guard) |
| GP109 Mystic | 20/20/15/15/15/10/5% over the first seven, from the event table during the event |
| GP121 Marvel | Bargon 20, Head Cutter 20, Figon 15, King Hopy 15, Hulk 12, Web 10, Dark Specter 5, Iron Guard 3 (event: their event monsters) |
| GP117-119 | One of four skeletons, chaos or Inferna monsters, uniformly |
| GP120 Extreme | Omicron 35, Spider Trooper 30, Mountain 20, Omega 15 |
| GP110, GP122 | Guardian Saint, Guardian Divine for the clan; refused (`ClanOnly`) |
| GP114-116 | Ricarten militia, Ricarten guard, Bless royal guard as castle mercenaries; refused (`CrystalRefused`) |
| GP201-209 | Soul stones: a hostile "soul of" monster for 4 min from seven per stone, uniformly |

Monsters are named by their key (`MonsterDefinition.Key`, the unique English name; a crystal pet is the crystal
variant of its monster, `HOPY 2` for Hopy) and a soul stone's by `Soul of <key>`. A crystal is refused in towns,
the quest arena and below its item level; a soul stone also in the castle (33), field 29 and field 16. Every crystal
use starts a three-second delay; billing crystals (GP117-120) and soul stones refuse during it. A pet is called beside
its user (one of the eight points 48 units around him, preferring one no monster stands on), lives 4 min plus 2 s per
owner level (levels 1-99) with 3 more life per level, hunts the nearest wild monster in its sight and never another
pet, and credits its owner with the kill's experience and drops. It follows its owner as a skill summon does
(`SummonBrain`: the leash, and the recall to a free point beside an owner 500 units away); the original's pet stands
where it was called. An idle pet standing inside another monster walks out of it (`FieldInstance.StepOutOfCrowd`), so
pets do not stand in each other. The owner keeps one status per pet (`MonsterCrystals.PetStatus`): code
`PetStatusBase` 0xF0000500 + the number of the crystal it shows + its slot (the owner's lowest free one, 0..9) in bits
12-23, GP101 in slot 0 0xF0000565, in slot 1 0xF0001565. A pet shows the crystal that calls its monster alone
(`ShownCrystal`: a Mystic's Hobgoblin shows GP102, a Marvel's Bargon GP104), else the crystal used. The status has
the pet's lifetime and starts with the next step (`QueueItemEffect`); a pet dies when it expires, with its status (its
end, the owner's death), when the owner dismisses it (`DismissCrystalPets`, the `DismissCrystalPets` request), enters a
town or leaves the field (`LeaveCrystalPets`; the statuses end in the field entered, `ArriveWithoutCrystalPets`); a
pet that falls ends its status. A Web pet's blow slows its target to half speed for 36 frames (not Kelvezu). A player
keeps at most ten pets (`MonsterCrystals.PetSlots`); an eleventh crystal is refused as `SummonLimit` and kept.
`events.crystal` (0 off, 1 from 12:00 to 23:59, 2 all day) switches crystals to their event monsters. A crystal whose
drawn monster the world data lacks is refused and kept; the data's soul monsters are absent, so soul stones refuse.

### Bless Castle items

| Items | Effect (castle field 33 only) |
| --- | --- |
| BC101 Athanasia | 30 s invulnerable, damage dealt -50% (excludes Eternal Life) |
| BC102 Deadly Edge | 30 s critical chance +5 (excludes Fatal Edge) |
| BC103 Avail of Evade | 30 s evasion +5 (excludes Avert Scroll) |
| BC106-108 Defiance Stone | 60 s: blows of the fire (sound 0x5020), ice (0x5022) or lightning (0x5021) crystal tower deal half |
| BC109-116 Might Stone | 60 s: player damage against Fighter, Mechanician, Pikeman, Archer, Knight, Atalanta, Magician or Priestess +20% |
| BC105 Restoration | Dead ten seconds in a castle-state field: revive in place at half life with 5 s guard |

One castle scroll, one Defiance Stone and one Might Stone run at a time; a new one replaces the running one of its
kind, and every castle item ends when its holder leaves the castle. The same code refuses while it runs. A dead player
in a castle field may use a Restoration after 10 s down (players stay down until they restart). Crystal towers spawn
only with a siege and Might Stones need castle player combat; neither exists in Etervale, so those stones have no
target.

### Boosters and skill delay

BC121-123, BC124-126 and BC127-129 raise maximum life, mana or stamina by 15% for 1 h, 3 h or 1 day (current values
are not refilled); refused in towns; kinds stack, a running kind refuses. BC130-132 make every skill recharge in 0.5 s
for 300 s, 3 h or 1 day (`PremiumRules.SkillCooldownMs`). They are premium timers that pause while the character is
offline: `SavedPremium.SavedAtSeconds` records the last tick's clock and the restore moves their expiry by the time
since. They show in the running-effects bar (`Booster_01..04.tga`), not the premium column.

### Event items

- Foods SP101, SP102, SP103, SP115, SP135, SP136, SP142: eaten like a potion, restoring `min + rand(max - min)` of the
  item's authored life, mana and stamina ranges (`PotionDefinition` on any item with restore ranges).
- Gift boxes SP105 (grade 1) to SP110 (grade 6) and the tiger capsule SP134: need room for a 2 x 2 item and ten free
  weight (five for the capsule); the player eats the box and the prize, drawn out of 10000 (`GiftBoxes`), lands in the
  inventory. The capsule ends its eating motion at the event and cheers (Yahoo). Grades 1-3 give Mystic Mana Potions x10, 4-5 Great, 6 Medium; all give Teleport Cores, crystals, force
  orbs, a 3 h package, Mana Recharging and Vampiric Cuspid.
- SP140 soccer-ball potion: the soccer-ball head (big-head look 3, one model per class) for 3 h of real time, refused
  while another head change runs; starting and ending guard for 5 s with the level-up burst.
- Chocolates: CH101 (female) and CH102 (male) grant Virtual Life 10, CH103 and CH104 (female) Virtual Life 4 and 8 —
  the Priestess skill's life share, damage and heal reduction and duration at that rank; refused in towns, while Virtual
  Life runs and for the other gender. Female classes are Archer, Atalanta, Priestess and Assassin.

### Puzzles

Puzzle pieces (PZ101-108, PZ201-208) drop sealed as lucky bags (`ItemInstance.Sealed`, saved with the item; the
owner, the warehouse and the ground show the PZ100 lucky bag). A right click with 1000 gold reveals the piece. The
eight pieces of one set laid in their cells of the inventory's last four columns (rows 0-3) finish the puzzle; the
puzzle collector (NPC event quest 2, `NpcServiceAction.Exchange`) takes them for one reward drawn from 0..10000:
Firecracker 2500, Mystic Mana Potion x10 2500, Transparo 1590, Mystic Crystal 1500, OR228 700, Event Amulet 700, Marvel
Crystal 500, Celesto 10; an unknown reward gives 30 Mystic Mana Potions. The exchange needs 120 free weight.

### Item events

The `events.*` settings (`ItemEventRates`, per 10000, all off by default) make event items replace a monster's main drop
rolls, tried in this order: `Mandoo` (on/off: SP101 4%, SP102 2%), `ValentineDay` (CH101 70% / CH102 30%),
`Watermelon` (SP115), `Valentine` (SP135), `CandyDays` (SP136), `WhiteDay` (CH103 70% / CH104 30%), `Puzzle` (a sealed
piece by the drop weights 743, 723, 704, 650, 650, 650, 380, 500 per set) and `GiftBox` (the box of the monster's
level band: below 20 SP110, then every 20 levels up to SP105 from 100). Most refuse a player more than 15 levels away
from the monster, or 8 below a level-20 monster; Valentine and CandyDays only the 15-level gap.

### No defined behaviour

These carried keys have no use in Etervale's rules and keep the generic tooltip: SP104, SP141, SP143-159, SP179,
SP183, SP185, SP186, the SP2xx keys, BC104, BC117-120, BC136, BC137, BC141, GP123, GP124, GP128, GP135-137,
GP141-144, GP301-313, GP342-344, EC106, EC107, BI195-198, BI240-244, BI248-257, BI260, BI263-277, BI284-299, every
BI5xx key, FO113-115, FO133-137 and FO151-163. GF, SD and QT items are exchange, quest or pick-up items, not used from
the inventory. The Field Core (BI193, BI194) has no rule beyond its level-20 gate and no item data. EC103 returns to
no town: it guards for 10 s with the return sparkle where the player stands. EC105, the Union Core, needs a regular
clan and refuses (`ClanOnly`).

## Client presentation

- Premium timer column: x 8, y 80 + 36 n, the original icons and grey covers measured against the 1 h, 3 h, 1 d,
  7 d and 30 d tiers, fade-in, ten-second blink, one-pixel-per-frame climb and the hover box with name, effect and
  `Time left: hh:mm:ss`. The column follows the `PremiumTimers` message, which the server sends when the timers change.
- Scrolls and force orbs are status effects (codes `0xF0000001..3`, `0xF0000200 + icon`) and show in the
  running-effects bar with their keep icons; castle items use `0xF0000400 + index` and their keep icons, boosters and
  the skill-delay item show there from the premium timers (`HudPremiumKeeps`, codes `0xF0000110 + kind`) with their
  full duration under the gauge. The hover box adds the effect line (`DataText.StatusEffect`, in the shown language).
- Ether cores, crystal summons (at the called monster) and the Restoration play the return sparkle with the learn
  sound; the soccer-ball head starts and ends with the learn light; boosters and lucky bags play the drink sound; a gift
  box the eat sound under an orange light. Foods show the potion effects of their restored resources.
- Crystal pets and summons carry the HUD's name box (`HudCombat.DrawNamePlates`): the name in pale green, the owner's
  name in parentheses on a second line, and under the box a life bar on the viewer's own.
- Each crystal pet's status shows in the running-effects bar with the inventory sprite of the crystal it shows
  (`image/Sinimage/Items/Event/it<KEY>.bmp`); the hover box reads `<name> Crystal`, "Summoned Pet that supports you in
  Battle", `Time Left: m:ss` and "Right-click to dismiss". A right click on it asks "Monster Crystal / Do you want to /
  dismiss this pet?" (`HudCrystalDismiss`); Yes (or Enter, while the chat input is closed) sends `DismissCrystalPets`
  for that pet's status and the chat says "Pet has been dismissed".
- While a puzzle piece or lucky bag is carried, the board backdrop (`NYEAR_00.bmp`) covers the last four columns'
  top four rows; a finished puzzle glows yellow under two orange lines sweeping one pixel every three 70 Hz frames. The
  collector asks "Do you wanna / exchange to the item?" under "Completed Puzzle", or explains the board.
- Item effects (`ItemEffects` message): firecracker bursts with the converted particle scripts and sounds, the
  skill-learn light of hair tints and force orbs (`ItemVfx.Learn`, rising streaks registered in code), the agony glow
  of skill masters, and pet strikes.
- A running force orb keeps a flare glow in the orb's colour on the holder's weapon bone, renewed every 29 s until the
  status ends, and each hit the holder lands bursts the weapon-hit spark on the target with a white light.
- The big head replaces the head model with the oversized one of the class (tier-0 head prefix, hair 1..3, look `b`
  or `t`); the descriptor carries it, so every client sees it.
- Pets are client actors driven by the player's description (the Phoenix flag, the helper pet and the head look);
  they follow their owner and play the strike the server announces. A premium helper pet takes the place of the
  beginner's pet ([application.md](application.md#beginners-helper-pet)), which uses the same dragons and strikes.

## Pitfalls

- `PremiumTimers.Expire` runs every tick before the rules read the timers; reading them elsewhere sees the state of
  the last tick.
- The commit re-applies the character part with the prepared clock and roll; it must not re-apply aging (the
  inventory commit already carries the upgraded item): `AgingMasteryUse.ReappliesAtCommit` is false.
- A new usable item kind is one `ItemUseAction` subclass in the family's `*Uses.cs` and one row in `ItemUseActions`;
  a kind without a row behaves as the plain character rules.
- Package timers share one expiry; a single item of a kind a package runs is refused while the package runs.
- `HudNpcServiceController` accepts NPC id 0 only for the shop: that is the mobile shop.
- Crystal pets are summons (`OwnerId`) but not in the skill summon table; `FieldSkills.StepSummon` steps them through
  `StepCrystalPet` first.
- A crystal pet is called in the command phase, so its status starts with the next step (`QueueItemEffect`) and a
  dismissal ends it the same way (`EndItemEffect`); a status event added in the command phase would be lost. Ends are
  applied before adds, so `EndItemEffect` also drops an add of that status still queued (a pet dismissed before its
  first step leaves no status behind). A summon is put in the collision index when called, so pets called before a step
  see each other.
- Castle item ends queued from the command phase reach clients with the next step (`EndItemEffect`); leaving the castle
  removes them directly.
- A puzzle bag keeps its piece's grid size; only its code, key and name are swapped for the lucky bag on the wire.

## Related

- [application.md](application.md) - durable operations and the tick
- [protocol.md](protocol.md) - the item-use opcodes
- [persistence.md](persistence.md) - the `premium` column
- [client.md](client.md) - HUD and scene
- [testing.md](testing.md) - `SpecialItemTests`, `MonsterCrystalTests`, `CastleItemTests`, `EventItemTests`,
  `PuzzleTests`, `--capture-item-hud`, `--capture-item-effects`
- [server.md](server.md) - the `events.*` settings
