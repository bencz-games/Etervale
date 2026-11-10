# Skills

The class skill system end to end: the class skill data and rules (`Shared/PristonTale.Simulation/Skills`), learning at
a skill master, server-side cast resolution (`Shared/PristonTale.Application/World/FieldSkills.*.cs`), the skill and
status extension messages, and the client skill window, mouse sockets, cast flow, buff bar and skill VFX hooks.
Monster skills are a separate system (see [world.md](world.md)); they share only the status-effect engine and messages.

## Read this when

- Changing a number of a class skill (damage, cost, cooldown, duration, range, area, motion, effect name).
- Adding a skill mechanic the rule format cannot express yet, or a new `SkillKind`.
- Debugging a refused cast (`SkillCastVerdict`), a wrong hit time, a missing impact or an effect that never ends.
- Changing skill learning: prices, level requirements, skill points, the skill master.
- Changing the skill window, mouse sockets, `F1`-`F8` keys, charge input, the buff bar or summon orders.
- Wiring a skill to a cast, projectile, impact, aura or familiar VFX or sound.

## Code map

| Path | Responsibility |
|---|---|
| [ClassSkillCatalog.cs](../Shared/PristonTale.Simulation/Skills/ClassSkillCatalog.cs) | Loads the embedded class files; `ClassSkillEntry` (definition fields); `SkillJson` options (camel case, string enums, hex codes) |
| `Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json` | One file per `PlayerClass`: 20 skills in skill-window order, definition plus rule. Embedded resources of the Simulation assembly |
| [SkillRules.cs](../Shared/PristonTale.Simulation/Skills/SkillRules.cs) | `SkillRule` and its sub-records (damage, buff, passive, summon, charge, companion, counter, burst); `SkillRuleCatalog` and its load-time validation |
| `Shared/PristonTale.Simulation/Skills/SkillRuleMechanics.cs` | Second half of the rule records: range/damage modes, motion variants, allies, enchant, absorb, reflect, guard, pulse, charm, resurrect; `ForRank`, `HitFrames` |
| `Shared/PristonTale.Simulation/Skills/Skill.cs` | `Skill` definition, `JobTier`, `SkillCategory`, `SkillMouseUse`; rank-indexed mana and stamina |
| `Shared/PristonTale.Simulation/Skills/SkillFormulas.cs`, `SkillFormulas.Mechanics.cs` | Shared formulas: cost, cooldown, proficiency, caster checks, range, motion timing, charge timing, damage, heal, duration, chances |
| `Shared/PristonTale.Simulation/Skills/SkillCastRequest.cs` | `SkillCastRequest`, `SkillCastOutcome` |
| `Shared/PristonTale.Contracts/Skills/SkillCodes.cs` | `SkillCastVerdict`, `SkillCastCommand`, `SkillEventPhase`, `SkillEventFlags` (with `Mode`/`WithMode`), `StatusEffectOp`: one declaration for the server, the wire and the client |
| `Shared/PristonTale.Simulation/Skills/LearnedSkills.cs` | Per-character ranks (1..10) and use counter (0..10000) |
| `Shared/PristonTale.Simulation/Skills/PassiveSkills.cs`, `PassiveSkillEffects.cs` | Passives: `values` tables read by derived stats and combat; `modifiers` passives synced as permanent effects |
| `Shared/PristonTale.Simulation/Skills/SkillProjectile.cs` | Projectile flight speed (900 units/s), shared by server damage time and client projectile VFX |
| `Shared/PristonTale.Simulation/Progression/SkillLearning.cs` | Skill-master training: window order, tier gates, level rule, prices, which point pool pays |
| `Shared/PristonTale.Simulation/Modifiers/` | Effect engine (`EffectContainer`, `StatKind`, `ControlFlags`, `EffectSourceKind`); player skills, items and monster skills apply effects through it |
| [FieldSkills.cs](../Shared/PristonTale.Application/World/FieldSkills.cs) | Server authority on one field: `Cast` validation, cast timeline, `Resolve` dispatch per `SkillKind`, event buffers |
| `Shared/PristonTale.Application/World/FieldSkills.<Mechanic>.cs` | One partial per mechanic: `Damage`, `Areas`, `Buffs`, `Allies`, `Defense`, `Companions`, `Charge`, `Charm`, `Resurrection`, `Summons` |
| `Shared/PristonTale.Application/World/PlayerSkillCooldowns.cs` | Per-player server cooldowns on the field clock; `Rebase` on field change; snapshot on change |
| `Shared/PristonTale.Application/World/ProgressionTransactions.cs` | `LearnSkill` command: skill-master reach check, then `PlayerCharacter.ApplyProgression` |
| `Shared/PristonTale.Protocol/Packets/SkillPackets.cs` | Wire records: `SkillCastPacket`, `SkillEventEntry`, `StatusEffectEntry`, `SkillCooldownsPacket` |
| `Server/PristonTale.Server/Net/ClientSession.cs` (`CastSkillAsync`), `WireMapping.cs` | Wire edge: packet to `SkillCastRequest`, outcome to `SkillCastResultPacket`, views to entries |
| `Client/PristonTale.Client.Net/Presence/SkillPresence.cs`, `Online/PresenceDecoder.cs` | Client-side decoded skill events, statuses, cooldowns, cast results |
| [GameScene.Skills.cs](../Client/PristonTale.Gameplay/World/GameScene.Skills.cs) | Mouse-socket casts, walk into range, local pre-checks, charge release, summon mode toggle, refusal notices |
| [SkillPresentation.cs](../Client/PristonTale.Gameplay/Effects/SkillPresentation.cs) | Server timeline to motions, VFX, sounds, floating text, auras; loads the VFX library |
| `Client/PristonTale.Gameplay/Effects/SkillState.cs` | `SkillCooldownBook` (local cooldowns), `StatusBook`/`StatusEntry` (statuses of every visible entity) |
| `Client/PristonTale.Gameplay/Effects/SkillSoundBank.cs` | Skill sounds by asset path, decoded on a worker, created on the game thread |
| `tools/PristonTale.VfxImport/Authored/Common/`, `Authored/Classes/<Class>/` | Named skill effects, installed into `<assetRoot>/Effect/Vfx` by `ptvfx import`; format in [effects.md](effects.md) |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSkillsPanel.cs`, `HudSkillTreeLayout.cs` | Skill window (`S`): 16 nodes, training and ready gauges, short-key marks, training clicks at a skill master |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSkillBindings.cs` | Left/right mouse sockets and the eight `F1`-`F8` short keys (in memory, not saved) |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSkillHud.cs` | Bottom-bar sockets with cooldown sweep; continuous-skill bar (keep icons, duration gauge, summon mode tooltip) |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSkillDetails.cs` | Tooltip numbers per rank, read from the same `SkillRule` |
| `tests/PristonTale.Tests/Skill*Tests.cs`, `<Class>SkillTests.cs`, `ClassSkillDataTests.cs`, `PassiveSkillTests.cs` | Regression cases (by mechanic, or by class for a class's own skills); `SkillScenario`/`SkillFixtures` build a one-player field for server tests |

## Skill identity and window order

- Code: `group << 24 | tierBit << 16 | slot`, written as a hex string (`"0x01020001"`). `tierBit` is the `JobTier`
  value (`Job1` 0x1, `Job2` 0x2, `Job3` 0x4, `Job4` 0x8, `Job5` 0x10) and must match `tier`; `slot` is 1..4.
- Class groups: Mechanician 0x01, Fighter 0x02, Pikeman 0x03, Archer 0x04, Knight 0x05, Atalanta 0x06,
  Priestess 0x07, Magician 0x08, Assassin 0x10, Martial Artist 0x20, Shaman 0x50.
- The file name is the `PlayerClass` enum name. The array order is the skill-window order (window index 0..19): it
  drives learning order, price, tier gates, proficiency streaks and HUD placement. It is not always the slot order:
  Mechanician lists `0x01010004` before `0x01010003`.
- `CharacterClass` takes its skill list from `ClassSkillCatalog.For`, so `character.Class.Skills[index]` is the window
  entry at `index`. A code is unique across all files (load fails otherwise).
- Martial Artist skill entries use the `TMA` button-art family under the `Martial` HUD directory, so the skill window
  resolves their icons independently of the Fighter button family.
- Every class has a rule for all 20 skills of its five tiers (the fifth at levels 80, 83, 86, 90). The Assassin's, the
  Shaman's and the Martial Artist's skill clips are those of their motion tables tagged from 163, 183 and 203 (the
  class motion table numbers tags per class: first tag + 4 x tier + slot - 1, tier 0 to 4); the Assassin Mastery grants
  its critical chance with daggers, which the original's empty weapon list never allowed.
- The Martial Artist strikes with vambraces: Low Kick adds its table to the kick (30% more on mutant and mechanic
  monsters); Double Backspin Blow, Hyper Punch, Twister Kick, Tiger Cannon (reaching 150), Barrage, Shoulder Charge,
  Typhoon Kick and Hunting Hawk strike once per clip event with their percent (Hyper Punch and Typhoon Kick with a
  percent attack rating, Twister Kick and Shoulder Charge with a relative critical, Twister Kick 30% more on normal
  monsters); Leap Smash strikes as it leaps, without an accuracy roll, and again on landing (30% more on demons);
  Elbow Rush raises the attack in a 100-wide box 120 ahead; Line Break strikes its target twice and, with the second
  strike, up to its rank's count of enemies around the target with its table raised by its rate plus the attack (a
  `followArea`). War Cry weakens up to 32 monsters around its target for 15 s (their damage down, the critical chance
  of their attackers up), without an accuracy roll or damage, turned away only by a full bionic resistance; a monster
  already weakened keeps its running War Cry, whoever casts it again. Rage raises the vambrace's attack speed, Deep
  Breath maximum life, Nerves of Steel defence and absorption (the buff starts with the clip, which has no event), and
  Hard Training the strength, agility and spirit the formulas read for the caster and the party within its range,
  kept on a member who already has it. Strength, Stamina and Defense Mastery are passives (Defense Mastery with a
  vambrace held). Leap Smash and Hunting Hawk play at four fifths of the attack speed.
- The Shaman's spells roll base + level x `levelPercent` / 100 + weapon item damage x `weaponTimes` + spirit / the
  rank's divisor, never critically. Haunt drains a share of its target's maximum life (bosses spared) and returns it
  up to 40% of the caster's maximum life; Soul Manacle holds a monster (bosses spared, hits do not end it); Chasing
  Hunt taunts for `tauntTenths`; Migal and Midranda reach the party within 400 and never stack; Mourning of Pray needs
  one of them on the caster, and party members holding one echo it around themselves with the caster's weapon.
- The Mechanician's fifth tier: Land Mining strikes up to its rank's count of enemies around the caster (no target
  needed) with its mine damage raised by its rate plus the attack damage, half more on mechanic monsters; Hyper Sonic
  speeds the attack only while one of its weapons is held; Rolling Smash strikes four times with a flat then percent
  attack rating; Power Enhancement raises both ends of the caster's single-target hits' attack range and half as much
  of the maximum of the party members within its range (`HitDamageMin`, `HitDamageMax`: normal attacks and weapon skills'
  hits, never areas or bursts), and ends on a Mechanician holding none of its weapons.
- The Fighter's fifth tier: Down Hit strikes three times with a percent attack rating; Power Dash reaches 190 and
  stuns the monster it struck for 10 to 37 s, which its hits do not end; Mortal Blow strikes its target with the attack
  raised by its percent and up to its rank's count of enemies around the caster with its table, once, on the clip's
  first event; Bloody Berserker adds flat damage to single-target hits and lowers absorption, and it and the Berserker
  end each other. Down Hit and Mortal Blow shake the camera of the players near the caster (`cameraShake`).
- The Pikeman's fifth tier: Dancing Reaper is one strong strike; Final Spear strikes up to six enemies around the caster
  without missing, with a spear rising under each (`targetEffect`); Amplified raises both ends of the single-target
  hits' attack range (as Power Enhancement) and the attack rating and lowers absorption; Side Spin Attack strikes twice
  and each landed strike drains 10 to 37% of the monster's maximum life (lowered by its bionic resistance) from a
  monster above half its life, bosses too but never the undead, giving back up to 40% of the caster's maximum life.
- The Archer's fifth tier: Evade Shot reaches 300 and its arrow, at the clip's second event, stuns the monster it struck
  for 4 to 17 s; Thunder Roop shoots twice, as the cast starts and at its event, with a percent attack rating and 20 to
  65 beyond the bow's reach; Nature Splash and Circle Trap strike every enemy around their target with the attack raised
  by their rate, Circle Trap slowing them by 10 to 28% for 0.5 to 5 s. Stuns of monster holds (Power Dash, Evade Shot)
  spare bosses, and both are cast on monsters only (`monstersOnly`).
- The Knight's fifth tier: Soul Breaker strikes as the cast starts with a flat attack rating; Crescent Moon strikes
  every enemy around its target with the attack raised by its rate; Saint Blade strikes as the cast starts and at its
  first event, the second hit borrowing Grand Cross's attack rating percent by its rank (`borrowedRating`), both raised
  against the undead; Holy Benedic adds damage against the undead to the caster and the party members within 200, and to
  a clicked player, in the party or not (`allies` with `anyClicked`). The Knight's fifth-tier clips are tagged 99 to
  102, past its twentieth tag.
- The Atalanta's fifth tier: Talaria adds 1 to 4 move speed and lifts the step limit (`MoveSpeedUnbound`) of the caster
  or a clicked player (any player, no party); Galaxy Coup strikes the enemies around its target that its accuracy,
  raised by its percent, reaches, with the attack raised by its rate; Summon Arcuda calls the bear (`Arcuda`) for
  355 s; Snippy Fear throws two javelins beyond the javelin's reach with a relative critical bonus.
- The Priestess's fifth tier: Spirit Impact strikes the enemies around its target with a spell roll (the Shaman's
  terms: base, level x 50%, weapon item damage x 2 to 5, spirit / 2 and / 1); Piercing Ice never misses, rolling its
  table plus the attack damage raised 200 to 830% (`attackPowerPercent`); Summon Ramiel gives evasion and an angel that
  strikes every 7 to 4 s, and it and Summon Muspell exclude each other; Blessing Krishna raises defence by a percent and
  absorption. Spells with `spellPower` take the elemental spirits' boost.
- The Magician's fifth tier: Silraphim strikes the enemies around its target with a spell roll (level x 100%); Vis
  Tenus's hit shrinks the monster it struck for 10 to 55 s (`lifeCut`, `FieldSkills.Shrink`): it loses 10 to 55% of its
  maximum life (a monster with no more life dies at once) and 10 to 37% of its damage and draws at half its size, all
  given back when it ends, bosses spared; the caster waits for it to end (`casterBuff` with `exclusive`), and it is cast
  on monsters only, never a boss or a castle's door, tower or crystal tower (`monstersOnly`, `refusesBosses`); Prima
  Ignis never misses, rolling its table plus the attack raised 200 to 830%; Third Anima adds 15 to 150 spell power (plus
  8 to 35 by Spirit Elemental's rank) and takes 5 to 50% of the maximum mana on the caster or a clicked Magician,
  Priestess or Shaman (`allyClasses`), and it and Spirit Elemental replace each other.
- The Assassin's fifth tier: Shadow Bomb strikes up to its rank's count of enemies around the caster with its table
  raised by Land Mining's rate (as the original server raises it) plus the attack damage; Rising Slash, Violent Stab
  (two hits) and Storm are strong strikes.
- The Shaman's fifth tier: Creed raises maximum mana and stamina 10 to 28% for 90 to 270 s; Press Of Deity strikes the
  enemies within 110 to 200 of its target with a spell roll (level x 100%, weapon item damage x 2 to 6), a meteor
  falling onto the target; Ghostly Nail is an unresisted spell bolt (weapon item damage x 4 to 13); High Regeneration
  raises maximum life 5 to 32% while learned.

## Learning (skill master)

Rules in `SkillLearning` and `PlayerCharacter.ApplyProgression`; the command is `ProgressionCommand` with
`ProgressionAction.LearnSkill`, the skill code and the NPC entity id.

- The NPC must offer `NpcServiceKind.SkillMaster` and be within reach (`NpcServiceTransactions.CanReach`); the player
  must be alive. Opening the skill master opens the skill window in training mode
  (`HudSkillsPanel.HandleTrainingClick`).
- Class tier `t` (0..4) opens window indices `< 4 (t + 1)`. A skill needs rank >= 1 in the window entry before it.
- Training from rank `r` to `r + 1` needs level `RequiredLevel + 2r`, rank < 10, ten mana entries, one point, money.
- Price: `BasePrices[index] + RankPrices[index] x r`, both tables indexed by window position (500..40000 base).
- Indices 0..11 spend skill points (one per even level from 10); indices 12..19 (fourth and fifth tiers) share the
  elite points (`EliteSkillPoints`, one per even level from 60).
- The fifth tier's prices continue the table: base 50,000, 60,000, 70,000, 80,000, plus 10,000, 12,000, 14,000,
  16,000 per current rank.
- Ranks and use counters persist with the character (`CharacterStateCodec`); see [persistence.md](persistence.md).

## Class skill file format

A JSON array (comments and trailing commas allowed) of objects. Each object is parsed twice: as `ClassSkillEntry`
(definition) and, when `kind` is present, as `SkillRule` from the same object. Names are camel case, enums are strings
(flags enums take `"A, B"`), skill codes are hex strings or numbers. An unknown property is refused (`InvalidDataException` naming the file and the
property).

Conventions: `[10]` is a per-rank table with exactly ten entries (rank 1..10; validation rejects other lengths, `[]`
means absent); `SkillRule.At` clamps the rank into the table. Distances are world units (wire scales them by 256).
`...Tenths` are tenths of a second, `...Ms` milliseconds, percents are integer percent unless noted.

### Definition fields (`ClassSkillEntry`)

| Field | Type | Meaning |
|---|---|---|
| `code` | hex | Skill code (see identity). Non-zero |
| `name`, `description` | string | Display text; `name` required |
| `icon` | string | Skill-window button art key (`Skill.IconKey`) |
| `tier` | `Job1`..`Job5` | Must equal the code's tier bit |
| `category` | `SkillCategory` | `ActiveAttack`, `AreaAttack`, `MultiHit`, `Buff`, `Passive`, `Heal`, `Summon`, `Debuff`; tests require `Passive` iff `kind` is `Passive` |
| `requiredLevel` | int >= 1 | Level for rank 1 (rank r+1: `+2r`); casting also needs it |
| `mouse` | `Left`/`Right`/`Both`/`None` | Socket the skill may occupy; `None` is not castable (passives) |
| `mana` | int[10] or `[]` | Mana per rank before `SkillManaCost` modifiers. Without ten entries the skill cannot be trained |
| `stamina` | `[base, perRank]` or `[]` | Stamina = base + perRank x (rank - 1) |
| `weapons` | `ItemKind` names | Main hand or off hand must hold one (shield skills list `Shield`); `[]` allows any |
| `kind` | `SkillKind` | Present: the entry also carries a rule. Absent: definition only, never castable |

### Rule fields (`SkillRule`), by group

- Resolution: `kind`: `Passive`, `WeaponAttack` (weapon swings, accuracy per hit), `MagicBolt` (never misses), `Area`,
  `SelfBuff`, `Debuff`, `Heal`, `LifeToMana`, `Resurrect`, `Charm`, `Summon`. `target`: `None`, `Self`, `Enemy`, `Ally`
  (caster or another player; no target = caster).
- Cooldown: `masteryBase`, `masteryPerRank` (mastery units; both 0 = no cooldown).
- Gates: `requiresSkill` (code that must be learned), `requiresClassArmor` (body armor made for the caster's class
  only), `exclusive` (codes: cast refused while one runs on the caster, or on the ally for `Ally` rules; list the
  skill's own code to forbid recasting while active), `replaces` (codes ended on cast, including a replaced summon).
- Range: `range[10]`; `rangeMode`: `Table` (table, or weapon reach if empty), `AtLeastWeapon`, `AddWeapon`;
  `reachPercent[10]`, `reachBonus[10]`: range = `r + (r - 50) x pct / 100 + bonus` from the weapon reach `r`.
- Area: `areaShape`: `Circle`, `Box` (`boxWidth` wide; length from `radius`, or the reach range + `boxLengthPad`),
  `Chain` (`radius` = link length, `maxTargetsByRank` = links); `areaCenter`: `Caster`/`Target`; `radius[10]`;
  `radiusTargetPad` (radius = distance to the clicked target + pad); `maxTargets` (default 41, 1..64),
  `maxTargetsByRank[10]` (a capped area strikes a random selection).
- Area options: `mainInArea` (clicked target always added, beyond the cap, no roll), `areaIncludesTarget` (target
  struck even outside the shape), `rollsAccuracy` (dodgers skipped), `areaAccuracy` (a miss keeps its slot),
  `areaCritical`, `mainHit` (damage rule: an extra weapon hit on the clicked target), `pushBy` (knock-back distance and
  flinch), `pushTo[10]` (push to this distance), `taunt` (targets lock on the caster for `tauntTenths[10]`, else the
  caster buff's or the buff's duration), `partyEcho` (party members within the `allies` radius holding one of
  `requiresEffect` strike the same area around themselves, with their level and spirit and the caster's weapon).
  `allyClasses`: the classes another player must be to take an `Ally` rule.
  Bosses are not pushed or taunted.
- Cast requirement: `requiresEffect` (codes: the cast is refused unless one of them runs on the caster); `monstersOnly`
  (never cast on a player) and `refusesBosses` (never on a boss or a castle's door, tower or crystal tower).
- Force orbs: `forceOrbs` (`All`, `Weapon`, `Magic`, `None`: the orb families the skill takes) and `forceOrbPercent` (0 to
  300; 0 takes the tier's share, `ForceOrbs.TierPercent`). The share reaches the skill's full charge, modes and follow
  area too (`SkillRule.OrbShare`, set when the catalog loads); a weapon orb adds to single-target weapon hits, a magic
  orb to spells, areas and a burst.
- Accuracy and critical: `attackRating[10]`, `attackRatingPlusLevel`, `attackRatingPercent[10]`, `criticalAdd[10]`,
  `criticalPercent[10]` (relative: crit + crit x pct / 100), `borrowedCritical` {`skill`, `values[10]` indexed by that
  skill's rank, `divisor`}, `borrowedRating` {`skill`, `values[10]`, `fromHit`}: attack rating percent from that
  skill's rank on the hits from `fromHit` (1-based) on, `noAccuracy`, `noAccuracyHits` (1-based hit numbers that skip the
  accuracy roll), `stopAfterMiss`, `noCriticalHits` (1-based hit numbers), `forceFlinch`,
  `volley[10]` (shots per strike, each rolls accuracy, damage adds up).
- Damage: `damage` (damage rule, below); `burst` {`afterHit`, `radius`, `percent` (100), `element`}: a burst around the
  caster after that weapon hit; `followArea` (an `Area` rule) with `followAreaFromHit` (1-based, default 1): weapon skills
  strike that area after the weapon hit of every hit from that one on (the skill's own effects apply); `lifeCost[10]`: weapon skills pay tenths of a percent of max life per landed hit,
  `LifeToMana` converts that percent of max life into the same percent of max mana.
- Effects: `buff`, `casterBuff` (buff rules, below; `casterBuff` applies on the first hit, for areas only when a target
  was hit; a weapon attack's harmful `buff` lands on the monster each of its hits struck); `healMin[10]`, `healMax[10]` (plus weapon item damage / 3 and spirit / 8 on the minimum, / 6 on the
  maximum); `allies`, `passive`, `summon`, `charm`, `resurrect` (below).
- Variants: `modes` (rule fragments, one drawn per cast, such as an element per cast; timing stays the parent's; at most
  four fit the wire's two mode bits); `charge` (below).
- Motion: `motion`: `Skill` (class skill clip `motionIndex`, `motionFrames`, `motionEvents` = hit frames) or `Attack`
  (the weapon's attack clip, `loops[10]` repeats, one hit each); `hitFramesByRank[10][]` replaces the events per rank;
  `extraEvents` (frames from clip start), `endHit` (a hit at the clip end), `maxHits` (first N events only);
  `motionVariants` [{`fromRank`, `motion`, `motionSpeed`, `motionIndex`, `motionFrames`, `motionEvents`,
  `extraEvents`}] replace the clip from a rank on.
- Motion speed: `motionSpeed` (fixed frame step, 80 = rate 1; 0 follows the attack speed), `motionSpeedByRank[10]`,
  `attackSpeedSteps[10]` and `motionSpeedSteps` (0..6; each step adds 0.125 to the rate), `motionRatePercent`,
  `motionSpeedPercent` (1..400, default 100).
- Presentation: `projectile` (hits land after the flight), `projectileArc` (lob height at mid flight), `castEffect`,
  `impactEffect`, `impactNeedsTarget` (no impact effect when the cast struck no one: Divine Lightning's bolts fall only on
  the monsters it hit), `projectileEffect`, `buffEffect` (aura while the status runs), `companionEffect` (familiar or counter
  strike), `castSound`, `impactSound`, `companionSound` (game-data paths under `wav/`), `keepIcon` (buff-bar icon).
- Presentation per hit and on actors (`SkillRulePresentation.cs`, ignored by the server): `impactSounds` with
  `impactSoundOrder` (`Sequence`: impact n of the cast plays entry n, wrapping; `Random`; `Final`: only the cast's last
  impact, `SkillFormulas.HitCount`), played on landed hits instead of `impactSound`; `impactEffects` (impact n plays entry
  n, the last entry after that); `-` in either list plays nothing at that impact. `statusLook` while the skill's
  status is on an entity: `Frost`, `Poison`, `Distortion` (harmful statuses only), `Vanish`, `Vague`. `rageGlow`
  (red light 0..255 at each impact, fading 70 a second), `shieldModel` (drawn instead of a held shield while the
  status lasts, from the cast's hit on), `weaponGrowth[10]` (percent growth of a red translucent weapon copy during the
  cast), `afterimages` (0..8, trailing copies through the cast), `castShieldBone` and `castShieldGlow[3]` (the held
  shield moves to that body bone and glows during the cast), `weaponOrbit` (two effects, a light and its trail, circling
  the holder's weapon while the status lasts), `cameraShake` (each impact shakes the camera of the players within 400
  of the caster by (400 - distance) / 15 units of view distance, `PristonCameraWave`), `targetEffect` (an effect on
  each enemy an area struck; the server reports each with a `Struck` impact), `defenseEffect` (an effect on the holder
  each time its buff defends it: an absorbing shield takes its share, a reflection strikes back, a guard returns life,
  or, for a buff of stats alone, a hit reaches the holder from a monster of `defenseBroods`, from any attacker when the
  list is empty; the server reports each with a `Defended` impact: Energy Shield, Holy Reflection, Divine Inhalation,
  Physical Absorption, and Holy Body against the undead), `hitEffect` (the holder's basic hits show it on the struck
  target instead of the weapon's spark, `<effect>.<element>` for the element the status drew: Enchant Weapon) and
  `shotEffect` (the holder's thrown basic attacks fly as it from the weapon to the target instead of the javelin: Frost
  Javelin). The hit bursts of basic attacks and the blood of players hit are in effects.md (`HitEffects`).

### Damage rule (`damage`, `mainHit`, `damage.main`)

| Field | Meaning |
|---|---|
| `percent[10]`, `percentAfter[10]` | Weapon hits: rolled attack gains `percent`, then `percentAfter` (each truncates) |
| `min[10]`, `max[10]` | Weapon hits: added per hit. Bolts and areas: base range. Equal lengths required |
| `levelDivisor`, `levelDivisorByRank[10]`, `levelOnMax` | Level / divisor added to both ends (max only with `levelOnMax`) |
| `weaponDivisor`, `weaponFromAttack`, `spiritMinDivisor`, `spiritMaxDivisor` | Magic terms: weapon (or attack) damage / divisor, spirit / divisor |
| `levelPercent[10]`, `spiritMinDivisorByRank[10]`, `spiritMaxDivisorByRank[10]`, `weaponTimesMin[10]`, `weaponTimesMax[10]` | Spell terms per rank: level x percent / 100 on both ends, spirit / the rank's divisor (0 none), weapon item damage x times |
| `tablePercent[10]` | Bolts and areas: `min`..`max` alone gain this percent before the attack power and the other terms join them |
| `strengthMaxDivisor` | Weapon rolls: maximum gains strength / divisor |
| `attackPower`, `spellPower` | Roll the caster's attack damage with `percent`; tables gain the spell-power percent |
| `attackPowerPercent[10]` | The attack damage alone gains this percent before it joins the roll |
| `areaPercent` (100), `mainPercent`, `mainElement`, `main` | Area share per target; extra share and element of the main target; a separate main hit |
| `randomHits[10]` | Power x a count rolled in `hits / 2 + 1 .. hits` |
| `element` (`Bionic`), `flatElement`, `resistAddedOnly` | Element; `flatElement` makes `min..max` an add resisted by its own element |
| `resistanceDivisor` (1) | Target resistance divided by this before it applies |
| `unresisted` | No element: no element resistance lowers the hit |
| `noCritical`, `neverMiss` | No critical; weapon hits skip the accuracy roll |
| `bonusBroods`, `bonusPercent`, `bonusPercentByRank[10]` | Extra percent vs broods (`Normal`, `Undead`, `Mutant`, `Demon`, `Mechanic`); every brood list loads as `MonsterBrood[]` and is matched with `SkillFormulas.MatchesBrood` |
| `mode` | `Rolled` (default), `MaxLifeShare`, `CurrentLifeShare` (percent of each target's life) |
| `share[10]`, `chance[10]`, `chanceLevelDivisor`, `shareResistedByBionic` | Share modes: percent, chance (+ level / divisor), bionic resistance. A rolled bolt and a weapon attack drain `share` percent of the struck monster's maximum life after each hit (bosses spared unless `drainsBosses`) |
| `minLifePercent`, `onlyBroods`, `excludeBroods`, `lifeToCasterCapPercent` | Share filters (the drain after a hit reads the life before it); drained life returns to the caster up to a cap |
| `drainsBosses` | The drain after a hit (a bolt's or a weapon attack's `share`) takes from bosses too |

### Buff rule (`buff`, `casterBuff`)

| Field | Meaning |
|---|---|
| `durationTenths[10]` | Required, all > 0 |
| `modifiers` [{`stat`, `op`, `values[10]`, `valuesMax[10]`}] | `StatKind`, `Flat`/`Percent`; `valuesMax` makes the value a roll per cast |
| modifier `levelDivisor`, `rankOf`, `othersPercent` | Adds level / divisor; indexes by another skill's rank (0 if unlearned); -1 follows `allies` |
| `control` | `ControlFlags`: `Stun`, `Root`, `Silence`, `Slow`, `Hidden`, `Charmed` |
| `harmful`, `resistedByBionic`, `resistedBy` | Duration x (100 - resistance) / 100 on the target (bionic or the named element) |
| `periodic` [{`resource`, `min[10]`, `max[10]`, `intervalMs`}] | `Life`/`Mana`/`Stamina` change per tick, rolled once per application; negative drains |
| periodic `percentOfMax`, `nonLethal` | Amount is a percent of the maximum; life damage stops at 1 life and ends the effect |
| `endsOnHit`, `endsOnAreaAttack`, `endsOnDamage` | Stealth consumed by the next single-target hit; ended by an area attack; stun broken by damage |
| `weaponBound`, `requiresWeapon` | Modifiers count only with a listed main-hand weapon; effect ends when none is wielded |
| `sparesBosses` | Boss monsters never take the effect |
| `fullResistanceOnly` | Only a full resistance (100) turns the effect away; a lower one does not shorten it |
| `keptWhileRunning` | A running copy, from any caster, is kept: a new cast neither refreshes nor stacks it |
| `lifeCut[10]` | A monster loses this percent of its maximum life while the effect lasts and draws smaller; one with no more life dies |
| `companion` | Familiar, see below |
| `counter` {`borrowSkill`, `percent[10]`, `element`, `bonusBroods`, `bonusPercent`} | Strike back when the shield blocks a monster's normal attack |
| `enchant` {`choices`, `basicOnly`} | Element added to hits; one choice drawn per cast |
| enchant choice {`element`, `min[10]`, `max[10]`, `slowTenths[10]`, `slowPercent[10]`} | Added damage; ice slows the struck monster (ice resistance shortens it) |
| `absorb` {`percent[10]`, `resource`} | Share of each monster hit paid by mana (default) or another resource |
| `reflect` {`percent[10]`, `broods`} | Share of a matching monster's attack returned to it |
| `guard` {`healPercent[10]`, `broods`, `onBlock`} | Life recovered from a matching monster's hits, or only from blocked ones |
| `pulse` {`intervalMs`, `durationTenths`} | Short copies given to the allies around the holder while it lasts |

`companion`: `intervalMs[10]` (strike interval), `flightSpeeds` (units/s there and back), `ownerTarget` (strike the
owner's target while the owner fights) or `radius` (300, nearest enemy around the holder); damage from `min[10]` and
`max[10]`, from `choices` (enchant choices, one drawn per cast, plus the `weaponDivisor`, `spiritMinDivisor` and
`spiritMaxDivisor` terms) or from `borrowSkill`'s table at its rank x `percent[10]`; `element`, `bonusBroods`,
`bonusPercent`. Strikes never miss and never crit.

### Other blocks

- `allies`: `radius[10]`, `includeCaster` (true), `othersPercent` (100; 0..100 strength on other players), `height`
  (80), `excludeWith` (codes whose holders are skipped), `party` (only the caster's party members, an ally target
  too unless `anyClicked`), `requiresParty` (a cast without a party is refused). Allies are the other living players of the field within
  the radius. As in the original, Holy Valor, Grand Healing (which needs a party) and Enchant Weapon are party skills;
  Force of Nature, Triumph and Hall of Valhalla, Virtual Life and Regeneration Field reach any player.
- `passive`: `effect` with `values[10]`: `Resistance` (with `element`), `WeaponDamagePercent`, `MaxStaminaPercent`,
  `CriticalChance`, `BlockChance` (`withoutShield`), `MaxLife`, `WeaponAttackRatingPercent`, `Evasion`; weapon effects
  need a weapon the skill lists, or one made for the class with `classWeapon`. Or `modifiers` (permanent effect via
  `PassiveSkillEffects`, optionally only while the main hand holds one of `weapons`).
- `summon`: `monster` (the world catalog monster's key, else its display name: model, motions, sight, reach), `lifetimeMs` (> 0),
  `damageMin[10]`/`damageMax[10]`, `attackRating[10]`, `defence[10]`, `life[10]` (empty keeps the catalog value),
  `lifePerStrength`, `defencePerAgility` (owner attributes added at the call), `scaleBase`, `scalePerRank` (model
  scale in 1/256; 0 keeps the catalog size), `aggressive`, `moveSpeedPercent` (100).
- `charm`: `chance[10]` (lowered by bionic resistance), `lifePercent[10]` (life kept), `broods`; `buff` gives the
  duration.
- `resurrect`: `chance[10]`, `radius` (300), `lifePercent` (50), `guardTenths` (50; time the revived player cannot be
  hurt). It revives the nearest dead member of the caster's party.
- `charge`: `startFrame` > 0, `fullFrame` > `startFrame`, `releaseSpeed` > 0 (frame step after release), `maxHits`,
  `full` (rule fragment that resolves a full charge). Requires `motionSpeedByRank[10]` and a hit event >= `fullFrame`.

### Load-time validation (`SkillRuleCatalog.Validate`)

The catalog loads in a static initializer and throws `InvalidDataException` on the first bad entry, which fails both
server and client start. Required per kind: `WeaponAttack`/`MagicBolt` need `damage`; `Area` needs a size
(`radius[10]`, `radiusTargetPad`, or a box with `reachPercent`/`reachBonus`) and one of `damage`, `buff`, `taunt`,
`casterBuff`; `SelfBuff`/`Debuff` need `buff`; `Heal` needs `healMin`/`healMax`; `Summon` a valid `summon`; `Charm`
`charm` and `buff`; `Resurrect` `resurrect`; `Passive` a `passive` with `values[10]` or `modifiers`. Castable
`Skill`-motion rules need `motionFrames` and hit frames (`motionEvents`, `extraEvents` or `hitFramesByRank`), and every
event frame must lie within the clip. A `followArea` is an `Area` rule of a `WeaponAttack` with the data an area needs.

Example (real entry, Mechanician window index 1):

```json
{ "code": "0x01010002", "name": "Mechanic Bomb", "tier": "Job1", "category": "AreaAttack", "icon": "TM12 M_bomb",
  "description": "...", "requiredLevel": 12, "mouse": "Right", "mana": [8, 10, 12, 14, 17, 20, 23, 26, 29, 33],
  "stamina": [28, 2], "weapons": [], "kind": "Area", "target": "Enemy", "masteryBase": 73, "masteryPerRank": 3,
  "range": [150, 159, 168, 177, 186, 192, 198, 204, 210, 216], "areaCenter": "Target",
  "radius": [85, 90, 95, 100, 105, 110, 115, 120, 125, 130],
  "damage": { "min": [5, 6, 7, 8, 9, 10, 11, 12, 13, 14], "max": [29, 33, 37, 42, 47, 53, 60, 68, 77, 87],
    "areaPercent": 50, "mainPercent": 50, "noCritical": true, "bonusBroods": ["Mutant", "Mechanic"],
    "bonusPercent": 50 },
  "motionIndex": 61, "motionFrames": 40, "motionEvents": [19], "projectile": true, "projectileArc": 60,
  "projectileEffect": "vfx.projectile.bomb", "impactEffect": "vfx.impact.bomb",
  "castSound": "wav/Effects/Skill/Tempskron/Mechanic_Bomb 01.wav",
  "impactSound": "wav/Effects/Skill/Tempskron/Mechanic_Bomb 02.wav" }
```

## Shared formulas

- Cooldown (`SkillFormulas.CooldownMs`): proficiency `P = min(min(talent / 3 + itemMastery, 50) x 100 + uses, 10000)`
  (`Proficiency`; fourth- and fifth-tier skills always 10000, `IsFullyTrained`; `itemMastery` is the worn magic mastery, truncated); mastery `M = clamp(masteryBase + masteryPerRank x rank - P / 100, 1, 70)`;
  cooldown = `35 x max(1, 35 / max(1, 70 / M)) x 14 ms`, from 0.49 s to 17.15 s.
- Proficiency (`UsesAfterCast`): every `CastsPerStep[windowIndex] + (rank - 1) / 3` casts add 100 uses (max 10000).
  The cast counter (`WorldPlayer.SkillCasts`) is per session; uses persist. A changed use count raises the skills
  revision, so the owner's skill list (`OwnedSkillView.Uses`) carries it to the skill window's training gauge.
- Mana: `mana[rank]` scaled by `SkillManaCost` percent plus flat, not below 0. Stamina has no modifiers.
- Timing (`Timing`, `Plan`, `MotionRate`): skill clips play at the fixed frame step / 80, or at the caster's attack
  rate + 0.125 per speed step, times the two percents; frames last 33 ms at rate 1 (`MotionTiming`). Weapon-clip
  skills repeat the caster's swing `loops` times, sped up by `WeaponClipSpeedup` (that rate over the attack rate; the
  client applies the same speed-up to each caster's attack rate). `ChargeTiming` gives the window start, full time and
  full-charge hits.
- The client uses the same functions for tooltips, cast range and motion playback, so client and server must ship the
  same data.

## Server cast pipeline

Threading: `ClientSession.CastSkillAsync` maps the packet to `SkillCastRequest`; `WorldServer.CastSkillAsync` enqueues
it on the world tick thread; `FieldInstance.CastSkill` checks the player is still on that field (`Stale`) and calls
`FieldSkills.Cast`. `FieldSkills` touches only memory and uses the field's `CombatRng`. See
[application.md](application.md) for the tick.

`FieldSkills.Cast` checks, in order (first failure is the verdict): `Dead`; non-`Cast` commands go to `Release` or
`SetSummonMode`; `Busy` (any non-idle action); `InVillage`; `NotCastable` (not in the caster's class, no rule, or a
summon whose monster the catalog lacks); `SkillFormulas.CheckCaster`: `NotLearned` (rank 0 or level below
`requiredLevel`), `NotCastable` (passive or `mouse: None`), `Busy` (stun or silence), `AlreadyActive` (`exclusive`,
non-ally rules), `WeaponNotAllowed`, `NotEnoughMana`, `NotEnoughStamina`; `Cooldown` (with remaining ms);
`ResolveTarget`: `requiresSkill`, `requiresClassArmor`, target kind, range + `RangeSlack` (24) and height difference <=
100, `AlreadyActive` on the ally. Then it ends `replaces` effects, spends mana and stamina, records uses, starts the
cooldown, starts the cast motion, sets the combat target, draws a mode, stores an `ActiveCast` and queues a `Start`
event for the next snapshot.

`FieldSkills.Step` (each tick): fires the hits whose time has come (`HitsAtMs` from the cast start); a projectile hit
resolves after `SkillProjectile.FlightMs` to the target's current position; a cast that stops before its motion ends
(death, warp) emits `Cancel` and drops its remaining hits; a motion that completes fires any remaining hits. `Resolve`
picks the rule (`charge.full` when fully charged, else the drawn mode, else the rule), dispatches on `kind`, applies
`casterBuff` and emits an `Impact` event with the target count. Then familiars, pulses, every effect timer, periodic
ticks, death and town clean-up, and natural regeneration (`Regeneration`) are stepped.

| Mechanic | Partial | Notes |
|---|---|---|
| Weapon hits, volleys, element adds, bursts, bolts, push, taunt | `Damage` | Weapon hits roll accuracy per hit unless `noAccuracy`/`neverMiss` |
| Areas and chains, life share, area-only effects | `Areas` | Area hits never miss or crit unless `areaAccuracy`/`areaCritical` |
| Timed effects, periodic, stealth, damage-broken stuns, counter | `Buffs` | Player-skill buffs end in town and on death |
| Self/ally buffs, group heals, weapon-bound effects, pulses | `Allies` | Effect keeps the skill's code even when a mode resolved it |
| Enchants, evasion, reflect, absorb, guard in normal combat | `Defense` | `IPlayerSkillCombat`, called by `FieldCombat` |
| Familiars | `Companions` | Owner-target familiars wait for the owner's next attack |
| Held charges | `Charge` | Early release shifts the timeline; past `fullFrame` the server marks it full itself |
| Charm, revive | `Charm`, `Resurrection` | Charmed monster becomes a summon for the buff duration |
| Summons | `Summons` | One per owner; new call replaces; recall at 500 units (`SummonBrain.RecallDistance`); ends on expiry, death, owner leaving, or the owner's status ending |

Cooldowns live in `WorldPlayer.Cooldowns` (not persisted); `Rebase` carries them across field changes.

## Wire messages

Etervale extension opcodes `SkillCast` (C→S), `SkillCastResult`, `SkillEvents`, `StatusEffects`, `SkillCooldowns`
(S→C), plus `Skills` (learned ranks), `OwnerStatus` (elite points, class tier) and
`ProgressionCommand`/`ProgressionResult`. Layouts, flags, phases and status ops are in [protocol.md](protocol.md).

- The client sends only skill code, target id, its position and a command flag; the server re-derives everything.
- The verdict, command, phase, event flags and status operation are the Contracts enums on every side; append values.
- Statuses carry `SourceKind`: `PlayerSkill`, `Item`, or `MonsterSkill` for the slows, poison, stuns, curses and lent
  power of monster skills ([world.md](world.md#monster-skills)); the value is the main modifier, or the drawn element
  (as an integer) for enchants and familiars.

## Client

Input and cast flow (`GameScene.Skills.cs`, `HudSkillBindings`, `GameplayHud`):

- `S` toggles the skill window. Releasing the left or right button over a learned castable skill selects it into that
  socket if its `mouse` allows it (otherwise a notice). Holding the button over a skill and pressing `F1`-`F8` binds
  that key to the skill and side (one key per skill); pressing `F1`-`F8` elsewhere selects the bound skill. `Ctrl`
  suppresses the keys. Sockets fall back to the basic attack (`0x11111111`) when a skill is lost.
- A click with a skill in the clicked socket picks the entity under the cursor: `Enemy` needs a monster (or, except a
  charm, a player of another clan where both stand in the castle's battle zone), `Ally` takes a
  picked player or none, `Self`/`None` cast at once. Local pre-checks (rank, cooldown) show a notice instead of sending;
  mana and stamina are the server's (`SkillFormulas.ManaCost`/`StaminaCost`, with the cost-reducing effects), refused as
  `NotEnoughMana`/`NotEnoughStamina`. The avatar walks until within `SkillFormulas.Range` of the owner's attack range,
  less 8, faces the target and sends.
  An unanswered cast locks the avatar for at most 1500 ms. Refusals map to notices in `OnCastResolved`. When the lock
  ends, the local motion controller returns from `Skill` to `Stand` (`EndSkillMotion`), so the floor is followed again.
- Charge: an accepted charge skill records its button; releasing it while casting sends `SkillCastCommand.Release`.
- Summons: double-clicking (0.4 s) a summon's buff icon sends `SummonAggressive`/`SummonPassive`; a new call resets
  to the rule's `aggressive`. A right click on a crystal pet's icon asks to dismiss it
  ([special-items.md](special-items.md#client-presentation)).

Presentation (`SkillPresentation`):

- Continued attack (`GameScene.SkillTrace`): a click with a skill that strikes an enemy, on a monster, keeps attacking it
  while that mouse button is held, or until it dies or leaves when Shift was down at the click, as the original's
  continued attack: the skill again whenever it is ready, the weapon's strikes (a sustained attack) while it is not. A
  cast the server refuses for a passing reason (mana, stamina, cooldown, range) is retried a second later without its
  notice; any other refusal ends the trace with its notice. Another click ends it.
- Own events run at once; another entity's events wait its playback delay (`ISkillSceneHost.PlaybackDelayMsOf`, the
  snapshot buffer's adaptive 150 to 600 ms) to match its interpolated motion.
- `Start`: plays the motion plan at a rate that fits the server's `DurationMs` (weapon clips: per loop), spawns
  `castEffect`, plays `castSound`, launches `projectileEffect` at `HitAtMs` over the shared flight time and arc.
- `Impact`: familiar/counter strikes fly `companionEffect` from above the caster; chain, familiar and ally impacts
  play the secondary effect; otherwise `impactEffect` (the mode's or `charge.full`'s when set) at the target or caster,
  skipped for entity-targeted rules that hit nothing. `Release` seeks the clip to `fullFrame`; `Cancel` stops effects.
- Statuses: `StatusBook` keeps timers between messages; on add, `buffEffect` spawns as an aura (variant
  `<name>.<element>` for multi-choice enchants or familiars when defined) and restarts while the status lasts; a
  status that arrived before its entity was shown starts its aura once the entity is there. An entity's statuses are
  forgotten when it leaves the view (the server sends the running ones again when it returns). A familiar's strike
  takes the same element variant of `impactEffect`.
- Looks: `ActorLookBook` turns statuses, casts and impacts into an `EntityLook` per entity each frame (the original's
  70 Hz ramps: frost +1 a tick to 160 blue and -80 alpha, poison to 100 green, both counting back after the status;
  vanish -160 alpha, vague -60 for the local player and -160 for others with a scrolling overlay; distortion +80
  red and green with a wobble; rage red with half as much green taken). The host applies it
  (`ISkillSceneHost.ApplyLook`); `ActorLookPresenter` paints body, head and held items, swaps or moves the shield, adds
  the enlarged weapon copy and the afterimage echoes (six two frames apart from -140 by -20 for the local player, four
  four frames apart at -160 for others) and reports Normal once a look ends.
- Combat events add floating damage, heal and evade text and the revive effect (`ReviveEffect`).
- VFX: every `<assetRoot>/Effect/Vfx/**/*.vfx.json`, read off the game thread in ordinal path order (docs/effects.md).
  Sounds are asset paths loaded by `SkillSoundBank`.
- HUD: `HudSkillHud` draws the socket icons with the cooldown sweep from `SkillCooldownBook` and, from (10, 10), the
  continuous-skill bar: one 32 px icon per status that has a `keepIcon` (or a skill button), longest running first,
  ten to a row in up to two rows (ten crystal pets leave room for the other effects),
  each the `keep/GA_.tga` ring, a 72-step fan filling clockwise as the time runs out (cyan, red for harmful) and the
  keep icon. Icons fade in over 255 and out over the last 180 frames, blink every 31 frames in the last 4 s and slide
  2 px per frame (70 Hz frames). Hovering shows name, time left and, for summons, `Attack type: Auto/Manual`
  (`GameScene.SummonAutomatic`); a double-click toggles the summon's mode, and every new summon starts automatic.
  `HudSkillDetails` builds the skill window tooltips from the rule.

Smoke check against a running server (`tools/PristonTale.SmokeClient`, see [tools.md](tools.md)): without
`--character` it creates a Mechanician in town and expects an answered (refused) cast; with `--character <name>
--skill <hex>` (default `0x01010001`) the character must have learned the skill and stand outside a town, and the cast
must be accepted with a `Start` and an `Impact` event; for buffs it also reports the added status.

## How to

Change a skill's numbers: edit its entry in `Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json` only.
Server resolution, client timing and tooltips follow; rebuild and ship server and client together. Run the regression
runner (`SkillCatalogTests` pins some tables). Changes to name, description, icon, level, mouse, weapons, mana or
stamina of an original skill fail `ClassSkillDataTests` against
`tests/PristonTale.Tests/Fixtures/class-skill-source.json`; change it only on purpose.

Add or change a skill with an existing mechanic:

1. Data: the entry in `<Class>.json` at its window position, with a code matching tier and slot (each class keeps 16).
2. Effects: define every effect name it references in
   `tools/PristonTale.VfxImport/Authored/Classes/<Class>/<Class>.vfx.json` (or `Authored/Common/` when shared; names
   unique across files) and run `ptvfx import <assetRoot>`. Format: [effects.md](effects.md).
3. Sounds: `wav/...` asset paths that exist in the game data (`SkillPresentationTests` checks when assets are found).
4. Motion: `motionIndex`/`motionFrames`/`motionEvents` must match the class motion table; check with
   `--verify-skill-motions <assets>` and preview with `--capture-skills` ([testing.md](testing.md)). If a shipped model
   lacks an authored skill index, the client selects a valid skill clip from that model instead of terminating.
5. Tests: a case in the matching `tests/PristonTale.Tests/Skill<Mechanic>Tests.cs` using `SkillScenario`.

Add a new mechanic or rule field:

1. Record field in `SkillRules.cs` or `SkillRuleMechanics.cs` (keep `[10]` tables), validation in
   `SkillRuleCatalog.Validate`/`WellFormed`/`MechanicsValid`/`Complete`.
2. Formula in `SkillFormulas.cs` or `SkillFormulas.Mechanics.cs` when the client also needs it (tooltip, range, timing).
3. Server resolution in the `FieldSkills.<Mechanic>.cs` partial named after the mechanic (new partial for a new
   mechanic); a new `SkillKind` needs a case in `FieldSkills.Resolve` and in `Complete`. Combat-side hooks go through
   `IPlayerSkillCombat` (`FieldSkills.Defense.cs`).
4. Tooltip lines in `HudSkillDetails`; presentation in `SkillPresentation` if it needs a new visual cue.
5. A new event flag or verdict: append it in `Contracts/Skills/SkillCodes.cs`, add the client notice in
   `OnCastResolved`, and document it in [protocol.md](protocol.md).
6. A new test file registered in `tests/PristonTale.Tests/Program.cs`; update this doc.

Change learning rules: `SkillLearning` (prices, gates), `PlayerCharacter.LevelUp` (point grants),
`PlayerCharacter.ApplyProgression`; tests in `ProgressionTransactionTests.cs` and `NpcServiceTests.cs`.

## Pitfalls

- Loading is strict: `SkillJson.RequireKnownProperties` rejects any property, at any depth, that neither
  `ClassSkillEntry` nor `SkillRule` (or the nested type) declares, and any brood entry that is not a `MonsterBrood`.
  Add the C# property before using a new JSON field. Summon monster names depend on imported data, so they are checked
  at cast time: a summon whose `monster` is missing from the world catalog is refused as `NotCastable` before costs.
- The lightning element is spelled `Lighting` (`Element.Lighting`).
- A table with a length other than 0 or 10 fails the whole catalog load (server and client).
- Array order is window order; moving an entry changes its price, gate, proficiency streak and HUD node.
- `modes` beyond four cannot be reported to clients (two flag bits); client impact effects pick the mode's
  `impactEffect`.
- The client computes cast range with `Owner.AttackRange` while the server uses `PlayerAttackReach`; the server adds
  24 units of slack. Keep `GameScene.CastRange` and `SkillFormulas.Range` in step when changing range rules.
- The client mana check ignores `SkillManaCost` modifiers; the server's answer is authoritative.
- `casterBuff` applies only on hit 0 and, for areas, only when a target was hit.
- Player-skill buffs end on entering a town and on death; summons are not called in town.
- Cooldown uses the uses count from before the current cast.
- Debuffs never land on summons; charmed monsters are skipped by players' attacks.

## Related

- [simulation.md](simulation.md): combat profiles, modifiers and derived stats that skills feed.
- [world.md](world.md): monster skills, AI and `SummonBrain`, monster broods.
- [application.md](application.md): world tick, field instance, combat pipeline, NPC services.
- [protocol.md](protocol.md): byte layouts of the skill and status messages.
- [client.md](client.md): HUD windows, remote entities, input.
- [effects.md](effects.md): VFX JSON format, effect data layout, the `ptvfx` converter, per-skill effect inventory.
- [testing.md](testing.md): regression runner, skill probes.
- [persistence.md](persistence.md): saved ranks and uses.
