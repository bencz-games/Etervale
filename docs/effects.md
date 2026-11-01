# Effects and VFX

Particle and skill-effect runtime of the client: `Client/PristonTale.Effects/` (the VFX system every effect plays on),
the effect data under `<assetRoot>/Effect/Vfx/`, the converter `tools/PristonTale.VfxImport` (`ptvfx`) that
writes that data, and the gameplay glue in `Client/PristonTale.Gameplay/Effects/` that turns skill events into effects.

## Read this when

- Adding or tuning the visual effect of a skill (cast, projectile, impact, aura, weapon band).
- Porting the effects of a class, an item or a monster from the original game data.
- Adding a field to the `.vfx.json` format, a builtin texture, or a new layer behaviour.
- Debugging an effect that is invisible, a white square, stuck alive, at the wrong position or on the wrong bone.
- Working on skill looks on actors (tints, translucency, shield swaps, enlarged weapons, afterimages) or costume wings.

## Code map

| Path | Responsibility |
|---|---|
| [`Client/PristonTale.Effects/Vfx/VfxDefinition.cs`](../Client/PristonTale.Effects/Vfx/VfxDefinition.cs) | Effect schema (emitters, trails, lights, sounds, children), JSON options, `ParseFile`, `Validate` |
| `Client/PristonTale.Effects/Vfx/VfxComponentDefinition.cs`, `VfxComponents.cs` | Component layer: models, quads and particle pools with keyed events |
| `Client/PristonTale.Effects/Vfx/VfxSequenceDefinition.cs`, `VfxSequences.cs` | Sequence layer: keyed particle streams |
| `Client/PristonTale.Effects/Vfx/VfxPartDefinition.cs`, `VfxParts.cs` | Part layer: animated billboards, streaks, ribbons, weapon bands, beams and models that move, orbit and fly to goals |
| `Client/PristonTale.Effects/Vfx/VfxCatalog.cs` | Reads every `*.vfx.json` under `<assetRoot>/Effect/Vfx` and lists the textures they use |
| `Client/PristonTale.Effects/Vfx/VfxLibrary.cs`, `VfxCompiled.cs` | Name -> `CompiledVfx` registry; `IVfxResources` (textures, models, animated billboards) |
| [`Client/PristonTale.Effects/Vfx/VfxSystem.cs`](../Client/PristonTale.Effects/Vfx/VfxSystem.cs) | `VfxSystem`, `VfxInstance`, emitter and trail simulation, `IVfxAnchors`, `IVfxServices` |
| `Client/PristonTale.Effects/Vfx/VfxRenderer*.cs` | Quads of every layer in one dynamic vertex buffer; effect models through their own draws |
| `Client/PristonTale.Effects/Vfx/VfxBuiltinTextures.cs` | Procedural `builtin:` sprite textures |
| `Client/PristonTale.Effects/Scripts/BillboardSequence.cs` | Original animated billboards (`Effect/AnimationData/*.ini`) and their tick-exact player |
| [`Client/PristonTale.Gameplay/Effects/VfxContent.cs`](../Client/PristonTale.Gameplay/Effects/VfxContent.cs) | Loads the catalog, models, animations and textures on a worker; registers them on the game thread in 2 ms slices; an animation no catalog file names (effects built in code) is read on first use |
| [`Client/PristonTale.Gameplay/Effects/SkillPresentation.cs`](../Client/PristonTale.Gameplay/Effects/SkillPresentation.cs) | Implements `IVfxServices`, maps skill events and statuses to effects |
| `Client/PristonTale.Gameplay/Effects/ScriptEffects.cs` | `IScriptEffects`: meteors, force-orb hits and premium pet shots start converted scripts (`particle.<name>`) through a `VfxSystem` (the skill presentation's, or the opening flight's own); `VfxNoAnchors` and `TransientLightServices` serve a scene without entities |
| [`Client/PristonTale.Gameplay/Effects/MonsterPresentation.cs`](../Client/PristonTale.Gameplay/Effects/MonsterPresentation.cs) | Monster effects: follows each monster's clip and starts `vfx.monster.*`; monster status effects and body tints |
| `Client/PristonTale.Gameplay/Effects/MonsterVisualFamilies.cs` | Sound family (and level, size) to effect family |
| `Client/PristonTale.Gameplay/Effects/ActorLooks.cs`, `World/Remote/ActorLookPresenter.cs` | `ActorLookBook`: skill looks per entity from statuses, casts and impacts; the presenter paints them on actors (tint, opacity, overlay, wobble, shield swap, weapon copy, afterimage echoes) |
| `Client/PristonTale.Gameplay/Effects/CostumeWings.cs` | Wings of the premium costume body items |
| `Shared/PristonTale.Simulation/Items/HeldItemGlow.cs`, `Client/PristonTale.Gameplay/World/GameScene.HeldItems.cs`, `Effects/ClassUpLights.cs` | The glow of held weapons and shields (unique colours, aging colours, mix textures) and the class-up weapon lights |
| `Shared/PristonTale.Simulation/Skills/SkillRulePresentation.cs` | Rule fields for per-hit sounds and effects and actor looks |
| `Client/PristonTale.Engine3D/Rendering/PristonVfxEffect.cs`, `PristonMeshEffect.cs` | VFX shader wrapper (`Vfx.fx`, blend modes) and baked effect models |
| [`tools/PristonTale.VfxImport/`](../tools/PristonTale.VfxImport) | `ptvfx import`: converts the original effect scripts and installs the hand-authored definitions |
| `tools/PristonTale.VfxImport/Authored/` | Hand-authored definitions (source of `Effect/Vfx/Common`, `Classes`, `Costumes` and `Monsters`) |
| `tests/PristonTale.Tests/SkillPresentationTests.cs`, `SkillLookTests.cs`, `VfxLayerTests.cs`, `VfxImportTests.cs`, `MonsterPresentationTests.cs` | Regression cases; `VfxCaptureProbe.cs`, `SkillCaptureProbe.cs` and `MonsterSkillCaptureProbe.cs` are GPU capture probes |

## Where effect data lives

Effect definitions are game data: the client reads `<assetRoot>/Effect/Vfx/**/*.vfx.json` and nothing is embedded in
the client. `assets/` is not in Git, so the data is produced by `ptvfx import <assetRoot>` (docs/tools.md):

| Folder under `Effect/Vfx/` | Content | Source |
|---|---|---|
| `Common/` | Shared skill building blocks: hit flashes, impact bursts, weapon bands, stun stars; `Progression.vfx.json`: the level-up burst (`vfx.player.levelup`: 20 streaks closing on the player, the burst flare and the sliding "LEVEL UP" title, light and chime) | `tools/PristonTale.VfxImport/Authored/Common/` |
| `Classes/<Class>/` | One file per class with every effect its skill rules name | `tools/PristonTale.VfxImport/Authored/Classes/<Class>/` |
| `Scripts/` | `script.<File>`: one per original effect group script (`Effect/NewEffect/*.lua`) | converted |
| `Particles/` | `particle.<File>`: one per original particle script (`Effect/Particle/Script/*.part`) | converted |
| `Costumes/` | Costume wings (`vfx.costume.wing.*`) | `tools/PristonTale.VfxImport/Authored/Costumes/` |
| `Monsters/<Group>/` | One file per monster family (`vfx.monster.<family>.*`), grouped by region: `Field`, `Iron`, `Ice`, `Temple`, `Revived` | `tools/PristonTale.VfxImport/Authored/Monsters/` |
| `Items/` | Reserved for item effects authored the same way | `Authored/Items/` |

Files load in ordinal order of their relative path. A name defined again replaces the earlier definition (reported as
an error by the catalog, so shipped data keeps names unique). A file that cannot be read, parsed or validated is
reported (`[Effects] <file>: <reason>`) and skipped as a whole; the other files still load.

Loading: `EffectLibrary.LoadBootstrap` creates `VfxContent`, whose worker reads the catalog, parses effect models
(with their sibling `.smb` skeleton), reads animated billboards and decodes every texture. `SkillPresentation.Update`
then registers prepared definitions on the game thread for at most 2 ms per frame and never waits for the worker;
effects requested before their registration are skipped. Probes, tools and the opening's load call
`VfxContent.CompileAll()`. A texture an effect model's material names but the model's folder lacks is found by file
name under `image/Sinimage/AssaEffect`, `Effect/AssaEffect` and `Effect/NewEffect/Res` (the game registers its effect
textures from those folders by name), first match in ordinal path order.

## Units, colours and blends

- Positions and sizes in world units, Y up; effect space has -Z forward. Offsets, directions and orbits turn with the
  spawn facing (`VfxInstance.Orientation`) unless a layer says otherwise. Times in seconds, angles in degrees.
- Emitter and trail colours are linear Rec.709 floats scaled by `intensity`. Component, sequence and part colours are
  the original 0..255 display values; they are decoded from sRGB to linear when drawn, alpha stays linear.
- Original rates per 1/70 s tick convert by x70 (speeds) and x4900 (accelerations); 1/256-unit fixed values divide by
  256 first. Animated billboards and part motion advance in 70 Hz ticks.
- Sizes of components, sequences and parts are full extents, as in the original (its billboard, axial and particle
  quads extend half their size to each side); strip and ribbon `width`, component beam sizes and converted axial
  particle widths are half widths (the original line faces also use half their width); emitter `size` is a half
  extent.

| `blend` | Draw | Shader mode |
|---|---|---|
| `Additive` | source x alpha + destination (lamp) | radiance |
| `Alpha` | over, non-premultiplied | radiance |
| `Color`, `AddColor` | source + destination (approximates source x source) | squared radiance |
| `Shadow` | destination x source | multiplier |
| `InvShadow` | destination x (1 - source) | inverse multiplier |

The renderer draws alpha layers first (instances back to front), then the darkening layers, then the light layers.
Effect models draw before the quads with their own blend.

## Effect (`VfxDefinition`)

| JSON | Type | Default | Meaning |
|---|---|---|---|
| `name` | string | required | Unique key (case-insensitive). `vfx.<class>.<skill>.<role>`, `vfx.common.<name>`, `script.<File>`, `particle.<File>` |
| `duration` | float s | 1 | `0 < duration <= 600`: layers that run until stopped stop then; the instance ends when every layer has ended |
| `emitters`, `trails` | arrays | [] | Sprite emitters and anchor ribbons (tables below) |
| `lights`, `sounds` | arrays | [] | One-shot transient lights and sounds |
| `components`, `sequences` | arrays | [] | Layers of the converted original scripts |
| `parts` | array | [] | Original-style effect objects (the skill ports use them) |
| `children` | array | [] | Other named effects started at an anchor |

An instance stays alive while any layer runs or a one-shot is still due. Emission ends at `Stop()` or when `duration`
runs out: waiting parts and one-shots never start, layers that run until stopped end, finite ones finish, and sequence
particles that live until stopped (a lifetime of 60 s or more; converted scripts use 1000) keep only their last three
seconds, as the original's fast stop does. `StopNow()` ends the instance at once and hides what it drew; a part's
`carryEffect` ends that way when the part lands or dies, as the original hides a projectile's glow.

### Emitter, trail, light, sound

| Layer | Fields (default) |
|---|---|
| emitter | `texture`, `frameColumns`/`frameRows` (1), `blend`, `intensity` (1), `softness` (16), `anchor` (Point), `bone`, `offset`, `local`, `delay`, `emitTime` (0 = whole duration), `rate`, `burst`, `maxParticles` (64), `lifetime` [1,1], `speed`, `direction` [0,1,0], `spread` deg, `shape` (Point/Sphere/Ring/Disc), `radius`, `gravity`, `drag`, `size` keys (half extent), `color` keys (linear RGBA), `rotation`, `spin`, `align` (Camera/Ground/Velocity), `stretch` (0.02) |
| trail | `texture`, `blend`, `intensity`, `anchor` (Caster), `bone`, `offset`, `width` (12, full), `lifetime` (0.25 per point), `delay`, `emitTime`, `color` keys head -> tail |
| light | `anchor`, `offset` [0,30,0], `color` [r,g,b] 0..1, `power` (160), `decay` (4 per tick), `delay`; original light units (radius power/2, brightness power x colour / 256) |
| sound | `path` (asset-relative wave), `variants` (further takes; each play picks one of `path` and these at random), `delay`, `volume` (1), `pick`; played at the caster |

A key is `{ "t": 0..1, "v": [..] }` with non-decreasing `t`, sampled linearly and clamped.

### Child (`VfxChildDefinition`)

`effect` (name), `anchor`, `bone`, `offset`, `delay`, `follow` (keep the child's spawn point on the anchor), `turn`
(give it this effect's facing), `stopWithParent` (true: the child stops when this effect's emission ends, by `Stop()`
or by its duration), `pick`. The child is looked up by name on first use, so it may
live in any file. Children (and sounds) that share a non-zero `pick` group start one member, drawn uniformly when the
effect starts (one of three hit bursts, one of two cast sounds).

### Component (`VfxComponentDefinition`)

One element of a converted effect group with its own clock, loops and keyed events: `kind` (`Model`, `Billboard`,
`Ground`, `Beam`, `Particles`), `anchor`, `bone`, `follow`, `offset`, `offsetTurn` (None/Facing/Reverse), `texture`,
`frames` + `frameTime` (numbered textures `<stem>1.<ext>`..), `model`, `skeleton`, `modelFrames` (30 per second),
`loop` (0 = until stopped), `delay`, `endTime` (quads: seconds per loop; particles: lifetime range), `color` (0..255),
`sizeMin`/`sizeMax`, `blend`, `beamFrom`/`beamTo`, particle `spawn` (Point/Box/Sphere/Doughnut) with
`spawnMin`/`spawnMax`, `count`, `rate`, `velocityMin`/`velocityMax`, `outward`, `beams`, and `events`
`[t, kind Color|Size, ramp, value]`. A set event replaces the value; a ramp event is a target that the event before
it moves towards, and the last step keeps going after the last ramp. Every loop restarts the events at 0 without
resetting values. Particle pools hold at most 100 live particles.

### Sequence (`VfxSequenceDefinition`)

A converted particle stream: `texture`, `blend`, `shape` (Billboard, Ground, Upright, Trail), `count` live at most,
`loops` (0 = until stopped), `rate` range drawn each frame, `delay`, `lifetime`, offset box `radiusMin`/`radiusMax`,
gravity box (drawn every frame), `anchor`, `bone`, `offset`, `follow`, and `events` (`t`, `ramp`, `set`, `min`, `max`,
`mode`). A particle that lives until stopped moves with its stream's anchor as `follow` does: it belongs to the effect
(a monster's ground circle the original traces on a bone), so a followed child carries it along. Properties: `Size`, `Stretch`, `Color`, `Red`..`Alpha`, `Velocity` (Vector, Outward, Inward) and its axes,
`Angle` and axes, `Orbit` and axes (turn the particle's offset), `Clock` (restart the event clock at a time).

### Part (`VfxPartDefinition`)

A group of `count` objects that start at `delay + delayStep x index + random(delayRandom)` and live for `lifetime`
seconds; a zero lifetime lives as long as its animation, model clip or flight, or until the effect stops.

| Group | Fields |
|---|---|
| Look | `shape`: `Billboard`, `Ground`, `Streak` (from where it started to where it is), `Line` (behind it along its velocity, `lineSeconds` long), `Ribbon` (through its last `trailPoints`, `trailFade` alpha off per point), `Blade` (band swept by a bone over `bladeLength` along its Y axis), `Beam` (to its goal, or to `beamTo`), `Model` |
| Image | `animation` (an `Effect/AnimationData` file: images, hold ticks, opacity, size, angle and blend come from it), `loop`, `sized` (start from `size` instead of the file's sizes), or `texture` with `frames`, `frameTime`, `pingPong`; `blend`, `color` (0..255), `size`, `height`, `growth`, `rotation`, `spin`, `width` (half width of strips) |
| Model | `model`, `skeleton`, `modelStart`, `modelFrames`, `modelSpeed` (clip frames per second), `loopFrom`, `angles`, `tumble`, `scale`, `pulse` + `pulsePeriod`, `faceGoal` (face the velocity) |
| Fade | `fade`: `Rise` (up over 25.5 % of the life, down at the same rate), `Fall` (down over the last 25.5 %), `InOut` (`fadeIn`, `fadeOut` seconds); `fadeOut` also times the fade after arriving |
| Place | `anchor`, `bone`, `boneWorld` (full bone transform, offset in bone space), `offset`, `follow`, `turn` (true), `jitterMin`/`jitterMax`, `ring` radius with `ringStart` + `ringStep` x index or `ringRandom`, `shell` (ellipsoid radii), `slideFrom` (a billboard sliding on the screen from that many pixels of a 600-pixel-high view, negative left, closing like `VfxScreenSlide`: 35 px every fourth tick, 3 fewer each time down to 4, rising 2 px a tick 45 ticks after it closed) |
| Motion | `radial` (outward along the ring angle), `rise`, `velocityMin`/`velocityMax`, `burst` (along the shell direction), `gravity`, `swirl` (deg/s about the vertical) + `swirlGrowth` (deg/s per second), `spiral` (the velocity turns too: the straight path turned about the origin), `moveTime` (stop after; with `end: Hold` the part then exits there, keeping its velocity for a line's length); `swirl`, `swirlGrowth` and `moveTime` are ranges drawn per part |
| Goal | `goal` (Caster, Target, Point) + `goalOffset`, `speed`, `reach`, `lift` (lob under `gravity`), `end`: `Vanish`, `LastFrame` (keep flying through the last frame or `fadeOut`), `Hold` (stop and fade out) |
| Orbit | `orbitRadius`, `orbitSpeed`, `orbitTilt`, `orbitRise`, `orbitGrowth`, `orbitPhase` + `orbitPhaseStep`, `orbitAlternate`; with a goal the orbit starts at the goal's bearing and distance |
| Effects | `endEffect` (where a part arrives or its life ends), `everyEffect` every `every` seconds at each part, `carryEffect` (started at birth, moved with the part, stopped when it ends) |

Use `carryEffect` for converted particle scripts that stream until stopped; `everyEffect` only for finite effects.

## Runtime: `VfxSystem`

- `Spawn(CompiledVfx, VfxSpawn(CasterId, TargetId, Point, Facing))` returns a `VfxHandle` to a pooled instance (not
  `Spawned` at 1024 live instances). The handle carries the instance's generation: once the effect ends and the
  instance runs another, the handle's `Alive` is false and its `Stop`, `MoveTo` and `Travel` reach nothing.
  `Update(seconds)` clamps the step to 0.1 s, refreshes caster and target positions, updates every layer, fires
  one-shots and children, and returns dead instances to the pool.
- Children, end, every and carry effects are looked up by name and cached once found; a name missing while the
  definitions are still registering is looked up again on the next spawn.
- `Travel(from, to, seconds, arc)` moves `Point` (projectiles, chain links, companion strikes) and stops the instance on
  arrival; parts with `follow` ride it, parts with a `goal` fly on their own and finish their flight after a stop.
- Anchors: `Point` is the spawn point; `Caster`/`Target` the entity position, replaced by a bone through
  `IVfxAnchors.TryGetBone` / `TryGetBoneWorld`. `GameScene` and the probes implement them from the actor bones.
- Memory: instances, parts, trails and pools are reused; after warm-up an update allocates nothing (asserted).
- Threading: `SkillPresentation.Update` and `Draw` run on the game thread; the system has no locks.

## Converting the original scripts (`ptvfx`)

`ptvfx import <assetRoot>` converts every effect group script to `Effect/Vfx/Scripts/<File>.vfx.json` (`script.<File>`)
and every particle script to `Effect/Vfx/Particles/<File>.vfx.json` (`particle.<File>`), installs `Authored/**`, and
prints every construct it kept with a caveat. Effect group scripts become components (Mesh -> `Model`, Billboard ->
`Billboard`, CreateMesh -> `Ground`, BillboardAxial -> `Beam`, ParticleSystem -> `Particles`, Parent -> offset) with
the original defaults (particles: loop 1, 50 per loop, 30 per second, life 1..2 s, size 5..10). Particle scripts keep
the original tokenizer: a value glued to `=` reads as 0, a script the original rejects plays nothing. Axes: Z is
negated and rotations about X and Y are mirrored. Remaining caveats of the shipped data (reported, not mapped): models
and animated textures the game data lacks (those components are left out), a particle system's start delay, and
values the original also fails to read.

## Skill events to effects (`SkillPresentation`)

Skill rules (`Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json`) name effects in `castEffect`,
`impactEffect`, `projectileEffect`, `buffEffect`, `companionEffect`, `modes[i].impactEffect` and
`charge.full.impactEffect`.

| Trigger | Effect | Spawn |
|---|---|---|
| `Start` phase | `castEffect` | At the caster; stopped on `Cancel` |
| `hitAtMs` after Start/Release, projectile rules | `projectileEffect` | Travels caster -> target |
| `Impact` phase | charged, mode, per-hit (`impactEffects`) or plain `impactEffect`; per-hit `impactSounds` | Point at the target (or caster for caster-centred areas), target id set |
| Companion/Counter impact | `companionEffect` | Travels from above the caster to the target |
| Chain impact | `impactEffect` | Travels from the previous link to the target |
| Status added | `buffEffect` (or `<buffEffect>.<element>`) | On the entity while the status lasts |
| Each frame | `statusLook`, `rageGlow`, `shieldModel`, `weaponGrowth`, `afterimages`, `castShieldBone` | Actor looks (`ActorLookBook`) |
| Revive | `vfx.priestess.resurrection.rise` | On the revived entity |

## Monster effects

Monster visuals are data: `MonsterPresentation` follows each drawn monster's animation, as the original client did, and
starts the family's named effects. The server only chooses the clip (`SwingBegin` carries its motion-table slot, the
client plays it) and reports statuses.

| Trigger | Starts when |
|---|---|
| `attack` | an attack clip starts |
| `attack.hit` | the attack clip passes its first or second event frame |
| `skill` | a skill clip starts |
| `skill.hit` | the skill clip passes one of its four event frames |
| `death` | the death clip starts (once) |
| `aura` | the monster appears; stopped at death or when it leaves (hand flames, body glows, circles) |
| `impact` | a skill hit lands on a player other than the swing's target |

Name: `vfx.monster.<family>.<trigger>`; clip triggers look up `<trigger>.k<key>.e<n>`, `<trigger>.k<key>`,
`<trigger>.e<n>`, then `<trigger>`, with the clip's authored key and the 1-based event number. Spawn:
`VfxSpawn(monster, its swing target, monster position, -facing)`, so `Caster`/`Target` anchors and bones follow the
actors. Families come from `MonsterVisualFamilies` by sound code, split where the original split by level (imp and
mephit; witch, succubus, incubus and incubus summoner; evil plant and maple; doom guard from level 65, doom lord when
larger; the level-50 scorpion's snail shot). An undefined name shows nothing. Shared helpers of a family use other
roles (`vfx.monster.hulk.hit`, `vfx.monster.revived.fx.*`).

A corpse darkens from 200 70 Hz ticks after its death clip starts, by half a light unit a tick, to 100 of 256 at 400
ticks (`MonsterPresentation.CorpseDim`); the bodies of light (Figon, Stygian Lord, Guardian Saint, the fire elemental,
Chaos Cara) fade by two alpha units of 256 a tick and are gone in about 1.8 s (`CorpseFade`). The renderer keeps this
apart from the skill looks (`PristonRenderer.SetActorCorpse`: a fade multiplying the opacity and light taken away).

Statuses that monster skills put on players and monsters (`MonsterStatuses`) show while they run: poison
(`vfx.monster.status.poison` and a green tint), the bloody knight's stun (`vfx.monster.status.bloodyknightstun`), the
lightning crystal's stun (`vfx.debuff.stun`), the defence and attack curses (`vfx.monster.status.cursedefence`,
`curseattack`; the mystic's curse visual is part of its skill), ice slows as a blue tint. A Minig reddens with anger
while it fights. Every family's choices and approximations are in the authored files; `--capture-monster-skills`
plays representative clips on real models ([testing.md](testing.md)).

To add or change a monster's visuals: edit its family file under `Authored/Monsters/<Group>/`, run `ptvfx import`, then
`MonsterPresentationTests` (families and names) and the capture probe.

## Skill effect inventory

What each active skill shows in the original and the effect names that carry it. "Hit" is the weapon point (socket plus
half the weapon length) unless noted; impacts are drawn at the target's chest. Every class is ported with parts and the
converted scripts.

### Mechanician

| Skill | Original | Effects |
|---|---|---|
| Extreme Shield | Flash, two shield bursts, sparks and two small bursts at the shield; a pulsing glow model on the shield hand while active | `vfx.cast.shield`, `vfx.buff.shield` |
| Mechanic Bomb | Tumbling bomb lobbed with dust behind it; fire burst of shards, rays, two flashes and a red light | `vfx.projectile.bomb`, `vfx.impact.bomb` |
| Physical Absorption | Rising spiral, falling sparks, outward streaks, burst ring, a shell model; two shields orbiting with trails while active | `vfx.cast.absorb`, `vfx.buff.absorb` |
| Great Smash | Blue band; round impact (flash, 20 rays, sparks, blue light) | `vfx.trail.blue`, `vfx.mech.greatsmash.hit` |
| Maximize, Automation | Gathering burst and six pairs of weapon emblems; rising streaks around the caster while active | `vfx.mech.maximize.*`, `vfx.mech.automation.*` |
| Spark | Unseen bolt dropping spark clusters; spark ball and spark streaks at the target | `vfx.mech.spark.*` |
| Metal Armor | Gathering burst; armour model and particle aura while active | `vfx.mech.metalarmor.*` |
| Grand Smash | Bone band; hit particles, rays, green light | `vfx.trail.bone`, `vfx.mech.grandsmash.hit` |
| Spark Shield | Four lightning bolts rising, shield particles, hand glow while active; lightning to the attacker on block | `vfx.mech.sparkshield.*` |
| Impulsion | Weapon glow; rays and hit particles; lightning arcs between chained targets | `vfx.mech.impulsion.*` |
| Compulsion, Metal Golem | Converted effect groups at the caster | `vfx.mech.compulsion.cast`, `vfx.mech.metalgolem.cast` |
| Magnetic Sphere | Orb machine hovering by the caster; lightning and shots to what it strikes | `vfx.mech.magneticsphere.*` |

### Fighter

| Skill | Original | Effects |
|---|---|---|
| Raving, Impact | Coloured band (red, gold); critical flash per hit | `vfx.trail.rage`, `vfx.trail.golden`, `vfx.impact.critical` |
| Triple Impact, Brutal Swing | Violet / green band; triple impact burst | `vfx.trail.violet`, `vfx.trail.green`, `vfx.fighter.tripleimpact.hit`, `vfx.fighter.brutalswing.hit` |
| Roar | Lights, five expanding roar models, dust; stun stars on targets | `vfx.fighter.roar.cast`, `vfx.debuff.stun` |
| Rage of Zecram | Marks and flashes at the cast; fire hit, then an eruption of models, rays and fire trails | `vfx.fighter.zecram.*` |
| Concentration, Swift Axe | Streaks gathering then flying out; particle aura while active | `vfx.fighter.concentration.*`, `vfx.fighter.swiftaxe.*` |
| Avenging Crash, Bone Crash | Hit particles and lights; Bone Crash adds the bone band and a ground smash | `vfx.fighter.avengingcrash.hit`, `vfx.fighter.bonecrash.hit` |
| Destroyer | Long bone band; converted hit group per hit | `vfx.trail.bone.long`, `vfx.fighter.destroyer.hit` |
| Berserker | Converted group and particles on the caster while active | `vfx.fighter.berserker.aura` |
| Cyclone Strike | Long bone band; normal hit on each target | `vfx.fighter.cyclone.*` |

### Pikeman

| Skill | Original | Effects |
|---|---|---|
| Pike Wind | Ring of pikes bursting from the ground and swirling dust | `vfx.area.pikewind` |
| Critical Hit, Jumping Crash | Band; critical flash | `vfx.trail.violet`, `vfx.impact.critical`, `vfx.impact.crash` |
| Ground Pike | Ice pikes racing out in ten directions with sparkles | `vfx.pike.groundpike.hit` |
| Tornado | Two bands sweeping a full turn through the target, two round impacts there | `vfx.pike.tornado.hit` |
| Expansion | Enlarged red translucent weapon (`weaponGrowth`); round impact per hit | `vfx.pike.expansion.hit` |
| Venom Spear | Particles ahead of the caster; venom model and three particle bursts at each target | `vfx.pike.venomspear.*` |
| Vanish | Flares rushing through the caster and particles; translucent body | `vfx.pike.vanish.cast`; `statusLook` |
| Chain Lance | Red band; hit particles per hit | `vfx.trail.red`, `vfx.pike.chainlance.hit` |
| Assassin Eye | Converted eye group above the marked target | `vfx.pike.assassineye.mark` |
| Charging Strike, Shadow Master | Charge group; triple impact per hit; Shadow Master trails afterimages | `vfx.pike.charging.*`, `vfx.pike.shadowmaster.hit`; `afterimages` |
| Vague | Translucent body with a scrolling overlay | `statusLook` |

### Archer

| Skill | Original | Effects |
|---|---|---|
| Scout Hawk | Flash and gate; the hawk flying its baked path around the caster | `vfx.cast.hawk`, `vfx.buff.hawk` |
| Wind Arrow, Perfect Aim | Arrow with dust puffs (Perfect Aim adds a streak); critical flash on arrival | `vfx.projectile.wind`, `vfx.projectile.aim`, `vfx.impact.arrow`, `vfx.archer.aim.hit` |
| Falcon, Golden Falcon | Gate of embers and pillars; the falcon hovering above; strike flight and triple impact | `vfx.archer.falcon.*`, `vfx.archer.goldenfalcon.*` |
| Arrow of Rage | Arrow rising over the target and bursting; rain of arrows with dust | `vfx.archer.rage.*` |
| Avalanche | Arrows leaving afterimages; triple impact | `vfx.archer.avalanche.*` |
| Elemental Shot | Arrow carrying fire; fire hit or lightning strike by element | `vfx.archer.elemental.*` |
| Bomb Shot | Javelin carrying sparks; explosion and three flying bursts | `vfx.archer.bombshot.*` |
| Perforation | Spinning shot with two ribbons through the line; twist particles at each victim | `vfx.archer.perforation.*` |
| Recall Wolverine | Fire particles | `vfx.archer.wolverine.cast` |
| Phoenix Shot | Phoenix model flying through the target carrying fire | `vfx.archer.phoenix.shot` |
| Force of Nature | Converted group on the caster while active | `vfx.archer.nature.aura` |

### Knight

| Skill | Original | Effects (`vfx.knight.`) |
|---|---|---|
| Sword Blast | Translucent blade model flying to and past the target dropping sparks; light and sound at launch | `swordblast.blade`, `swordblast.projectile` |
| Holy Body | Two counter-rotating body models, flash, ring of sparkles; the orbiting shields while active | `holybody.cast`, `holybody.aura` |
| Double Crash | Weapon band; normal hit flash and red light per hit | `doublecrash.blade`, `doublecrash.hit` |
| Holy Valor, Drastic Spirit, God Bless | 20 lines gathering, 40 flying out; foot aura (God Bless: shield particles and its group at the chest) | `holyvalor.*`, `drastic.*`, `godbless.*` |
| Brandish | Two slash models, rays, falling sparks, two ground rings, a ground flash 48 ahead | `brandish.blade`, `brandish.hit` |
| Piercing | Body flare, sparkles and afterimage lines on the weapon, orbiting glows; the piercing model shooting forward with waves | `piercing.cast`, `piercing.thrust`, `piercing.wake` |
| Divine Inhalation, Godly Shield | Lines out of the left hand at the motion event, hand particles while active; the held shield drawn as the skill's shield | `divineinhalation.cast`, `godlyshield.cast`, `shield.aura`; `shieldModel` |
| Holy Incantation | Hand particles; a burst on the target; a mark above a charmed monster | `incantation.cast`, `incantation.hit`, `incantation.charm` |
| Grand Cross | Hit particles on the first hit, the cross model and other particles on the second | `grandcross.hit`, `grandcross.hit2` (`impactEffects`) |
| Sword of Justice | Its group at the caster and a blue light | `justice.blade`, `justice.hit` |
| Divine Piercing | Hit particles per hit; the sound on the last hit only | `divinepiercing.blade`, `divinepiercing.hit` |

### Atalanta

| Skill | Original | Effects (`vfx.atalanta.`) |
|---|---|---|
| Shield Strike | The thrown shield follows its own bone and glows; a turning disc and shrinking flashes on it; stun stars | `shieldstrike.cast`; `castShieldBone`, `vfx.debuff.stun` |
| Farina, Bigger Spear | Sparks along the flight (Bigger Spear: a spear model); spinning lines, falling sparks and a growing light on arrival | `farina.*`, `biggerspear.*` |
| Windy, Frost Javelin | Light and body flare, streaks gathering into the hand; particles on the javelin while active | `windy.*`, `frost.*` |
| Twist Javelin, Fire Javelin | Rolling javelin model carrying its particles; a hit group | `twist.*`, `firejavelin.*` |
| Soul Sucker | Thrown shield; a soul rising from the target, streams flying to the caster and spiralling down around her | `soulsucker.drain`; `castShieldBone` |
| Split Javelin | Three javelins fading in, hovering, then flying, each ending in its group | `split.cast` |
| Triumph / Hall of Valhalla | A comet to the ally's hand; Valhalla particles on the right hand; the Hall above the caster | `triumph.*`, `hall.*` |
| Lightning Javelin | Animated bolt strip; spark ribbons, a light and the lightning group on arrival | `lightning.shot` |
| Storm Javelin | Per hit a rolling storm javelin with light strips flying ahead; twist burst on the target | `storm.javelin` |
| X-Rage, Vengeance | Shots diving from above the caster (Vengeance: from the hand), each ending in a hit group | `xrage.cast`, `vengeance.cast` |

### Priestess

Every active skill draws `vfx.priestess.castcircle` at the feet: the animated circle model and two ground quads.

| Skill | Original | Effects (`vfx.priestess.`) |
|---|---|---|
| Healing | Translucent spiral model rising on the healed body with sparkles, white light | `healing.impact` |
| Holy Bolt, Multi Spark | A stream of puffs (Multi Spark: five homing sparks); streaks and sparks on arrival | `holybolt.*`, `multispark.*` |
| Holy Mind | Violet light and burst; flares above the head while cursed | `holymind.impact`, `holymind.mark` |
| Divine Lightning | Three bolts falling into crawling sparks | `divinelightning.strike` |
| Holy Reflection | Angels, petals, flare lines; two rotating net shells while active | `reflection.cast`, `reflection.aura` |
| Grand Healing | Wings, stars, spiral ribbons; ribbons, rings and models on each healed member | `grandhealing.cast`, `grandhealing.member` |
| Vigor Ball | Two homing balls; a burst per hit | `vigorball.*` |
| Resurrection | Aurora and a rising angel; a halo on the revived player | `resurrection.shot`, `resurrection.rise` |
| Extinction, Virtual Life | Holy bursts around the caster; the life model and aura while active | `extinction.burst`, `virtuallife.aura` |
| Glacial Spike | The ice block group ahead and a blue light; frozen targets turn blue | `glacialspike.spikes`; `statusLook` Frost |
| Regeneration Field | The field model and particles while channelling | `regenfield.cast` |
| Chain Lightning | A sky bolt at the hit; flickering beams between the chained targets | `chainlightning.cast`, `chainlightning.link` |
| Summon Muspell | The spirit hovering above the caster with flames; its strikes | `muspell.*` |

### Magician

Every cast draws `vfx.magician.castcircle` (the Magician's circle textures) unless the skill has its own cast.

| Skill | Original | Effects (`vfx.magician.`) |
|---|---|---|
| Agony | A point circling up the caster dropping particles, white light | `agony.cast`, `vfx.agony` |
| Fire Bolt, Fire Ball | A spark (bubble) stream with smoke; flash, puffs and streaks (Fire Ball: fire lines, meteor flashes) on arrival | `firebolt.*`, `fireball.*` |
| Zenith | Five element glyphs circling the caster | `zenith.cast`, `zenith.aura` |
| Watornado | The water column running to the target, splash model and bubbles there | `watornado.column`, `watornado.hit` |
| Enchant Weapon | Flash; the element's particles on the weapon; ice-slowed monsters turn blue | `enchant.cast`, `enchant.aura[.fire/.ice/.lighting]`; `statusLook` Frost |
| Dead Ray | A green laser from the staff through the target | `deadray.beam` |
| Energy Shield, Spirit Elemental | The shield model with particles; the spirit model above the caster | `energyshield.*`, `spiritelemental.*` |
| Diastrophism | The rock eruption model and dust (once per cast) | `diastrophism.quake`; `impactEffects` |
| Dancing Sword | The floating sword by element; strikes by element | `dancingsword.blade[.fire/.ice]`, `dancingsword.strike[.fire/.ice]` |
| Fire Elemental, Distortion | The cast circle; distorted targets glow red-yellow and wobble | `castcircle`; `statusLook` Distortion |
| Flame Wave | Five fire streams fanning ahead | `flamewave.wave` |
| Meteo | Four burning meteors falling around the target, a burst where each lands (once per cast) | `meteo.fall`, `meteo.hit`; `impactEffects` |

### Actor looks and per-hit sounds

Rule fields beside the effect names (docs/skills.md) draw what the original does to the actors themselves:
`statusLook` Frost (Ground Pike, Glacial Spike, Frost Javelin, Enchant Weapon and Dancing Sword slows), Poison (Venom
Spear), Distortion, Vanish and Vague; `rageGlow` (Raving 150, Berserker 200); `shieldModel` (Spark Shield, Divine
Inhalation, Godly Shield); `weaponGrowth` (Expansion: a red translucent copy of the weapon growing to 1 + (percent x
2.56 + 64) / 256); `afterimages` (Shadow Master); `castShieldBone`/`castShieldGlow` (Shield Strike, Soul Sucker). Per-hit
sounds: Chain Lance in order, Shadow Master, Lightning and Storm Javelin, Divine Lightning, Vigor Ball, Glacial Spike,
Chain Lightning, Watornado, Flame Wave and Dancing Sword at random, Vengeance in order, Divine Piercing on its final hit,
Grand Cross with a swing on the first hit. Effect sounds take `variants` at random (Fire Bolt, Fire Ball, X-Rage).

### Costume wings

The premium costume armours and robes show a wing at the wearer's back (`CostumeWings`, `Effect/Vfx/Costumes`):
Cygnus CA166-169 / CA266-269 `vfx.costume.wing.blue`, Aquila CA185-188 / CA285-288 `vfx.costume.wing.white`, Corvus
CA189-192 / CA289-292 `vfx.costume.wing.red`. The wing model hangs 2.42 below the wearer's origin plus its own height,
turns with the wearer and is hidden in a village and on the dead. Costume bodies keep the class body.

### Basic shots

`PlayerShots` (`GameScene.Shots.cs`): a player's swing names its target and the attack clip's first event throws the
shot, for every drawn player. A bow's arrow (`weapons/arrow.smd`, 8 units a frame of 70 per second) and a javelin
itself (its own model, 6 a frame, its length along the flight; the held javelin is not drawn while it flies) fly in a
straight line from the weapon toward the target's chest (24 above its feet); within 64 units across and 80 up of the
target they fly 7 frames more and end, as they do after 120 frames. Magicians and priestesses shoot the Imp's fire ball
(`vfx.monster.imp.attack.hit`) with a staff; other classes strike with it. A glowing bow or javelin sheds about 3.5
sparks a frame along its flight (`Effect/ImageData/Particle/cc.tga`, additive, half its glow colour plus 128, fading to
half that colour and out over 0.7 s). The flying weapons and sparks go through `VfxRenderer.AddModel` and
`AddBillboard`; `VfxContent.LoadModel` reads a model no effect definition names.

### Held item glow

`HeldItemGlow.For(item)` (Simulation) is what a held weapon or off-hand item shows; the server puts it in the player's
appearance (`PlayerAppearanceView.WeaponGlow`, `OffHandGlow`) and description, so every viewer draws the same:

- A unique's file glow (`ItemDefinition.Glow`: signed colour offsets, the pulse `Blink`, the peak `Scale`).
- An item aged +4 to +20 (`UpgradeLevel`) takes its level's colour with blink 9 instead: +4 light purple, +5 yellow,
  +6 green, +7 blue, +8 purple, +9 orange, +10 red, +11 teal, +12 light green, +13 purple, +14 deep pink, +15 sky blue,
  +16 light orange, +17 brown, +18 blue violet, +19 light yellow, +20 white.
- A mixed copy whose highest sheltom is Devine (6) to Mirage (10) takes the matching mixing colour with blink 9.
- A colour of the aging table (from +7) or of the mixing table brings its item mix texture (`mixS_01..05`,
  `mixM_01..05` under `image/sinImage/Items/DropItem`) and scroll form (8..14), whatever gave the colour.
- `ItemDefinition.DisplayEffect` non-zero: the class-up light.

`HeldItemGlow.Sample(ms)`: each way of the pulse lasts `2^Blink` ms; the added light is `step * channel >> Blink`
(255 = a full white light), alpha below 0 fades the item with the pulse and above 0 keeps it translucent; a negative
blink is a steady colour; a positive scale grows the item up to it with the pulse, a negative one is a fixed scale.
`GameScene.HeldItems.cs` paints each frame, after the skill looks, every glowing held item of the local player and of
every other described player: `ActorLookPresenter.PaintGlow` adds the pulse to the look the item last took (the mix
texture replaces the look's overlay) and `EndGlowFrame` returns items that stopped glowing to that look.

Class-up lights (`ClassUpLights`): while its holder is alive, a weapon with the class-up light carries two lights,
each `particle.ClassUpWeapon1` (the light) plus `particle.ClassUpWeapon2` (its trail), circling a point of the weapon
(along its length, `TryGetActorReach`: a sword at 0.8 of it, 0.75 above 5000 fixed units; a staff 0.7; a bow 0.7 turned
45 degrees, in the left hand its end less 10 turned a quarter; a javelin 0.4 behind the grip; a claw twice its reach
beside the hand; anything else its middle; a two-handed weapon's point leans 9 degrees). The circle turns 70/4096 of a
turn and its radius moves 10 between 1000 and 1600 fixed units each 70 Hz frame, across the weapon (a claw's and a
bow's along it).

## Limitations

- There is no mixing yet, so no item carries a mixing colour.
- Monster effects keep their authored size whatever the model size.
- Skill events carry no critical flag (Double Crash's critical flash), no block, reflect or absorb event (the Divine
  Inhalation and Godly Shield block flash, Holy Reflection's reflected blow, Energy Shield's absorb burst, enchanted
  weapon hits, the enchant flash on an ally) and only the primary target of an area (Divine Lightning, Extinction, Soul
  Sucker and Storm Javelin strike every victim in the original; Divine Lightning and Extinction use fixed spots). Per-hit
  sounds and effects count the impacts of the cast on the client.
- Thrown javelins: the equipped javelin is neither hidden nor flown; Frost Javelin's frost shots on basic attacks are
  not drawn.
- Part layer gaps, approximated in data: darkening (negative) lights (Diastrophism), upright spinning quads (Zenith),
  animated strip width (Dead Ray), effects riding an effect model's bones (Spirit Elemental, Dancing Sword), colour
  ramps over a part's life, random part counts (Multi Spark draws five sparks), an effect when a status ends (Muspell's
  dismiss burst), rank-dependent models (Watornado uses the rank 4-6 column) and camera shakes (Meteo, Diastrophism).
- Weapon bands use a fixed blade length per effect, not the equipped weapon's length.
- Afterimages and the enlarged weapon are extra actors; translucent actors draw in the transparent queue without depth
  writes or motion vectors and cast no shadow.
- Costume bodies (CA) keep the class body; only their wings are drawn.
- `Color`/`AddColor` blends draw additively with squared colour; `Shadow`/`InvShadow` darken in the HDR scene.

## Pitfalls

- Property names are camelCase and unknown properties are ignored: a typo silently falls back to the default.
- A missing texture draws the white fallback; a missing animated billboard or model draws nothing.
- Parts that run until the effect stops keep a buff alive; give one-shot parts a lifetime, an animation or a goal.
- `everyEffect` with a converted particle script that streams until stopped leaks for ten minutes; use `carryEffect`.
- Shipped names must be unique across files; the catalog reports a replaced name as an error.

## Related

- [rendering.md](rendering.md) - frame graph, HDR scene target, transient lights.
- [shaders.md](shaders.md) - `Vfx.fx` modes, colour encoding.
- [skills.md](skills.md) - skill rules and events that trigger effects.
- [tools.md](tools.md) - `ptvfx`.
- [testing.md](testing.md) - regression runner, `--capture-vfx` and `--capture-skills`.
