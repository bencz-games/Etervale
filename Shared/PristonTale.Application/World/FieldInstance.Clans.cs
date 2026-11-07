using PristonTale.Simulation.Social;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// The field's part of the clans: a member's cheer heals the clan's monsters nearby (its clan pets, never the castle's
// defenders), and a clan crystal calls one pet a member while fewer than ClanRules.NearbyMonsters of the clan's pets
// stand near.
public sealed partial class FieldInstance
{
    private readonly MonsterInstance?[] _cheered = new MonsterInstance?[ClanRules.NearbyMonsters];

    // Whether the member may call a clan pet here.
    public Simulation.Items.Usable.ItemUseStatus ClanPetRoom(WorldPlayer player)
    {
        if (player.Clan is not { } clan)
        {
            return Simulation.Items.Usable.ItemUseStatus.ClanOnly;
        }

        var nearby = 0;
        foreach (var monster in _monsters.Values)
        {
            if (monster.ClanId != clan.ClanId || !monster.IsAlive || monster.CastleRole != Simulation.Social.Castle.CastleObject.None)
            {
                continue;
            }

            if (monster.OwnerId == player.EntityId)
            {
                return Simulation.Items.Usable.ItemUseStatus.ClanPetOwned;
            }

            if (System.Numerics.Vector3.DistanceSquared(monster.Position, player.Position) < ClanRules.NearbyRangeSquared)
            {
                nearby++;
            }
        }

        return nearby >= ClanRules.NearbyMonsters ? Simulation.Items.Usable.ItemUseStatus.ClanPetsNearby : Simulation.Items.Usable.ItemUseStatus.Used;
    }

    // The first ClanRules.NearbyMonsters living monsters of the player's clan within range share the heal.
    private void HealClanMonsters(WorldPlayer player)
    {
        if (player.Clan is not { } clan)
        {
            return;
        }

        var count = 0;
        foreach (var monster in _monsters.Values)
        {
            if (count < _cheered.Length && monster.ClanId == clan.ClanId && monster.IsAlive && monster.CastleRole == Simulation.Social.Castle.CastleObject.None
                && System.Numerics.Vector3.DistanceSquared(monster.Position, player.Position) < ClanRules.NearbyRangeSquared)
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
