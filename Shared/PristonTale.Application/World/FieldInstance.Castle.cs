using System.Numerics;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// The siege in the castle field: the defenders it places (the field's wild monsters leave first), the clans' damage to
// the crystal towers and to the Tower of Valhalla once no crystal tower stands (until then the tower is kept whole),
// and the tower's fall.
public sealed partial class FieldInstance
{
    private CastleSiege? _siege;

    // The castle field, outside the league: its battle zone lets players fight (WorldServerOptions.CastleLeague).
    internal bool PlayersFight { get; set; }

    internal bool MayFight(WorldPlayer attacker, WorldPlayer target) =>
        PlayersFight && attacker.EntityId != target.EntityId && !attacker.IsDead && !target.IsDead && !attacker.Hidden && !target.Hidden
        && CastleSiegeRules.MayFight(attacker.Position, attacker.Clan?.ClanId ?? 0, target.Position, target.Clan?.ClanId ?? 0);

    bool IFieldCombatHost.MayFight(WorldPlayer attacker, WorldPlayer target) => MayFight(attacker, target);

    bool IFieldSkillHost.MayFight(WorldPlayer attacker, WorldPlayer target) => MayFight(attacker, target);

    bool IFieldSkillHost.PlayersFight => PlayersFight;

    void IFieldSkillHost.HurtPlayer(WorldPlayer attacker, WorldPlayer target, int damage, bool crit) => _combat.HurtPlayer(attacker, target, damage, crit);

    internal void OpenSiege(CastleSiege siege)
    {
        _siege = siege;
        if (siege.Placed)
        {
            return;
        }

        siege.Placed = true;
        foreach (var wild in _monsters.Values.Where(static monster => !monster.IsSummon).Select(static monster => monster.EntityId).ToList())
        {
            RemoveMonster(wild);
        }

        var order = siege.Order;
        var (towers, mercenaries) = CastleSiegeRules.Defenders(order.OwnerClanId, order.Towers, order.Mercenaries, _rng);
        PlaceDefender(siege, CastleSiegeRules.DoorKey, CastleSiegeRules.Door, CastleObject.Door);
        siege.ValhallaId = PlaceDefender(siege, CastleSiegeRules.ValhallaKey, CastleSiegeRules.Valhalla, CastleObject.Valhalla)?.EntityId ?? 0;
        for (var slot = 0; slot < CastleSiegeRules.TowerSlots; slot++)
        {
            PlaceDefender(siege, CastleSiegeRules.CrystalKey(towers[slot]), CastleSiegeRules.CrystalSlots[slot], CastleObject.Crystal);
        }

        for (var kind = 0; kind < CastleSiegeRules.MercenaryKinds; kind++)
        {
            for (var post = 0; post < mercenaries[kind]; post++)
            {
                if (PlaceDefender(siege, CastleSiegeRules.MercenaryKeys[kind], CastleSiegeRules.MercenaryPost(kind, post), CastleObject.Mercenary) is { } mercenary)
                {
                    mercenary.Potions = mercenary.Def.PotionCount;
                }
            }
        }

        if (siege.Weakened)
        {
            WeakenSiege();
        }

        siege.Changed = true;
    }

    internal void CloseSiege()
    {
        if (_siege is not { } siege)
        {
            return;
        }

        foreach (var defender in siege.Defenders)
        {
            RemoveMonster(defender);
        }

        siege.Defenders.Clear();
        _siege = null;
    }

    // A game master's easy siege: no lives in reserve, no potions, one life (the tower's too, kept while a crystal tower
    // stands).
    internal void WeakenSiege()
    {
        if (_siege is not { } siege)
        {
            return;
        }

        siege.Weakened = true;
        foreach (var defender in siege.Defenders)
        {
            if (_monsters.TryGetValue(defender, out var monster) && monster.IsAlive)
            {
                monster.VirtualLives = 0;
                monster.Potions = 0;
                monster.Life = 1;
            }
        }
    }

    private MonsterInstance? PlaceDefender(CastleSiege siege, string key, Vector3 position, CastleObject role)
    {
        if (!_catalog.TryFindKey(key, out var definition))
        {
            return null;
        }

        var monster = Place(definition, role == CastleObject.Mercenary ? Terrain.SettleNear(position, FieldTerrain.ArrivalTolerance) : position, -1, 0f);
        monster.ClanId = siege.Order.OwnerClanId;
        monster.CastleRole = role;
        siege.Defenders.Add(monster.EntityId);
        return monster;
    }

    // A clan's hit on a crystal tower, or on the Tower of Valhalla once no crystal tower stands.
    private void RecordSiegeDamage(WorldPlayer player, MonsterInstance monster, int damage)
    {
        if (_siege is { } siege && player.Clan is { } clan
            && (monster.CastleRole == CastleObject.Crystal || monster.CastleRole == CastleObject.Valhalla && siege.Crystals == 0))
        {
            siege.Board.Record(clan.ClanId, damage);
        }
    }

    // A player's blow on another during a siege, while the tower stands.
    private void RecordSiegeBlow(WorldPlayer attacker, WorldPlayer target, int damage)
    {
        if (_siege is { ValhallaFallen: false } siege && attacker.Clan is { } clan)
        {
            siege.Board.Record(clan.ClanId, CastleSiegeRules.PkScore(damage, clan.ClanId, target.Clan?.ClanId ?? 0, siege.Order.OwnerClanId));
        }
    }

    // What still stands; while a crystal tower stands no blow takes the tower's life (MinimumLife) and its lives are kept,
    // and its fall gives the castle to the board's leader.
    private void StepSiege()
    {
        if (_siege is not { } siege)
        {
            return;
        }

        var door = false;
        var crystals = 0;
        Span<int> mercenaries = stackalloc int[CastleSiegeRules.MercenaryKinds];
        MonsterInstance? valhalla = null;
        foreach (var defender in siege.Defenders)
        {
            if (!_monsters.TryGetValue(defender, out var monster) || !monster.IsAlive)
            {
                continue;
            }

            switch (monster.CastleRole)
            {
                case CastleObject.Door:
                    door = true;
                    break;
                case CastleObject.Crystal:
                    crystals++;
                    break;
                case CastleObject.Valhalla:
                    valhalla = monster;
                    break;
                case CastleObject.Mercenary:
                    var kind = CastleSiegeRules.MercenaryKind(monster.Def.SoundCode);
                    if (kind >= 0)
                    {
                        mercenaries[kind]++;
                    }

                    break;
            }
        }

        if (valhalla is not null)
        {
            valhalla.MinimumLife = crystals == 0 ? 0 : siege.Weakened ? 1 : valhalla.Def.Combat.MaxLife;
            if (crystals > 0 && !siege.Weakened)
            {
                valhalla.Life = valhalla.Def.Combat.MaxLife;
                valhalla.VirtualLives = valhalla.VirtualLivesMax;
            }
        }

        var changed = siege.DoorDown == door || siege.Crystals != crystals || !mercenaries.SequenceEqual(siege.Mercenaries);
        siege.DoorDown = !door;
        siege.Crystals = crystals;
        mercenaries.CopyTo(siege.Mercenaries);
        if (valhalla is null && siege.ValhallaId != 0 && !siege.ValhallaFallen)
        {
            siege.ValhallaFallen = true;
            siege.Winner = siege.Board.Leader;
            changed = true;
        }

        siege.Changed |= changed;
    }
}
