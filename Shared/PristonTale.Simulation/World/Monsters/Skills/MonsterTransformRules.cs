using PristonTale.Simulation.World.Npcs;
namespace PristonTale.Simulation.World.Monsters.Skills;

// Monsters that turn into another monster when hurt or after many blows: they play their transformation clip, then the
// server replaces them with a full-life monster of the other form at the same place.
public static class MonsterTransformRules
{
    // More than 80 blows make a shape-shifter angry enough to change whatever its life.
    public const int AngerLimit = 80;

    public static bool IsShapeShifter(MonsterDefinition definition) =>
        definition.SoundCode is CharacterSoundCodes.Hungky or CharacterSoundCodes.DeadHopt or CharacterSoundCodes.BebeChick;

    public static bool Wants(MonsterInstance monster)
    {
        var definition = monster.Def;
        if (!IsShapeShifter(definition) || monster.DuelPlayerId != 0)
        {
            return false;
        }

        var percent = monster.LifePercent ?? 100;
        var threshold = definition.SoundCode == CharacterSoundCodes.DeadHopt ? 20 : 30;
        return percent < threshold || monster.AttackAnger > AngerLimit;
    }

    // The form it turns into: Hungky becomes a hulk below level 80, or one of level 80 and more when angry; the undead hopy
    // a king hopy of level 108 and more; the chick its parent.
    public static bool Accepts(MonsterInstance monster, MonsterDefinition candidate)
    {
        return monster.Def.SoundCode switch
        {
            CharacterSoundCodes.Hungky => candidate.SoundCode == CharacterSoundCodes.Hulk
                && (monster.AttackAnger > AngerLimit ? candidate.Combat.Level >= 80 : candidate.Combat.Level < 80),
            CharacterSoundCodes.DeadHopt => candidate.SoundCode == CharacterSoundCodes.DeadKingHopy && candidate.Combat.Level >= 108,
            CharacterSoundCodes.BebeChick => candidate.SoundCode == CharacterSoundCodes.PapaChick,
            _ => false
        };
    }
}
