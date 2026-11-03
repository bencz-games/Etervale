using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Contracts.Units;

namespace PristonTale.Effects.Vfx;

// Quads and models of the part layers.
public sealed partial class VfxRenderer
{
    private Vector3 _viewForward = -Vector3.UnitZ;
    // World units per pixel of a 600-pixel-high view at unit depth.
    private float _worldPerPixel;

    private void AppendParts(VfxInstance instance, int pass, Vector3 right, Vector3 up, Vector3 camera)
    {
        foreach (var state in instance.PartStates)
        {
            var compiled = state.Part;
            var source = compiled.Source;
            if (!compiled.Visible || source.Shape == VfxPartShape.Model || PassOf(compiled.Blend) != pass)
            {
                continue;
            }

            for (var index = 0; index < state.Parts.Length; index++)
            {
                ref readonly var part = ref state.Parts[index];
                if (part.Phase != PartPhase.Live || state.Texture(part) is not { } texture)
                {
                    continue;
                }

                var color = compiled.Color;
                var alpha = state.Fade(part);
                if (compiled.Animation is not null)
                {
                    alpha *= Math.Clamp(part.Player.Alpha, 0f, 255f) / 255f;
                }

                color.W *= alpha;
                if (color.W <= 0.5f)
                {
                    continue;
                }

                var tint = Decode(color);
                var animated = compiled.Animation is { } animation && animation.Channels != Scripts.BillboardChannels.Opacity;
                var width = animated ? part.Player.Size : part.Size;
                var height = animated ? part.Player.Size * (part.Height / MathF.Max(1e-3f, part.Size)) : part.Height;
                var angle = part.Rotation + (compiled.Animation is not null ? part.Player.Angle : 0f);
                var position = part.Position;
                if (source.SlideFrom != 0f)
                {
                    var slide = VfxScreenSlide.At(source.SlideFrom, (int)(part.Age * GameUnits.TicksPerSecond));
                    var depth = MathF.Max(1f, Vector3.Dot(position - camera, _viewForward)) * _worldPerPixel;
                    position += right * (slide.X * depth) - up * (slide.Y * depth);
                }

                switch (source.Shape)
                {
                    case VfxPartShape.Billboard:
                        if (angle == 0f)
                        {
                            AppendBillboard(texture, compiled.Blend, position, width * 0.5f, height * 0.5f, tint, right, up);
                        }
                        else
                        {
                            AppendTurnedBillboard(texture, compiled.Blend, position, width * 0.5f, height * 0.5f, tint, Matrix.CreateRotationZ(MathHelper.ToRadians(angle)));
                        }

                        break;
                    case VfxPartShape.Ground:
                        AppendGround(texture, compiled.Blend, position, width * 0.5f, height * 0.5f, tint,
                            Matrix.CreateRotationY(MathHelper.ToRadians(angle)) * state.Turn);
                        break;
                    case VfxPartShape.Streak:
                        AppendStrip(texture, compiled.Blend, part.Start, position, part.HalfWidth, tint, camera, false);
                        break;
                    case VfxPartShape.Line:
                        AppendStrip(texture, compiled.Blend, position - part.Velocity * source.LineSeconds, position, part.HalfWidth, tint, camera, false);
                        break;
                    case VfxPartShape.Beam:
                        AppendStrip(texture, compiled.Blend, position, part.Start, part.HalfWidth, tint, camera, false);
                        break;
                    case VfxPartShape.Ribbon:
                        AppendRibbon(state, index, texture, compiled.Blend, part.HalfWidth, color, camera);
                        break;
                    case VfxPartShape.Blade:
                        AppendBlade(state, index, texture, compiled.Blend, tint);
                        break;
                }
            }
        }
    }

    // A camera-facing ribbon through the remembered positions, newest first; each older point loses TrailFade alpha.
    private void AppendRibbon(PartState state, int index, Texture2D texture, VfxBlend blend, float halfWidth, Vector4 color, Vector3 camera)
    {
        ref readonly var part = ref state.Parts[index];
        var stride = state.Part.TrailStride;
        var at = index * stride;
        var fade = state.Part.Source.TrailFade;
        for (var segment = 0; segment < part.TrailCount - 1; segment++)
        {
            var a = state.Trail[at + (part.TrailHead - segment + stride) % stride];
            var b = state.Trail[at + (part.TrailHead - segment - 1 + stride) % stride];
            var along = b - a;
            if (along.LengthSquared() < 1e-6f)
            {
                continue;
            }

            var side = Vector3.Cross(along, camera - a);
            if (side.LengthSquared() < 1e-8f)
            {
                continue;
            }

            side = Vector3.Normalize(side) * halfWidth;
            var c0 = color with { W = MathF.Max(0f, color.W - fade * segment) };
            var c1 = color with { W = MathF.Max(0f, color.W - fade * (segment + 1)) };
            var v0 = segment / (float)(part.TrailCount - 1);
            var v1 = (segment + 1) / (float)(part.TrailCount - 1);
            AppendGradient(texture, blend, a + side, a - side, b - side, b + side, Decode(c0), Decode(c1), v0, v1);
        }
    }

    // The band a bone swept: quads between consecutive (base, tip) samples, U from base (0) to tip (1), V from the newest
    // (0) to the oldest (1). Each older sample is TrailFade (of 255) dimmer, colour and opacity alike, so additive bands
    // fade too.
    private void AppendBlade(PartState state, int index, Texture2D texture, VfxBlend blend, Vector4 tint)
    {
        ref readonly var part = ref state.Parts[index];
        var stride = state.Part.TrailStride;
        var points = stride / 2;
        var at = index * stride;
        var fade = state.Part.Source.TrailFade / 255f;
        for (var segment = 0; segment < part.TrailCount - 1; segment++)
        {
            var first = tint * MathF.Max(0f, 1f - fade * segment);
            var second = tint * MathF.Max(0f, 1f - fade * (segment + 1));
            var newer = at + (part.TrailHead - segment + points) % points * 2;
            var older = at + (part.TrailHead - segment - 1 + points) % points * 2;
            var v0 = segment / (float)(part.TrailCount - 1);
            var v1 = (segment + 1) / (float)(part.TrailCount - 1);
            AppendGradient(texture, blend, state.Trail[newer], state.Trail[newer + 1], state.Trail[older + 1], state.Trail[older], first, second, v0, v1);
        }
    }

    // A quad with one colour along its first edge (a, b) and another along its second (c, d); U across, V along.
    private void AppendGradient(Texture2D texture, VfxBlend blend, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 first, Vector4 second, float v0, float v1)
    {
        if (!BeginQuad(texture, blend, out var at))
        {
            return;
        }

        var parameters = new Vector2(VfxSystem.StripSoftness, 0f);
        _scratch[at] = new VfxVertex { Position = a, Color = first, TexCoord = new Vector2(0f, v0), Params = parameters };
        _scratch[at + 1] = new VfxVertex { Position = b, Color = first, TexCoord = new Vector2(1f, v0), Params = parameters };
        _scratch[at + 2] = new VfxVertex { Position = c, Color = second, TexCoord = new Vector2(1f, v1), Params = parameters };
        _scratch[at + 3] = new VfxVertex { Position = d, Color = second, TexCoord = new Vector2(0f, v1), Params = parameters };
    }

    private void DrawPartModels(VfxInstance instance, Matrix view, Matrix projection)
    {
        foreach (var state in instance.PartStates)
        {
            var compiled = state.Part;
            var source = compiled.Source;
            if (source.Shape != VfxPartShape.Model || compiled.Model is not { } model)
            {
                continue;
            }

            foreach (ref readonly var part in state.Parts.AsSpan())
            {
                if (part.Phase != PartPhase.Live)
                {
                    continue;
                }

                var color = compiled.Color;
                color.W *= state.Fade(part);
                if (color.W <= 0.5f)
                {
                    continue;
                }

                var scale = Matrix.CreateScale(part.Scale * state.PulseScale(part));
                var rotation = VfxAngles.Rotation(part.Angles);
                Matrix world;
                if (source.BoneWorld)
                {
                    world = scale * rotation * part.Attach;
                }
                else
                {
                    var facing = state.Turn;
                    if (source.FaceGoal && part.Velocity.LengthSquared() > 1e-4f)
                    {
                        var forward = Vector3.Normalize(part.Velocity);
                        facing = Matrix.CreateWorld(Vector3.Zero, forward, MathF.Abs(forward.Y) > 0.999f ? Vector3.Forward : Vector3.Up);
                    }

                    world = scale * rotation * ModelFront * facing * Matrix.CreateTranslation(part.Position);
                }

                var frame = (int)state.ModelFrame(part) % Math.Max(1, model.FrameCount);
                model.Draw(frame, world, view, projection, Decode(color), ModelBlend(compiled.Blend));
                LastModelCount++;
            }
        }
    }
}
