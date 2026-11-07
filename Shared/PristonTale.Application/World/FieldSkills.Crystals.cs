using System.Numerics;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// Crystal pets: monsters a crystal calls to fight for its user. Unlike a skill summon a player may keep several (ten). Each
// follows its user as a skill summon does (SummonBrain's leash, the recall to a far user) and runs at the nearest wild
// monster in its sight, never at another pet; its kills credit the user as the user's own. The original's pet stands
// where it was called; Etervale's follows. The user keeps a status per pet (MonsterCrystals.PetStatus, in the pet's slot)
// with its lifetime: its icon shows the crystal of the pet's monster and the time left, and a right click dismisses that
// pet. A pet dies with its status (expiry, the user's death), when the user dismisses it, enters a town or leaves the
// field; its status ends when the pet dies.
internal sealed partial class FieldSkills
{
    private readonly Dictionary<ulong, CrystalPet> _crystalPets = new();
    private readonly List<ulong> _endingPets = new();

    private sealed record CrystalPet(ulong OwnerId, uint StatusCode);

    public bool HasCrystalPetSlot(WorldPlayer owner) => FreePetSlot(owner) >= 0;

    // Calls the pet beside its owner with the owner-level lifetime and life bonus, and starts the owner's status for it
    // with the next step; returns where it stands, or null when every pet slot of the owner is taken.
    public Vector3? CallCrystalPet(WorldPlayer owner, MonsterCrystalDefinition crystal, MonsterDefinition definition)
    {
        var slot = FreePetSlot(owner);
        if (slot < 0)
        {
            return null;
        }

        var level = owner.Character.Level;
        var combat = definition.Combat;
        var owned = definition with
        {
            Faction = MonsterFaction.Npc,
            Brood = MonsterBrood.User,
            Combat = combat with { MaxLife = combat.MaxLife + MonsterCrystals.PetBonusLife(level) }
        };
        var lifetimeMs = MonsterCrystals.PetLifetimeMs(level);
        var monster = _host.AddSummon(owned, SpawnPoint(owner), owner.Facing);
        monster.OwnerId = owner.EntityId;
        monster.ClanId = crystal.Kind == CrystalKind.ClanPet ? owner.Clan?.ClanId ?? 0 : 0;
        monster.ExpiresAtMs = _host.NowMs + (long)lifetimeMs;
        monster.Aggressive = true;
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MotionState.Stand;
        var status = MonsterCrystals.PetStatus(MonsterCrystals.ShownCrystal(crystal, definition.Key), slot);
        _crystalPets[monster.EntityId] = new CrystalPet(owner.EntityId, status.Code);
        QueueItemEffect(owner.EntityId, status, lifetimeMs);
        return monster.Position;
    }

    // The lowest slot that neither a pet of the owner nor a pet status still on the owner holds (a dismissed pet's
    // status ends with the next step), or -1 when all are taken.
    private int FreePetSlot(WorldPlayer owner)
    {
        var taken = 0;
        foreach (var pet in _crystalPets.Values)
        {
            if (pet.OwnerId == owner.EntityId)
            {
                taken |= 1 << MonsterCrystals.PetSlot(pet.StatusCode);
            }
        }

        foreach (var effect in owner.Character.Effects.Effects)
        {
            if (MonsterCrystals.TryGetByPetStatus(effect.Code, out _))
            {
                taken |= 1 << MonsterCrystals.PetSlot(effect.Code);
            }
        }

        for (var slot = 0; slot < MonsterCrystals.PetSlots; slot++)
        {
            if ((taken & (1 << slot)) == 0)
            {
                return slot;
            }
        }

        return -1;
    }

    // The owner's request (the icon's right click, confirmed): the pet of that status dies now and the status ends with
    // the next step, even one whose pet is already gone. False when the owner keeps neither.
    public bool DismissCrystalPets(WorldPlayer owner, uint statusCode)
    {
        if (!MonsterCrystals.TryGetByPetStatus(statusCode, out _)
            || !EndCrystalPets(owner.EntityId, statusCode) && !owner.Character.Effects.TryGet(statusCode, out _) && !HasPendingItemEffect(owner, statusCode))
        {
            return false;
        }

        EndItemEffect(owner, statusCode);
        return true;
    }

    // The owner leaves the field: the pets stay behind and die (their statuses end in the field entered,
    // EndCrystalPetStatuses, where the owner's client hears it).
    public void LeaveCrystalPets(WorldPlayer owner) => EndCrystalPets(owner.EntityId, AnyCrystal);

    // A player who arrived from another field left its pets there: their statuses end with this field's next step.
    public void EndCrystalPetStatuses(WorldPlayer player)
    {
        foreach (var effect in player.Character.Effects.Effects)
        {
            if (MonsterCrystals.TryGetByPetStatus(effect.Code, out _))
            {
                EndItemEffect(player, effect.Code);
            }
        }
    }

    private const uint AnyCrystal = 0;

    // The owner's pet of one status (AnyCrystal: every pet) dies where it stands; false when the owner keeps none.
    private bool EndCrystalPets(ulong ownerId, uint statusCode)
    {
        _endingPets.Clear();
        foreach (var (petId, pet) in _crystalPets)
        {
            if (pet.OwnerId == ownerId && (statusCode == AnyCrystal || pet.StatusCode == statusCode))
            {
                _endingPets.Add(petId);
            }
        }

        foreach (var petId in _endingPets)
        {
            _crystalPets.Remove(petId);
            if (_host.TryGetMonster(petId, out var pet) && pet.IsAlive)
            {
                pet.ApplyDamage(pet.Life);
            }
        }

        return _endingPets.Count > 0;
    }

    private MonsterAttackEvent? StepCrystalPet(MonsterInstance pet, MonsterAiTuning tuning, float dtMs)
    {
        if (!pet.IsAlive)
        {
            LoseCrystalPet(pet);
            return null;
        }

        if (!_host.TryGetPlayer(pet.OwnerId, out var owner) || _host.NowMs >= pet.ExpiresAtMs || owner.InVillage
            || SummonBrain.NeedsRecall(pet, owner.Position) && !TryRecallBeside(pet, owner))
        {
            LoseCrystalPet(pet);
            return null;
        }

        return FollowAndFight(pet, owner, tuning, dtMs);
    }

    // A pet that died, expired, cannot follow its owner or whose owner is gone or in a town: it falls, and the owner's
    // status for it ends with the next step.
    private void LoseCrystalPet(MonsterInstance pet)
    {
        if (_crystalPets.Remove(pet.EntityId, out var lost) && _host.TryGetPlayer(lost.OwnerId, out var owner))
        {
            EndItemEffect(owner, lost.StatusCode);
        }

        if (pet.IsAlive)
        {
            pet.ApplyDamage(pet.Life);
        }
    }

    // A web's blow slows the struck monster for a moment; Kelvezu is immune.
    private void SlowFromWeb(MonsterInstance summon, MonsterInstance target)
    {
        if (summon.Def.SoundCode != CharacterSoundCodes.Web || target.Def.SoundCode == CharacterSoundCodes.Kelvezu || !target.IsAlive)
        {
            return;
        }

        if (target.Effects.Apply(MonsterCrystals.WebSlow(), summon.EntityId, MonsterCrystals.WebSlowMs, target.EntityId) is { } change)
        {
            _statusEvents.Add(StatusView(change, 1));
        }
    }
}
