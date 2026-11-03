using PristonTale.Simulation.World.Spawning;
using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;

namespace PristonTale.Application.World;

// The field side of quests: kill credit and quest drops, deaths, hunt timers, the periodic upkeep check, and the duels
// of the quest arena with their opponents' behaviour.
public sealed partial class FieldInstance
{
    private const long UpkeepCheckIntervalMs = 5000;
    private const float QuestStepIntervalMs = 1000;

    private readonly Dictionary<ulong, Duel> _duels = new();
    private readonly List<ulong> _duelScratch = new();
    private readonly List<ulong> _companionScratch = new();

    // Wall-clock milliseconds for quest item lifetimes; the world sets it from its time provider.
    internal Func<long> QuestClock { get; set; } = static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Called when a player has quest work the upkeep must store; the world forwards it to the upkeep worker.
    internal Action<WorldPlayer>? QuestUpkeepRequested { get; set; }

    private sealed class Duel(ulong playerId, QuestCode quest, long expiresAtMs, bool master, bool magicMaster)
    {
        public ulong PlayerId { get; } = playerId;
        public QuestCode Quest { get; } = quest;
        public long ExpiresAtMs { get; } = expiresAtMs;
        public bool Master { get; } = master;
        public bool MagicMaster { get; } = magicMaster;
        public ulong HelperId { get; set; }
        public long HelperExpiresAtMs { get; set; }
        public bool WasSkilling { get; set; }
        public DuelPreyView Prey { get; } = new();
    }

    // The single player a duel opponent sees; an opponent that gave up sees no one.
    private sealed class DuelPreyView : IPreyView
    {
        private readonly List<PreyCandidate> _candidates = new(1);

        public IReadOnlyList<PreyCandidate> Candidates => _candidates;

        public void Set(WorldPlayer? player)
        {
            _candidates.Clear();
            if (player is not null)
            {
                _candidates.Add(new PreyCandidate(player.EntityId, player.Position, !player.IsDead));
            }
        }

        public bool TryGet(ulong entityId, out PreyCandidate prey)
        {
            foreach (var candidate in _candidates)
            {
                if (candidate.EntityId == entityId)
                {
                    prey = candidate;
                    return true;
                }
            }

            prey = default;
            return false;
        }
    }

    private sealed class NoQuestItems : IQuestItemSource
    {
        public static readonly NoQuestItems Instance = new();

        public bool TryCreate(uint code, int count, uint jobSpecMask, out ItemInstance item)
        {
            item = null!;
            return false;
        }

        public bool TryCreateQuestVariant(uint code, uint jobSpecMask, out ItemInstance item)
        {
            item = null!;
            return false;
        }
    }

    private QuestContext TickContext(WorldPlayer player) =>
        new(player.Character, QuestNpc.None, NoQuestItems.Instance, _rng, QuestClock(), FieldId);

    // Posts the context's notices and dialog and refreshes the quest effects when the journal moved.
    private static void Publish(WorldPlayer player, QuestContext context, long revisionBefore)
    {
        player.QuestState.Post(context.Notices, context.Prompt);
        if (player.Character.Quests.Revision != revisionBefore)
        {
            player.Character.RefreshQuestEffects();
        }
    }

    // Kill credit for the credited player's hunts; quest and ingredient drops leave the ground loot for its pack.
    private KillPayout CreditQuests(KillPayout reward)
    {
        _monsters.TryGetValue(reward.MonsterEntityId, out var killed);
        WorldPlayer? credited = null;
        if (reward.CreditedPlayerId != 0 && _players.TryGetValue(reward.CreditedPlayerId, out var found))
        {
            credited = found;
        }

        if (killed is not null && credited is not null)
        {
            var context = TickContext(credited);
            var revision = credited.Character.Quests.Revision;
            if (QuestTick.OnKill(context, killed.Def.QuestCode))
            {
                credited.QuestState.UpkeepDue = true;
            }

            Publish(credited, context, revision);
        }

        if (killed is not null && killed.DuelPlayerId != 0)
        {
            OnDuelOpponentDied(killed);
        }

        var anyQuestDrop = false;
        foreach (var drop in reward.Drops)
        {
            anyQuestDrop |= drop.ToInventory;
        }

        if (!anyQuestDrop)
        {
            return reward;
        }

        var ground = new List<RolledDrop>(reward.Drops.Count);
        foreach (var drop in reward.Drops)
        {
            if (!drop.ToInventory)
            {
                ground.Add(drop);
            }
            else if (credited is not null)
            {
                credited.QuestState.AddDrop(new PendingQuestDrop(drop.ItemCode, drop.Amount, killed?.Def.QuestCode ?? 0,
                    drop.FromMainTable ? QuestDropSource.MainTable : QuestDropSource.BonusTable));
            }
        }

        return reward with { Drops = ground };
    }

    private void StepQuests(float dtMs, long nowMs)
    {
        foreach (var player in _players.Values)
        {
            var state = player.QuestState;
            var character = player.Character;
            if (player.IsDead && !state.DeathHandled)
            {
                state.DeathHandled = true;
                var context = TickContext(player);
                var revision = character.Quests.Revision;
                if (QuestTick.OnDeath(context))
                {
                    state.UpkeepDue = true;
                }

                if (FieldId == QuestArena.FieldCode)
                {
                    LoseDuel(player, context);
                }

                Publish(player, context, revision);
            }
            else if (!player.IsDead && state.DeathHandled)
            {
                state.DeathHandled = false;
                if (FieldId == QuestArena.FieldCode)
                {
                    // A player who fell in the arena stands up again in the home town.
                    state.PendingTravel = new QuestTravel(QuestTravelKind.Home, QuestClasses.HomeField(character), 0, 0);
                }
            }

            state.StepCarryMs = character.Quests.Active.IsIdle || player.IsDead ? 0 : state.StepCarryMs + dtMs;
            if (state.StepCarryMs >= QuestStepIntervalMs)
            {
                // Quest time moves in whole steps so a running timer republishes the journal once per step, not per tick.
                var elapsed = (long)state.StepCarryMs;
                state.StepCarryMs = 0;
                var context = TickContext(player);
                var revision = character.Quests.Revision;
                QuestTick.Step(context, elapsed);
                Retreat(player, context);
                if (context.Notices.Count > 0 || character.Quests.Revision != revision)
                {
                    Publish(player, context, revision);
                }
            }

            if (nowMs >= state.NextUpkeepCheckMs)
            {
                state.NextUpkeepCheckMs = nowMs + UpkeepCheckIntervalMs;
                if (QuestDrops.UpkeepDue(character, QuestClock()))
                {
                    state.UpkeepDue = true;
                }
            }

            if (state.UpkeepDue && !state.UpkeepSignaled && QuestUpkeepRequested is not null)
            {
                state.UpkeepSignaled = true;
                QuestUpkeepRequested(player);
            }
        }

        StepDuels(nowMs);
    }

    // A quest ended the player's duel on the tick: the player goes home, and the duel closes once the player has left.
    private void Retreat(WorldPlayer player, QuestContext context)
    {
        if (context.Retreat && FieldId == QuestArena.FieldCode)
        {
            player.QuestState.PendingTravel = new QuestTravel(QuestTravelKind.Home, QuestClasses.HomeField(player.Character), 0, 0);
        }
    }

    // Opens the duel a quest asked for: the opponent appears at the arena's duel spot, grown with the player's level,
    // and fights only that player. A previous opponent of the player leaves first. A boss with lives (Hestian) keeps its
    // lives and must be killed; the others carry three potions more than their own.
    internal MonsterInstance? OpenDuel(WorldPlayer player, QuestArenaDuel duel, long nowMs)
    {
        CloseDuelsOf(player.EntityId);
        if (!_catalog.TryLowestOfFamily(duel.SoundCode, duel.Level, out var definition))
        {
            return null;
        }

        var level = player.Level;
        var combat = definition.Combat with
        {
            MaxLife = QuestArena.ScaledLife(definition.Combat.MaxLife, level),
            AttackDamage = new MonsterRange(QuestArena.ScaledDamage(definition.Combat.AttackDamage.Min, level),
                QuestArena.ScaledDamage(definition.Combat.AttackDamage.Max, level))
        };
        var skill = definition.Skill == MonsterSkillStats.None
            ? definition.Skill
            : definition.Skill with
            {
                SkillDamage = new MonsterRange(QuestArena.ScaledDamage(definition.Skill.SkillDamage.Min, level),
                    QuestArena.ScaledDamage(definition.Skill.SkillDamage.Max, level))
            };
        var grown = definition with { Combat = combat, Skill = skill };
        var position = Terrain.PlaceOnFloor(new Vector3(QuestArena.OpponentX, 0f, QuestArena.OpponentZ));
        var opponent = new MonsterInstance(_ids.NextMonsterId(), grown, position, 0f, spawnPointIndex: -1)
        {
            DuelPlayerId = player.EntityId,
            Potions = definition.PotionCount + QuestArena.ExtraPotions
        };
        if (MonsterLifeRules.HasVirtualLives(definition))
        {
            opponent.VirtualLives = definition.PotionCount;
            opponent.VirtualLivesMax = definition.PotionCount;
            opponent.Potions = 0;
        }

        var master = duel.Quest == QuestCode.RankUpMasterTrial;
        opponent.MinimumLife = master ? QuestArena.YieldLife(combat.MaxLife) : opponent.Potions > 0 ? 1 : 0;
        _monsters[opponent.EntityId] = opponent;
        _duels[opponent.EntityId] = new Duel(player.EntityId, duel.Quest, nowMs + QuestArena.OpponentLifetimeMs(duel.Quest), master,
            definition.SoundCode == CharacterSoundCodes.MagicMaster);
        player.QuestState.DuelOpponentId = opponent.EntityId;
        return opponent;
    }

    // The prey a duel opponent or its helper fights: only the duel's player.
    private IPreyView? DuelPrey(MonsterInstance monster)
    {
        if (monster.DuelPlayerId == 0)
        {
            return null;
        }

        var owner = _duels.TryGetValue(monster.EntityId, out var duel) ? duel : FindDuelOfHelper(monster.EntityId);
        var prey = owner?.Prey ?? new DuelPreyView();
        _players.TryGetValue(monster.DuelPlayerId, out var player);
        prey.Set(monster.DuelYielded ? null : player);
        return prey;
    }

    private Duel? FindDuelOfHelper(ulong helperId)
    {
        foreach (var duel in _duels.Values)
        {
            if (duel.HelperId == helperId)
            {
                return duel;
            }
        }

        return null;
    }

    private void StepDuels(long nowMs)
    {
        if (_duels.Count == 0)
        {
            return;
        }

        _duelScratch.Clear();
        foreach (var (opponentId, duel) in _duels)
        {
            if (!_monsters.TryGetValue(opponentId, out var opponent) || !opponent.IsAlive)
            {
                _duelScratch.Add(opponentId);
                continue;
            }

            _players.TryGetValue(duel.PlayerId, out var player);
            if (player is null || nowMs >= duel.ExpiresAtMs)
            {
                _duelScratch.Add(opponentId);
                continue;
            }

            if (duel.HelperId != 0 && nowMs >= duel.HelperExpiresAtMs)
            {
                DismissHelper(duel);
            }

            StepOpponent(opponent, duel, player, nowMs);
        }

        foreach (var opponentId in _duelScratch)
        {
            CloseDuel(opponentId);
        }
    }

    private void StepOpponent(MonsterInstance opponent, Duel duel, WorldPlayer player, long nowMs)
    {
        var maxLife = opponent.Def.Combat.MaxLife;
        if (duel.Master && !opponent.DuelYielded && QuestArena.MasterYields(opponent.Life, maxLife))
        {
            // The master gives up: it stands down and the player has won the duel.
            opponent.DuelYielded = true;
            opponent.Life = QuestArena.YieldLife(maxLife);
            opponent.TargetId = 0;
            DismissHelper(duel);
            WinDuel(player, duel.Quest);
            return;
        }

        if (opponent.DuelYielded)
        {
            return;
        }

        if (opponent.Potions > 0 && opponent.Life * 100L < maxLife * (long)QuestArena.PotionThresholdPercent)
        {
            opponent.Potions--;
            var heal = QuestArena.PotionHeal(maxLife, duel.MagicMaster);
            opponent.Life = Math.Min(maxLife, opponent.Life + heal);
            EmitCombatEvent(new CombatEventView(opponent.EntityId, opponent.EntityId, CombatEventKind.Heal, damage: heal));
            if (!duel.Master && opponent.Potions == 0)
            {
                // Fury falls for good once its potions are gone.
                opponent.MinimumLife = 0;
            }
        }

        var skilling = opponent.Motion == MotionState.Skill;
        if (skilling && !duel.WasSkilling && duel.Master && !HelperAlive(duel) && _rng.RollPercent() < QuestArena.HelperPercent)
        {
            CallHelper(opponent, duel, player);
            duel.HelperExpiresAtMs = nowMs + QuestArena.HelperLifetimeMs;
        }

        duel.WasSkilling = skilling;
        if (duel.MagicMaster && !skilling && _rng.RollPercent() < QuestArena.MagicMasterBlinkPercent
            && Vector3.DistanceSquared(Flat(opponent.Position), Flat(player.Position)) < QuestArena.MagicMasterBlinkReach * QuestArena.MagicMasterBlinkReach)
        {
            var reach = (int)QuestArena.MagicMasterBlinkReach;
            var x = QuestArena.OpponentX + _rng.NextInclusive(-reach, reach - 1);
            var z = QuestArena.OpponentZ + _rng.NextInclusive(-reach, reach - 1);
            opponent.Position = Terrain.PlaceOnFloor(new Vector3(x, opponent.Position.Y, z));
        }
    }

    private static Vector3 Flat(Vector3 position) => new(position.X, 0f, position.Z);

    private bool HelperAlive(Duel duel) => duel.HelperId != 0 && _monsters.TryGetValue(duel.HelperId, out var helper) && helper.IsAlive;

    // The skill master calls a metal golem or a wolverine, the magic master a fire elemental; one helper at a time.
    private void CallHelper(MonsterInstance opponent, Duel duel, WorldPlayer player)
    {
        var sound = duel.MagicMaster ? CharacterSoundCodes.FireElemental
            : _rng.NextExclusive(0, 2) == 0 ? CharacterSoundCodes.MetalGolem : CharacterSoundCodes.SummonedWolverine;
        if (!_catalog.TryLowestOfFamily(sound, 0, out var definition))
        {
            return;
        }

        var helper = ((ISpawnSink)this).Spawn(definition, opponent.Position, -1, opponent.Facing);
        helper.DuelPlayerId = player.EntityId;
        duel.HelperId = helper.EntityId;
    }

    private void DismissHelper(Duel duel)
    {
        if (duel.HelperId != 0)
        {
            RemoveMonster(duel.HelperId);
        }

        duel.HelperId = 0;
    }

    private void OnDuelOpponentDied(MonsterInstance killed)
    {
        if (!_duels.TryGetValue(killed.EntityId, out var duel) || duel.Master)
        {
            return;
        }

        // Fury beaten with no potion left, or Hestian with no life left.
        if (_players.TryGetValue(duel.PlayerId, out var player))
        {
            WinDuel(player, duel.Quest);
        }

        DismissHelper(duel);
        RemoveCompanions(duel.PlayerId, killed.EntityId);
        _duels.Remove(killed.EntityId);
    }

    private void WinDuel(WorldPlayer player, QuestCode quest)
    {
        var context = TickContext(player);
        var revision = player.Character.Quests.Revision;
        var won = quest switch
        {
            QuestCode.FuryOfPhantom => FuryOfPhantomQuest.OnDuelWon(context),
            QuestCode.RankUpHestian => RankUpByHestian.OnDuelWon(context),
            _ => RankUpByMasterTrial.OnDuelWon(context)
        };
        if (won)
        {
            Retreat(player, context);
            Publish(player, context, revision);
        }
    }

    private void LoseDuel(WorldPlayer player, QuestContext context)
    {
        RankUpByMasterTrial.OnDuelLost(context);
        FuryOfPhantomQuest.OnDuelLost(context);
        RankUpByHestian.OnDuelLost(context);
        CloseDuelsOf(player.EntityId);
    }

    private void CloseDuelsOf(ulong playerId)
    {
        _duelScratch.Clear();
        foreach (var (opponentId, duel) in _duels)
        {
            if (duel.PlayerId == playerId)
            {
                _duelScratch.Add(opponentId);
            }
        }

        foreach (var opponentId in _duelScratch)
        {
            CloseDuel(opponentId);
        }
    }

    private void CloseDuel(ulong opponentId)
    {
        if (_duels.Remove(opponentId, out var duel))
        {
            DismissHelper(duel);
            RemoveCompanions(duel.PlayerId, opponentId);
            if (_players.TryGetValue(duel.PlayerId, out var player) && player.QuestState.DuelOpponentId == opponentId)
            {
                player.QuestState.DuelOpponentId = 0;
            }
        }

        RemoveMonster(opponentId);
    }

    // The monsters an opponent called for its player (Hestian's Xetans) leave with the duel.
    private void RemoveCompanions(ulong playerId, ulong opponentId)
    {
        _companionScratch.Clear();
        foreach (var (id, monster) in _monsters)
        {
            if (id != opponentId && monster.DuelPlayerId == playerId)
            {
                _companionScratch.Add(id);
            }
        }

        foreach (var id in _companionScratch)
        {
            RemoveMonster(id);
        }
    }
}
