using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Effects;

// What an entity's actors look like this frame because of skills: a signed light tint and opacity of the body, the
// distortion wobble, the ground shadow, the vague overlay, a shield model drawn instead of the held shield, an enlarged
// copy of the weapon, and afterimages trailing the body.
public readonly record struct EntityLook(
    Vector3 Tint,
    float Opacity,
    bool Wobble,
    bool CastsShadow,
    bool Overlay,
    string ShieldModel,
    float WeaponScale,
    float WeaponOpacity,
    Vector3 WeaponTint,
    int Afterimages,
    float AfterimageLagFrames,
    float AfterimageOpacity,
    float AfterimageOpacityStep,
    string ShieldBone,
    Vector3 ShieldGlow)
{
    public static EntityLook Normal => new(Vector3.Zero, 1f, false, true, false, "", 0f, 0f, Vector3.Zero, 0, 0f, 0f, 0f, "", Vector3.Zero);

    public bool IsNormal => this == Normal;
}

// Tracks, per entity, the skill looks the original draws: frost, poison and rage tints that ramp in 70 Hz ticks, the
// distortion, vanish and vague bodies, shield swaps while a status lasts, the enlarged weapon and afterimages of a cast,
// and a guarded player's blink (PlayerGrace: 80 more red and blue light for 16 ticks out of every 32 left). Colour
// offsets are the original's 0..255 light units (255 = one white light), opacity the original's alpha offset over 255.
public sealed class ActorLookBook
{
    private const float FrostPeak = 160f;
    private const float PoisonPeak = 100f;
    private const float VanishAlpha = -160f;
    private const float OwnVagueAlpha = -60f;
    private const float DistortionLight = 80f;
    private const float GraceLight = 80f;
    private const int GraceBlinkTicks = 16;
    private const float GrowthStartTicks = 0f;
    // An enlarged weapon starts a quarter larger and grows 8/256 a tick up to the rank's percent; its opacity falls from
    // -96 by 2 a tick to -192, and its red light is 128.
    private const float GrowthBase = 64f;
    private const float GrowthRate = 8f;
    private const float GrowthAlphaStart = -96f;
    private const float GrowthAlphaRate = 2f;
    private const float GrowthAlphaFloor = -192f;
    private const float GrowthRed = 128f;
    // Afterimages: the local player sees six, two frames apart, from -140 by -20; other players four, four frames apart, at -160.
    private const int OwnAfterimageLimit = 6;
    private const int OtherAfterimageLimit = 4;

    private readonly Dictionary<ulong, State> _states = new();
    private readonly List<ulong> _finished = new();

    // Looks to apply this frame, including a last Normal for entities whose look just ended.
    public Dictionary<ulong, EntityLook> Looks => _looks;

    private readonly Dictionary<ulong, EntityLook> _looks = new();

    // A cast started: its weapon growth and afterimages follow the cast's clip.
    public void Cast(SkillEventInfo skillEvent, SkillRule rule, double nowMs)
    {
        if (rule.ShieldModel.Length > 0)
        {
            // The held shield shows through the cast until its motion event, when the status starts.
            StateOf(skillEvent.CasterId).ShieldHeldUntilMs = nowMs + skillEvent.HitAtMs;
        }

        if (rule.WeaponGrowth.Length == 0 && rule.Afterimages == 0 && rule.CastShieldBone.Length == 0)
        {
            return;
        }

        var state = StateOf(skillEvent.CasterId);
        state.CastRule = rule;
        state.CastRank = skillEvent.Rank;
        state.CastStartMs = nowMs;
        state.CastEndMs = nowMs + Math.Max(skillEvent.DurationMs, 1f);
    }

    public void Impact(SkillEventInfo skillEvent, SkillRule rule)
    {
        if (rule.RageGlow > 0)
        {
            var state = StateOf(skillEvent.CasterId);
            state.Rage = Math.Max(state.Rage, rule.RageGlow);
        }
    }

    public void Cancel(ulong casterId)
    {
        if (_states.TryGetValue(casterId, out var state))
        {
            state.CastRule = null;
        }
    }

    public void Clear()
    {
        _states.Clear();
        _looks.Clear();
    }

    public void Update(float seconds, double nowMs, StatusBook statuses, ulong selfId)
    {
        _looks.Clear();
        foreach (var (entity, entry) in statuses.All())
        {
            if (entry.Code == PlayerGrace.StatusCode
                || SkillRuleCatalog.TryGet(entry.Code, out var rule) && (rule.StatusLook != SkillLook.None || rule.ShieldModel.Length > 0))
            {
                StateOf(entity);
            }
        }

        var ticks = seconds * GameUnits.TicksPerSecond;
        _finished.Clear();
        foreach (var (entity, state) in _states)
        {
            var look = Evaluate(entity, state, ticks, nowMs, statuses.For(entity), entity == selfId);
            if (look.IsNormal && state.Idle)
            {
                _finished.Add(entity);
            }

            _looks[entity] = look;
        }

        foreach (var entity in _finished)
        {
            _states.Remove(entity);
        }
    }

    private EntityLook Evaluate(ulong entity, State state, float ticks, double nowMs, List<StatusEntry> entries, bool self)
    {
        var frost = false;
        var poison = false;
        var distortion = false;
        var vanish = false;
        var vague = false;
        var shield = "";
        var grace = 0f;
        var graced = false;
        foreach (var entry in entries)
        {
            if (entry.Code == PlayerGrace.StatusCode)
            {
                var left = (int)(entry.RemainingMs(nowMs) * GameUnits.TicksPerSecond / 1000d);
                graced = left > 0;
                grace = graced && (left & GraceBlinkTicks) == 0 ? GraceLight : 0f;
                continue;
            }

            if (!SkillRuleCatalog.TryGet(entry.Code, out var rule))
            {
                continue;
            }

            if (rule.ShieldModel.Length > 0 && !entry.Harmful && nowMs >= state.ShieldHeldUntilMs)
            {
                shield = rule.ShieldModel;
            }

            switch (rule.StatusLook)
            {
                case SkillLook.Frost when entry.Harmful:
                    frost = true;
                    break;
                case SkillLook.Poison when entry.Harmful:
                    poison = true;
                    break;
                case SkillLook.Distortion when entry.Harmful:
                    distortion = true;
                    break;
                case SkillLook.Vanish:
                    vanish = true;
                    break;
                case SkillLook.Vague:
                    vague = true;
                    break;
            }
        }

        // A distortion replaces the frost of an earlier ice slow.
        state.Frost = Ramp(state.Frost, frost && !distortion, FrostPeak, ticks);
        state.Poison = Ramp(state.Poison, poison, PoisonPeak, ticks);
        state.Rage = MathF.Max(0f, state.Rage - ticks);
        var ice = MathF.Abs(state.Frost);
        var red = state.Rage + (distortion ? DistortionLight : 0f) + grace;
        var green = -state.Rage / 2f + MathF.Abs(state.Poison) + (distortion ? DistortionLight : 0f);
        var alpha = -ice / 2f;
        if (vanish || vague)
        {
            alpha += vague && self ? OwnVagueAlpha : VanishAlpha;
        }

        var look = EntityLook.Normal with
        {
            Tint = new Vector3(red, green, ice + grace) / 255f,
            Opacity = Math.Clamp((255f + alpha) / 255f, 0f, 1f),
            Wobble = distortion,
            CastsShadow = !vanish && !vague,
            Overlay = vague,
            ShieldModel = shield
        };

        if (state.CastRule is { } cast)
        {
            if (nowMs >= state.CastEndMs)
            {
                state.CastRule = null;
            }
            else
            {
                look = WithCast(look, cast, state.CastRank, (float)(nowMs - state.CastStartMs) / 1000f * GameUnits.TicksPerSecond, self);
            }
        }

        state.Idle = state.CastRule is null && state.Frost == 0f && state.Poison == 0f && state.Rage == 0f && !graced;
        return look;
    }

    private static EntityLook WithCast(EntityLook look, SkillRule rule, int rank, float ticks, bool self)
    {
        if (rule.CastShieldBone.Length > 0)
        {
            var glow = rule.CastShieldGlow.Length == 3 ? new Vector3(rule.CastShieldGlow[0], rule.CastShieldGlow[1], rule.CastShieldGlow[2]) / 255f : Vector3.Zero;
            look = look with { ShieldBone = rule.CastShieldBone, ShieldGlow = glow };
        }

        if (rule.WeaponGrowth.Length > 0)
        {
            var percent = rule.At(rule.WeaponGrowth, rank);
            var size = MathF.Min((ticks - GrowthStartTicks) * GrowthRate, percent * GameUnits.FixedScale / 100f) + GrowthBase;
            var alpha = MathF.Max(GrowthAlphaFloor, GrowthAlphaStart - GrowthAlphaRate * ticks);
            look = look with
            {
                WeaponScale = 1f + size / GameUnits.FixedScale,
                WeaponOpacity = (255f + alpha) / 255f,
                WeaponTint = new Vector3(GrowthRed / 255f, 0f, 0f)
            };
        }

        if (rule.Afterimages > 0)
        {
            look = self
                ? look with
                {
                    Afterimages = Math.Min(rule.Afterimages, OwnAfterimageLimit),
                    AfterimageLagFrames = 2f,
                    AfterimageOpacity = (255f - 140f) / 255f,
                    AfterimageOpacityStep = -20f / 255f
                }
                : look with
                {
                    Afterimages = Math.Min(rule.Afterimages, OtherAfterimageLimit),
                    AfterimageLagFrames = 4f,
                    AfterimageOpacity = (255f - 160f) / 255f,
                    AfterimageOpacityStep = 0f
                };
        }

        return look;
    }

    // The original's signed ramp: up by one a tick to the peak while active; once over it turns negative and counts back
    // to zero at the same rate.
    private static float Ramp(float value, bool active, float peak, float ticks)
    {
        if (active)
        {
            return value < 0f ? MathF.Min(peak, -value + ticks) : MathF.Min(peak, MathF.Max(1f, value + ticks));
        }

        if (value > 0f)
        {
            value = -value;
        }

        return MathF.Min(0f, value + ticks);
    }

    private State StateOf(ulong entity)
    {
        if (!_states.TryGetValue(entity, out var state))
        {
            state = new State();
            _states.Add(entity, state);
        }

        return state;
    }

    private sealed class State
    {
        public float Frost;
        public float Poison;
        public float Rage;
        public SkillRule CastRule;
        public int CastRank;
        public double CastStartMs;
        public double CastEndMs;
        public double ShieldHeldUntilMs;
        public bool Idle;
    }
}
