using PristonTale.Application.Sessions;
using PristonTale.Application.World.ItemUses;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Touched only by the tick thread: the random rolls of an item use (firecracker colours, big-head look, prize tables).
    private readonly Random _itemRandom = new();

    // The owner confirmed a right click on a crystal pet's icon: the pet of that status dies and the icon goes; false when
    // the owner keeps no such pet or status.
    public Task<bool> DismissCrystalPetsAsync(ulong entityId, uint statusCode)
    {
        return EnqueueAsync(entityId, () => _connections.TryGetValue(entityId, out var connection) && _fields.TryGetValue(connection.FieldId, out var field)
            && field.DismissCrystalPets(connection.Player, statusCode));
    }

    public Task<PreparedItemUseOperation?> PrepareItemUseAsync(PlayerSession session, ItemUseCommand command)
    {
        return EnqueueAsync<PreparedItemUseOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            var now = _clock.GetUtcNow();
            var context = new ItemUseContext(session, connection.Player, field, _catalog, _worldData.Current.Items, now.ToUnixTimeSeconds(),
                now.ToUnixTimeMilliseconds(), _itemRandom.Next(0, 8192) * 2, _itemRandom.Next(0, 10000), new Simulation.Combat.CombatRng(_itemRandom.Next()),
                MonsterCrystals.EventActive(_options.CrystalEvent, _clock.GetLocalNow().Hour), MemberLookup);
            return ItemUseTransactions.Prepare(context, command);
        });
    }

    private Func<string, (WorldPlayer Player, int FieldId)?>? _memberNamedLookup;

    private Func<string, (WorldPlayer Player, int FieldId)?> MemberLookup => _memberNamedLookup ??= MemberNamed;

    private (WorldPlayer Player, int FieldId)? MemberNamed(string name) => ConnectionNamed(name) is { } connection ? (connection.Player, connection.FieldId) : null;

    // The open field of a code, for inspection outside the tick (tests and probes).
    internal bool TryGetOpenField(int fieldId, out FieldInstance field) => _fields.TryGetValue(fieldId, out field!);

    // Exempt from the player's quota: the use is already stored.
    public Task<bool> CommitItemUseAsync(PlayerSession session, PreparedItemUseOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null || operation.Rule is null)
            {
                throw new CommitInvariantException("Character ownership changed during item use commit.");
            }

            var rule = operation.Rule;
            var action = ItemUseActions.For(rule.Action);
            var original = operation.Original;
            original.CommitInventoryOperation(operation.Candidate);
            original.ItemUses.CopyFrom(operation.Candidate.ItemUses);
            if (operation.Money != 0 && !original.TryChangeMoney(operation.Money))
            {
                throw new CommitInvariantException("Money changed beyond capacity during item use commit.");
            }

            if (action.ReappliesAtCommit)
            {
                UsableItemEffects.Apply(original, rule, operation.NowSeconds, ItemUseTransactions.BigHeadMode(operation.Roll));
            }

            action.Commit(session, rule);
            if (TryGetPlaying(session, out var connection) && _fields.TryGetValue(connection.FieldId, out var field))
            {
                var player = connection.Player;
                var effect = action.Apply(new ItemUseInWorld(player, field, operation, UnixNowMs));
                if (effect.Kind != ItemEffectKind.None)
                {
                    field.ItemEvents.Add(new ItemEffectView(player.EntityId, operation.ItemCode, effect.Kind, effect.At ?? player.Position, operation.Roll));
                }
            }

            if (operation.Arrival is { } arrival)
            {
                Arrive(session, arrival);
            }

            return true;
        }, waitForCapacity: true);
    }
}
