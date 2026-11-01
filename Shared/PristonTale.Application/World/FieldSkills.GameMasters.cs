using System.Numerics;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;

namespace PristonTale.Application.World;

// Monsters a game master calls as pets (/mymonster): as many as called, none replacing another and none taking the
// skill summon's place. Each takes the player's side like an aggressive summon, follows its caller as a summon does and
// falls when its lifetime ends, when it cannot follow or when the caller is gone. They show no keep icon on the caller.
internal sealed partial class FieldSkills
{
    private readonly HashSet<ulong> _gameMasterPets = new();

    public MonsterInstance CallGameMasterPet(WorldPlayer owner, MonsterDefinition definition, float lifetimeMs, Vector3? position = null)
    {
        var pet = definition with { Faction = MonsterFaction.Npc, Brood = MonsterBrood.User };
        var monster = _host.AddSummon(pet, position ?? SpawnPoint(owner), owner.Facing);
        monster.OwnerId = owner.EntityId;
        monster.SummonSkill = 0;
        monster.ExpiresAtMs = _host.NowMs + (long)lifetimeMs;
        monster.Aggressive = true;
        monster.TargetId = 0;
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MotionState.Stand;
        _gameMasterPets.Add(monster.EntityId);
        return monster;
    }

    private MonsterAttackEvent? StepGameMasterPet(MonsterInstance pet, MonsterAiTuning tuning, float dtMs)
    {
        if (!pet.IsAlive || !_host.TryGetPlayer(pet.OwnerId, out var owner) || _host.NowMs >= pet.ExpiresAtMs
            || SummonBrain.NeedsRecall(pet, owner.Position) && !TryRecallBeside(pet, owner))
        {
            _gameMasterPets.Remove(pet.EntityId);
            if (pet.IsAlive)
            {
                pet.ApplyDamage(pet.Life);
            }

            return null;
        }

        return FollowAndFight(pet, owner, tuning, dtMs);
    }
}
