using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;

namespace PristonTale.Application.World;

// Charm skills turn a monster of the listed broods to the caster's side for the buff's duration: the rank's chance,
// lowered by the monster's bionic resistance, decides; the charmed monster's life becomes a percent of what it had,
// it stops fighting players, players' attacks pass it by, and it follows and fights for its caster as a summon until
// the charm ends, when it falls.
internal sealed partial class FieldSkills
{
    private bool Charm(WorldPlayer caster, ActiveCast cast)
    {
        var rule = cast.Rule;
        if (!_host.TryGetMonster(cast.TargetId, out var monster) || !Strikeable(monster) || monster.Def.IsBoss
            || !SkillFormulas.MatchesBrood(rule.Charm.Broods, monster.Def)
            || _rng.RollPercent() >= SkillFormulas.CharmChance(rule, cast.Rank, monster.Def.Combat.Resistances.Bionic))
        {
            return false;
        }

        var duration = SkillFormulas.BuffDurationMs(rule, cast.Rank, 0);
        if (!ApplyEffect(monster.Effects, monster.EntityId, caster.EntityId, rule.Code, rule.Buff, cast.Rank, 0, caster.Character))
        {
            return false;
        }

        monster.Life = (int)Math.Clamp((long)monster.Life * SkillFormulas.RankValue(rule.Charm.LifePercent, cast.Rank) / 100, 1, int.MaxValue);
        AdoptSummon(caster, monster, rule.Code, cast.Rank, duration);
        if (caster.CombatTargetId == monster.EntityId)
        {
            caster.CombatTargetId = 0;
        }

        return monster.Effects.Has(ControlFlags.Charmed);
    }
}
