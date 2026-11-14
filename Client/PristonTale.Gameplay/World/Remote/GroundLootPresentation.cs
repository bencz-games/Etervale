using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D;
using PristonTale.Contracts.Units;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.World.Remote;

// The server settles drops on its copy of the stage collision; its height selects the surface among stacked floors.
public delegate bool FloorHeightQuery(float x, float referenceHeight, float z, out float y);

// A drop's name to draw over the scene: its screen point (back-buffer pixels) and whether the cursor is on the drop.
public readonly record struct GroundLabel(Guid Id, string Text, Vector2 Anchor, bool Hovered);

public sealed class GroundLootPresentation(IActorScene scene, PristonActorLoader loader, string root, IClientLog log) : IDisposable
{
    // Two labels swap places in the stacking order only once the farther one stands this much nearer than the other: drops
    // a ring cell apart in one row lie at nearly one depth, which the camera's slightest turn would otherwise flip.
    internal const float OrderHysteresis = 12f;

    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly List<Entry> _labelOrder = [];
    private readonly Dictionary<string, Task<PristonActor>> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private int _floorRevision;
    private Guid _hovered;

    public void InvalidateFloor() => _floorRevision++;

    public void Apply(PresenceUpdate update)
    {
        foreach (var id in update.GroundRemoved)
        {
            if (!_entries.Remove(id, out var entry))
            {
                continue;
            }

            entry.Shown = false;
            if (entry.Handle.IsValid)
            {
                scene.RemoveActor(entry.Handle);
            }
        }

        foreach (var drop in update.GroundAdded)
        {
            if (_entries.ContainsKey(drop.Id))
            {
                continue;
            }

            if (!_prepared.TryGetValue(drop.CatalogKey, out var task))
            {
                task = PrepareAsync(drop.CatalogKey);
                _prepared.Add(drop.CatalogKey, task);
                _ = ObserveAsync(task);
            }

            _entries.Add(drop.Id, new Entry(drop, task));
        }
    }

    private Task<PristonActor> PrepareAsync(string key) => Task.Run(async () =>
    {
        var actor = loader.LoadRigid(Path.Combine(root, EquipmentVisuals.MeshPath(key)));
        await scene.PrepareActorResourcesAsync(actor).ConfigureAwait(false);
        return actor;
    });

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Error($"[Loot] Resource preparation failed: {exception.Message}");
        }
    }

    public void Update(FloorHeightQuery floor)
    {
        int uploads = 0;
        foreach (var entry in _entries.Values)
        {
            if (entry.FloorRevision != _floorRevision)
            {
                var source = entry.Drop.Position;
                entry.FloorFound = floor(source.X, source.Y, -source.Z, out float height);
                entry.Position = WorldSpace.Mirror(new Vector3(source.X, height, source.Z));
                entry.FloorRevision = _floorRevision;
            }

            if (!entry.FloorFound)
            {
                if (entry.Handle.IsValid)
                {
                    scene.RemoveActor(entry.Handle);
                    entry.Handle = ActorHandle.None;
                }

                entry.LabelBounds = Rectangle.Empty;
                continue; // Never publish a mesh at placeholder Y=0 before its stage is resident.
            }

            if (!entry.Handle.IsValid && !entry.PublishFailed && entry.Preparation.IsCompletedSuccessfully && uploads < 1)
            {
                var actor = entry.Preparation.Result;
                entry.Offset = -RestingHeight(actor.Mesh.Bounds, entry.Rotation);
                try
                {
                    entry.Handle = scene.AddActor(new PristonActorInstance(actor, World(entry), MotionCode.Stand));
                }
                catch (Exception exception)
                {
                    entry.PublishFailed = true;
                    log.Error($"[Loot] Model publication failed: {exception.Message}");
                }

                uploads++;
            }

            if (entry.Handle.IsValid)
            {
                scene.SetActorWorld(entry.Handle, World(entry));
            }
        }
    }

    private static Matrix World(Entry entry) => entry.Rotation * Matrix.CreateTranslation(entry.Position + Vector3.Up * (entry.Offset + 0.5f));

    // A drop lies as the original lays it: turned by a yaw drawn from its position, and a weapon turned a quarter turn
    // about X so it lies flat instead of standing on its tip.
    internal static Matrix Orientation(GroundDropView drop)
    {
        int turn = (int)MathF.Round(drop.Position.X * GameUnits.FixedScale) + (int)MathF.Round(drop.Position.Z * GameUnits.FixedScale) >> 2;
        var yaw = Matrix.CreateRotationY(GameUnits.FromAngle(turn));
        return IsWeapon(drop) ? Matrix.CreateRotationX(MathHelper.PiOver2) * yaw : yaw;
    }

    private static bool IsWeapon(GroundDropView drop) =>
        ItemCodes.TryParse(drop.CatalogKey, out _) && drop.CatalogKey[0] is 'W' or 'w';

    // The lowest point of the turned mesh, so it rests on the floor whichever way it lies.
    private static float RestingHeight(BoundingBox bounds, Matrix rotation)
    {
        float lowest = float.MaxValue;
        foreach (var corner in bounds.GetCorners())
        {
            lowest = MathF.Min(lowest, Vector3.Transform(corner, rotation).Y);
        }

        return lowest;
    }

    public bool TryGetDrop(Guid id, out GroundDropView drop)
    {
        drop = _entries.TryGetValue(id, out var entry) ? entry.Drop : null;
        return drop is not null;
    }

    public bool TryGet(Guid id, out Vector3 position)
    {
        if (_entries.TryGetValue(id, out var entry) && entry.FloorFound)
        {
            position = entry.Position;
            return true;
        }

        position = default;
        return false;
    }

    // The drop nearest along x within reach of the origin and within depth of its z.
    public bool FindNear(Vector3 origin, float reach, float depth, out Guid id, out Vector3 position)
    {
        id = Guid.Empty;
        position = default;
        var nearest = reach;
        foreach (var entry in _entries.Values)
        {
            var across = MathF.Abs(entry.Position.X - origin.X);
            if (entry.FloorFound && MathF.Abs(entry.Position.Z - origin.Z) < depth && across < nearest)
            {
                nearest = across;
                id = entry.Drop.Id;
                position = entry.Position;
            }
        }

        return id != Guid.Empty;
    }

    public bool Pick(Point cursor, Ray ray, out Guid id, out Vector3 position, bool showAllLabels = false)
    {
        id = Guid.Empty;
        position = default;
        // An explicit label click takes precedence over a drop's 3D pick volume.
        foreach (var entry in _entries.Values)
        {
            if (entry.FloorFound && (showAllLabels || entry.Drop.Id == _hovered) && entry.LabelBounds.Contains(cursor))
            {
                id = entry.Drop.Id;
                position = entry.Position;
                return true;
            }
        }

        float nearest = float.MaxValue;
        foreach (var entry in _entries.Values)
        {
            if (!entry.FloorFound)
            {
                continue;
            }

            var hit = ray.Intersects(new BoundingSphere(entry.Position + Vector3.Up * 7, 16));
            if (hit.HasValue && hit.Value < nearest)
            {
                nearest = hit.Value;
                id = entry.Drop.Id;
                position = entry.Position;
            }
        }

        return id != Guid.Empty;
    }

    // The labels to draw this frame, each anchored at its own drop 16 units up, in the order the HUD stacks them: nearest
    // first, kept from frame to frame (SettleOrder) and a newly named drop placed by its depth. Holding A names the drops
    // 32 to 768 units from the camera; the hovered drop is named at any distance. The HUD reports where each label landed
    // (SetLabelBounds) so a click on it picks the drop.
    public void CollectLabels(Viewport viewport, PristonCamera camera, Guid hovered, bool showAll, List<GroundLabel> labels)
    {
        labels.Clear();
        _hovered = hovered;
        foreach (var entry in _entries.Values)
        {
            entry.LabelBounds = Rectangle.Empty;
            entry.Shown = TryAnchorLabel(entry, viewport, camera, entry.Drop.Id == hovered, showAll);
        }

        var kept = 0;
        for (var index = 0; index < _labelOrder.Count; index++)
        {
            var entry = _labelOrder[index];
            entry.Ordered = entry.Shown;
            if (entry.Shown)
            {
                _labelOrder[kept++] = entry;
            }
        }

        _labelOrder.RemoveRange(kept, _labelOrder.Count - kept);
        foreach (var entry in _entries.Values)
        {
            if (entry.Shown && !entry.Ordered)
            {
                entry.Ordered = true;
                InsertByDepth(_labelOrder, entry, static entry => entry.LabelDepth);
            }
        }

        SettleOrder(_labelOrder, static entry => entry.LabelDepth);
        foreach (var entry in _labelOrder)
        {
            labels.Add(new GroundLabel(entry.Drop.Id, entry.Label, entry.LabelAnchor, entry.Drop.Id == hovered));
        }
    }

    private static bool TryAnchorLabel(Entry entry, Viewport viewport, PristonCamera camera, bool hovered, bool showAll)
    {
        if (!entry.FloorFound || (!showAll && !hovered))
        {
            return false;
        }

        float depth = -Vector3.Transform(entry.Position, camera.View).Z;
        if (!hovered && (depth < 32 || depth >= UI.Rendering.HudNameColors.OverheadDepth))
        {
            return false;
        }

        var point = viewport.Project(entry.Position + Vector3.Up * 16, camera.Projection, camera.View, Matrix.Identity);
        if (point.Z < 0 || point.Z > 1)
        {
            return false;
        }

        entry.LabelDepth = depth;
        entry.LabelAnchor = new Vector2(point.X, point.Y);
        return true;
    }

    // Before the first item that lies farther.
    internal static void InsertByDepth<T>(List<T> order, T item, Func<T, float> depth)
    {
        var index = 0;
        while (index < order.Count && depth(order[index]) <= depth(item))
        {
            index++;
        }

        order.Insert(index, item);
    }

    // Neighbours swap only while the first lies farther than the second by more than OrderHysteresis; each swap undoes one
    // such pair and makes none, so the passes end.
    internal static void SettleOrder<T>(List<T> order, Func<T, float> depth)
    {
        for (var swapped = true; swapped;)
        {
            swapped = false;
            for (var index = 1; index < order.Count; index++)
            {
                if (depth(order[index - 1]) > depth(order[index]) + OrderHysteresis)
                {
                    (order[index - 1], order[index]) = (order[index], order[index - 1]);
                    swapped = true;
                }
            }
        }
    }

    public void SetLabelBounds(Guid id, Rectangle bounds)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            entry.LabelBounds = bounds;
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Handle.IsValid)
            {
                scene.RemoveActor(entry.Handle);
            }
        }

        _entries.Clear();
        _labelOrder.Clear();
        _prepared.Clear();
    }

    private sealed class Entry(GroundDropView drop, Task<PristonActor> preparation)
    {
        public GroundDropView Drop { get; } = drop;
        public Matrix Rotation { get; } = Orientation(drop);
        public Task<PristonActor> Preparation { get; } = preparation;
        public ActorHandle Handle { get; set; } = ActorHandle.None;
        public Vector3 Position { get; set; }
        public float Offset { get; set; }
        public Rectangle LabelBounds { get; set; }
        public Vector2 LabelAnchor { get; set; }
        public float LabelDepth { get; set; }
        public bool Shown { get; set; }
        public bool Ordered { get; set; }
        public int FloorRevision { get; set; } = -1;
        public bool FloorFound { get; set; }
        public bool PublishFailed { get; set; }
        public string Label { get; } = drop.Gold ? ClientText.Format("Loot.Gold", drop.Amount) : drop.Name;
    }
}
