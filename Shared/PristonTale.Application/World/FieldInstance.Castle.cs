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

    // The castle's tax in force, added to the fees the field's players pay (CastleRules.WithTax).
    internal int CastleTax { get; set; }

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
                PlaceDefender(siege, CastleSiegeRules.MercenaryKeys[kind], CastleSiegeRules.MercenaryPost(kind, post), CastleObject.Mercenary);
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
        siege.Called.Clear();
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
                Weaken(monster);
            }
        }
    }

    private static void Weaken(MonsterInstance defender)
    {
        defender.VirtualLives = 0;
        defender.Potions = 0;
        defender.Life = 1;
    }

    // The kind is the mercenary's, by its sound code; a crystal whose monster is missing or no mercenary is not usable.
    internal Simulation.Items.Usable.ItemUseStatus MercenaryRoom(WorldPlayer player, MonsterDefinition? mercenary)
    {
        if (_siege is not { ValhallaFallen: false } siege || !CastleSiegeRules.HoldsCastle(player.Clan?.ClanId ?? 0, siege.Order.OwnerClanId))
        {
            return Simulation.Items.Usable.ItemUseStatus.CrystalRefused;
        }

        var kind = mercenary is null ? -1 : CastleSiegeRules.MercenaryKind(mercenary.SoundCode);
        if (kind < 0)
        {
            return Simulation.Items.Usable.ItemUseStatus.NotUsable;
        }

        var (clan, own, ofKind) = (0, 0, 0);
        foreach (var (monsterId, caller) in siege.Called)
        {
            if (!_monsters.TryGetValue(monsterId, out var monster) || !monster.IsAlive)
            {
                continue;
            }

            clan++;
            own += caller == player.Character.Name ? 1 : 0;
            ofKind += CastleSiegeRules.MercenaryKind(monster.Def.SoundCode) == kind ? 1 : 0;
        }

        return CastleSiegeRules.MayCall(clan, own, ofKind) ? Simulation.Items.Usable.ItemUseStatus.Used : Simulation.Items.Usable.ItemUseStatus.MercenaryLimit;
    }

    // At the use's commit only the siege is checked again: two calls prepared together may pass a limit by one, rather
    // than lose a crystal; a siege that ended since takes the crystal without a mercenary (null).
    internal Vector3? CallMercenary(WorldPlayer player, MonsterDefinition definition)
    {
        if (_siege is not { ValhallaFallen: false } siege || CastleSiegeRules.MercenaryKind(definition.SoundCode) < 0
            || PlaceDefender(siege, definition.Key, player.Position, CastleObject.Mercenary) is not { } mercenary)
        {
            return null;
        }

        if (siege.Weakened)
        {
            Weaken(mercenary);
        }

        siege.Called[mercenary.EntityId] = player.Character.Name;
        return mercenary.Position;
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
        if (role == CastleObject.Mercenary)
        {
            monster.Potions = monster.Def.PotionCount;
        }

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
