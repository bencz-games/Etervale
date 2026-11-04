using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

// A camera shake: every delay + 1 frames of 70 per second the factor (world units) flips its sign and loses a tenth (a
// fifth with a delay of 0 or 1, or from a factor of 40), and the view distance swings by it; it stops below one unit.
public sealed class PristonCameraWave
{
    private float _frames;
    private int _ticks;
    private int _delay;
    private int _offset;

    public int Factor { get; private set; }

    public bool Active => Factor != 0;

    public void Start(int factor, int delayFrames)
    {
        Factor = factor;
        _delay = Math.Max(0, delayFrames);
        _offset = 0;
    }

    public void Stop()
    {
        Factor = 0;
        _offset = 0;
        _ticks = 0;
        _frames = 0f;
    }

    // Steps the wave by the elapsed time; returns the distance to add to the view distance.
    public float Advance(float deltaSeconds)
    {
        if (!Active)
        {
            return 0f;
        }

        _frames += MathF.Max(0f, deltaSeconds) * GameUnits.TicksPerSecond;
        while (_frames >= 1f && Active)
        {
            _frames -= 1f;
            _ticks++;
            if (_ticks > _delay)
            {
                _ticks = 0;
                Factor = _delay > 1 && Factor < 40 ? -(int)(Factor / 10f * 9f) : -(int)(Factor / 10f * 8f);
                _offset = Factor;
            }

            if (Math.Abs(Factor) < 1)
            {
                Stop();
            }
        }

        return _offset;
    }
}
