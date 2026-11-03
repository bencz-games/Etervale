using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// What the premium timers need from their field.
internal interface IFieldPremiumHost
{
    long NowMs { get; }
    CombatRng Rng { get; }
    bool TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster);
    bool TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player);
    void PetStrike(WorldPlayer owner, MonsterInstance monster, int damage);
    void QueueMonsterEffect(ulong monsterId, EffectSpec spec, float durationMs);
    void AddItemEvent(in ItemEffectView effect);
}

// Premium timers on the field tick: they expire on the real clock, the vampiric and mana-recharging timers answer each
// attack of their owner, and the pets fight beside their owner. The hits of one attack land in the same tick, so the
// tick's hits of a player form one attack: the number of monsters hit and their average damage feed the life and mana
// it returns, and its first monster is the one a pet may join in on.
internal sealed class FieldPremium
{
    // Bless Castle: the per-attack premium effects do not work there.

    private readonly Dictionary<ulong, Attack> _attacks = new();
    private readonly Dictionary<ulong, PetCycle> _helpers = new();
    private readonly Dictionary<ulong, PetCycle> _phoenixes = new();
    private readonly Dictionary<ulong, long> _phoenixLastHit = new();
    private readonly List<ulong> _scratch = new();
    private readonly int _fieldId;
    private readonly IFieldPremiumHost _host;
    private bool _petStriking;

    public FieldPremium(int fieldId, IFieldPremiumHost host)
    {
        _fieldId = fieldId;
        _host = host;
    }

    // Unix seconds of the world clock, set by the world before each step.
    public long NowSeconds { get; set; }

    public void RecordHit(WorldPlayer player, MonsterInstance monster, int damage)
    {
        if (_petStriking || _fieldId == FieldCodes.Castle || damage <= 0)
        {
            return;
        }

        var current = _attacks.GetValueOrDefault(player.EntityId);
        _attacks[player.EntityId] = new Attack(current.Hits + 1, current.Damage + damage, current.Hits == 0 ? monster.EntityId : current.Target);
    }

    public void Forget(ulong entityId)
    {
        _helpers.Remove(entityId);
        _phoenixes.Remove(entityId);
        _phoenixLastHit.Remove(entityId);
        _attacks.Remove(entityId);
    }

    public void Step(Dictionary<ulong, WorldPlayer>.ValueCollection players)
    {
        if (NowSeconds > 0)
        {
            foreach (var player in players)
            {
                var premium = player.Character.Premium;
                var head = premium.BigHeadMode;
                premium.Expire(NowSeconds);
                if (head == PremiumTimers.SoccerHeadMode && premium.BigHeadMode == 0)
                {
                    // The soccer-ball head ends with the head-change burst and its short guard.
                    player.Guard(EventFoods.HeadChangeGuardMs);
                    _host.AddItemEvent(new ItemEffectView(player.EntityId, 0, ItemEffectKind.HeadChange, player.Position, 0));
                }
            }
        }

        StepPets(_helpers);
        StepPets(_phoenixes);
        if (_attacks.Count == 0)
        {
            return;
        }

        foreach (var (entityId, attack) in _attacks)
        {
            if (!_host.TryGetPlayer(entityId, out var player) || player.IsDead)
            {
                continue;
            }

            var character = player.Character;
            var power = (int)(attack.Damage / attack.Hits);
            player.Heal(PremiumRules.LifeFor(character.Premium, character.Attributes.Strength, power, attack.Hits));
            var mana = PremiumRules.ManaFor(character.Premium, character.Attributes.Spirit, power, attack.Hits);
            if (mana > 0)
            {
                character.Mana = Math.Min(character.MaxMana, character.Mana + mana);
            }

            StartPets(player, attack.Target);
        }

        _attacks.Clear();
    }

    private void StartPets(WorldPlayer owner, ulong target)
    {
        var premium = owner.Character.Premium;
        var now = _host.NowMs;
        if (Simulation.Characters.HelpPets.Active(owner.Character) is { } helper && !_helpers.ContainsKey(owner.EntityId)
            && _host.Rng.RollPercent() < PremiumPets.HelperChance(owner.Level))
        {
            _helpers[owner.EntityId] = new PetCycle(helper, target, now + PremiumPets.HelperHitMs, now + PremiumPets.HelperEndMs, false);
            Announce(owner, helper, target);
        }

        if (premium.IsActive(PremiumEffectKind.PhoenixPet) && !_phoenixes.ContainsKey(owner.EntityId)
            && now - _phoenixLastHit.GetValueOrDefault(owner.EntityId, long.MinValue / 2) >= PremiumPets.PhoenixCooldownMs
            && _host.TryGetMonster(target, out var monster) && Vector3.Distance(owner.Position, monster.Position) < PremiumPets.PhoenixReach)
        {
            _phoenixes[owner.EntityId] = new PetCycle(PremiumEffectKind.PhoenixPet, target, now + PremiumPets.PhoenixHitMs, now + PremiumPets.PhoenixEndMs, false);
            Announce(owner, PremiumEffectKind.PhoenixPet, target);
        }
    }

    private void Announce(WorldPlayer owner, PremiumEffectKind pet, ulong target)
    {
        var position = _host.TryGetMonster(target, out var monster) ? monster.Position : owner.Position;
        _host.AddItemEvent(new ItemEffectView(owner.EntityId, (uint)pet, ItemEffectKind.PetAttack, position, 0, target));
    }

    private void StepPets(Dictionary<ulong, PetCycle> cycles)
    {
        if (cycles.Count == 0)
        {
            return;
        }

        var now = _host.NowMs;
        _scratch.Clear();
        _scratch.AddRange(cycles.Keys);
        foreach (var ownerId in _scratch)
        {
            var cycle = cycles[ownerId];
            if (!_host.TryGetPlayer(ownerId, out var owner) || owner.IsDead)
            {
                cycles.Remove(ownerId);
                continue;
            }

            if (!cycle.Struck && now >= cycle.HitAtMs)
            {
                cycle = cycle with { Struck = true };
                cycles[ownerId] = cycle;
                Strike(owner, cycle);
            }

            if (now >= cycle.EndAtMs)
            {
                cycles.Remove(ownerId);
            }
        }
    }

    private void Strike(WorldPlayer owner, PetCycle cycle)
    {
        if (!_host.TryGetMonster(cycle.Target, out var monster) || !monster.IsAlive || monster.IsSummon)
        {
            return;
        }

        var defender = DefenderProfile.FromMonster(monster);
        var damage = PremiumPets.Damage(cycle.Pet, owner.Level, defender, _host.Rng);
        if (cycle.Pet == PremiumEffectKind.PhoenixPet)
        {
            _phoenixLastHit[owner.EntityId] = _host.NowMs;
        }

        if (cycle.Pet == PremiumEffectKind.HelpPetNepsys)
        {
            var duration = PremiumPets.SlowDurationMs(defender.Resistances.Ice);
            if (duration > 0f)
            {
                _host.QueueMonsterEffect(monster.EntityId, PremiumPets.Slow(), duration);
            }
        }

        _petStriking = true;
        try
        {
            _host.PetStrike(owner, monster, damage);
        }
        finally
        {
            _petStriking = false;
        }
    }

    private readonly record struct Attack(int Hits, long Damage, ulong Target);

    private readonly record struct PetCycle(PremiumEffectKind Pet, ulong Target, long HitAtMs, long EndAtMs, bool Struck);
}
