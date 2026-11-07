namespace PristonTale.Application.World;

// The descriptor revision of every entity of one field: it rises when anything a viewer's description shows changes
// (level, maximum life, template, speed, attack rate, equipment, looks, village flag, shop sign, owner, size). It is
// computed once per tick for the whole field, so every viewer sees the same revision and the encoders never compare
// descriptions themselves.
internal sealed class FieldDescriptors
{
    // Entries of entities that left are dropped after this many ticks without a view.
    private const int PruneIntervalTicks = 200;
    private readonly Dictionary<ulong, Entry> _entries = new();
    private readonly List<ulong> _departed = new();
    private int _tick;

    public void BeginTick() => _tick++;

    public byte RevisionOf(in EntityView view)
    {
        ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_entries, view.EntityId, out var exists);
        if (!exists)
        {
            entry = new Entry(new Descriptor(view), 1, _tick);
            return 1;
        }

        entry.SeenTick = _tick;
        if (!entry.Descriptor.Matches(view))
        {
            entry.Descriptor = new Descriptor(view);
            // Revision 0 is never used: it marks a view whose revision was not computed (a view built outside a field).
            entry.Revision = (byte)(entry.Revision == byte.MaxValue ? 1 : entry.Revision + 1);
        }

        return entry.Revision;
    }

    public void Prune()
    {
        if (_tick % PruneIntervalTicks != 0)
        {
            return;
        }

        _departed.Clear();
        foreach (var (id, entry) in _entries)
        {
            if (entry.SeenTick != _tick)
            {
                _departed.Add(id);
            }
        }

        foreach (var id in _departed)
        {
            _entries.Remove(id);
        }
    }

    private struct Entry(Descriptor descriptor, byte revision, int seenTick)
    {
        public Descriptor Descriptor = descriptor;
        public byte Revision = revision;
        public int SeenTick = seenTick;
    }

    private readonly struct Descriptor(in EntityView view)
    {
        private readonly int _maxLife = view.MaxLife;
        private readonly int _level = view.Level;
        private readonly uint _template = view.TemplateId;
        private readonly int _moveSpeed = view.MoveSpeed;
        private readonly float _attackRate = view.AttackRate;
        private readonly ulong _owner = view.OwnerId;
        private readonly float _scale = view.ModelScale;
        private readonly PlayerAppearanceView _appearance = view.Appearance;

        public bool Matches(in EntityView view)
        {
            var appearance = view.Appearance;
            return _maxLife == view.MaxLife && _level == view.Level && _template == view.TemplateId && _moveSpeed == view.MoveSpeed
                && _attackRate.Equals(view.AttackRate) && _owner == view.OwnerId && _scale.Equals(view.ModelScale)
                && _appearance.Tier == appearance.Tier && _appearance.HairIndex == appearance.HairIndex && _appearance.FaceIndex == appearance.FaceIndex
                && _appearance.InVillage == appearance.InVillage && _appearance.HelperPet == appearance.HelperPet && _appearance.Phoenix == appearance.Phoenix
                && _appearance.BigHead == appearance.BigHead && _appearance.Class == appearance.Class
                && string.Equals(_appearance.WeaponKey, appearance.WeaponKey, StringComparison.Ordinal)
                && string.Equals(_appearance.ArmorKey, appearance.ArmorKey, StringComparison.Ordinal)
                && string.Equals(_appearance.OffHandKey, appearance.OffHandKey, StringComparison.Ordinal)
                && _appearance.WeaponGlow == appearance.WeaponGlow && _appearance.OffHandGlow == appearance.OffHandGlow
                && string.Equals(_appearance.Name, appearance.Name, StringComparison.Ordinal)
                && string.Equals(_appearance.ShopSign, appearance.ShopSign, StringComparison.Ordinal)
                && _appearance.ClanId == appearance.ClanId && _appearance.ClanMark == appearance.ClanMark
                && string.Equals(_appearance.ClanName, appearance.ClanName, StringComparison.Ordinal)
                && string.Equals(_appearance.ModelName, appearance.ModelName, StringComparison.Ordinal)
                && string.Equals(_appearance.ModelName2, appearance.ModelName2, StringComparison.Ordinal);
        }
    }
}
