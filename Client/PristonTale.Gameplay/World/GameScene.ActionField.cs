using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World;

// An action field plays side-on, as in the original. The camera stands at the field's height and depth, follows the
// player along x within the field's bounds and looks along +z; every actor is drawn facing left or right. The arrow keys
// walk, and pressing left or right again within half a second of letting it go dashes until it is released or turned.
// Space attacks the nearest monster ahead on the player's line, or else picks up the nearest drop; Ctrl uses the right
// skill, at once when it needs no target, else on the nearest monster ahead (or, for a skill on allies, the nearest
// player). Letting the keys go stops the walk and the attack. The mouse keeps its usual clicks.
public sealed partial class GameScene
{
    private const double DashTapMs = 500;

    // A target stands within AimDepth of the player's line; a drop within LootDepth of it and LootReach along it.
    private const float AimDepth = 32f;
    private const float LootReach = 32f;
    private const float LootDepth = 16f;

    // The key walk heads this far ahead of the player, every frame the key is held.
    private const float KeyWalkLead = 64f;

    private const int KeyUp = 1;
    private const int KeyDown = 2;
    private const int KeyRight = 4;
    private const int KeyLeft = 8;

    private Keys _actionKeyHeld;
    private Keys _actionKeyReleased;
    private double _actionKeyReleasedMs = double.NegativeInfinity;
    private bool _actionDash;
    private int _actionDirection;
    private bool _actionAttackHeld;
    private bool _actionSkillHeld;
    private bool _sideView;

    private FieldActionCamera ActionCamera => _currentField is { State: FieldState.Action } action ? action.ActionCamera : null;

    private bool InActionField => ActionCamera is not null;

    private void UpdateCamera(GameTime gameTime)
    {
        var view = ActionCamera;
        if (_remote is not null)
        {
            _remote.SideView = view is not null;
        }

        if (view is null)
        {
            if (_sideView)
            {
                _sideView = false;
                _camera.SnapFollow(_playerBody.Position, _fieldCollision);
            }

            _camera.Update(gameTime, _graphicsDevice, _playerBody.Position, _playerForward, _fieldCollision, PointerCaptured, CameraInputEnabled,
                keysEnabled: !KeyboardCaptured);
            return;
        }

        _sideView = true;
        var feet = GroundTraversal.ToVisualPosition(_playerBody.Position);
        var x = view.FollowX(feet.X);
        _camera.SetScriptedView(_graphicsDevice, new Vector3(x, view.Height, -view.Z), new Vector3(x, feet.Y, -FieldActionCamera.LookAtZ), Vector3.Up,
            FieldActionCamera.FieldOfViewRadians);
    }

    private void UpdateActionKeys(GameTime gameTime)
    {
        if (!InActionField || KeyboardCaptured)
        {
            if (_actionDirection != 0)
            {
                StopKeyWalk();
            }

            _actionDash = false;
            _actionKeyHeld = Keys.None;
            return;
        }

        var keys = Keyboard.GetState();
        var nowMs = gameTime.TotalGameTime.TotalMilliseconds;
        var direction = 0;
        if (!_playerController.LocomotionFrozen && !_selfAttacking && !SelfCasting && !SelfStunned)
        {
            if (keys.IsKeyDown(Keys.Up))
            {
                _actionKeyHeld = Keys.Up;
                direction |= KeyUp;
            }
            else if (keys.IsKeyDown(Keys.Down))
            {
                _actionKeyHeld = Keys.Down;
                direction |= KeyDown;
            }

            if (keys.IsKeyDown(Keys.Right))
            {
                direction |= HoldSide(Keys.Right, KeyRight, nowMs);
            }
            else if (keys.IsKeyDown(Keys.Left))
            {
                direction |= HoldSide(Keys.Left, KeyLeft, nowMs);
            }
            else if (_actionDash)
            {
                _actionKeyHeld = Keys.None;
                _actionDash = false;
            }
        }

        if (direction == 0 && _actionKeyHeld != Keys.None)
        {
            _actionKeyReleased = _actionKeyHeld;
            _actionKeyReleasedMs = nowMs;
            _actionKeyHeld = Keys.None;
        }

        if (direction != 0)
        {
            KeyWalk(direction);
        }
        else if (_actionDirection != 0)
        {
            StopKeyWalk();
        }

        _actionDirection = direction;

        var attack = keys.IsKeyDown(Keys.Space);
        if (attack && !_actionAttackHeld && _playerController.Current != MotionCode.Attack)
        {
            Strike(rightSkill: false);
        }

        _actionAttackHeld = attack;
        var skill = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        if (skill && !_actionSkillHeld && _playerController.Current != MotionCode.Attack)
        {
            Strike(rightSkill: true);
        }

        _actionSkillHeld = skill;
    }

    // Left or right held: a press soon after letting the same key go dashes, and turning ends the dash.
    private int HoldSide(Keys key, int bit, double nowMs)
    {
        if (_actionKeyHeld == Keys.None && _actionKeyReleased == key && nowMs - _actionKeyReleasedMs < DashTapMs)
        {
            _actionDash = true;
        }

        if (_actionDash && _actionKeyReleased != key)
        {
            _actionDash = false;
        }

        _actionKeyHeld = key;
        return bit;
    }

    // Up and down walk along +z and -z; a straight walk keeps the side the player faces.
    private void KeyWalk(int direction)
    {
        var side = _playerForward.X < 0f ? -1f : 1f;
        var across = (direction & KeyRight) != 0 ? 1f : (direction & KeyLeft) != 0 ? -1f : side * 0.001f;
        var along = (direction & KeyUp) != 0 ? 1f : (direction & KeyDown) != 0 ? -1f : 0f;
        var heading = Vector2.Normalize(WorldSpace.Planar(across, along));
        _moveTarget = _playerBody.PlanarPosition + heading * KeyWalkLead;
        _hasMoveTarget = true;
        _followCursor = false;
        if (direction != _actionDirection)
        {
            _reports.Request();
        }
    }

    private void StopKeyWalk()
    {
        _actionDirection = 0;
        ClearAttackTarget();
        StopPredictedMovement();
        _reports.Request();
    }

    private void Strike(bool rightSkill)
    {
        if (_remote is null || _playerBody is null || WorldClicksBlocked)
        {
            return;
        }

        var code = rightSkill ? RightSkill : 0u;
        var skill = code is not (0 or HudSkillBindings.BasicAttackId) && SkillRuleCatalog.TryGet(code, out var rule) ? rule : null;
        if (skill is { Target: SkillTarget.Self or SkillTarget.None })
        {
            if (!SelfCasting)
            {
                RequestCast(code, skill, 0, null);
            }

            return;
        }

        var origin = _playerBody.Position;
        var allies = skill is { Target: SkillTarget.Ally };
        var target = _remote.FindAhead(_remote.SelfEntityId, origin, _playerForward.X >= 0f, Owner?.AttackRange ?? AttackPresentation.MeleeRange,
            AimDepth, allies);
        if (skill is not null && !SelfCasting && (target != 0 || allies))
        {
            if (!allies)
            {
                _targetSelection.Trace(target);
            }

            _hasMoveTarget = false;
            RequestCast(code, skill, target, null);
            return;
        }

        if (target != 0 && skill is null)
        {
            _npcTargetId = 0;
            _lootInteraction.Cancel();
            _hasMoveTarget = false;
            _targetSelection.Trace(target);
            SendAttackIntent(_attackInput.Press(target, automatic: false, rightButton: rightSkill));
            return;
        }

        if (_groundLoot is not null && !_lootInteraction.Busy && _groundLoot.FindNear(origin, LootReach, LootDepth, out var drop, out var position))
        {
            ClearAttackTarget();
            _lootInteraction.Select(drop);
            _moveTarget = new Vector2(position.X, position.Z);
            _hasMoveTarget = true;
            _reports.Request();
        }
    }
}
