using Microsoft.Xna.Framework.Input;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.World;

// The original's continued attack with a skill: a click of a mouse button whose skill strikes an enemy, on a monster,
// keeps attacking it while the button is held, or until it dies or leaves when Shift was down at the click. The skill
// goes again whenever it is ready; while it is not, the player strikes with the weapon. A cast the server refuses
// (mana, stamina) is tried again a second later without its notice, the weapon striking meanwhile.
public sealed partial class GameScene
{
    private const double TraceRetryMs = 1000d;

    private SkillTrace? _skillTrace;
    private double _traceRetryMs;
    private uint _traceCastSequence;

    private readonly record struct SkillTrace(ulong TargetId, bool RightButton, bool Automatic);

    private void StartSkillTrace(uint code, ulong target, bool rightButton)
    {
        _skillTrace = Rank(code) > 0 ? new SkillTrace(target, rightButton, ShiftHeld(Keyboard.GetState())) : null;
        _traceRetryMs = 0d;
    }

    private void UpdateSkillTrace(MouseState mouse)
    {
        if (_skillTrace is not { } trace || _remote is null || _skillFx is null)
        {
            return;
        }

        var held = trace.RightButton ? mouse.RightButton == ButtonState.Pressed : mouse.LeftButton == ButtonState.Pressed;
        var code = trace.RightButton ? RightSkill : LeftSkill;
        if (!held && !trace.Automatic || !_remote.TryGetTarget(trace.TargetId, out _, out var dead) || dead
            || !SkillRuleCatalog.TryGet(code, out var rule) || rule.Target != SkillTarget.Enemy)
        {
            EndSkillTrace();
            return;
        }

        if (WorldClicksBlocked || SelfCasting || _pendingCast is not null)
        {
            return;
        }

        if (_skillFx.Cooldowns.Ready(code, _skillClockMs) && _skillClockMs >= _traceRetryMs)
        {
            RequestCast(code, rule, trace.TargetId, null, repeat: true);
            _targetSelection.Trace(trace.TargetId);
            return;
        }

        if (_attackInput.TargetId != trace.TargetId)
        {
            SendAttackIntent(_attackInput.Press(trace.TargetId, trace.Automatic, trace.RightButton));
        }
    }

    // The weapon's strikes of a trace end with it; a pending cast of the trace is dropped.
    private void EndSkillTrace()
    {
        if (_skillTrace is { } trace && _attackInput.TargetId == trace.TargetId)
        {
            ClearAttackTarget();
        }

        _skillTrace = null;
    }

    // A refused cast of the trace waits a second and says nothing; answers whether the result was the trace's.
    private bool TraceCastRefused(Client.Net.Presence.SkillCastResultInfo result)
    {
        if (result.Accepted || _skillTrace is null || result.Sequence != _traceCastSequence)
        {
            return false;
        }

        _traceRetryMs = _skillClockMs + TraceRetryMs;
        return true;
    }
}
