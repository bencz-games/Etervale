using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Gameplay.World.Remote;

// pickedUp hears every drop the server handed over, for its sound. toCursor, asked as an item's pickup leaves, says whether
// it comes to the inventory's cursor (InventoryCommand.PickupToBag); toCursorAnswered then hears that pickup's reply.
public sealed class GroundLootInteraction(Func<InventoryCommand, Task<InventoryOutcome>> submit, IClientLog log, Action<GroundDropView> pickedUp = null,
    Func<GroundDropView, bool> toCursor = null, Action<bool, long> toCursorAnswered = null)
{
    // The client picks up within this of a drop, inside the server's reach (FieldGroundLoot.PickupReach, 32) so a
    // request it sends is not refused for distance.
    private const float PickupReach = 28f;
    private Guid _selected;
    private Task<InventoryOutcome> _pending;
    private GroundDropView _pendingDrop;
    private bool _pendingToCursor;
    public bool Busy => _pending is not null;

    public void Select(Guid id)
    {
        if (!Busy)
        {
            _selected = id;
        }
    }

    public void Cancel() => _selected = Guid.Empty;

    public void Update(GroundLootPresentation ground, Vector3 position, Vector2 serverPosition, OwnerState owner)
    {
        if (_pending is not null)
        {
            if (_pending.IsCompleted)
            {
                if (_pending.IsCompletedSuccessfully && !_pending.Result.Ok)
                {
                    log.Info($"[Loot] Pickup rejected: {_pending.Result.Message}");
                }
                else if (_pending.IsCompletedSuccessfully && _pendingDrop is not null)
                {
                    pickedUp?.Invoke(_pendingDrop);
                }

                if (_pending.IsFaulted)
                {
                    log.Error($"[Loot] Pickup failed: {_pending.Exception?.GetBaseException().Message}");
                }

                if (_pendingToCursor)
                {
                    var ok = _pending.IsCompletedSuccessfully && _pending.Result.Ok;
                    toCursorAnswered?.Invoke(ok, ok ? _pending.Result.Revision : 0);
                }

                _pending = null;
            }

            return;
        }

        if (_selected == Guid.Empty || owner is null)
        {
            return;
        }

        if (owner.Life <= 0 || !ground.TryGet(_selected, out var target))
        {
            Cancel();
            return;
        }

        var planar = new Vector2(target.X, target.Z);
        if (Vector2.DistanceSquared(new Vector2(position.X, position.Z), planar) > PickupReach * PickupReach
            || Vector2.DistanceSquared(serverPosition, planar) > PickupReach * PickupReach)
        {
            return;
        }

        var id = _selected;
        Cancel();
        _pendingDrop = ground.TryGetDrop(id, out var drop) ? drop : null;
        _pendingToCursor = _pendingDrop is { Gold: false } && toCursor?.Invoke(_pendingDrop) == true;
        var destination = _pendingToCursor ? InventoryCommand.PickupToBag : 0;
        _pending = submit(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.Pickup, id, destination, 1, default));
        _ = ObserveAsync(_pending);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Update reports the failure; observing also covers leaving the screen while I/O is pending.
        }
    }
}
