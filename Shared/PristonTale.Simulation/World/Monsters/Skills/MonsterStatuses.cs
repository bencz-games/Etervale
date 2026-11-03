using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Units;

namespace PristonTale.Simulation.World.Monsters.Skills;

// Timed effects that monster attacks and skills put on players (and the power a succubus lends another monster).
// Original durations count 70 Hz frames; an element's resistance shortens them, never below four frames.
public static class MonsterStatuses
{
    public const uint Poison = 0xF0000401;
    public const uint Stun = 0xF0000402;
    public const uint LightningStun = 0xF0000403;
    public const uint IceSlow = 0xF0000404;
    public const uint WebSlow = 0xF0000405;
    public const uint CurseDefence = 0xF0000406;
    public const uint CurseAttack = 0xF0000407;
    public const uint Power = 0xF0000408;

    // Movement and the attack, flinch and potion clips run at speed/256 while slowed.
    public const int WebSlowSpeed = 180;
    public const int IceSlowSpeed = 200;

    public const int WebSlowFrames = 70 * 2;
    public const int PoisonFrames = 70 * 5;
    public const int StunFrames = 70 * 3;
    public const int ColdSlowFrames = 70 * 4;
    public const int FrostSlowFrames = 70 * 6;
    public const int CrystalIceFrames = 70 * 4;
    public const int CrystalStunFrames = 70 * 3;

    // A poisoned player loses one life every second frame, never the last one; it is paid in steps of 14 frames.
    public const int PoisonDamagePerTick = 7;
    public const float PoisonIntervalMs = 14 * GameUnits.TickMs;

    // Curses last ten seconds; the power a succubus lends lasts 320 monster updates of four frames each.
    public const float CurseMs = 10_000f;
    public const float PowerMs = 320 * 4 * GameUnits.TickMs;

    public static bool IsMonsterStatus(uint code) => code is >= Poison and <= Power;

    // power x (100 - resistance) / 100 with the resistance clamped to 0..100; a result of 0 or less becomes 4.
    public static int Resisted(int power, int resistance)
    {
        var result = CombatResolver.Shortened(power, resistance);
        return result <= 0 ? 4 : result;
    }

    public static float ResistedMs(int frames, int resistance) => Resisted(frames, resistance) * GameUnits.TickMs;

    public static float FramesMs(int frames) => frames * GameUnits.TickMs;

    public static EffectSpec PoisonSpec() => new(Poison, EffectSourceKind.MonsterSkill, true, [],
        [new PeriodicEffect(ResourceKind.Life, -PoisonDamagePerTick, PoisonIntervalMs, NonLethal: true)]);

    public static EffectSpec StunSpec(uint code) => new(code, EffectSourceKind.MonsterSkill, true, [], control: ControlFlags.Stun);

    public static EffectSpec SlowSpec(uint code, int speed)
    {
        var percent = speed * 100 / 256 - 100;
        return new EffectSpec(code, EffectSourceKind.MonsterSkill, true,
            [StatModifier.Percent(StatKind.MoveSpeed, percent), StatModifier.Percent(StatKind.AttackMotionSpeed, percent)], control: ControlFlags.Slow);
    }

    // The defence curse lowers the absorption a cursed player sets against monster blows by its amount.
    public static EffectSpec CurseDefenceSpec(int amount) =>
        new(CurseDefence, EffectSourceKind.MonsterSkill, true, [StatModifier.Flat(StatKind.Absorption, -amount)]);

    // The attack curse lowers the damage a cursed player deals by its percent.
    public static EffectSpec CurseAttackSpec(int percent) =>
        new(CurseAttack, EffectSourceKind.MonsterSkill, true, [StatModifier.Percent(StatKind.DamageDealt, -percent)]);

    // A monster's normal blows deal its percent more.
    public static EffectSpec PowerSpec(int percent) =>
        new(Power, EffectSourceKind.MonsterSkill, false, [StatModifier.Percent(StatKind.DamageDealt, percent)]);

    public static Element? ResistedBy(uint code) => code switch
    {
        Poison => Element.Poison,
        IceSlow => Element.Ice,
        LightningStun => Element.Lighting,
        _ => null
    };
}
