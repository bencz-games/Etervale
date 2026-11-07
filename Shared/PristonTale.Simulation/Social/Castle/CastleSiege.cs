using System.Collections.Immutable;
using System.Numerics;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Social.Castle;

// What a monster is to the siege: the castle door, the Tower of Valhalla, a crystal tower or a mercenary; None for
// every other monster.
public enum CastleObject : byte
{
    None = 0,
    Door = 1,
    Valhalla = 2,
    Crystal = 3,
    Mercenary = 4
}

// The element the owner chose for a crystal tower slot; None stands a neutral tower crystal.
public enum CastleTower : byte
{
    None = 0,
    Ice = 1,
    Lightning = 2,
    Fire = 3
}

// A clan's damage to the castle in a siege.
public readonly record struct CastleClanScore(long ClanId, long Damage);

// The siege's defenders and where they stand. The door, the Tower of Valhalla and the crystal towers stand still, carry
// their potion count as lives (MonsterLifeRules) and stand at their places' heights (on the castle's structures);
// mercenaries carry all their potions and wander around their post. Every one belongs to the owner clan (they and its
// players never hurt each other, and they never pick its players). A castle without an owner is held by twenty
// mercenaries of each kind and crystal towers of random elements.
public static class CastleSiegeRules
{
    public const int TowerSlots = Contracts.Social.CastleLimits.TowerSlots;
    public const int MercenaryKinds = Contracts.Social.CastleLimits.MercenaryKinds;
    public const int MercenaryMax = 20;
    public const int TopClans = Contracts.Social.CastleLimits.TopClans;

    // The castle field south of this line is the battle zone; north of it lies the arrival area, where players never
    // fight.
    public const float BattleLineZ = -24043f;

    // A player's blow on another scores a tenth of its damage (at least 1).
    public const int PkScoreDivisor = 10;

    public const string DoorKey = "castledoor";
    public const string ValhallaKey = "tower of valhala";

    public static Vector3 Door { get; } = new(34552, 680, -25545);

    public static Vector3 Valhalla { get; } = new(34559, 800, -31375);

    public static ImmutableArray<Vector3> CrystalSlots { get; } =
    [
        new(34224, 646, -25857), new(34891, 646, -25857), new(33755, 686, -28297),
        new(35351, 686, -28297), new(34151, 754, -30313), new(34952, 754, -30313)
    ];

    // Ricarten militia, Ricarten guard and Bless royal guard.
    public static ImmutableArray<string> MercenaryKeys { get; } = ["ricarten militia", "ricarten guard", "bless royal guard"];

    public const int CalledClanMax = 20;
    public const int CalledOwnMax = 1;
    public const int CalledKindMax = 5;

    // A member of the owner clan calls a mercenary with a crystal while the clan's called mercenaries standing number
    // fewer than CalledClanMax, the member's fewer than CalledOwnMax and those of the kind fewer than CalledKindMax.
    public static bool MayCall(int clan, int own, int ofKind) => clan < CalledClanMax && own < CalledOwnMax && ofKind < CalledKindMax;

    // Each kind's posts: two rows of five on the west side and two on the east side.
    private static readonly MercenaryRows[] Rows =
    [
        new(-27968, -28052, 688, 33251, 33605, 35497, 35851),
        new(-29534, -29618, 706, 33590, 33945, 35159, 35513),
        new(-30882, -30967, 754, 33898, 34252, 34852, 35206)
    ];

    private const int PostsPerRow = 5;

    private readonly record struct MercenaryRows(float FrontZ, float BackZ, float Height, float WestFrom, float WestTo, float EastFrom, float EastTo);

    public static string CrystalKey(CastleTower element) => element switch
    {
        CastleTower.Ice => "ice crystal",
        CastleTower.Lightning => "lightning crystal",
        CastleTower.Fire => "fire crystal",
        _ => "tower crystal"
    };

    // Post 0..MercenaryMax-1 of a kind: the west rows first, front row then back row.
    public static Vector3 MercenaryPost(int kind, int post)
    {
        var rows = Rows[kind];
        var side = post / (PostsPerRow * 2);
        var column = post % PostsPerRow;
        var (from, to) = side == 0 ? (rows.WestFrom, rows.WestTo) : (rows.EastFrom, rows.EastTo);
        return new Vector3(from + (to - from) * column / (PostsPerRow - 1), rows.Height, post / PostsPerRow % 2 == 0 ? rows.FrontZ : rows.BackZ);
    }

    public static CastleObject ObjectOf(int soundCode) => soundCode switch
    {
        CharacterSoundCodes.CastleDoor => CastleObject.Door,
        CharacterSoundCodes.CastleTower => CastleObject.Valhalla,
        CharacterSoundCodes.CastleCrystalFire or CharacterSoundCodes.CastleCrystalLightning or CharacterSoundCodes.CastleCrystalIce
            or CharacterSoundCodes.CastleCrystalNeutral => CastleObject.Crystal,
        CharacterSoundCodes.CastleSoldierA or CharacterSoundCodes.CastleSoldierB or CharacterSoundCodes.CastleSoldierC => CastleObject.Mercenary,
        _ => CastleObject.None
    };

    // The kind (0..MercenaryKinds-1) of a mercenary's sound code; -1 for any other.
    public static int MercenaryKind(int soundCode) => soundCode switch
    {
        CharacterSoundCodes.CastleSoldierA => 0,
        CharacterSoundCodes.CastleSoldierB => 1,
        CharacterSoundCodes.CastleSoldierC => 2,
        _ => -1
    };

    // A player of this clan belongs to the clan holding the castle.
    public static bool HoldsCastle(long clanId, long ownerClanId) => ownerClanId != 0 && clanId == ownerClanId;

    public static bool InBattleZone(Vector3 position) => position.Z <= BattleLineZ;

    // Two players fight each other in the battle zone unless they share a clan.
    public static bool MayFight(Vector3 attacker, long attackerClan, Vector3 target, long targetClan) =>
        InBattleZone(attacker) && InBattleZone(target) && (attackerClan == 0 || attackerClan != targetClan);

    // What a player's blow on another adds to the attacker's clan on the board: nothing without a clan, nor when the
    // target has no clan and neither side is the owner clan.
    public static long PkScore(int damage, long attackerClan, long targetClan, long ownerClan) =>
        attackerClan == 0 || damage <= 0 || targetClan == 0 && attackerClan != ownerClan ? 0 : Math.Max(1, damage / PkScoreDivisor);

    public static bool StandsStill(int soundCode) => ObjectOf(soundCode) is CastleObject.Door or CastleObject.Valhalla or CastleObject.Crystal;

    // The towers and mercenaries a siege opens with: the owner's setup, or the ownerless castle's.
    public static (ImmutableArray<CastleTower> Towers, ImmutableArray<int> Mercenaries) Defenders(long ownerClanId, ImmutableArray<CastleTower> towers,
        ImmutableArray<int> mercenaries, ISimRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (ownerClanId == 0)
        {
            var random = new CastleTower[TowerSlots];
            for (var slot = 0; slot < TowerSlots; slot++)
            {
                random[slot] = (CastleTower)(rng.NextExclusive(0, (int)CastleTower.Fire) + 1);
            }

            return ([.. random], [.. Enumerable.Repeat(MercenaryMax, MercenaryKinds)]);
        }

        return ([.. Enumerable.Range(0, TowerSlots).Select(slot => slot < towers.Length ? towers[slot] : CastleTower.None)],
            [.. Enumerable.Range(0, MercenaryKinds).Select(kind => kind < mercenaries.Length ? Math.Clamp(mercenaries[kind], 0, MercenaryMax) : 0)]);
    }
}

// The damage each clan dealt to the castle in a siege. The leader is the clan with the most (the lower id on a tie).
public sealed class CastleDamageBoard
{
    private readonly Dictionary<long, long> _damage = new();

    public long Total { get; private set; }

    // Grows with every record, for readers that publish the board when it changed.
    public int Version { get; private set; }

    public void Record(long clanId, long damage)
    {
        if (clanId == 0 || damage <= 0)
        {
            return;
        }

        _damage[clanId] = _damage.GetValueOrDefault(clanId) + damage;
        Total += damage;
        Version++;
    }

    public long Leader
    {
        get
        {
            var leader = new CastleClanScore(0, 0);
            foreach (var (clanId, damage) in _damage)
            {
                if (damage > leader.Damage || damage == leader.Damage && clanId < leader.ClanId)
                {
                    leader = new CastleClanScore(clanId, damage);
                }
            }

            return leader.ClanId;
        }
    }

    public ImmutableArray<CastleClanScore> Top(int count) =>
        [.. _damage.Select(static pair => new CastleClanScore(pair.Key, pair.Value)).OrderByDescending(static score => score.Damage)
            .ThenBy(static score => score.ClanId).Take(count)];
}
