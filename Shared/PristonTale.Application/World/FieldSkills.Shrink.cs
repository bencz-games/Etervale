using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// A shrinking effect (SkillBuffRule.LifeCut, Vis Tenus): the monster it lands on loses a share of its maximum life and
// draws smaller (half its size, or ShrunkScale of a model drawn at its own size) while it lasts; a monster with no more
// life than the share dies at once. The life and the size come back when the effect ends.
internal sealed partial class FieldSkills
{
    private const float ShrunkScale = 150f / 256f;

    private readonly Dictionary<ulong, (int Cut, float Scale)> _shrunk = new();

    // Whether the effect may land: false when the monster is already shrunk or died of the cut.
    private bool Shrink(WorldPlayer caster, MonsterInstance monster, SkillBuffRule buff, int rank)
    {
        if (_shrunk.ContainsKey(monster.EntityId))
        {
            return false;
        }

        var cut = (int)((long)monster.Def.Combat.MaxLife * SkillFormulas.At(buff.LifeCut, rank) / 100);
        if (monster.Life <= cut)
        {
            Pay(caster, monster, monster.Life, false);
            return false;
        }

        monster.Life -= cut;
        _shrunk[monster.EntityId] = (cut, monster.ModelScale);
        monster.ModelScale = monster.ModelScale > 0f ? monster.ModelScale / 2f : ShrunkScale;
        return true;
    }

    private void EndShrink(in EffectChange change)
    {
        if (!_shrunk.Remove(change.Entity, out var shrunk) || !_host.TryGetMonster(change.Entity, out var monster))
        {
            return;
        }

        monster.ModelScale = shrunk.Scale;
        if (monster.IsAlive)
        {
            monster.Life = Math.Min(monster.Def.Combat.MaxLife, monster.Life + shrunk.Cut);
        }
    }
}
