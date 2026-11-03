using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonMaterialRuntime
{
    public PristonMaterialRuntime(
        Texture2D[] textures,
        bool twoSided,
        bool transparent,
        bool alphaTest,
        uint blendType,
        float transparency,
        uint textureFormState,
        uint frameMask,
        uint shiftFrameSpeed,
        uint animationFrame,
        bool selfIllum,
        bool invisible,
        bool water,
        bool blinking,
        float windAmplitudeX,
        float windAmplitudeZ,
        PristonStageOperation baseOperation = PristonStageOperation.Modulate,
        PristonMaterialLayer[] layers = null,
        PristonMaterialDescription description = null)
    {
        Textures = textures;
        Description = description ?? PristonMaterialDescription.Default;
        BaseOperation = baseOperation;
        Layers = layers ?? [];
        TwoSided = twoSided;
        Transparent = transparent;
        AlphaTest = alphaTest;
        BlendType = blendType;
        Transparency = transparency;
        TextureFormState = textureFormState;
        FrameMask = frameMask;
        ShiftFrameSpeed = shiftFrameSpeed;
        AnimationFrame = animationFrame;
        SelfIllum = selfIllum;
        Invisible = invisible;
        Water = water;
        Blinking = blinking;
        WindAmplitudeX = windAmplitudeX;
        WindAmplitudeZ = windAmplitudeZ;
        var textureHash = textures.Length == 0 ? 0 : textures[0].GetHashCode() & 0xFFFF;
        SortKey = ((ulong)(alphaTest ? 1u : 0u) << 48)
            | ((ulong)(transparent ? 1u : 0u) << 47)
            | ((ulong)(twoSided ? 1u : 0u) << 46)
            | ((ulong)(blendType & 0xFFFF) << 16)
            | (uint)textureHash;
    }

    public Texture2D[] Textures { get; }
    public bool TwoSided { get; }
    public bool Transparent { get; }

    // The material model's inputs (PristonMaterialAdapter).
    public PristonMaterialDescription Description { get; }

    // The second texture layer the material model uses, if any.
    public PristonMaterialLayer Layer => Layers.Length > 0 ? Layers[0] : null;

    // How the base texture combined with the vertex light, and the texture layers above it, as authored.
    public PristonStageOperation BaseOperation { get; }
    public PristonMaterialLayer[] Layers { get; }
    public int StageCount => 1 + Layers.Length;
    public bool HasLayers => Layers.Length > 0;

    public bool Invisible { get; }

    public bool Water { get; }

    public bool Blinking { get; }

    public float WindAmplitudeX { get; }
    public float WindAmplitudeZ { get; }
    public bool Wind => WindAmplitudeX != 0f || WindAmplitudeZ != 0f;

    // The texture form state that replaces a layer's UVs with a camera-space reflection sphere map.
    internal const uint ReflexFormState = 5;

    public bool StageReflex(int stage) =>
        stage == 0 ? TextureFormState == ReflexFormState : Layers[stage - 1].FormState == ReflexFormState;

    public bool Reflex
    {
        get
        {
            for (var stage = 0; stage < StageCount; stage++)
            {
                if (StageReflex(stage))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool AlphaTest { get; }
    public uint BlendType { get; }
    public float Transparency { get; }
    public float AlphaCutoff => 60f / 255f;

    // Global transparency at/below which a surface counts as visually solid (writes depth /
    // casts a shadow), as the original game decided depth writes.
    internal const float SolidTransparencyThreshold = 0.2f;

    // The alpha blend (1) is not a late blend in the original game.
    private bool IsLateBlend => PristonBlendType.IsLate(BlendType);

    public bool CastsShadow =>
        !IsLateBlend && (AlphaTest || Transparency <= SolidTransparencyThreshold);

    // Only the global transparency decides depth writes, so additive
    // and coloured blends with zero transparency still occlude what is drawn after them, as in the original.
    public bool WritesDepth => Transparency <= SolidTransparencyThreshold;

    public bool SelfIllum { get; }
    public uint TextureFormState { get; }
    public uint FrameMask { get; }
    public uint ShiftFrameSpeed { get; }
    public uint AnimationFrame { get; }
    public BlendState BlendState
    {
        get
        {
            if (AlphaTest && Transparency <= SolidTransparencyThreshold)
                return BlendState.Opaque;

            // These follow the original blend table; several modes are not alpha blends.
            return BlendType switch
            {
                PristonBlendType.Alpha => BlendState.NonPremultiplied,    // SrcAlpha, InvSrcAlpha
                PristonBlendType.Color => BlendColor,                     // SrcColor, InvSrcColor
                PristonBlendType.Shadow => BlendShadow,                   // Zero, SrcColor (darken)
                PristonBlendType.Lamp => BlendState.Additive,             // SrcAlpha, One
                PristonBlendType.AddColor => BlendAddColor,               // SrcColor, One (additive)
                PristonBlendType.InverseShadow => BlendInvShadow,         // Zero, InvSrcColor
                _ => Transparent ? BlendState.NonPremultiplied : BlendState.Opaque,
            };
        }
    }

    // The alpha channel must remain One/InverseSourceAlpha for offscreen composition.
    private static readonly BlendState BlendColor = MakeBlend("PristonBlendColor", Blend.SourceColor, Blend.InverseSourceColor);
    private static readonly BlendState BlendShadow = MakeBlend("PristonBlendShadow", Blend.Zero, Blend.SourceColor);
    private static readonly BlendState BlendAddColor = MakeBlend("PristonBlendAddColor", Blend.SourceColor, Blend.One);
    private static readonly BlendState BlendInvShadow = MakeBlend("PristonBlendInvShadow", Blend.Zero, Blend.InverseSourceColor);

    private static BlendState MakeBlend(string name, Blend colorSource, Blend colorDestination) => new()
    {
        Name = name,
        ColorSourceBlend = colorSource,
        ColorDestinationBlend = colorDestination,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add,
    };

    public ulong SortKey { get; }

    public Texture2D SelectTexture(uint renderTime)
    {
        if (Textures.Length == 1)
            return Textures[0];

        uint frame;
        if (AnimationFrame == 0x100)
        {
            var shift = (int)Math.Min(ShiftFrameSpeed, 31);
            frame = (renderTime >> shift) & FrameMask;
        }
        else
        {
            frame = AnimationFrame;
        }

        return Textures[(int)(frame % Textures.Length)];
    }
}
