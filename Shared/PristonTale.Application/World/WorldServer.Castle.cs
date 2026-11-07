using PristonTale.Application.Castle;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// Bless Castle's siege on the tick: the castle field places its defenders when the siege opens (or when the field opens
// during one), and the world publishes what stands and the clans' damage at most once a second while it changes.
public sealed partial class WorldServer : ICastleWorld
{
    private CastleSiege? _castleSiege;
    private CastleSiegeView _castleSiegeView = CastleSiegeView.None;
    private long _castleOwnerClanId;
    private int _castleSkill;
    private long _clanSkillRenewAt;

    public CastleSiegeView Siege => Volatile.Read(ref _castleSiegeView);

    // The owner clan and the clan skill in force; the skill is renewed on the tick that follows.
    public void SetOwner(long clanId, CastleClanSkill skill)
    {
        Volatile.Write(ref _castleOwnerClanId, clanId);
        Volatile.Write(ref _castleSkill, (int)skill);
        Volatile.Write(ref _clanSkillRenewAt, 0);
    }

    public Task OpenSiegeAsync(CastleSiegeOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return EnqueueAsync(NoOwner, () =>
        {
            _castleSiege = new CastleSiege(order);
            if (_fields.TryGetValue(FieldCodes.Castle, out var field))
            {
                field.OpenSiege(_castleSiege);
            }

            PublishSiege(_castleSiege);
            return true;
        }, waitForCapacity: true);
    }

    public Task CloseSiegeAsync() =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_castleSiege is { } siege)
            {
                if (_fields.TryGetValue(FieldCodes.Castle, out var field))
                {
                    field.CloseSiege();
                }

                Volatile.Write(ref _castleSiegeView, View(siege) with { Running = false });
                _castleSiege = null;
            }

            return true;
        }, waitForCapacity: true);

    // Living players in the castle field outside the owner clan go to a random start point of the castle (the dead rise
    // there anyway).
    public Task<IReadOnlyList<CastleArrival>> EndSiegeAsync(long ownerClanId) =>
        EnqueueAsync<IReadOnlyList<CastleArrival>>(NoOwner, () =>
        {
            Volatile.Write(ref _castleSiegeView, CastleSiegeView.None);
            if (!_catalog.TryGetFieldByCode(FieldCodes.Castle, out var definition))
            {
                return [];
            }

            var arrivals = new List<CastleArrival>();
            foreach (var connection in _connections.Values.Where(connection => connection.FieldId == FieldCodes.Castle).ToList())
            {
                var player = connection.Player;
                if (player.IsDead || CastleSiegeRules.HoldsCastle(player.Clan?.ClanId ?? 0, ownerClanId)
                    || FieldArrival(FieldCodes.Castle, Random.Shared) is not { } arrival)
                {
                    continue;
                }

                var position = MoveTo(connection, definition, arrival.Position with { Y = 0f }, onFloor: true);
                arrivals.Add(new CastleArrival(player.EntityId, FieldCodes.Castle, position));
            }

            return arrivals;
        }, waitForCapacity: true);

    // False without a siege; the castle field weakens its defenders now, or when it places them.
    public Task<bool> WeakenSiegeAsync() =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_castleSiege is not { } siege)
            {
                return false;
            }

            siege.Weakened = true;
            if (_fields.TryGetValue(FieldCodes.Castle, out var field))
            {
                field.WeakenSiege();
            }

            return true;
        }, waitForCapacity: true);

    // Every CastleClanSkills.RenewSeconds: the owner clan's members get the clan skill in force again; everyone else
    // (and a member holding another skill) loses it.
    private void RenewClanSkill(long nowSeconds)
    {
        if (nowSeconds < Volatile.Read(ref _clanSkillRenewAt))
        {
            return;
        }

        Volatile.Write(ref _clanSkillRenewAt, nowSeconds + CastleClanSkills.RenewSeconds);
        var owner = Volatile.Read(ref _castleOwnerClanId);
        var skill = (CastleClanSkill)Volatile.Read(ref _castleSkill);
        foreach (var connection in _connections.Values)
        {
            var player = connection.Player;
            var field = _fields[connection.FieldId];
            var holds = skill != CastleClanSkill.None && CastleSiegeRules.HoldsCastle(player.Clan?.ClanId ?? 0, owner);
            foreach (var other in CastleClanSkills.All)
            {
                var code = CastleClanSkills.StatusCode(other);
                if ((!holds || other != skill) && player.Character.Effects.TryGet(code, out _))
                {
                    field.EndItemEffect(player, code);
                }
            }

            if (holds)
            {
                field.QueueItemEffect(player.EntityId, CastleClanSkills.Spec(skill), CastleClanSkills.LastSeconds * 1000f);
            }
        }
    }

    private void OpenSiegeIn(FieldInstance field)
    {
        if (field.FieldId == FieldCodes.Castle && _castleSiege is { } siege)
        {
            field.OpenSiege(siege);
        }
    }

    // After the fields stepped: the siege's view when it changed and a second passed since the last one, or at once when
    // the Tower of Valhalla fell.
    private void PublishSiege(long nowSeconds)
    {
        if (_castleSiege is not { } siege || !siege.Changed && siege.Board.Version == siege.PublishedVersion)
        {
            return;
        }

        if (nowSeconds > siege.PublishedSecond || siege.ValhallaFallen)
        {
            siege.PublishedSecond = nowSeconds;
            PublishSiege(siege);
        }
    }

    private void PublishSiege(CastleSiege siege)
    {
        siege.Changed = false;
        siege.PublishedVersion = siege.Board.Version;
        Volatile.Write(ref _castleSiegeView, View(siege));
    }

    private static CastleSiegeView View(CastleSiege siege) => new(true, siege.DoorDown, siege.Crystals, [.. siege.Mercenaries], siege.ValhallaFallen,
        siege.Winner, siege.Board.Top(CastleSiegeRules.TopClans), siege.Board.Total);
}
