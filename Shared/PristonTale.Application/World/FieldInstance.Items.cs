using PristonTale.Simulation.World.Spawning;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// The field side of used items that act on the world: crystal pets and soul monsters, castle items and the
// Restoration's wait.
public sealed partial class FieldInstance
{
    // The field's kind (town, castle, ...), set by the world when it opens the field.
    internal FieldState State { get; set; }

    // Soul-stone monsters and the field time they vanish at.
    private readonly Dictionary<ulong, long> _soulMonsters = new();
    private readonly List<ulong> _soulScratch = new();

    // The server's item events, which replace drop rolls with event items.
    internal ItemEventRates ItemEventRates
    {
        get => _combat.EventRates;
        set => _combat.EventRates = value;
    }

    internal int ExperienceMultiplier
    {
        get => _combat.ExperienceMultiplier;
        set => _combat.ExperienceMultiplier = value;
    }

    internal int ExperienceEventPercent
    {
        get => _combat.ExperienceEventPercent;
        set => _combat.ExperienceEventPercent = value;
    }

    internal Simulation.Items.Aging.AgingSettings Aging { get; set; } = Simulation.Items.Aging.AgingSettings.Default;

    internal Simulation.Items.Mixing.MixingSettings Mixing { get; set; } = Simulation.Items.Mixing.MixingSettings.Default;

    internal DailyDropLedger? DropLedger
    {
        get => _combat.DropLedger;
        set => _combat.DropLedger = value;
    }

    // The field's random source for item prizes rolled by NPC services.
    internal ISimRng ItemRng => _rng;

    // The monsters of the field, for inspection outside the tick (tests and probes).
    internal IEnumerable<MonsterInstance> MonstersForInspection => _monsters.Values;

    public bool TryFindMonsterKey(string key, [MaybeNullWhen(false)] out MonsterDefinition definition) => _catalog.TryFindKey(key, out definition);

    // Calls a crystal's monster for its user: a pet beside the user, or a soul stone's hostile monster where the user
    // stands.
    public Vector3? CallCrystalMonster(WorldPlayer owner, string crystalKey, string monsterKey)
    {
        if (!MonsterCrystals.TryGet(crystalKey, out var crystal) || !_catalog.TryFindKey(monsterKey, out var definition))
        {
            return null;
        }

        if (crystal.Kind != CrystalKind.SoulStone)
        {
            return _skills.CallCrystalPet(owner, crystal, definition);
        }

        var monster = ((ISpawnSink)this).Spawn(definition, owner.Position, -1, owner.Facing);
        _soulMonsters[monster.EntityId] = NowMs + MonsterCrystals.SoulLifetimeSeconds * 1000L;
        return monster.Position;
    }

    public bool HasCrystalPetSlot(WorldPlayer owner) => _skills.HasCrystalPetSlot(owner);

    // The owner dismisses the pet of one status (the code its icon shows); false when the owner keeps no such pet here.
    public bool DismissCrystalPets(WorldPlayer owner, uint statusCode) =>
        _players.TryGetValue(owner.EntityId, out var present) && ReferenceEquals(present, owner) && _skills.DismissCrystalPets(owner, statusCode);

    // A player arriving from another field left its crystal pets there; their statuses end here.
    public void ArriveWithoutCrystalPets(WorldPlayer player) => _skills.EndCrystalPetStatuses(player);

    // Soul monsters fall when their four minutes pass.
    private void ExpireSoulMonsters()
    {
        if (_soulMonsters.Count == 0)
        {
            return;
        }

        _soulScratch.Clear();
        foreach (var (id, endMs) in _soulMonsters)
        {
            if (!_monsters.TryGetValue(id, out var monster) || !monster.IsAlive)
            {
                _soulScratch.Add(id);
            }
            else if (NowMs >= endMs)
            {
                monster.ApplyDamage(monster.Life);
                _combat.BeginCorpse(id);
                _soulScratch.Add(id);
            }
        }

        foreach (var id in _soulScratch)
        {
            _soulMonsters.Remove(id);
        }
    }

    // A castle item replaces the running one of its kind; Athanasia also guards its holder for its 30 s.
    public void StartCastleItem(WorldPlayer player, CastleItemDefinition item)
    {
        if (CastleItems.Running(player.Character.Effects, item.Kind) is { } running)
        {
            _skills.EndItemEffect(player, running.StatusCode);
        }

        var durationMs = item.DurationSeconds * 1000f;
        _skills.QueueItemEffect(player.EntityId, item.Spec(), durationMs);
        if (item.Kind == CastleItemKind.Scroll && item.Target == (int)ScrollEffectKind.Invulnerability)
        {
            player.Guard(durationMs);
        }
    }

    // Castle items end when their holder leaves the castle field.
    private void LeaveCastleItems(WorldPlayer player)
    {
        if (FieldId != FieldCodes.Castle)
        {
            return;
        }

        foreach (var item in CastleItems.All)
        {
            player.Character.Effects.Remove(item.StatusCode, player.EntityId, null);
        }
    }
}
