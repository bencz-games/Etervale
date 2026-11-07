using PristonTale.Simulation.Social;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// The field's part of the clans: a member's cheer heals the clan's monsters nearby (its clan pets).
public sealed partial class FieldInstance
{
    private readonly MonsterInstance?[] _cheered = new MonsterInstance?[YahooRules.ClanMonsters];

    // The first YahooRules.ClanMonsters living monsters of the player's clan within range share the heal.
    private void HealClanMonsters(WorldPlayer player)
    {
        if (player.Clan is not { } clan)
        {
            return;
        }

        var count = 0;
        foreach (var monster in _monsters.Values)
        {
            if (count < _cheered.Length && monster.ClanId == clan.ClanId && monster.IsAlive
                && System.Numerics.Vector3.DistanceSquared(monster.Position, player.Position) < YahooRules.ClanMonsterRangeSquared)
            {
                _cheered[count++] = monster;
            }
        }

        var heal = YahooRules.ClanMonsterHeal(player.Character.Level, count);
        for (var index = 0; index < count; index++)
        {
            var monster = _cheered[index]!;
            monster.Life = Math.Min(monster.Def.Combat.MaxLife, monster.Life + heal);
            _cheered[index] = null;
        }
    }
}
