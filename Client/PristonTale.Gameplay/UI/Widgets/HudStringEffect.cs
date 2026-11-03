using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.UI.Widgets;

// The word images the original interface flashes over the bottom bar when the player blocks a blow: 128x64 at (354, 488)
// in design pixels, fully opaque for 26 interface frames, then fading by 10 alpha per frame while it grows by 3 pixels and
// drifts up-left by 1. The interface runs 70 frames per second.
public sealed class HudStringEffect
{
    private const int MaxEffects = 10;
    private const int SteadyFrames = 26;
    private readonly Effect[] _effects = new Effect[MaxEffects];
    private ulong _self;

    public void Observe(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            if (spawn.IsSelf)
            {
                _self = spawn.EntityId;
            }
        }

        foreach (var combat in update.Events)
        {
            if (combat.Kind == CombatEventKind.Block && combat.TargetId == _self && _self != 0)
            {
                Show(HudArt.BlockText);
            }
        }
    }

    public void Show(string image)
    {
        for (var index = 0; index < _effects.Length; index++)
        {
            if (_effects[index].Image is null)
            {
                _effects[index] = new Effect { Image = image };
                return;
            }
        }
    }

    public void Update(float seconds)
    {
        for (var index = 0; index < _effects.Length; index++)
        {
            ref var effect = ref _effects[index];
            if (effect.Image is null)
            {
                continue;
            }

            effect.Elapsed += seconds;
            while (effect.Image is not null && effect.Elapsed >= GameUnits.TickSeconds)
            {
                effect.Elapsed -= GameUnits.TickSeconds;
                effect.Frames++;
                if (effect.Frames > SteadyFrames)
                {
                    effect.Fade++;
                    if (255 - effect.Fade * 10 <= 0)
                    {
                        effect = default;
                    }
                }
            }
        }
    }

    public void Draw(HudPainter painter, HudLayout layout)
    {
        foreach (var effect in _effects)
        {
            if (effect.Image is null)
            {
                continue;
            }

            var alpha = Math.Clamp(255 - effect.Fade * 10, 0, 255) / 255f;
            var bounds = new Rectangle(354 - effect.Fade, layout.BottomOffset + 488 - effect.Fade, 128 + effect.Fade * 3, 64 + effect.Fade * 3);
            painter.Stretch(effect.Image, bounds, null, Color.White * alpha);
        }
    }

    private struct Effect
    {
        public string Image;
        public float Elapsed;
        public int Frames;
        public int Fade;
    }
}
