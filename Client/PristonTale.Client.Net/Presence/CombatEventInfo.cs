using System.Numerics;
using PristonTale.Contracts.Combat;

namespace PristonTale.Client.Net.Presence;

public sealed record CombatEventInfo(
    ulong AttackerId,
    ulong TargetId,
    CombatEventKind Kind,
    float DurationMs,
    float HitAtMs,
    int Damage,
    bool Critical,
    bool Killed,
    int Resources = 0,
    int SourceSoundCode = 0,
    Vector3 Position = default,
    float Facing = 0f)
{
    // A monster's swing: the motion-table slot of its clip (-1 for the first clip of the state).
    public int Clip { get; init; } = -1;

    // A monster's skill swing, or a hit dealt by a skill or a pet's strike rather than the attacker's basic attack.
    public bool Skill { get; init; }
}
