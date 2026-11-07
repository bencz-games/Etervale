using System.Numerics;

namespace PristonTale.Simulation.World.Ai;

// SightPercent scales the sight at which monsters notice the prey (stealth lowers it); 100 is unchanged. ClanId is the
// prey's clan (0 for none): a monster of that clan never picks it.
public readonly record struct PreyCandidate(ulong EntityId, Vector3 Position, bool IsAlive, int SightPercent = 100, long ClanId = 0);

public interface IPreyView
{
    IReadOnlyList<PreyCandidate> Candidates { get; }

    IReadOnlyList<PreyCandidate> Nearby(Vector3 position, float radius) => Candidates;

    bool TryGet(ulong entityId, out PreyCandidate prey);
}
