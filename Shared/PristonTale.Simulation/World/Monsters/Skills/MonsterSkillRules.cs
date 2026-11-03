using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.World.Monsters.Skills;

public enum MonsterCurse
{
    None = 0,
    // The cursed player's absorption against monster blows drops (curse code 101).
    Defence = 101,
    // Lends another monster nearby more damage instead of attacking (curse code 102).
    AllyPower = 102,
    // The cursed player's attacks deal less (curse code 103).
    Attack = 103
}

// What a landed blow does besides its damage.
public enum MonsterRiderKind
{
    Status,
    // The struck player loses a percent of its maximum mana or stamina.
    DrainPercent,
    // The struck player loses mana equal to the blow's power.
    ManaByPower,
    // The blow's power is resisted by the struck player's element before absorption.
    ResistedPower
}

public readonly record struct MonsterHitRider(MonsterRiderKind Kind, uint Status = 0, int Frames = 0, Element? ResistedBy = null, int Speed = 0,
    ResourceKind Resource = ResourceKind.Life, int Percent = 0, bool UnlessStunned = false);

// Per-family rules of monster skills and blows (docs/world.md "Monster skills").
public static class MonsterSkillRules
{
    // A monster casts when its definition has a skill rating; the clip and its target decide the rest.
    public static bool HasSkill(MonsterDefinition definition) => definition.Skill.SkillRatingPercent > 0;

    // The mystic's skill only curses; every other skill deals its damage roll.
    public static bool DealsSkillDamage(MonsterDefinition definition) => definition.SoundCode != CharacterSoundCodes.Mystic;

    public static MonsterCurse Curse(MonsterDefinition definition) =>
        (MonsterCurse)definition.Skill.CurseSkillCode is var curse && curse is MonsterCurse.Defence or MonsterCurse.AllyPower or MonsterCurse.Attack
            ? curse
            : MonsterCurse.None;

    public static (int Min, int Max) CurseDefenceRange(MonsterDefinition definition) =>
        definition.SoundCode == CharacterSoundCodes.Mystic ? (10, 16) : (2, 6);

    public static readonly (int Min, int Max) CurseAttackRange = (10, 15);

    public static readonly (int Min, int Max) PowerRange = (10, 20);

    // The ally a power-lending skill looks for: another live monster within 200 planar units and 80 of height.
    public const float AllyRadius = 200f;

    public const float AllyHeight = 80f;

    // Ratoo's skill wakes the stunned monsters within 200 planar units and 80 of height.
    public const float WakeRadius = 200f;

    // The hit of Babel's and Valento's skills lands 120 frames into the clip; their regeneration is due then too.
    public const float LateSkillHitMs = 120 * GameUnits.TickMs;

    // Babel's normal attack strikes at its event and again 40 frames later.
    public const float BabelSecondHitMs = 40 * GameUnits.TickMs;

    public static bool HasLateSkillHit(MonsterDefinition definition) => definition.SoundCode is CharacterSoundCodes.Babel or CharacterSoundCodes.DeathKnight;

    public static bool StrikesTwice(MonsterDefinition definition) => definition.SoundCode == CharacterSoundCodes.Babel;

    // Babel: each blow has a 60 % chance to also hit the players within 100 units of its target with half the damage, and a
    // target farther than 200 units takes 70 %.
    public const int BabelSplashChance = 60;
    public const float BabelSplashRadius = 100f;
    public const float BabelSplashHeight = 60f;
    public const int BabelSplashPercent = 50;
    public const float BabelFarDistance = 200f;
    public const int BabelFarPercent = 70;

    // A curse monster drops its player target after each normal blow and looks again on its next scan.
    public static bool DropsTargetAfterBlow(MonsterDefinition definition) => definition.Skill.CurseSkillCode != 0;

    // The great bosses deal eight times their roll to monsters; Kelvezu's blow kills a monster outright.
    public static bool MultipliesAgainstMonsters(MonsterDefinition definition) =>
        definition.SoundCode is CharacterSoundCodes.Babel or CharacterSoundCodes.ChaosCara or CharacterSoundCodes.DeathKnight or CharacterSoundCodes.Mokova
            or CharacterSoundCodes.Kelvezu or CharacterSoundCodes.Hestian;

    public const int MonsterDamageMultiplier = 8;

    public static bool KillsMonsters(MonsterDefinition definition) => definition.SoundCode == CharacterSoundCodes.Kelvezu;

    // What a normal blow adds when it lands on a player.
    public static int BlowRiders(MonsterDefinition definition, Span<MonsterHitRider> riders)
    {
        var count = 0;
        switch (definition.SoundCode)
        {
            case CharacterSoundCodes.Web:
                riders[count++] = new MonsterHitRider(MonsterRiderKind.Status, MonsterStatuses.WebSlow, MonsterStatuses.WebSlowFrames,
                    Speed: MonsterStatuses.WebSlowSpeed);
                break;
            case CharacterSoundCodes.KingBat:
                riders[count++] = PoisonRider;
                break;
        }

        count += DrainRiders(definition, riders[count..]);
        return count;
    }

    // What a skill adds to each player it strikes. The key is the authored key of the skill clip.
    public static int SkillRiders(MonsterDefinition definition, char key, Span<MonsterHitRider> riders)
    {
        var count = 0;
        switch (definition.SoundCode)
        {
            case CharacterSoundCodes.ColdEye:
            case CharacterSoundCodes.Frozen:
                riders[count++] = IceSlow(MonsterStatuses.ColdSlowFrames);
                break;
            case CharacterSoundCodes.Frost:
            case CharacterSoundCodes.DeathKnight:
                riders[count++] = IceSlow(MonsterStatuses.FrostSlowFrames);
                break;
            case CharacterSoundCodes.Stingray:
            case CharacterSoundCodes.KingBat:
                riders[count++] = PoisonRider;
                break;
            case CharacterSoundCodes.BloodyKnight:
            case CharacterSoundCodes.Chimera:
                riders[count++] = new MonsterHitRider(MonsterRiderKind.Status, MonsterStatuses.Stun, MonsterStatuses.StunFrames);
                break;
            case CharacterSoundCodes.ChaosCara or CharacterSoundCodes.Hestian when char.ToUpperInvariant(key) != 'J':
                riders[count++] = new MonsterHitRider(MonsterRiderKind.ManaByPower);
                break;
            case CharacterSoundCodes.CastleCrystalIce:
                riders[count++] = IceSlow(MonsterStatuses.CrystalIceFrames);
                break;
            case CharacterSoundCodes.CastleCrystalLightning:
                riders[count++] = new MonsterHitRider(MonsterRiderKind.Status, MonsterStatuses.LightningStun, MonsterStatuses.CrystalStunFrames,
                    Element.Lighting, UnlessStunned: true);
                break;
            case CharacterSoundCodes.CastleCrystalFire:
                riders[count++] = new MonsterHitRider(MonsterRiderKind.ResistedPower, ResistedBy: Element.Fire);
                break;
        }

        count += DrainRiders(definition, riders[count..]);
        return count;
    }

    // Succubus-family blows above level 75 drain 6 % of the struck player's stamina (below level 80) or mana.
    private static int DrainRiders(MonsterDefinition definition, Span<MonsterHitRider> riders)
    {
        var level = definition.Combat.Level;
        if (definition.SoundCode != CharacterSoundCodes.Succubus || level <= 75)
        {
            return 0;
        }

        riders[0] = new MonsterHitRider(MonsterRiderKind.DrainPercent, Resource: level < 80 ? ResourceKind.Stamina : ResourceKind.Mana, Percent: 6);
        return 1;
    }

    private static readonly MonsterHitRider PoisonRider =
        new(MonsterRiderKind.Status, MonsterStatuses.Poison, MonsterStatuses.PoisonFrames, Element.Poison);

    private static MonsterHitRider IceSlow(int frames) =>
        new(MonsterRiderKind.Status, MonsterStatuses.IceSlow, frames, Element.Ice, MonsterStatuses.IceSlowSpeed);

    public const int MaxRiders = 4;
}
