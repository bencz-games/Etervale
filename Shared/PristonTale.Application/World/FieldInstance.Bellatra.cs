using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// Bellatra's arena field: the rounds' monsters it opens in their rooms, and the score its entrants' kills give.
public sealed partial class FieldInstance
{
    // The world's game, set on the arena field only.
    internal BellatraGame? Bellatra { get; set; }

    internal int BellatraScoreDivisor { get; set; } = BellatraRules.DefaultScoreDivisor;

    // The round bosses entrants killed since the world last told them, with their score.
    internal List<(ulong KillerId, long Score)> BellatraBossKills { get; } = [];

    // The seals entrants broke since the world last told their teams: the picker, its team and the seal.
    internal List<(string Picker, int Team, BellatraSeal Seal)> BellatraSeals { get; } = [];

    // The round's monsters by the catalog, resolved once a round (null for a name it lacks).
    internal MonsterDefinition?[] ResolveBellatraMonsters(BellatraRound round, out MonsterDefinition? boss)
    {
        boss = round.Boss != "" && _catalog.TryFind(round.Boss, out var found) ? found : null;
        var monsters = new MonsterDefinition?[round.Monsters.Length];
        for (var index = 0; index < monsters.Length; index++)
        {
            monsters[index] = _catalog.TryFind(round.Monsters[index].Name, out var monster) ? monster : null;
        }

        return monsters;
    }

    // One monster a pulse in each room of the round with no more than the round's cap standing; a room's boss once the
    // room has opened more than the cap less BellatraRound.BossMargin.
    internal void SpawnBellatraRound(BellatraGame game, BellatraRound round)
    {
        var (first, end) = BellatraArena.RoomsOf(game.Round);
        for (var room = first; room < end; room++)
        {
            var standing = 0;
            foreach (var monster in _monsters.Values)
            {
                standing += monster.BellatraRoom == room && monster.IsAlive ? 1 : 0;
            }

            if (standing > round.MaxMonsters)
            {
                continue;
            }

            var boss = game.Boss is not null && !game.BossOpened[room] && game.Opened[room] > round.MaxMonsters - BellatraRound.BossMargin;
            if ((boss ? game.Boss : game.Monsters[round.Pick(_rng.NextExclusive(0, 100))]) is not { } definition)
            {
                continue;
            }

            game.BossOpened[room] |= boss;
            game.Opened[room]++;
            var opened = ((Simulation.World.Spawning.ISpawnSink)this).Spawn(definition, BellatraArena.Rooms[room].RandomPoint(_rng), -1,
                Simulation.World.Spawning.FieldSpawner.RandomFacing(_rng));
            opened.BellatraRoom = room;
            opened.BellatraBoss = boss;
        }
    }

    // potions: the game's end after its last round, when each falls leaving a Mystic mana potion.
    internal void KillBellatraMonsters(bool potions)
    {
        foreach (var monster in _monsters.Values)
        {
            if (monster.BellatraRoom >= 0 && monster.IsAlive)
            {
                KillOutright(monster);
                if (potions)
                {
                    GroundLoot.PlaceItem(BellatraRules.EndPotion, Terrain.SettleNear(monster.Position, FieldTerrain.DropTolerance), NowMs, _rng);
                }
            }
        }
    }

    // In the arena only entrants take what lies on the ground.
    internal bool MayTakeBellatraLoot(WorldPlayer player) =>
        Bellatra is not { } game || !BellatraArena.InArea(player.Position) || game.Entrants.ContainsKey(player.Character.Name);

    // An entrant breaks a seal it picked up in the room it stands in while a round is played; false, nothing broken,
    // outside a room, between rounds, for a player who did not enter or for a monster the catalog lacks.
    internal bool BreakBellatraSeal(WorldPlayer player, BellatraSeal seal)
    {
        var room = BellatraArena.RoomAt(player.Position);
        if (Bellatra is not { Round: > 0 } game || room < 0 || !game.Entrants.TryGetValue(player.Character.Name, out var entrant))
        {
            return false;
        }

        var bounds = BellatraArena.Rooms[room];
        switch (seal)
        {
            case BellatraSeal.Quake or BellatraSeal.Stun or BellatraSeal.Freeze:
                SealRoom(seal, room, bounds);
                break;
            case BellatraSeal.Rabbit when !TurnToRabies(room, bounds):
                return false;
            case BellatraSeal.Stygian or BellatraSeal.Guardian:
                var family = seal == BellatraSeal.Stygian ? CharacterSoundCodes.StygianLord : CharacterSoundCodes.GuardianSaint;
                if (!_catalog.TryLowestOfFamily(family, seal == BellatraSeal.Stygian ? BellatraRules.StygianLevel : 0, out var helper))
                {
                    return false;
                }

                CallGameMasterPet(player, helper, BellatraRules.SummonMs, bounds.Centre);
                break;
            case BellatraSeal.Points:
                entrant.Score += BellatraRules.PointsBonus;
                game.TeamScores[entrant.Team] += BellatraRules.PointsBonus;
                game.Revision++;
                break;
        }

        BellatraSeals.Add((player.Character.Name, entrant.Team, seal));
        return true;
    }

    // The statuses start with the next step, so that their events reach its snapshot.
    private void SealRoom(BellatraSeal seal, int room, BellatraRoom bounds)
    {
        foreach (var monster in _monsters.Values)
        {
            if (monster.BellatraRoom != room || !monster.IsAlive || !bounds.Holds(monster.Position))
            {
                continue;
            }

            switch (seal)
            {
                case BellatraSeal.Quake when !IsBellatraBoss(monster):
                    monster.Life = Math.Max(1, monster.Life - monster.Def.Combat.MaxLife / BellatraRules.QuakeShare);
                    break;
                case BellatraSeal.Stun:
                    QueueItemEffect(monster.EntityId, BellatraRules.StunSeal, BellatraRules.StunMs);
                    break;
                case BellatraSeal.Freeze:
                    QueueItemEffect(monster.EntityId, BellatraRules.FreezeSeal, BellatraRules.FreezeMs);
                    break;
            }
        }
    }

    // About half the room's round monsters but its bosses turn into Rabies, and a Mutant Rabie boss of the round opens at
    // the room's centre; false without Rabies in the catalog.
    private bool TurnToRabies(int room, BellatraRoom bounds)
    {
        if (!_catalog.TryLowestOfFamily(CharacterSoundCodes.Rabie, 0, out var rabie))
        {
            return false;
        }

        _bellatraScratch.Clear();
        foreach (var monster in _monsters.Values)
        {
            if (monster.BellatraRoom == room && monster.IsAlive && bounds.Holds(monster.Position) && !IsBellatraBoss(monster) && _rng.NextExclusive(0, 2) == 0)
            {
                _bellatraScratch.Add(monster);
            }
        }

        var sink = (Simulation.World.Spawning.ISpawnSink)this;
        foreach (var monster in _bellatraScratch)
        {
            RemoveMonster(monster.EntityId);
            sink.Spawn(rabie, monster.Position, -1, monster.Facing).BellatraRoom = room;
        }

        _bellatraScratch.Clear();
        foreach (var candidate in _catalog.Family(CharacterSoundCodes.MutantRabie))
        {
            if (candidate.IsBoss)
            {
                var opened = sink.Spawn(candidate, bounds.Centre, -1, 0f);
                opened.BellatraRoom = room;
                opened.BellatraBoss = true;
                break;
            }
        }

        return true;
    }

    private static bool IsBellatraBoss(MonsterInstance monster) => monster.BellatraBoss || monster.Def.IsBoss;

    private readonly List<MonsterInstance> _bellatraScratch = [];

    // An entrant's kill of a round's monster while a round is played: the score to the entrant and its team, a round
    // boss's told to the killer, and now and then a surprise where the kill's loot lies.
    internal void ScoreBellatra(MonsterInstance killed, ulong killerId, System.Numerics.Vector3 dropPosition)
    {
        if (Bellatra is not { Round: > 0 } game || killed.BellatraRoom < 0 || !_players.TryGetValue(killerId, out var killer)
            || !game.Entrants.TryGetValue(killer.Character.Name, out var entrant))
        {
            return;
        }

        var score = BellatraRules.Score((long)Math.Min(killed.Def.ExpReward, long.MaxValue), BellatraScoreDivisor, killed.BellatraBoss, game.Round);
        entrant.Score += score;
        entrant.Kills++;
        game.TeamScores[entrant.Team] += score;
        game.Revision++;
        if (killed.BellatraBoss)
        {
            BellatraBossKills.Add((killerId, score));
        }

        if (_rng.NextExclusive(0, 1000) < BellatraRules.SurprisePerMille
            && Simulation.Items.ItemCodes.TryParse(BellatraRules.SurpriseFor(_rng.RollPercent()), out var surprise))
        {
            GroundLoot.PlaceItem(surprise, dropPosition, NowMs, _rng);
        }
    }
}
