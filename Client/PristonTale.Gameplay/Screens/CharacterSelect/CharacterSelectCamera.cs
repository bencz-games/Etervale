using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal sealed class CharacterSelectCamera
{
    private const float CameraTargetY = 16f;
    private const int CameraAngleX = 90;

    private static readonly float[] CameraZPos = [300f, 370f, 550f, 720f, 700f];
    // The original's vertical angle (pi / 4.4) over the full rail: the lineup, the statues and the orb keep its framing.
    private const float FieldOfViewRadians = MathF.PI / 4.4f;

    private static float Rail(int state) => CameraZPos[state];

    private const float CameraDollyFastPerSecond = 235f;
    private const float CameraDollySlowPerSecond = 23f;
    private const float CameraDollySnapThreshold = 3.1f;

    private const float SwayAmplitude = 1.25f;
    private const float SwayPeriodSeconds = 160f / 60f;

    private readonly PristonCamera _camera;
    private readonly Vector3 _target;
    private float _distance;
    private int _currentState;
    private float _swaySeconds;

    public CharacterSelectCamera(GraphicsDevice graphicsDevice, int initialState)
    {
        _target = new Vector3(0f, CameraTargetY, 0f);
        _currentState = initialState;
        _distance = Rail(initialState);
        _camera = new PristonCamera(_target, Vector3.Forward);
        Apply(graphicsDevice, 0f);
    }

    public PristonCamera Camera => _camera;
    public Matrix View => _camera.View;
    public Matrix Projection => _camera.Projection;
    public Vector3 Position => _camera.Position;

    public void Update(GraphicsDevice graphicsDevice, int targetState, float dt)
    {
        UpdateDolly(targetState, dt);
        _swaySeconds += dt;
        var sway = TriangleWave(_swaySeconds, SwayPeriodSeconds) * SwayAmplitude;
        Apply(graphicsDevice, sway);
    }

    private void UpdateDolly(int targetState, float dt)
    {
        if (targetState == _currentState)
        {
            _distance = Rail(targetState);
            return;
        }

        var fromDist = Rail(_currentState);
        var toDist = Rail(targetState);
        var span = MathF.Abs(toDist - fromDist);
        var remaining = MathF.Abs(toDist - _distance);
        var rate = remaining < span / 16f ? CameraDollySlowPerSecond : CameraDollyFastPerSecond;
        var step = rate * dt;

        if (targetState > _currentState)
        {
            _distance += step;
            if (_distance > toDist - CameraDollySnapThreshold)
            {
                _distance = toDist;
                _currentState = targetState;
            }
        }
        else
        {
            _distance -= step;
            if (_distance < toDist + CameraDollySnapThreshold)
            {
                _distance = toDist;
                _currentState = targetState;
            }
        }
    }

    private void Apply(GraphicsDevice graphicsDevice, float sway)
    {
        var radians = CameraAngleX * GameUnits.RadiansPerAngle;
        var offset = new Vector3(0f, _distance * MathF.Sin(radians), _distance * MathF.Cos(radians));
        var target = _target + new Vector3(0f, sway, 0f);
        _camera.SetScriptedView(graphicsDevice, target + offset, target, Vector3.Up, FieldOfViewRadians);
    }

    private static float TriangleWave(float seconds, float period)
    {
        var phase = (seconds % period) / period;
        return phase < 0.5f ? (phase * 4f - 1f) : (3f - phase * 4f);
    }
}
