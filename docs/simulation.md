# Simulation core

Shared gameplay rules in `Shared/PristonTale.Simulation` used by both the server (authority) and the client
(prediction and presentation): characters, classes, items, inventory, combat formulas, the modifier engine,
progression, regeneration and movement rates. `World/`, `Physics/` and `Skills/` are covered by
[world.md](world.md) and [skills.md](skills.md).

## Read this when

- Changing how a derived stat (life, mana, stamina, attack rating, defence, absorption, damage, speed) is computed.
- Adding a stat a buff, item or passive can change, or changing how modifiers stack or expire.
- Changing hit, block, critical, element or absorption math for players or monsters.
- Adding an item family or an equipment rule.
- Changing inventory placement, stacking, equip rules or the command/revision contract.
- Changing experience, level-up rewards, stat/skill points or skill-master training rules.
- Changing life/mana/stamina regeneration, walk/run speed or animation rates.
- Adding a class or changing a class's base attributes or formula profile.

## Code map

| Path | Responsibility |
| --- | --- |
| `PristonTale.Simulation.csproj` | net10.0, references only `PristonTale.Geometry`; embeds `Skills/Data/Classes/*.json` |
| `SimulationCore.cs` | Description constant only |
| `Characters/CharacterEnums.cs` | `CharacterBrood`, `JobBitCode`, `EquipmentSlot` (`PlayerClass` is in Contracts) |
| `Characters/JobBits.cs` | The one map between `PlayerClass` and its `JobBitCode`, both ways |
| `Characters/CharacterStats.cs` | `CharacterAttributes` (five attributes), `CharacterFunctionProfile` (formula selectors) |
| `Characters/CharacterClass*.cs`, `Classes/` | Class base type, `CharacterClassDef`, `CharacterClassRegistry`, one file per class |
| [`Characters/PlayerCharacter.cs`](../Shared/PristonTale.Simulation/Characters/PlayerCharacter.cs) | Character aggregate: progress, pools, gear, effects, recompute |
| `Characters/DerivedStats.cs` | Base derived-stat formulas from attributes, level and equipped item stats |
| `Characters/Regeneration.cs` | Per-second life/mana/stamina recovery and the `RegenerationState` accumulator |
| `Characters/Locomotion.cs`, `AnimationRate.cs` | Move-speed stat to world units per second; walk/run/attack animation rates |
| `Characters/DestinationMovement.cs` | Straight-line step toward a destination (client player and NPC walking) |
| `Characters/HelpPets.cs` | The beginner's helper pet: one of the four helper dragons drawn below level 10 (`PlayerCharacter.MeetHelpPet`), active while shown and below level 10 behind a premium helper pet (`HelpPets.Active`), its chat commands |
| `Characters/StarterGifts.cs` | The starter gifts of a new character: the class's weapon, armour and off-hand and the crystals, created perfect with the class's specialization, their pace and chat line |
| `Characters/OwnerState.cs` | `OwnerState.Capture`: the owner-only character snapshot sent to the client |
| `Characters/CharacterHead*.cs`, `CharacterVisuals.cs` | Head/face/hair model path resolution per class and tier; `CharacterVisualSet` |
| `Items/Item.cs` | `ItemKind`, `ItemStatBlock`, `ItemDefinition`, `ItemInstance` |
| `Items/ItemTemplate.cs`, `ItemRoll.cs` | Authored value ranges of a kind; the values of one copy (`ItemRoll`, `ItemSpecBonus`) |
| `Items/ItemRoller.cs` | Shop (lowest) form and drop rolls, class specialization and its requirement shift |
| `Items/EquipmentTotals.cs` | What the usable worn items and their specializations add (`EquipmentTotals.Sum`) |
| `Items/ItemUsability.cs`, `CarriedWeight.cs` | Requirement and class checks; carried weight and the weight limit checks |
| `Items/ItemAbsorbCorrection.cs` | Absorb added to armor, robes, boots, gloves and shields by catalog number |
| `Items/ItemDetailView.cs`, `ItemRuleMessages.cs` | Owner view of one copy; the texts of item refusals |
| `Items/Inventory.cs` | Bitmask grid with anchors, stacking, move/split/merge/exchange; an item added without a place goes to the first free space down each column from the left |
| `Items/StoreGrid.cs` | The 9x9 store grid of the warehouse and the shops |
| `Items/InventoryCommand.cs` | `InventoryCommand` and `InventoryCommands.Apply` |
| `Items/ItemCodes.cs` | Catalog key (`WA101`) to 32-bit item code and back, for every item family; `KeyOf` gives the key or "" for an empty slot |
| `Items/ItemEquipmentMetadata.cs` | Kind and slot by catalog family; the two-handed list taken from the item table |
| `Items/ItemKinds.cs` | `Classify`: the kind of any item key (wearable families, then the non-wearable groups); the importer stores it |
| `Items/ItemClassRestrictions.cs` | Class/gender restriction masks for armor, robes and orbs by catalog key |
| `Items/ItemCatalog.cs`, `PotionDefinition.cs` | Definition lookup by code; potion restore ranges |
| `Items/Usable/` | Usable item rules, premium timers and their effects, pets, scrolls, force orbs ([special-items.md](special-items.md)) |
| `Trades/` | Trades: `TradeRules` (what may pass, the 9x4 grid, an offer's check and the exchange of two offers on candidates), `TradeOffer`, `TradeStatus` and the original's `TradeMessages` |
| `Social/ContactBook.cs` | A character's friends and ignored players: the lists' rules (`ContactLimits`), `ContactStatus` and `ContactMessages` |
| `Parties/` | Parties: `PartyRules` (level gap, experience shares, share range, gold split, request time) and the original's `PartyMessages` |
| `PersonalShops/` | Personal shops: `PersonalShopRules` (listing, opening, a sale on candidates), `PersonalShop` (an open listing), purchases, outcomes, listings and texts |
| [`Combat/CombatResolver.cs`](../Shared/PristonTale.Simulation/Combat/CombatResolver.cs) | Hit, block, damage, critical, element and absorption resolution |
| `Combat/CombatProfiles.cs`, `CombatProfileFactory.cs` | `AttackProfile`/`DefenderProfile` and how they are built from characters and monsters |
| `Combat/WeaponDamage.cs` | Weapon damage range of a player |
| `Combat/CombatRng.cs` | Seeded RNG with the original's inclusive range semantics |
| `Combat/Elements.cs` | `Element` and `ResistanceBlock` |
| `Combat/PlayerAttackReach.cs`, `MeleeReach.cs` | Player attack range (melee 50, ranged from the weapon) |
| `Combat/MonsterAttackReach.cs`, `MonsterOffensiveSkill.cs` | Monster basic-attack reach and offensive-skill areas |
| [`Modifiers/ModifierTypes.cs`](../Shared/PristonTale.Simulation/Modifiers/ModifierTypes.cs) | `StatKind`, `StatModifier`, `EffectSourceKind`, `ControlFlags`, `StackRule`, `PeriodicEffect` |
| `Modifiers/EffectContainer.cs` | `EffectSpec`, `ActiveEffect`, `EffectContainer`, `ModifierTotals` |
| `Modifiers/ModifierRules.cs` | How totals change derived stats, damage, resistances, dealt/taken scaling |
| `Progression/ExperienceTable.cs` | 285-level cumulative experience thresholds and the saturation ceiling |
| `Progression/SkillLearning.cs` | Skill-master training: window, prerequisites, prices, point kinds |
| `Progression/ProgressionCommand.cs` | `ProgressionCommand` (allocate attribute, learn skill) |

## Units and conventions

- Integer stats throughout; formulas truncate toward zero like the original integer code. Several formulas are
  computed in `float` and cast (`DerivedStats.Compute`); keep the cast points when editing.
- World distances are in original world units (the same units as `PristonTale.Geometry` and field data).
- Fixed point: `Locomotion` and `AnimationRate` use 8-bit fixed point (256 = 1.0) and the original 70 Hz tick.
  `Locomotion.BaseTranslation(stepLevel) = 250 + 10 * (max(stepLevel, 1) - 1)` per tick, where the derived `MoveSpeed`
  is the step level: the move speed held to 9, or one above the move speed while `MoveSpeedUnbound` lifts the limit
  (Talaria, `Locomotion.StepLevel`); walk in the field is
  `* 180 >> 8`, run is `* 460 >> 8`, walk in a village uses the base; units/s = perTick / 256 * 70.
- `AnimationRate.AttackSpeedMainFrame(stat) = 256 + 32 * clamp(stat - 6, 0, 6)`; `AttackRate` adds the
  `AttackMotionSpeed` percent, then frame step `(80 * speed) >> 8`, normalised by 80. `MoveRate` caps the per-tick
  translation at 350 and uses `(80 * speed) / 300 / 80`.
- Times in the modifier engine and regeneration are milliseconds (`float dtMs`). Permanent effects use
  `float.PositiveInfinity` as the duration.
- Percent stats are whole percent; regeneration stats (`LifeRegen`, `ManaRegen`, `StaminaRegen`) and
  `LifeCostPerAttack` are tenths of a point.
- `Element.Lighting` is the lightning element; the same spelling is used by the world-data records.
- Item codes: 32-bit `category | ordinal << 8`, low byte zero (see Items).

## Determinism and RNG

- `CombatRng` wraps `System.Random(seed)` and implements `ISimRng` (`World/Ai/ISimRng.cs`). `Range(min, max)` is
  inclusive on both ends (span `max + 1 - min`) and returns `min` when `max <= min`. `RollPercent` is 0..99,
  `RollPerMyriad` 0..9999, `NextExclusive` excludes the upper bound.
- The server creates one `CombatRng` per field instance seeded with the field id (`WorldServer`), and a fixed seed for
  the boss schedule. Every roll of that field (hits, blocks, crits, drops, AI) shares one stream, so outcomes depend
  on call order. Tests seed their own `CombatRng`.
- Roll order inside `CombatResolver.Resolve` is part of the contract: hit, block, damage, critical. A blocked or missed
  hit consumes no damage roll. Adding or reordering a roll shifts every later outcome in the field.
- Nothing in this project reads the clock, the disk or the network; callers pass `dtMs` and RNGs.

## Classes

`CharacterClassRegistry` builds the ten classes; each `Classes/<Name>Class.cs` calls `CharacterClass.BuildDefinition`
and declares the class's `ClassProfile` (`Characters/ClassProfile.cs`, looked up with `ClassProfiles.Of(class)` or
`CharacterClassDef.Profile`): the four tier titles, the fallback motion timing (`PlayerMotionFallback`), the favoured
weapon (`WeaponDamage`'s level / 6 bonus), the casting weapon that attacks at range (`PlayerAttackReach`), the
requirement shift of items specialised for it (`ItemRoller`), the quest weapon, beginner weapon, hunt and rank-up ring
(`QuestClasses`), the realm (the definition's `Brood` and the town of `DeathPenalty.TownField`) and whether its
characters are female (the female-only armors and the chocolates). A per-class fact belongs in the profile, not in a
switch over `PlayerClass`.
The class also loads its skill window from `ClassSkillCatalog.For(class)` (see [skills.md](skills.md)).

| Class | Id | `JobBitCode` | Brood | Str/Spi/Tal/Agi/Hlt | Sight | Life/Mana/Sta/Melee/Ranged/Magic fn |
| --- | --- | --- | --- | --- | --- | --- |
| Fighter | 1 | 0x2 | Tempskron | 28/6/21/17/27 | 70 | 1/3/1/1/2/0 |
| Mechanician | 2 | 0x1 | Tempskron | 24/8/25/18/24 | 65 | 2/2/2/2/2/0 |
| Archer | 3 | 0x8 | Tempskron | 17/11/21/27/23 | 80 | 3/3/2/3/1/0 |
| Pikeman | 4 | 0x4 | Tempskron | 26/9/20/19/25 | 75 | 1/3/1/1/2/0 |
| Atalanta | 5 | 0x20000 | Morion | 23/15/19/19/23 | 200 | 2/2/2/2/1/0 |
| Knight | 6 | 0x10000 | Morion | 26/13/17/19/24 | 190 | 2/2/2/1/2/0 |
| Magician | 7 | 0x80000 | Morion | 16/29/19/14/21 | 190 | 5/1/3/3/2/1 |
| Priestess | 8 | 0x40000 | Morion | 15/28/21/15/20 | 200 | 4/1/3/3/2/2 |
| Assassin | 9 | 0x10 | Tempskron | 25/10/22/20/22 | 80 | 3/3/2/3/1/0 |
| Shaman | 10 | 0x100000 | Morion | 15/27/20/15/22 | 190 | 5/1/3/3/2/1 |

- Two job masks exist: `JobBitCode` (non-sequential bits) is used for item class restrictions and item specialization;
  `CharacterClassDef.MotionJobMask = 1 << (Class - 1)` is used for animation tables. Do not mix them. A class's
  `JobBitCode` comes from `JobBits.Of`; nothing else maps a class to its bit.
- `GrowthPerLevel` is empty for every class: attributes grow only through allocated status points (5 per level).
- `Tier` (0..4, `PlayerCharacter.LastTier`) is the class rank. It opens skill-window rows, selects head models
  (`CharacterHead`) and gates experience above level 100 (below `UncappedTier` 3). It changes only through the rank-up
  quests (see [quests.md](quests.md)).
- Head models: `CharacterHead.Resolve` builds `char/tmABCD/<prefix><variant 01..13><suffix>.inx` from the per-class,
  per-tier pattern in `CharacterHeadModels` (the fifth tier's heads end in `d`; the Priestess's 6 to 9 are spelled
  `006`..`009`). Assassin and Shaman tiers 1-4 have only the three creation faces;
  `Apply` falls back to the face index instead of inventing a file name.

## Character aggregate

`PlayerCharacter` owns everything mutable about one character. It is not thread-safe: the authoritative field tick (or
a transaction candidate) is the only writer.

| Member group | Contract |
| --- | --- |
| `Level`, `Experience`, `Money`, point counters, `Tier` | Loaded by `RestoreProgress` (non-negative, tier 0..4) |
| `Life`, `Mana`, `Stamina` | Plain settable pools; clamped to maxima by every recompute and by `Equip` |
| `Attributes` | Base plus allocated points; equipment never changes them |
| `Derived` | Cached `DerivedStats`; reading it recomputes when `Effects.Revision` changed |
| `Equipment`, `Inventory` | 10 equipment slots and the 12x6 carried grid |
| `Effects` | `EffectContainer` with every timed and permanent modifier |
| `Premium`, `ItemUses` | Running premium timers (persisted in `premium`) and the recent item uses; a change recomputes |
| `Skills` | `LearnedSkills` (ranks and use counts); a change triggers a recompute |
| `InventoryRevision`, `ProgressionRevision` | Optimistic-concurrency counters for client commands |

### Derived-stat pipeline

`PlayerCharacter.Recompute` is the single path. It runs from the constructor, `LevelUp`, `Equip`,
`TryAllocateAttribute`, `RestoreDurability`, `CommitInventoryOperation`, `Skills.Changed` and lazily from `Derived`.

1. `PassiveSkillEffects.Sync` puts each learned passive with a modifier block into `Effects` as a permanent
   `PlayerSkill` effect keyed by skill code (only when its weapon kind matches).
2. `Effects.SetHeld` records the item kinds in the weapon and off-hand slots (bit per `ItemKind`); effects with a
   `HeldMask` count only while a matching kind is held.
3. `EquipmentTotals.Sum` walks the slots in `EquipmentSlot` order and adds every worn item the character can use
   (`ItemUsability.CanUse`; see [Items](#items)). Per copy: attack rating, damage range, critical, defence, rated absorb
   cut to one decimal (`(int)(absorb * 10.000001) / 10 + 0.000001`, absorb = copy + `ItemAbsorbCorrection`), block,
   move speed, attack speed, shooting range, life/mana/stamina regeneration, added life/mana/stamina, resistances; an
   item set whose pieces are worn (each ring counts; `ItemCatalog` attaches the `ItemSetDefinition` to its items) adds
   its bonus once: added life, absorb, block and the three regenerations;
   magic mastery (the copy's, `ItemInstance.MagicMastery`) and belt capacity (`PotionSpace`) are set by the last item
   that has one. A copy whose `SpecJob`
   includes the class bit also adds its `ItemSpecBonus`: absorb (cut the same way), defence, speed, block, attack
   speed, critical, range, magic mastery, `Level / divisor` life, mana, attack rating and maximum damage, and half of
   its life, mana and stamina regeneration. `AbsorbItems` counts the copies (and specializations) with an absorb of
   their own.
4. `ArmorDefence` percent: if non-zero and the worn armor is specialized for the class, the armor's own defence
   (specialization included) grows by that percent before it feeds defence and absorption.
5. `DerivedStats.Compute(attributes, level, functionProfile, totals, carriedWeight)` (formulas below).
6. `PassiveSkills.Apply` (table-driven passives, see [skills.md](skills.md)), then `ModifierRules.Apply` with
   `Effects.Totals`; `MaxWeight` gains the premium weight bonus (Might of Awell, Gravity Scrolls). `PremiumEffects.Sync`
   runs with `PassiveSkillEffects.Sync` and keeps Mana Reduce, Terry and Mute as permanent system modifiers.
7. Pools are clamped to the new maxima.

An inventory change marks the derived stats stale (carried weight feeds move speed), so the next `Derived` read
recomputes. `PlayerCharacter.EquipmentTotals` returns the totals of the current recompute.

### Base formulas (`DerivedStats.Compute`)

Level `L`, attributes `Str Spi Tal Agi Hlt`; float arithmetic, truncated at the end unless noted.

| Stat | Formula |
| --- | --- |
| AttackRating | `trunc(items.AR + Agi*3.1 + L*1.9 + Tal*1.5) + items.LevelAttackRating` |
| Defence | `Agi/2 + Tal/4 + L*1.4 + items.Defence` |
| Absorption | `trunc(Defence/100 + L/10 + items.Absorb + (Str+Tal)/40) + 1 + min(2, items.AbsorbItems/2)` (truncated defence) |
| MaxWeight | `(int)(Str*2 + Hlt*1.5 + L*3 + 60)` |
| MoveSpeed | `trunc((Tal + Hlt + L + 60) / 150 - carried/MaxWeight + items.MoveSpeed) + 1`; after the modifiers, the step level (`Locomotion.StepLevel`) |
| MaxLife fn 1 / 2 | `L*2.1 + Hlt*2.4 + Str*0.8 - 10` / `L*2.1 + Hlt*2.2 + Str*0.6 - 5` |
| MaxLife fn 3 / 4 / 5 | `L*1.8 + Hlt*2.1 + Str*0.3` / `L*1.5 + Hlt*2.1` / `L*1.5 + Hlt*1.9` |
| MaxMana fn 1 / 2 / 3 | `L*1.5+Spi*3.8` / `L*0.9+Spi*2.7` / `L*0.6+Spi*2.2` |
| MaxStamina | `Hlt*1.4 + (Str+Tal)/2 + L*2.3 + 80 + Spi` |
| Bare-hand damage (melee fn 1) | integer: min `2 + (Str+130)/130 + (Tal+Agi)/40`, max `3 + (Str+130)/130 + (Tal+Agi)/35` |
| Bare-hand damage (other) | integer: min `2 + (Str+200)/200 + (Tal+Agi)/50`, max `3 + (Str+200)/200 + (Tal+Agi)/45` |
| Bare-hand maximum | also `+ items.LevelDamage` |
| AttackSpeed | `items.AttackSpeed` |
| CriticalChance | `min(50, items.Critical)` |
| BlockChance | `trunc(items.Block)` |
| ShootingRange, Resistances | `items.ShootingRange`, `items.Resistances` |
| LifeRegen, ManaRegen, StaminaRegen | item regeneration in points per second, added by `Regeneration` |
| PotionSpace | the last worn item's belt capacity, else 2 |

Life, mana and stamina are truncated to whole numbers (32-bit, never narrowed), then gain
`(int)(items.LevelLife + items.AddLife)`, the mana `(int)(items.AddMana)` for mana function 1 and
`(int)(items.LevelMana + items.AddMana)` otherwise, and the stamina `(int)items.AddStamina`. `CarriedWeight` is
`CarriedWeight.Of(character)`: every owned item, worn ones included; a potion weighs one per unit, other items their
weight per unit, gold nothing.

### Weapon damage (`WeaponDamage.Compute`)

With no weapon it returns the derived bare-hand range (modifiers already applied). Otherwise `wMin`/`wMax` are the
damage of every usable worn item (`EquipmentTotals.DamageMin/DamageMax`; a held weapon the character cannot use adds
nothing):

| Weapon | Primary | Divisor | Secondary |
| --- | --- | --- | --- |
| Staff or Phantom, magic fn > 0 | Spi | 150 (fn 1) or 170 | `Tal / 30` |
| Bow or ThrowingWeapon | Agi | 130 (ranged fn 1) or 190 | `(Tal+Str) / 40` (fn 1) or `/ 50` |
| Anything else | Str | 130 / 150 / 190 (melee fn 1 / 2 / other) | `(Tal+Agi) / 40 / 45 / 50` |

- `min = 2 + wMin*(primary+divisor)/divisor + secondary + job + (wMin+wMax)/16`
- `max = 4 + wMax*(primary+divisor)/divisor + secondary + job + items.LevelDamage`
- `job = Level / 6` when the weapon is the class's favoured kind (Fighter axe, Archer bow, Pikeman pole, Knight sword,
  Atalanta javelin, Assassin dagger, Magician/Priestess staff, Shaman phantom; Mechanician: weapon specialized for
  Mechanician).
- Adds the passive weapon mastery bonuses (`PassiveSkills.WeaponBonus`, `PassiveSkillEffects.WeaponBonus`), then
  `ModifierRules.ApplyDamage` (DamageMin/DamageMax totals; max is never below min).

## Modifier engine

Every temporary or permanent stat change is an `EffectSpec` in an entity's `EffectContainer` (players and monsters
each own one). Sources: player skills (buffs, debuffs, passives), monster skills, summons/companions, and the
`Item`, `Consumable`, `Aura` and `System` source kinds reserved in `EffectSourceKind`. Equipped item stats do not go
through the engine; they are summed by `EquipmentTotals.Sum`.

- `EffectSpec(code, source, harmful, modifiers, periodic, control, stacking, maxStacks, heldMask)` is immutable.
  `Code` identifies the effect for stacking and on the wire: skills use their skill code.
- `StatModifier(Stat, Op, Value)`: `Flat` adds; `Percent` scales. `ModifierTotals.Apply(stat, base)` returns
  `(base + flat) * (100 + percent) / 100`, truncated, clamped to `int`.
- `ModifierTotals` sums values times `Stacks` over all effects whose `HeldMask` is 0 or matches `Held`. It is rebuilt
  only when `EffectContainer.Revision` changes; any `Apply`, `Remove`, expiry or `SetHeld` change bumps the revision,
  which in turn makes `PlayerCharacter.Derived` recompute.
- `StackRule`: `Refresh` (restart timer, take new values), `Stack` (add a stack up to `MaxStacks`), `KeepStrongest`
  (replace only if `Magnitude`, the sum of absolute values, is at least as high; `Apply` returns `null` otherwise),
  `PerSource` (one copy per source entity).
- `PeriodicEffect(resource, amount, intervalMs, percentOfMax, nonLethal)`: `Step` emits `EffectChangeKind.Ticked`
  changes; the Application applies them (`FieldSkills.Buffs.cs`).
- `ControlFlags` (Stun, Root, Silence, Slow, Hidden, Charmed) are the OR of all active specs; read with `Has`.
- Lifecycle (driven by the Application tick, `FieldSkills.StepEffects`): `Step(dtMs)` advances timers and removes
  expired effects; on death `ClearTimed` drops timed effects; entering a village removes timed `PlayerSkill` effects
  (`RemoveTimedFrom`); permanent effects stay until removed by code or source (`Remove`, `RemoveFrom`).
- All mutating calls append `EffectChange` records to a caller-owned scratch list; the Application turns them into
  wire events.

`ModifierRules` maps totals onto stats:

| Function | Effect |
| --- | --- |
| `Apply(DerivedStats, totals)` | Every `DerivedStats` field except MaxWeight; MaxLife min 1, AttackSpeed unclamped, rest min 0 |
| `ApplyDamage` | Weapon damage range with DamageMin/DamageMax |
| `Resistances` | Adds `ResistAll` plus the per-element flat values |
| `ScaleDamage` | `damage * (100 + DamageDealt) / 100 * (100 + DamageTaken) / 100`, flat and percent summed, min 1 |
| `AgainstUndead` | Removes `AbsorptionVsUndead` percent (capped at 100) of damage from undead attackers, min 1 |

Stats with no derived-stat slot (block, critical, evasion, leech, ranges, next-hit, companion damage, and so on) are
read directly from `Effects.Totals` by the combat profiles, `PlayerAttackReach`, `Regeneration` and the skill code.

## Combat

Profiles decouple the formulas from entities. `CombatProfileFactory` builds them:

- `ForPlayerBasicAttack(character, normalAttack)`: weapon damage range, derived attack rating, element Bionic,
  critical chance `CriticalChance(character)` (worn items, specializations and critical passives capped at 50 together,
  then the `CriticalChance` totals), `CritBonusPercent` from `CriticalDamage`. With
  `normalAttack`, `NormalAttackWeaponDamage`/`NormalAttackWeaponMax` add a share of the weapon's own damage.
  `SkillFormulas.NormalAttack` wraps it and adds `HitDamage` as the flat add.
- `ForPlayerDefender`: derived defence and absorption; block from every worn item's block rating (weapons included)
  and specialization plus totals and passives; `Resistances(character)`: worn items and specializations, the element
  passives, then the resistance totals.
- `ForMonsterBasicAttack` / `DefenderProfile.FromMonster`: from `MonsterCombatStats` (see [world.md](world.md));
  `FromMonster(MonsterInstance)` adds the monster's `CriticalChanceTaken`.

### Player attacking (`CombatResolver.Resolve`)

1. Hit chance: `acR = trunc((AR - defence) * 1.4)`; below -190 gives 50, above 2100 gives 95, else the step table
   value of the highest threshold `<= acR`. Then `chance = trunc(acc - (defLevel - atkLevel) / 100 * 28)`, clamped
   30..95. Hit when `RollPercent() < chance` (skipped with `alwaysHit`).
2. Block (player defenders only; a monster's authored block rating never stops a hit):
   `trunc(block + (defLevel - atkLevel) / 100 * 25)`, capped at 50 only while a shield is held; blocked hits deal 0.
3. Damage: `Range(min, max)`, then `+ dmg * SkillMultiplierPercent / 100 + SkillFlatAdd`, then `+ ExtraPercent`.
4. Critical: `chance = CritChance + (atkLevel - defLevel) * 25 / 100 (+ CritChanceTaken if CritChance > 0)`, capped
   at 70; damage `* (170 + CritBonusPercent) / 100`.
5. Element: `dmg - dmg * resist / 100`, resist clamped to -100..100; with `ResistedPart >= 0` only that part is
   reduced.
6. Absorption as a percent (`ApplyAbsorbMonster`): `dmg - dmg * absorption / 100`, min 1.

The Application then applies `ModifierRules.ScaleDamage` and skill hooks (`FieldCombat.ResolvePlayerSwingHit`).

### Monster attacking a player

- `ResolveMonsterAttack`: block roll first (the player's block chance, capped at 50 only with a shield; no cap
  without one), then hit with `MonsterHitChance` (difference `(AR - defence) * 2`, the
  first table entry whose threshold is `>=` it, level term `* 50`, clamp 30..95, miss only when roll `>` chance),
  damage roll, element, then absorption subtracted as flat points (min 1).
- `ResolveMonsterSkill`: no hit or block roll; `Range(min, max) - absorption`, min 1, Bionic.
- Afterwards the Application applies `ScaleDamage`, `AgainstUndead`, evasion and defensive skills.
- Reach: `MonsterAttackReach.CanBegin` (planar distance below `MeleeRange`, vertical gap <= 64) and `CanLandBasic`
  (squared planar distance below `48 * 256`, or `460^2` for sound families with a ranged basic attack).
  `MonsterOffensiveSkill` supports five sound families and computes area center, query radius and containment.

Absorption is a percent against monsters but flat points against players; this asymmetry is intentional.

`PlayerAttackReach.For`: 50 for melee or no weapon; for bows, javelins, staves held by Magician/Priestess and phantoms
held by Shaman, the worn items' and specialization shooting range (`Derived.ShootingRange`) plus the `AttackRange`
totals.

## Items

- `ItemDefinition` is immutable catalog data built by `WorldDataFactory.ToItemDefinition` in
  `Persistence/PristonTale.Persistence.Abstractions` from imported item templates (see
  [world-import.md](world-import.md) and [persistence.md](persistence.md)). That factory takes the inventory footprint
  the importer measured (`grid_width`, `grid_height`), applies `ItemClassRestrictions.AllowedJobs` and
  `ItemEquipmentMetadata.Apply`, sets `BaseStats`, `Resistances` and
  `MaxDurability` to the low end of each range and keeps every authored range in `Template` (`ItemTemplate`, with the
  specialization bonuses in `ItemSpecTemplate`). `Critical`, `ShootingRange`, `PotionSpace`, `UniqueItem` and
  `BaseStats.AttackSpeed` are fixed per kind. A range whose upper value is 0 is a fixed value. Magic
  mastery is authored as a range: the definition holds its low end, and a dropped copy rolls whole points up to the
  top (`ItemRoll.MagicMasteryBonus`, the last draw of the roll; the middle mode trims it like every whole-number
  range); the shop form adds nothing and the best copy the whole span. The item tooltip shows it as `Magic APT`.
- `Code` is the 32-bit item code; `CatalogKey` is the 5-character key (`WA101`). A key is a family of two letters and a
  letter or digit (`WA1`, `DA3`) plus a two-digit ordinal. `ItemCodes.TryParse` gives `family << 8 | ordinal`, where the
  family number is `(first * 26 + second) * 36 + third + 1` (letters A-Z = 0-25; third character 0-9 then A-Z = 10-35),
  case-insensitively; `TryFormat` is the inverse. Every well-formed key has a code, so a new item family needs no
  registration. `ItemCodes.Family` compares families (`Family(code) == Family("WM1")`).
- `ItemKind` is persisted as an integer: append new values at the end only.
- Equippable means `EquipSlot` is set, which only `ItemEquipmentMetadata` does by family, and it also overrides the
  imported kind:

  | Families | Kind | Slot |
  | --- | --- | --- |
  | `WA1 WC1 WH1 WM1 WP1 WS1 WS2 WT1` | axe, claw, hammer, staff, pole, bow, sword, javelin | Weapon |
  | `WD1 WN1 WV1` | dagger, phantom, vambrace | Weapon (one-handed) |
  | `DA1 DA2` | armor (robes are `DA2`) | Armor |
  | `DB1`, `DG1` | boots, gloves | Boots, Gloves |
  | `DS1`, `OM1` | shield, orb | OffHand |
  | `OA1`, `OA2` | amulet, bracelet | Amulet, Bracelet |
  | `OR1 OR2` | ring | either ring slot (`RingLeft` first) |
  | `OS1` | sheltom | Sheltom |

  Two-handed weapons are the listed keys of the item table (axes, hammers, staves, poles, bows and swords; claws,
  javelins, daggers, phantoms and vambraces have none). Force orbs (`FO1`) and other families are not wearable.
- `AllowedJobs` (0 = any class): `DA1` armor for non-casters, `DA2` robes and `OM1` for casters, with fixed key
  numbers restricted to male-only or female-only classes. A specialization never restricts who may wear an item.
- `ItemInstance`: `InstanceId` (Guid, preserved across moves and copies with `preserveId`), `Count`, `UpgradeLevel`,
  durability, and `Roll` (`ItemRoll`): damage range, attack rating, defence, absorb (one decimal), block (two
  decimals), move speed, life/mana/stamina regeneration, added life/mana/stamina, resistances, requirements, price,
  `SpecJob` and the `ItemSpecBonus`. `TotalStats` exposes the roll's damage, defence and rating with the kind's
  attack speed; `JobSpecMask` is `Roll.SpecJob`. Durability never removes stats: a copy at 0 keeps every value and
  only sells and repairs for less (`NpcShopRules`).
- Shop form (`ItemRoller.Lower`, what `new ItemInstance(definition)` holds): the low end of every range, durability
  current = maximum = the low durability; an authored fixed specialization stays, with the middle of each requirement
  shift and the low end of each bonus range; the price is unchanged.
- Drop roll (`ItemRoller.Create(definition, count, rng)`): maximum durability in its range, current durability between
  half of it and it; every other range rolled inclusive (absorb, move speed and regeneration in tenths, block in
  hundredths). A kind with a fixed or random specialization specializes four times in ten (`rng 0..9 <= 3`); then,
  three times in ten when it has random classes, one random class replaces the fixed one. A specialized copy costs a
  fifth more, its requirements shift by a random percent of its class's range (strength, spirit, talent, agility; see the
  class's `ClassProfile.Shift`) and its bonus ranges are rolled in hundredths. Unspecialized copies carry no bonus.
- `ItemUsability.CanUse`: level and the five attribute requirements of the copy, and `AllowedJobs`.
- Weight: `CarriedWeight.CanTake(character, weight)` allows carried + weight up to `MaxWeight`; `IsOverweight` is
  carried above it. See [Equipment rules](#equipment-rules-playercharactercanequip) and the NPC rules in
  [world.md](world.md) for where it is enforced.
- Prices: the copy's `Price` (`Roll.Price`), weight and durability feed `NpcShopRules` and `MoneyLimits.CarryLimit`
  in `World/Npcs` (see [world.md](world.md)).
- `PotionDefinition.Restore` adds `NextExclusive(min, max)` (upper bound excluded unless min == max) to each pool,
  clamped to the maximum; does nothing to a dead character. The authored data gives potions no stack size; the catalog
  (`WorldDataFactory.ToItemDefinition`) stacks them to `PotionDefinition.StackSize` (9999), as the original counts the
  potions of one bag item without a limit of its own.

### Inventory grid

- `Inventory()` is the 12x6 carried grid (72 cells); `Inventory(width, height)` builds any grid up to 32x32 (the
  warehouse and the shops use the 9x9 `StoreGrid`). Cell index = `y * Width + x`; an item is stored only at its top-left
  anchor and its footprint (`GridWidth x GridHeight`) is tracked in one `uint` bit row per grid row.
- `TryAdd` first tops up compatible stacks, then places new stacks in the first free space down each column from the
  left; it plans everything before mutating and fails without change. Compatible stacks need a stackable, equal
  definition and identical upgrade, durability and roll.
- `TryPlace`, `TryMove`, `TrySplit` (new instance id), `TryMerge`, `TryJoin` (a whole stack onto a compatible one),
  `TrySwap`, `TryExchange` (take an item out and put a replacement at a given cell, or at the same anchor or first fit;
  `CanExchange` checks it without changing anything), `TakeAt`, `TakeFrom` (`ItemInstance.Split` for part of a stack),
  `TryPlaceFirstFree` (no stacking), `ConsumeAt`. `Covered` names the one item a footprint would lie over. `Occupied`,
  `Items` and `CopyFrom` walk or copy a grid. Every change bumps `Version` and raises `Changed`.

### Equipment rules (`PlayerCharacter.CanEquip`)

- `CanHold`: count 1, matching slot (rings fit either ring slot), the same instance not in another slot, and no
  two-handed weapon together with an off-hand item (`PairAllows`, also the stored pair's rule in `RestoreStored`).
  `Equip` (and save restore) needs only this: a worn item whose
  requirements stop being met stays worn and adds nothing.
- `CanEquip` adds `ItemUsability.CanUse`; `CanWear` is `CanEquip` without the hand rule. Aged copies
  (`UpgradeLevel > 0`) equip like any other.
- `TryEquipFromInventory(anchor, slot)`: the copy must pass `CanWear`. A two-handed weapon first sends the off-hand item
  to the bag (first free place); an off-hand item first sends a held two-handed weapon there; then the item in the
  slot takes the bag cell. Without room nothing changes. `InventoryCommands.Refusal` names an unusable item
  (`ItemRuleMessages.CannotUse`).
- An item the character receives (`TryReceive`: a pickup, a starter or game master's gift, a quest grant, a prize)
  goes on the first empty slot that `CanEquip` allows (`TryWearReceived`), as the original's client wears what it
  receives: an armour on an empty armour slot, rings on either free ring slot, never a two-handed weapon beside an
  off-hand item or an item whose requirements are not met. A full bag does not keep it off an empty slot; otherwise
  it goes to the bag.
- `TryUnequip` needs inventory space and is refused while the character is overweight (`ItemRuleMessages.OverWeight`).
- `ReceiveGroundLoot` refuses an item that would carry the character over the weight limit. With `intoBag` (a pickup
  with the inventory open, `InventoryCommand.PickupToBag`) the item goes into the page shown's first free cells as a
  stack of its own, for the client to carry, or with no room there to the hand (`PlayerCharacter.Held`), as the
  original puts every such pickup on the mouse; an item the special item box keeps, or one that finds the hand full, is
  received as any item (`TryReceive`). `TryReceive` tries the page shown, then the other page (the original tries the
  page shown only and leaves the item on the ground).
- The hand (`PlayerCharacter.Held.cs`): one item on neither page. It is owned (weight, quests, logs), saved
  (`SavedItem.Held`), stays held across `SwitchBagPage`, and leaves by `Move`, `Merge` or `Swap` naming it
  (`TryPutDownHeld`: onto free cells of the page shown, all of it onto a stack of its kind, or over exactly one item,
  which is held instead) or by `Drop` (`TakeHeld`). Owner views give it the slot `OwnedItemView.HeldSlot`
  (`OwnedItemView.Held`).
- `EquipmentSlot` has no helmet and keeps `Sheltom` separate (the persisted equipment order).

## Commands, revisions and transactions

- `InventoryCommand` and `ProgressionCommand` carry an `OperationId` (non-empty) and `ExpectedRevision`; they apply only
  when it equals the current `InventoryRevision`/`ProgressionRevision`. The last 64 applied operations of each kind
  (plus NPC service operations, personal shop purchases, item uses and quest operations) are kept for idempotent replay
  and persisted with the character, each in an `OperationHistory<T>` (`Characters/OperationHistory.cs`: `Find`,
  `Record`, `Restore` with the kind's own validity check). Applied records implement `IAppliedOperation` explicitly, so
  their stored JSON is only their own members.
- `InventoryRevision` counts changes, not commands: one equip can bump it more than once. Clients must adopt the
  revision returned in the outcome.
- `InventoryAction.Swap` sets an item down over exactly one other bag item (`Inventory.TrySwap`), which moves to where
  the first one was, or to the first free space; anything else refuses it.
- The two sides (`PlayerCharacter.Sides.cs`), as the original's E and W keys: a second bag page (`HiddenBag`, swapped
  whole with `Inventory` by `SwitchBagPage`, which also takes the item on the cursor, when the command names one, to the
  page shown's first free cells and is refused without room there; `BagPage` 0 A, 1 B) and a second weapon and off-hand pair (`StoredWeapon`,
  `StoredOffHand`, swapped with the held pair by `SwitchWeaponSet`; `WeaponSet`). Hidden items are still owned (weight,
  `FindOwnedItem`) and saved: `SavedItem.Hidden` and `SavedItems.ActiveSide` (the shown page in the inventory list, the
  held pair in the equipment list). Commands act on the shown page only; owner views carry `BagPage` and `WeaponSet`.
- The potion belt (`PotionBelt`, `PlayerCharacter.Belt`): three boxes of one potion stack each, of at most
  `Derived.PotionSpace` potions. `BeltPut` fills a box from the bag or another box (count 0: as many as fit; a box of
  another potion sends its stack to the bag first, or two boxes change places), `BeltTake` returns a box to the bag,
  `ConsumePotion` drinks from a box as from the bag, and an equip or unequip that lowers the space sends the excess
  back to the bag (`FitBeltToPotionSpace`). A potion the character receives (`TryReceive`, so a pickup too) goes to the
  belt first (`TryReceiveOnBelt`): it fills the boxes of the same potion up to the space, then the empty boxes in
  order, and the bag takes the rest; when the bag has no room for the rest, nothing moves and the receive fails. The
  original takes the boxes strictly in order (an empty first box before a half-full second one of the same potion) and
  sends a box's overflow straight to the bag. Owner views give a belt item the slot `PotionBelt.FirstOwnerSlot` + box
  (`OwnedItemView.BeltBox`); saved items carry `Belt` and restore after the gear that sets the space.
- The special item box (`QuestItemBox`, `PlayerCharacter.QuestBox`): 18 boxes of one unit each (6 columns by 3 rows)
  for the quest items QT101 to QT116 and the ingredients MA101, MA201 and MA202 (`QuestItems.Boxed`) and the wings
  QW101 to QW106 (`WingGates.IsWing`), which the wing gate reads there too. What the character
  receives (`TryReceive`: pickups, quest grants and drops, prizes) goes into the first free box down each column, then
  the next column, and when the box is full or does not keep it to the potion belt (a potion) or the bag;
  `StoreQuestItem` moves such an item left in the bag into a freed box. Quests count and remove items there and on both
  bag pages (`BagPages`). Owner views give a
  box item the slot `QuestItemBox.FirstOwnerSlot` + box (`OwnedItemView.QuestBox`); saved items carry `QuestBox`.
  `ItemSlots` is the row of one-item boxes the belt and this box share (`Find`, `TakeAt`, `Occupied`).
- `InventoryAction.Drop` and `DropGold` are handled by the Application (`GroundThrowTransactions`), as `Pickup` is;
  `Inventory.TakeFrom` takes all of an item or splits a new stack from it. `BoundItems` is what never leaves the
  character, neither on the ground nor in the warehouse: quest items, the quest's bound weapon and ring, the `MA1`,
  `MA2` and `QW1` families and `GF101`. `ItemDropRules` adds the `BI1` and `BI2` families and the original's premium,
  event and time-limited keys. `ItemCodes.FamilyOf` and `InFamily` read a key's family (its first three characters).
- Personal shops (`PersonalShops/`): a character in a town lays up to 30 bag items (`PersonalShopLimits`, Contracts) on
  the store grid, each at a price per unit from 1 to 100,000,000, under a sign of at most 128 UTF-8 bytes. Only items
  `ItemDropRules` lets go sell, never the `PZ1` and `PZ2` puzzle pieces, and the character's gold with the whole listing
  sold must stay within `MoneyLimits.CarryLimit` (`TryOpen`). The items stay in the seller's bag; `PersonalShop.Prune`
  drops an entry whose item left it. `TrySell` moves `Count` units and their price between two candidates when the
  unit price and the units are the ones the buyer was shown (`PriceChanged`, `CountChanged`, `SoldOut` otherwise),
  the buyer has the gold, room and weight, and the seller may carry the gold. The buyer records the purchase
  (`ShopPurchases`, `AppliedShopPurchase`).
- `InventoryAction.ConsumePotion` only removes one potion; the restore is applied later by the Application when the
  eat motion reaches its event (`WorldPlayer`). `Pickup` is handled by `GroundPickupTransactions`, not here.
- The Application validates on a copy: `CloneForInventoryOperation` (inventory, equipment, progress, skills, pools,
  histories), applies the command, persists the snapshot, then `CommitInventoryOperation` adopts the candidate's
  inventory and equipment, or replays the progression command on the original (see [application.md](application.md)).

## Progression

- Experience is `ulong` everywhere (character, table, kill rewards, premium boosts, owner state, wire); thresholds pass
  2^32 from level 84. Sums saturate instead of wrapping; persistence stores it as a signed 64-bit integer (the cap is far
  below 2^63) and refuses a negative stored value.
- `ExperienceTable` holds 285 cumulative thresholds (level 285 starts at 2^63 - 1); `ForLevel(1) = 0`, `LevelFor`
  returns the highest level whose threshold is reached.
- `AwardExperience(amount)`: ignores zero and saturates at `ExperienceTable.Ceiling` (2^64 - 2: no wrap, not a level
  cap). `LoseExperience(amount)` (death penalties) never goes below the current level's threshold. Below tier 3, an award that
  would pass `ForLevel(100)` is rejected entirely (not clamped). Levels are applied one `LevelUp` at a time.
- `LevelUp`: +5 status points; +1 skill point at even levels from 10; +1 final-tier (elite) point at even levels from
  60; pools refilled; `OnLevelUp` and `LeveledUp` raised.
- `TryAllocateAttribute` spends status points one attribute at a time (overflow-checked).
- `SkillLearning` (skill master): the class window has 20 skills in order; tier `t` opens the first `4 * (t + 1)`.
  A skill trains when it has ten ranks of costs, rank < 10, its point kind is available (indices 12-19, the fourth and
  fifth tiers, share the elite points, the rest spend skill points), `level >= RequiredLevel + 2 * rank`, and the
  previous window skill has a rank.
  Price = `BasePrices[index] + RankPrices[index] * currentRank`. `ApplyProgression` requires a non-zero
  `NpcEntityId`; proximity to the master is checked by the Application.

## Regeneration

`Regeneration` returns per-second rates; `RegenerationState.Step(character, posture, dtMs)` accumulates fractions
and applies whole points, clamped to the pool. A full pool discards its fraction; nothing regenerates at 0 life. The
server steps it per player in `FieldSkills.StepEffects` and resets it on death. All rates include the factor
`17.5 / 17` (the original applies value / 17 every four 70 Hz ticks).

| Pool | Per second |
| --- | --- |
| Life | `((L + Str/2 + Hlt) / 180 + LifeRegen/10 + items) / 1.5` (`Str/2` integer) |
| Mana | `(L + Spi*1.2 + Hlt/2) / 115 + ManaRegen/10 + items` (`Hlt/2` integer) |
| Stamina, standing | `base + L/7 + StaminaRegen/10 + items`; base 3.8 / 3.3 / 2.9 for stamina fn 1 / 2 / other; `L/7` integer |
| Stamina, walking | 0.6 x standing |
| Stamina, running | `-((1000 + carried) / max(1, MaxWeight + Str/2 + 500) + 0.4)` (integer division) |
| Stamina, acting | 0 (attacking, casting, flinching, eating) |

## Consumers

- `Skills/` builds on `PlayerCharacter`, `EffectContainer`, `StatKind`, `CombatResolver` and `CombatProfileFactory`
  (`SkillFormulas`, `PassiveSkills`, `PassiveSkillEffects`, `LearnedSkills`); see [skills.md](skills.md).
- `World/` uses `CombatRng`/`ISimRng`, `MonsterAttackReach`, `MonsterOffensiveSkill`, items and `Inventory`
  (loot, shops, warehouse); see [world.md](world.md).
- `Shared/PristonTale.Application` drives everything on the authoritative tick (`FieldCombat`, `FieldSkills.*`,
  `WorldPlayer`, transactions); see [application.md](application.md).
- The client uses `Locomotion`, `AnimationRate`, `DestinationMovement`, `CharacterHead` and `OwnerState`
  (`OwnerState.Capture` reuses unchanged lists by revision and returns the previous instance when the new capture is
  equal as a record, so the encoder sends only changes); see [client.md](client.md).

## How to

- Add a modifiable stat: append to `StatKind` before `Count` (arrays are sized by `Count`). Skill JSON names stats
  by enum member (`JsonStringEnumConverter` in `ClassSkillCatalog`), so never rename members. Read it via
  `Effects.Totals` where it applies, or add it to `ModifierRules.Apply` if it maps onto `DerivedStats`; add a case in
  `tests/PristonTale.Tests` (`PassiveSkillTests`, `SkillSystemTests` or `EquipmentTests`).
- Change a derived-stat formula: edit `DerivedStats.Compute` (or `WeaponDamage`/`Regeneration`), keep the truncation
  points, then run the regression runner (`EquipmentTests`, `ProgressionAndOwnershipTests`, `PotionTests` cover them).
- Change hit/crit/block math: edit `CombatResolver`; keep the roll order or expect seeded tests in
  `MonsterCombatTests` and the skill tests to change outcomes.
- Add an item family: its code and inventory footprint need nothing (the footprint comes from its inventory sprite at
  import). Add its kind and slot to `ItemEquipmentMetadata` if wearable and not covered by a two-character group (two-handed
  keys to its list); restrictions in `ItemClassRestrictions`.
  Check `WorldDataFactory` and the importer ([world-import.md](world-import.md)).
- Add an item stat: carry its range in `ItemTemplate` (from `WorldDataFactory.ToItemTemplate`), its value in
  `ItemRoll`, roll it in `ItemRoller`, sum it in `EquipmentTotals`, apply it in `DerivedStats.Compute`, persist it
  (the roll is saved as JSON, see [persistence.md](persistence.md)), and add it to the item details packet and the
  tooltip ([protocol.md](protocol.md), [client.md](client.md)).
- Add an `ItemKind`: append it, update `ItemDefinition.IsWeapon`, `WeaponDamage` favoured kinds,
  `PlayerAttackReach` and the client item presentation.
- Add a class: new `Classes/<Name>Class.cs`, register it in `CharacterClassRegistry`, add `PlayerClass` (Contracts), its `JobBitCode` and the `JobBits` row,
  its `ClassProfile` in the class file and the `ClassProfiles.Of` row, head patterns in `CharacterHeadModels`, restrictions in
  `ItemClassRestrictions`, and the skill data ([skills.md](skills.md)).
- Change level rewards or training: `PlayerCharacter.LevelUp`, `ExperienceTable`, `SkillLearning`; tests in
  `ProgressionDataTests`, `ProgressionTransactionTests`.
- Verify with the Release build and `dotnet run --project tests/PristonTale.Tests -c Release --no-build`
  (`--filter <text>` narrows cases); see [testing.md](testing.md).

## Pitfalls

- Reading `PlayerCharacter.Derived` can mutate `Life`/`Mana`/`Stamina` (recompute clamps them). Read it on the
  owning thread only.
- `CloneForInventoryOperation` copies no timed effects; the clone's derived stats lack buffs. Do not read buffed
  values from a transaction candidate, and commit copies only inventory, equipment and histories.
- `Equip` throws on an invalid item; use `CanEquip`/`TryEquipFromInventory` for client input.
- `TryPlace`, `TryAdd` and `TryExchange` store copies (same `InstanceId`): compare worn and carried items by
  `InstanceId`, and change a worn copy through `GetEquipped`, not the instance that was placed.
- Weight changes move speed: anything that adds items to a character outside `Inventory` methods must mark the
  inventory changed (`TouchInventory`).
- Player absorption is flat, monster absorption is percent; the monster hit check uses `>` while the player check
  uses `<`. Both match the original receivers.
- `TrySplit` gives the split stack a new `InstanceId`; `TryAdd` keeps the id only for the first new stack.
- `Compatible` stacking uses record equality of `ItemDefinition`; two catalogs with equal data compare equal.
- `ItemEquipmentMetadata` is a data table; edit entries, not structure. Its families are looked up by the
  three-character prefix first, then by the first two characters (`DA` covers every body armour and robe family).
- Requirements and price live in the copy's roll (a specialization shifts them); read `ItemInstance.Roll`, not the
  definition, for anything about a particular copy.

## Related

- [architecture.md](architecture.md) - project layering and threading
- [skills.md](skills.md) - skill data, passives, server skill resolution
- [world.md](world.md) - monsters, AI, loot, spawning, NPC shops and warehouse
- [application.md](application.md) - authoritative tick, transactions, `FieldCombat`
- [persistence.md](persistence.md), [world-import.md](world-import.md) - item and character data sources
- [protocol.md](protocol.md) - how owner state, items and effects travel on the wire
- [glossary.md](glossary.md)
