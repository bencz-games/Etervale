using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// The generic monster skill: which monsters cast, the clip a swing plays, area shapes, damage, impact and presence.
internal static class MonsterSkillTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every monster with a skill rating and a skill clip casts, from any family", SelectionAsync);
        yield return ("monster swings draw their clip among the model's clips and keep its timing", VariantsAsync);
        yield return ("monster skills preserve spherical and forward square boundaries", ShapesAsync);
        yield return ("monster skill impact is single-use, interruptible and independent of target movement", ImpactAsync);
        yield return ("single-target monster skill cannot hit bystanders or escaping prey", SingleTargetAsync);
        yield return ("field swings carry the clip slot and the skill flag through presence", PresenceAsync);
    }

    internal static MonsterDefinition Definition(int soundCode = 0x19F0) => new(
        1, "Skill fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(1, 0, new MonsterRange(1, 1), 6, 80, 0, 0, 0, 10000, ResistanceBlock.Zero), "fixture.inx")
    {
        SoundCode = soundCode,
        Skill = new MonsterSkillStats(new MonsterRange(30, 30), 100, 0, 100, 0),
        Ai = new MonsterAi(6, MonsterNature.Evil, 1000, 1, 64, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static MotionTimingSet Timing(uint impact = 1600) => MotionTimingSet.Build([
        new((int)MotionCode.Attack, new MotionClip(0, 10, 800, 0, 0, 0, false)),
        new((int)MotionCode.Skill, new MotionClip(100, 130, impact, 0, 0, 0, false))
    ]);

    private static Task SelectionAsync()
    {
        var definition = Definition();
        Assert.True(!new MonsterAiTuning(definition).CanCastOffensiveSkill, "missing skill clip fabricated a cast");
        Assert.True(!new MonsterAiTuning(definition with { Skill = definition.Skill with { SkillRatingPercent = 0 } }, Timing()).CanCastOffensiveSkill,
            "a monster without a skill rating cast");
        foreach (var sound in new[] { 0x0000, 0x1100, 0x1600, 0x1830, 0x2150, 0x5020 })
        {
            Assert.True(new MonsterAiTuning(definition with { SoundCode = sound }, Timing()).CanCastOffensiveSkill, $"family {sound:X} could not cast");
        }

        var curse = definition with { Skill = new MonsterSkillStats(MonsterRange.Zero, 0, 0, 10, 101) };
        Assert.True(new MonsterAiTuning(curse, Timing()).CanCastOffensiveSkill, "a curse skill was not castable");
        var tuning = new MonsterAiTuning(definition, Timing());
        Assert.Near(330, tuning.SkillWindupMs);
        Assert.Near(990, tuning.SkillDurationMs);
        Assert.Near(990, new MonsterAiTuning(definition, Timing(uint.MaxValue)).SkillWindupMs);
        foreach (var late in new[] { CharacterSoundCodes.Babel, CharacterSoundCodes.DeathKnight })
        {
            var lateTuning = new MonsterAiTuning(definition with { SoundCode = late },
                MotionTimingSet.Build([new((int)MotionCode.Skill, new MotionClip(0, 165, 1600, 0, 0, 0, false))]));
            Assert.Near(120f * 1000f / 70f, lateTuning.SkillWindupMs, .5f);
        }

        return Task.CompletedTask;
    }

    private static Task VariantsAsync()
    {
        var definition = MonsterSkillField.Monster(0x1830, rating: 100);
        var tuning = new MonsterAiTuning(definition, MonsterSkillField.Timing(twoSkills: true));
        Assert.True(tuning.SkillClips.Count == 2 && tuning.SkillClips[1].Slot == 4 && tuning.SkillClips[1].Key == 'K', "skill clips lost their slot or key");
        var brain = new MonsterAiBrain(new CombatRng(3));
        var prey = new FieldPreyView();
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Target", 100);
        prey.Rebuild(new Dictionary<ulong, WorldPlayer> { [1] = new WorldPlayer(1, new Vector3(40, 0, 0), character, false, default) }.Values);
        var seen = new HashSet<int>();
        for (var swing = 0; swing < 40; swing++)
        {
            var monster = new MonsterInstance(9, definition, Vector3.Zero, 0f, 0) { TargetId = 1, AiState = MonsterAiState.Chase };
            brain.Tick(monster, tuning, prey, 60);
            Assert.True(monster.AttackIsSkill && monster.ActionSlot is 3 or 4, "skill swing played no skill clip");
            var clip = tuning.SkillClips.First(value => value.Slot == monster.ActionSlot);
            Assert.True(monster.ActionDurationMs == clip.DurationMs && monster.ActionHitAtMs == clip.HitAtMs && monster.ActionKey == clip.Key,
                "swing timing did not follow its clip");
            seen.Add(monster.ActionSlot);
        }

        Assert.True(seen.Count == 2, "random clip choice always picked one clip");
        return Task.CompletedTask;
    }

    private static Task ShapesAsync()
    {
        var skill = Definition().Skill;
        Assert.True(MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(99, 0, 0)), "sphere interior excluded");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(100, 0, 0)), "sphere boundary became inclusive");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(80, 80, 0)), "sphere ignored height");
        skill = skill with { SkillDistance = 200, SkillRange = 50 };
        var center = MonsterOffensiveSkill.AreaCenter(Vector3.Zero, MathF.PI / 2, skill);
        Assert.Near(200, center.X);
        Assert.True(MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(249, 0, 49)), "forward square corner excluded");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(250, 0, 0)), "forward area boundary became inclusive");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(200, 140, 0)), "area crossed vertical gate");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(-200, 0, 0)), "forward skill hit behind caster");
        skill = skill with { SkillRange = 0 };
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(460, 0, 0)), "single-target skill has unlimited reach");
        Assert.True(MonsterOffensiveSkill.QueryRadius(skill with { SkillRange = int.MaxValue }) < 1087, "query can scan unbounded grid cells");
        return Task.CompletedTask;
    }

    private static Task ImpactAsync()
    {
        var definition = MonsterSkillField.Monster(0x1100, range: 100);
        var world = new MonsterSkillField(definition);
        var target = world.AddPlayer(1, new Vector3(50, 0, 0));
        var bystander = world.AddPlayer(2, new Vector3(-50, 0, 0));
        var monster = world.Spawn(definition, Vector3.Zero, target: 1);
        world.StepUntil(() => monster.Motion == MotionState.Skill, 5, "skill never started");
        var origin = monster.SkillOrigin;
        target.Teleport(new Vector3(500, 0, 0));
        var targetLife = target.Life;
        var bystanderLife = bystander.Life;
        world.StepUntil(() => world.Events.Any(value => value.Kind == CombatEventKind.MonsterSkillImpact), 20, "skill never hit");
        Assert.True(target.Life == targetLife && bystander.Life < bystanderLife, "area followed escaping target or missed another player");
        var effect = world.Events.Single(value => value.Kind == CombatEventKind.MonsterSkillImpact);
        Assert.True(effect.SourceSoundCode == 0x1100 && effect.Position == origin && effect.TargetId == 1, "impact lost its origin, family or target");
        Assert.True(world.HitsOn(2).All(value => value.Skill), "skill hits were not marked as skill");
        world.Combat(monster, new MonsterAttackEvent(monster.EntityId, 1, true));
        Assert.True(world.Events.Count(value => value.Kind == CombatEventKind.MonsterSkillImpact) == 1, "replayed impact repeated");

        var interrupted = new MonsterSkillField(definition);
        interrupted.AddPlayer(1, new Vector3(50, 0, 0));
        var caster = interrupted.Spawn(definition, Vector3.Zero, target: 1);
        interrupted.StepUntil(() => caster.Motion == MotionState.Skill, 5, "skill never started");
        MonsterAiBrain.ApplyFlinchOnHit(caster, new CombatRng(1), 5000, false, 700);
        interrupted.Step(20);
        Assert.True(!interrupted.Events.Any(value => value.Kind == CombatEventKind.MonsterSkillImpact), "flinched caster still emitted an impact");
        return Task.CompletedTask;
    }

    private static Task SingleTargetAsync()
    {
        var definition = MonsterSkillField.Monster(0x1840);
        foreach (var escape in new[] { false, true })
        {
            var world = new MonsterSkillField(definition);
            var target = world.AddPlayer(1, new Vector3(50, 0, 0));
            var neighbour = world.AddPlayer(2, new Vector3(60, 0, 10));
            var monster = world.Spawn(definition, Vector3.Zero, target: 1);
            world.StepUntil(() => monster.Motion == MotionState.Skill, 5, "skill never started");
            if (escape)
            {
                target.Teleport(new Vector3(600, 0, 0));
            }

            var before = target.Life;
            var neighbourLife = neighbour.Life;
            world.StepUntil(() => world.Events.Any(value => value.Kind == CombatEventKind.MonsterSkillImpact), 20, "skill never hit");
            Assert.True((target.Life < before) != escape, "single-target skill ignored escape or reach");
            Assert.True(neighbour.Life == neighbourLife, "single-target skill damaged a bystander");
        }

        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        var definition = MonsterSkillField.Monster(0x1830, rating: 100);
        var world = new MonsterSkillField(MonsterSkillField.Timing(twoSkills: true), definition);
        var player = world.AddPlayer(1, new Vector3(50, 0, 0));
        var monster = world.Spawn(definition, Vector3.Zero, target: 1);
        var clips = new HashSet<int>();
        var round = new WireRoundTrip(player.EntityId);
        for (uint tick = 1; tick <= 200; tick++)
        {
            world.Field.Step(MonsterSkillField.Tick);
            var events = world.Field.SnapshotEvents() ?? [];
            if (!events.Any(value => value.Kind == CombatEventKind.SwingBegin && value.AttackerId == monster.EntityId))
            {
                continue;
            }

            var presence = round.PushMerged(new WorldSnapshot(tick, world.Field.BuildViews(), events, 7));
            var swing = presence.Events.Single(value => value.Kind == CombatEventKind.SwingBegin && value.AttackerId == monster.EntityId);
            Assert.True(swing.Skill && swing.Clip is 3 or 4, "presence lost the swing's clip or skill flag");
            var clip = swing.Clip == 3 ? 990f : 1980f;
            Assert.Near(clip, swing.DurationMs, 1f);
            clips.Add(swing.Clip);
        }

        Assert.True(clips.Count == 2, "repeated casts played only one clip");
        return Task.CompletedTask;
    }

    internal static void Combat(this MonsterSkillField world, MonsterInstance monster, MonsterAttackEvent landed) =>
        world.Field.Combat.ResolveMonsterHit(monster, landed);
}
