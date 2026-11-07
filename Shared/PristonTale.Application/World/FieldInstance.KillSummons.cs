using System.Numerics;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// The monsters a player's kill calls (KillSummons); each appears with the return sparkle at its place.
public sealed partial class FieldInstance
{
    internal void CallKillSummons(MonsterInstance fallen, ulong killerId)
    {
        foreach (var kid in KillSummons.Kids)
        {
            if (!fallen.Def.Key.Equals(kid.Parent, StringComparison.OrdinalIgnoreCase) && !fallen.Def.Name.Equals(kid.Parent, StringComparison.OrdinalIgnoreCase)
                || !_catalog.TryFind(kid.Child, out var child))
            {
                continue;
            }

            for (var count = 0; count < kid.Count; count++)
            {
                var monster = ((Simulation.World.Spawning.ISpawnSink)this).Spawn(child, Spread(new Vector2(fallen.Position.X, fallen.Position.Z), fallen.Position.Y),
                    -1, Simulation.World.Spawning.FieldSpawner.RandomFacing(_rng));
                SummonSparkle(killerId, monster);
            }
        }

        if (_siege is not { } siege || fallen.CastleRole is not (CastleObject.Crystal or CastleObject.Door))
        {
            return;
        }

        var door = fallen.CastleRole == CastleObject.Door;
        var key = door ? KillSummons.DoorSoldier : KillSummons.CrystalGuards[_rng.NextExclusive(0, KillSummons.CrystalGuards.Length)];
        if (!_catalog.TryFind(key, out var guard))
        {
            return;
        }

        foreach (var post in KillSummons.GuardPosts)
        {
            if (!KillSummons.NearPost(post, fallen.Position))
            {
                continue;
            }

            for (var count = 0; count < (door ? KillSummons.DoorSoldierCount : KillSummons.CrystalGuardCount); count++)
            {
                if (PlaceDefender(siege, guard.Key, Spread(post, fallen.Position.Y), CastleObject.Mercenary) is { } placed)
                {
                    if (siege.Weakened)
                    {
                        Weaken(placed);
                    }

                    SummonSparkle(killerId, placed);
                }
            }
        }
    }

    private Vector3 Spread(Vector2 around, float y) =>
        new(around.X + (_rng.NextUnit() * 2 - 1) * KillSummons.Spread, y, around.Y + (_rng.NextUnit() * 2 - 1) * KillSummons.Spread);

    private void SummonSparkle(ulong killerId, MonsterInstance monster) =>
        ItemEvents.Add(new ItemEffectView(killerId, 0, ItemEffectKind.Return, monster.Position, 0));
}
