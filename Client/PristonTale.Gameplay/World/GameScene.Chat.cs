using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.HelpPet;
using PristonTale.Simulation.Social;

namespace PristonTale.Gameplay.World;

// The chat's hooks into the scene: sending typed lines and placing speech bubbles over the characters drawn.
public sealed partial class GameScene
{
    // The bubble hangs this far above the head bone (or above the feet when the head is not known).
    private const float BubbleAboveHead = 14f;
    private const float BubbleAboveFeet = 60f;
    private const float BubbleAbovePet = 10f;

    // The chat input has the keyboard: letters must not act as game keys (the ground labels' A key).
    public bool KeyboardCaptured { get; set; }

    // Space cheers until this tick count; the server keeps its own window and checks the stamina.
    private long _yahooKeyUntilMs = long.MinValue;

    public Task SendChatAsync(string text, bool party)
    {
        if (!party && YahooRules.IsWord(text))
        {
            _yahooKeyUntilMs = Environment.TickCount64 + YahooRules.KeyWindowMs;
        }

        return _playChannel is not null ? _playChannel.SendChatAsync(text, party, _presenceCts.Token) : Task.CompletedTask;
    }

    public void PressYahooKey()
    {
        // Space attacks in an action field.
        if (_playChannel is not null && !InActionField && Environment.TickCount64 < _yahooKeyUntilMs)
        {
            _ = _playChannel.SendYahooKeyAsync(_presenceCts.Token);
        }
    }

    // The screen point over an entity's head and its depth from the camera, or null when it is behind the camera or not
    // drawn.
    public HudOverheadPoint? Overhead(ulong entityId)
    {
        if (_camera is null)
        {
            return null;
        }

        Vector3 anchor;
        if (((PristonTale.Effects.Vfx.IVfxAnchors)this).TryGetBone(entityId, UI.Widgets.HudTargetPortrait.HeadBone, out var head))
        {
            anchor = head + Vector3.Up * BubbleAboveHead;
        }
        else if (TryGetDrawnPosition(entityId, out var feet))
        {
            anchor = feet + Vector3.Up * BubbleAboveFeet;
        }
        else
        {
            return null;
        }

        return Project(anchor);
    }

    // The local player's helper pet and the point over it where it speaks (null while it is not drawn).
    public HudHelperPetView SelfHelperPet()
    {
        if (_pets is null || !_pets.TryGetHelper(SelfEntityId, out var kind, out var position))
        {
            return default;
        }

        return new HudHelperPetView(kind, position is { } at && _camera is not null ? Project(at + Vector3.Up * BubbleAbovePet) : null);
    }

    private HudOverheadPoint? Project(Vector3 anchor)
    {
        var depth = -Vector3.Transform(anchor, _camera.View).Z;
        if (depth <= 0f)
        {
            return null;
        }

        var viewport = _graphicsDevice.Viewport;
        var point = viewport.Project(anchor, _camera.Projection, _camera.View, Matrix.Identity);
        return point.Z is < 0f or > 1f ? null : new HudOverheadPoint(new Vector2(point.X, point.Y), depth);
    }
}
