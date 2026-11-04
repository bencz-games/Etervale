namespace PristonTale.Gameplay.World;

// When the local player reports its movement: at once when its motion, target or field changes or a report is
// requested, every 200 ms while it moves and every second while it stands (the original reports every 64 frames;
// moving avatars report at 5 Hz for smoother peers). Never faster than one report per server tick: the server closes a
// connection that sends more than 251 packets in 5 s, so a report asked for inside the gap waits for the next slot.
internal sealed class MovementReportPacer
{
    private const double MovingReportMs = 200;
    private const double IdleReportMs = 1000;
    private const double MinReportGapMs = 50;

    private double _clockMs;
    private double _lastMs = double.NegativeInfinity;
    private bool _due;
    private bool _moving;
    private bool _running;
    private ulong _target;
    private int _fieldCode = -1;

    // Something the server must learn soon: a new destination, a target that moved, a field change.
    public void Request() => _due = true;

    // The field the server already places the player in (world entry, a confirmed change), or -1 when none.
    public void SetField(int fieldCode) => _fieldCode = fieldCode;

    public void Advance(float seconds) => _clockMs += seconds * 1000.0;

    // A report sent outside the pacing (the position a skill cast is measured from): its state is the last one reported.
    public void Taken(bool moving, bool running, ulong target, int fieldCode)
    {
        _due = false;
        _lastMs = _clockMs;
        _moving = moving;
        _running = running;
        _target = target;
        _fieldCode = fieldCode;
    }

    // Whether a report goes out now with this state; the state is then the last one reported.
    public bool TryTake(bool moving, bool running, ulong target, int fieldCode)
    {
        var elapsed = _clockMs - _lastMs;
        var changed = moving != _moving || running != _running || target != _target || fieldCode != _fieldCode;
        if (!_due && !changed && elapsed < (moving ? MovingReportMs : IdleReportMs))
        {
            return false;
        }

        if (elapsed < MinReportGapMs)
        {
            _due = true;
            return false;
        }

        _due = false;
        _lastMs = _clockMs;
        _moving = moving;
        _running = running;
        _target = target;
        _fieldCode = fieldCode;
        return true;
    }
}
