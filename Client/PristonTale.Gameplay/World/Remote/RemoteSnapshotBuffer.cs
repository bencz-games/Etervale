using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World.Remote;

// Rest marks the copy the buffer adds of the sample before a silence: the entity holds that place until it moves again,
// and the copy gives no motion state.
public readonly record struct RemoteSample(double TimeMs, Vector3 Position, float Facing, MotionState MoveState, int Life, bool Rest = false);

public enum RemotePoseKind
{
    Held,
    Interpolated,
    Extrapolated
}

// SteadyState: both samples around the render time carry the same motion state.
public readonly record struct RemotePose(Vector3 Position, float Facing, float PlanarSpeed, RemotePoseKind Kind, bool SteadyState = false);

// Plays one remote entity back on the server timeline: positions, Y and facing are interpolated linearly between the two
// samples around renderTime = now - delay, and each sample's motion state becomes due when renderTime reaches it. The delay
// starts at 150 ms and follows the observed sample cadence (5 Hz near ring, 2 Hz far ring) so a far entity still
// interpolates instead of stopping between packets. A walking sample may be extrapolated for 250 ms; after that the lead
// returns to the last sample over another 250 ms, so an entity whose updates stopped never stays ahead of the server.
// The server sends nothing about an entity that stands still and repeats a moving one's place at least every
// CadenceGapMs, so a sample after a longer silence started one server tick before it: the entity rests at the previous
// place until then instead of creeping across the whole silence and leaping at the end.
public sealed class RemoteSnapshotBuffer
{
    public const int Capacity = 32;
    public const double BaseDelayMs = 150;
    public const double MaxDelayMs = 600;
    public const double MaxExtrapolationMs = 250;
    public const double ExtrapolationReturnMs = 250;
    public const float SnapDistance = 256f;
    private const double DelayMarginMs = 50;
    private const double CadenceGapMs = 750;
    private const int CadenceWindow = 4;
    private const double DelayGrowthPerMs = 0.25;
    private const double DelayShrinkPerMs = 0.1;
    private const float MovedDistance = 0.5f;

    private readonly List<RemoteSample> _samples = new(Capacity + 1);
    private readonly double[] _intervals = new double[CadenceWindow];
    private int _intervalCount;
    private int _intervalNext;
    private int _pendingState;
    private double _delayMs = BaseDelayMs;
    private double _renderTimeMs = double.NegativeInfinity;

    public RemoteSnapshotBuffer(in RemoteSample seed)
    {
        Reset(seed, stateApplied: true);
    }

    public int Count => _samples.Count;

    public double DelayMs => _delayMs;

    public double RenderTimeMs => _renderTimeMs;

    public RemoteSample Latest => _samples[^1];

    public double TargetDelayMs
    {
        get
        {
            var cadence = 0d;
            for (var index = 0; index < _intervalCount; index++)
            {
                cadence = Math.Max(cadence, _intervals[index]);
            }

            return Math.Clamp(cadence - DelayMarginMs, BaseDelayMs, MaxDelayMs);
        }
    }

    // Returns true when the sample was a snap (teleport, warp or an error above 256 units) that discarded the history.
    public bool Add(in RemoteSample sample, Vector3 rendered)
    {
        var latest = _samples[^1];
        var stamped = sample with { TimeMs = Math.Max(sample.TimeMs, latest.TimeMs) };
        if (Vector3.Distance(latest.Position, stamped.Position) > SnapDistance || Vector3.Distance(rendered, stamped.Position) > SnapDistance)
        {
            Reset(stamped, stateApplied: false);
            return true;
        }

        if (stamped.TimeMs == latest.TimeMs)
        {
            _samples[^1] = stamped;
            _pendingState = Math.Min(_pendingState, _samples.Count - 1);
            return false;
        }

        RecordInterval(latest, stamped);
        if (stamped.TimeMs - latest.TimeMs > CadenceGapMs)
        {
            _samples.Add(latest with { TimeMs = stamped.TimeMs - ServerTick.IntervalMs, Rest = true });
        }

        _samples.Add(stamped);
        while (_samples.Count > Capacity)
        {
            _samples.RemoveAt(0);
            _pendingState = Math.Max(0, _pendingState - 1);
        }

        return false;
    }

    public void Advance(double elapsedMs)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs <= 0)
        {
            return;
        }

        var target = TargetDelayMs;
        _delayMs = _delayMs < target
            ? Math.Min(target, _delayMs + elapsedMs * DelayGrowthPerMs)
            : Math.Max(target, _delayMs - elapsedMs * DelayShrinkPerMs);
    }

    public RemotePose Sample(double nowMs)
    {
        _renderTimeMs = nowMs - _delayMs;
        var time = _renderTimeMs;
        var count = _samples.Count;
        var first = _samples[0];
        if (count == 1 || time <= first.TimeMs)
        {
            return new RemotePose(first.Position, first.Facing, 0f, RemotePoseKind.Held);
        }

        var last = _samples[count - 1];
        if (time >= last.TimeMs)
        {
            TrimBefore(count - 2);
            return Extrapolate(time);
        }

        var index = 0;
        while (index + 1 < count && _samples[index + 1].TimeMs <= time)
        {
            index++;
        }

        var from = _samples[index];
        var to = _samples[index + 1];
        var span = to.TimeMs - from.TimeMs;
        var amount = (float)((time - from.TimeMs) / span);
        var position = Vector3.Lerp(from.Position, to.Position, amount);
        var facing = MathHelper.WrapAngle(from.Facing + MathHelper.WrapAngle(to.Facing - from.Facing) * amount);
        var speed = (float)(PlanarDistance(from.Position, to.Position) / (span / 1000d));
        TrimBefore(index);
        return new RemotePose(position, facing, speed, RemotePoseKind.Interpolated, from.MoveState == to.MoveState);
    }

    // Yields, in order, the samples whose motion state is due at the render time of the last Sample call; a rest gives
    // no state of its own.
    public bool TryTakeDueState(out RemoteSample sample)
    {
        while (_pendingState < _samples.Count && _samples[_pendingState].TimeMs <= _renderTimeMs)
        {
            sample = _samples[_pendingState];
            _pendingState++;
            if (!sample.Rest)
            {
                return true;
            }
        }

        sample = default;
        return false;
    }

    private RemotePose Extrapolate(double time)
    {
        var count = _samples.Count;
        var last = _samples[count - 1];
        var elapsed = time - last.TimeMs;
        if (count < 2 || last.MoveState is not (MotionState.Walk or MotionState.Run))
        {
            return new RemotePose(last.Position, last.Facing, 0f, RemotePoseKind.Held);
        }

        var previous = _samples[count - 2];
        var segment = last.TimeMs - previous.TimeMs;
        if (segment <= 0 || segment > CadenceGapMs)
        {
            return new RemotePose(last.Position, last.Facing, 0f, RemotePoseKind.Held);
        }

        var velocity = new Vector3(last.Position.X - previous.Position.X, 0f, last.Position.Z - previous.Position.Z) / (float)segment;
        var lead = elapsed <= MaxExtrapolationMs
            ? elapsed
            : Math.Max(0d, MaxExtrapolationMs - (elapsed - MaxExtrapolationMs) * MaxExtrapolationMs / ExtrapolationReturnMs);
        if (lead <= 0)
        {
            return new RemotePose(last.Position, last.Facing, 0f, RemotePoseKind.Held);
        }

        var speed = elapsed <= MaxExtrapolationMs ? velocity.Length() * 1000f : 0f;
        return new RemotePose(last.Position + velocity * (float)lead, last.Facing, speed, RemotePoseKind.Extrapolated);
    }

    private void Reset(in RemoteSample sample, bool stateApplied)
    {
        _samples.Clear();
        _samples.Add(sample);
        _pendingState = stateApplied ? 1 : 0;
    }

    private void RecordInterval(in RemoteSample previous, in RemoteSample next)
    {
        var interval = next.TimeMs - previous.TimeMs;
        if (interval > CadenceGapMs || PlanarDistance(previous.Position, next.Position) < MovedDistance)
        {
            return;
        }

        _intervals[_intervalNext] = interval;
        _intervalNext = (_intervalNext + 1) % CadenceWindow;
        _intervalCount = Math.Min(_intervalCount + 1, CadenceWindow);
    }

    private void TrimBefore(int index)
    {
        var removable = Math.Min(index, _pendingState);
        if (removable <= 0)
        {
            return;
        }

        _samples.RemoveRange(0, removable);
        _pendingState -= removable;
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
