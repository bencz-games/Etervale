using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The Fighter's skills that need more than their table: the rated strikes, the dash that stuns what it struck, the
// strike with its area, the bloody berserker that takes the berserker's place, and the camera shake of the heavy blows.
internal static class FighterSkillTests
{
    private const uint Berserker = 0x02080002;
    private const uint DownHit = 0x02100001;
    private const uint PowerDash = 0x02100002;
    private const uint MortalBlow = 0x02100003;
    private const uint BloodyBerserker = 0x02100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("down hit strikes three times with a percent attack rating and its damage percent", DownHitAsync);
        yield return ("power dash reaches 190 and stuns the monster it struck for its rank's time", PowerDashAsync);
        yield return ("mortal blow strikes the target and at most its rank's count of enemies around the caster once", MortalBlowAsync);
        yield return ("bloody berserker adds damage, lowers absorption and replaces the berserker, which replaces it back", BloodyBerserkerAsync);
        yield return ("a camera shake swings the view distance with a flipping, fading factor and stops", CameraWaveAsync);
    }

    private static Task DownHitAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(13, definition);
        var fighter = SkillFixtures.Player(field, 1, PlayerClass.Fighter, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Axe));
        fighter.Character.Skills.Restore(DownHit, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        SkillRuleCatalog.TryGet(DownHit, out var rule);
        var basic = CombatProfileFactory.ForPlayerBasicAttack(fighter.Character);
        var profile = SkillFormulas.WeaponAttack(fighter.Character, rule, 10, definition, new CombatRng(1));
        Assert.True(profile.AttackRating == basic.AttackRating + basic.AttackRating * 50 / 100, $"down hit rank 10 attack rating {profile.AttackRating}");
        WithMana(fighter);
        SkillFixtures.Accept("Down Hit", field, fighter, DownHit, monster.EntityId);
        Assert.True(Impacts(field, fighter, DownHit) == 3, "down hit did not strike three times");
        return Task.CompletedTask;
    }

    private static Task PowerDashAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(17, definition);
        var fighter = SkillFixtures.Player(field, 1, PlayerClass.Fighter, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        WithMana(fighter);
        fighter.Character.Skills.Restore(PowerDash, 1);
        SkillRuleCatalog.TryGet(PowerDash, out var rule);
        Assert.True(SkillFormulas.Range(rule, 1, fighter.Character) == 190, "power dash does not reach 190");
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(150, 0, 0));
        // A missed dash stuns nothing; the next waits for the cooldown.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            monster.Position = new Vector3(150, 0, 0);
            SkillFixtures.Accept("Power Dash", field, fighter, PowerDash, monster.EntityId);
            SkillFixtures.Finish(field, fighter);
            if (monster.Effects.TryGet(PowerDash, out _))
            {
                break;
            }

            SkillFixtures.Wait(field, 18f);
        }

        // Rank 1 stuns for 10 s from the hit, a second or two before this check.
        Assert.True(monster.Effects.TryGet(PowerDash, out var stun) && monster.Effects.Has(ControlFlags.Stun), "power dash did not stun the monster it struck");
        Assert.True(stun.RemainingMs is > 7000f and < 10000f, $"power dash rank 1 stun has {stun.RemainingMs} ms left");
        return Task.CompletedTask;
    }

    private static Task MortalBlowAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(7, definition);
        var fighter = SkillFixtures.Player(field, 1, PlayerClass.Fighter, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Axe));
        fighter.Character.Skills.Restore(MortalBlow, 1);
        var main = SkillFixtures.Spawn(field, definition, new Vector3(40, 0, 0));
        // Rank 1: five more targets within 120 of the caster; eight stand at 90, one at 300.
        for (var index = 0; index < 8; index++)
        {
            SkillFixtures.Spawn(field, definition, new Vector3(MathF.Sin(index) * 90, 0, MathF.Cos(index) * 90));
        }

        var far = SkillFixtures.Spawn(field, definition, new Vector3(-300, 0, 0));
        WithMana(fighter);
        SkillFixtures.Accept("Mortal Blow", field, fighter, MortalBlow, main.EntityId);
        var hits = SkillFixtures.FinishCollect(field, fighter).Where(value => value.Kind == CombatEventKind.Hit && value.Damage > 0).ToList();
        var struck = hits.Select(value => value.TargetId).ToHashSet();
        Assert.True(struck.Contains(main.EntityId) && !struck.Contains(far.EntityId), "mortal blow missed its target or reached beyond its radius");
        Assert.True(struck.Count <= 6, $"mortal blow struck {struck.Count} enemies");
        Assert.True(hits.Count(value => value.TargetId == main.EntityId) <= 2, "mortal blow struck again on its second event");
        return Task.CompletedTask;
    }

    private static Task BloodyBerserkerAsync()
    {
        var field = SkillFixtures.Field();
        var fighter = SkillFixtures.Player(field, 1, PlayerClass.Fighter, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Axe));
        WithMana(fighter);
        fighter.Character.Skills.Restore(Berserker, 1);
        fighter.Character.Skills.Restore(BloodyBerserker, 10);
        SkillFixtures.Accept("Berserker", field, fighter, Berserker);
        SkillFixtures.Finish(field, fighter);
        SkillFixtures.Accept("Bloody Berserker", field, fighter, BloodyBerserker);
        SkillFixtures.Finish(field, fighter);
        var effects = fighter.Character.Effects;
        Assert.True(!effects.TryGet(Berserker, out _) && effects.TryGet(BloodyBerserker, out _), "bloody berserker did not replace the berserker");
        Assert.True(effects.Totals.Flat(StatKind.HitDamage) == 380 && effects.Totals.Flat(StatKind.Absorption) == -23, "bloody berserker rank 10 values");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "bloody berserker recast while running", field, fighter, BloodyBerserker);
        SkillFixtures.Wait(field, 18f);
        SkillFixtures.Accept("Berserker", field, fighter, Berserker);
        SkillFixtures.Finish(field, fighter);
        Assert.True(effects.TryGet(Berserker, out _) && !effects.TryGet(BloodyBerserker, out _), "the berserker did not replace the bloody berserker");
        return Task.CompletedTask;
    }

    private static Task CameraWaveAsync()
    {
        // Factor 26 every third frame: -23, 20, -18, ... until it falls below one unit.
        var wave = new PristonCameraWave();
        wave.Start(26, 2);
        var frame = 1f / 70f;
        var offsets = new List<float>();
        for (var step = 0; step < 200 && wave.Active; step++)
        {
            offsets.Add(wave.Advance(frame));
        }

        Assert.True(offsets[0] == 0f && offsets[1] == 0f && offsets[2] == -23f && offsets[5] == 20f, $"camera wave began {string.Join(',', offsets.Take(6))}");
        Assert.True(!wave.Active && wave.Advance(frame) == 0f, "camera wave did not stop");
        return Task.CompletedTask;
    }

    private static int Impacts(FieldInstance field, WorldPlayer caster, uint code)
    {
        var impacts = 0;
        for (var tick = 0; tick < 200 && (caster.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            impacts += field.SnapshotSkillEvents().Count(value => value.SkillCode == code && value.Phase == SkillEventPhase.Impact);
        }

        return impacts;
    }

    // A level-90 Fighter without gear lacks the mana of the top ranks.
    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
}
