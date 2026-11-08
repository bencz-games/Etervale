using PristonTale.Client.Net.Presence;
using PristonTale.Effects.Vfx;
using PristonTale.Gameplay.Effects;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Skills;

namespace PristonTale.Tests;

// Skill looks on actors (tints, translucency, shield swaps, enlarged weapons, afterimages), per-hit impact sounds and
// effects, sound takes and the costume wings.
internal static class SkillLookTests
{
    private const uint Self = 1;
    private const uint Other = 2;
    private const uint Monster = 3;
    private const uint GroundPike = 0x03020001;
    private const uint VenomSpear = 0x03040001;
    private const uint Vanish = 0x03040002;
    private const uint Vague = 0x03080003;
    private const uint Expansion = 0x03020004;
    private const uint ShadowMaster = 0x03080004;
    private const uint ChainLance = 0x03040004;
    private const uint Raving = 0x02010003;
    private const uint SparkShield = 0x01040004;
    private const uint EnchantWeapon = 0x08020003;
    private const uint Distortion = 0x08080003;
    private const uint GrandCross = 0x05040004;
    private const uint DivinePiercing = 0x05080004;
    private const uint ShieldStrike = 0x06010001;
    private const uint Meteo = 0x08080004;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("frost and poison tints ramp in 70 Hz ticks, fade back and show only on harmful statuses", AfflictionsAsync);
        yield return ("vanish and vague bodies are translucent and shadowless; the own vague is clearer and overlaid", TranslucencyAsync);
        yield return ("distortion tints red-yellow and wobbles; a look that ends reports Normal once", DistortionAsync);
        yield return ("a guarded player blinks with 80 more red and blue light for 16 ticks of every 32 left, until the grace ends", GraceAsync);
        yield return ("rage glows red at each impact and fades by 70 a second", RageAsync);
        yield return ("shield swaps wait for the cast's hit; thrown shields move bone and glow during the cast", ShieldsAsync);
        yield return ("expansion grows a red translucent weapon copy; shadow master trails six or four afterimages", CastLooksAsync);
        yield return ("impact sounds follow the hit order, a random take or only the final hit; per-hit impact effects", ImpactsAsync);
        yield return ("effect sounds pick a random take and costume wings follow the worn body item", SoundsAndWingsAsync);
    }

    private static SkillRule Rule(uint code)
    {
        Assert.True(SkillRuleCatalog.TryGet(code, out var rule), $"rule {code:X8} is missing");
        return rule;
    }

    private static StatusEffectInfo Status(ulong entity, uint code, bool harmful, StatusEffectOp op = StatusEffectOp.Add) =>
        new(entity, Self, code, op, 1, 2, 0, harmful ? StatusEffectFlags.Harmful : (ushort)0, 60000, 60000, 5);

    private static SkillEventInfo Cast(uint caster, uint code, float durationMs = 1000f, float hitAtMs = 300f, int targets = 1) =>
        new(caster, Monster, code, SkillEventPhase.Start, 5, 0, durationMs, hitAtMs, default, 0f, targets, 0f);

    private static EntityLook Step(ActorLookBook book, StatusBook statuses, ref double nowMs, float seconds, ulong entity)
    {
        nowMs += seconds * 1000d;
        book.Update(seconds, nowMs, statuses, Self);
        return book.Looks.TryGetValue(entity, out var look) ? look : EntityLook.Normal;
    }

    private static Task AfflictionsAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        statuses.Apply(Status(Monster, GroundPike, harmful: true), now);
        var look = Step(book, statuses, ref now, 1f, Monster);
        Assert.True(MathF.Abs(look.Tint.Z * 255f - 70f) < 0.5f, $"frost after a second is {look.Tint.Z * 255f}, not 70 ticks");
        Assert.True(MathF.Abs(look.Opacity - (255f - 35f) / 255f) < 0.01f, $"frost opacity {look.Opacity}");
        look = Step(book, statuses, ref now, 3f, Monster);
        Assert.True(MathF.Abs(look.Tint.Z * 255f - 160f) < 0.5f && look.CastsShadow, $"frost did not stop at 160: {look.Tint.Z * 255f}");
        statuses.Apply(Status(Monster, GroundPike, harmful: true, StatusEffectOp.Remove), now);
        look = Step(book, statuses, ref now, 1f, Monster);
        Assert.True(MathF.Abs(look.Tint.Z * 255f - 90f) < 0.5f, $"frost did not count back: {look.Tint.Z * 255f}");
        look = Step(book, statuses, ref now, 2f, Monster);
        Assert.True(look.IsNormal, "frost did not fade to normal");

        statuses.Apply(Status(Monster, VenomSpear, harmful: true), now);
        look = Step(book, statuses, ref now, 2f, Monster);
        Assert.True(MathF.Abs(look.Tint.Y * 255f - 100f) < 0.5f && look.Tint.Z == 0f, $"poison tint {look.Tint}");

        // An ice enchant on its caster is a buff: no frost; the slow it puts on a monster is harmful: frost.
        statuses.Apply(Status(Self, EnchantWeapon, harmful: false), now);
        look = Step(book, statuses, ref now, 1f, Self);
        Assert.True(look.IsNormal, "a buff showed an affliction");
        statuses.Apply(Status(Monster, EnchantWeapon, harmful: true), now);
        look = Step(book, statuses, ref now, 0.5f, Monster);
        Assert.True(look.Tint.Z > 0f, "the enchant's slow showed no frost");
        return Task.CompletedTask;
    }

    private static Task TranslucencyAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        statuses.Apply(Status(Other, Vanish, harmful: false), now);
        var look = Step(book, statuses, ref now, 0.1f, Other);
        Assert.True(MathF.Abs(look.Opacity - 95f / 255f) < 0.01f && !look.CastsShadow && !look.Overlay, $"vanish look {look}");
        statuses.Apply(Status(Self, Vague, harmful: false), now);
        statuses.Apply(Status(Other, Vague, harmful: false), now);
        book.Update(0.1f, now += 100d, statuses, Self);
        var own = book.Looks[Self];
        var other = book.Looks[Other];
        Assert.True(MathF.Abs(own.Opacity - 195f / 255f) < 0.01f && own.Overlay && !own.CastsShadow, $"own vague {own}");
        Assert.True(MathF.Abs(other.Opacity - 95f / 255f) < 0.01f && other.Overlay, $"other vague {other}");
        return Task.CompletedTask;
    }

    private static Task DistortionAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        statuses.Apply(Status(Monster, Distortion, harmful: true), now);
        var look = Step(book, statuses, ref now, 0.1f, Monster);
        Assert.True(look.Wobble && MathF.Abs(look.Tint.X * 255f - 80f) < 0.5f && MathF.Abs(look.Tint.Y * 255f - 80f) < 0.5f, $"distortion {look}");
        statuses.Apply(Status(Monster, Distortion, harmful: true, StatusEffectOp.Remove), now);
        book.Update(0.1f, now += 100d, statuses, Self);
        Assert.True(book.Looks.TryGetValue(Monster, out var ended) && ended.IsNormal, "the ended look was not reported as Normal");
        book.Update(0.1f, now += 100d, statuses, Self);
        Assert.True(!book.Looks.ContainsKey(Monster), "a normal entity kept being reported");
        return Task.CompletedTask;
    }

    private static Task GraceAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        statuses.Apply(new StatusEffectInfo(Other, Other, PlayerGrace.StatusCode, StatusEffectOp.Add, 0, 0, 0, 0, 10_000, 10_000, 0), now);
        var (lit, plain) = (0, 0);
        for (var tick = 0; tick < 64; tick++)
        {
            var look = Step(book, statuses, ref now, 1f / 70f, Other);
            if (look.IsNormal)
            {
                plain++;
            }
            else
            {
                Assert.True(MathF.Abs(look.Tint.X * 255f - 80f) < 0.5f && MathF.Abs(look.Tint.Z * 255f - 80f) < 0.5f && look.Tint.Y == 0f
                    && look.Opacity == 1f, $"the blink looked {look}");
                lit++;
            }
        }

        Assert.True(lit is >= 30 and <= 34 && plain is >= 30 and <= 34, $"the grace blinked {lit} ticks lit and {plain} plain of 64");
        Step(book, statuses, ref now, 10f, Other);
        var after = Step(book, statuses, ref now, 0.1f, Other);
        Assert.True(after.IsNormal, "the blink outlived the grace");
        return Task.CompletedTask;
    }

    private static Task RageAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        var raving = Rule(Raving);
        book.Impact(Cast(Self, Raving) with { Phase = SkillEventPhase.Impact }, raving);
        var look = Step(book, statuses, ref now, 1f, Self);
        Assert.True(MathF.Abs(look.Tint.X * 255f - 80f) < 0.5f && MathF.Abs(look.Tint.Y * 255f + 40f) < 0.5f, $"rage after a second {look.Tint * 255f}");
        look = Step(book, statuses, ref now, 2f, Self);
        Assert.True(look.IsNormal, "rage did not fade out");
        return Task.CompletedTask;
    }

    private static Task ShieldsAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        statuses.Apply(Status(Self, SparkShield, harmful: false), now);
        book.Cast(Cast(Self, SparkShield, hitAtMs: 500f), Rule(SparkShield), now);
        var look = Step(book, statuses, ref now, 0.2f, Self);
        Assert.True(look.ShieldModel.Length == 0, "the swap showed before the cast's hit");
        look = Step(book, statuses, ref now, 0.4f, Self);
        Assert.True(look.ShieldModel == "weapons/Mc3_sparkShield.smd", $"swap model {look.ShieldModel}");

        book.Cast(Cast(Other, ShieldStrike, durationMs: 800f), Rule(ShieldStrike), now);
        look = Step(book, statuses, ref now, 0.3f, Other);
        Assert.True(look.ShieldBone == "Bip-shield" && MathF.Abs(look.ShieldGlow.X * 255f - 100f) < 0.5f, $"thrown shield {look}");
        look = Step(book, statuses, ref now, 0.6f, Other);
        Assert.True(look.ShieldBone.Length == 0, "the shield stayed on the throwing bone");
        return Task.CompletedTask;
    }

    private static Task CastLooksAsync()
    {
        var book = new ActorLookBook();
        var statuses = new StatusBook();
        var now = 0d;
        book.Cast(Cast(Self, Expansion, durationMs: 2000f), Rule(Expansion), now);
        // 0.2 s are 14 ticks: 14 x 8 + 64 of 256 larger, opacity -96 - 28.
        var look = Step(book, statuses, ref now, 0.2f, Self);
        Assert.True(MathF.Abs(look.WeaponScale - (1f + (14f * 8f + 64f) / 256f)) < 0.01f, $"weapon scale {look.WeaponScale}");
        Assert.True(MathF.Abs(look.WeaponOpacity - (255f - 124f) / 255f) < 0.01f && look.WeaponTint.X > 0.5f, $"weapon look {look}");
        look = Step(book, statuses, ref now, 1.5f, Self);
        Assert.True(MathF.Abs(look.WeaponScale - (1f + (62f * 256f / 100f + 64f) / 256f)) < 0.01f, $"rank 5 growth cap {look.WeaponScale}");

        book.Cast(Cast(Self, ShadowMaster, durationMs: 1500f), Rule(ShadowMaster), now);
        book.Cast(Cast(Other, ShadowMaster, durationMs: 1500f), Rule(ShadowMaster), now);
        book.Update(0.1f, now += 100d, statuses, Self);
        var own = book.Looks[Self];
        var other = book.Looks[Other];
        Assert.True(own.Afterimages == 6 && own.AfterimageLagFrames == 2f && MathF.Abs(own.AfterimageOpacity - 115f / 255f) < 0.01f
            && own.AfterimageOpacityStep < 0f, $"own afterimages {own}");
        Assert.True(other.Afterimages == 4 && other.AfterimageLagFrames == 4f && other.AfterimageOpacityStep == 0f, $"other afterimages {other}");
        book.Update(2f, now += 2000d, statuses, Self);
        Assert.True(book.Looks[Self].Afterimages == 0, "afterimages outlived the cast");
        return Task.CompletedTask;
    }

    private static Task ImpactsAsync()
    {
        var random = new Random(3);
        var chain = Rule(ChainLance);
        var played = Enumerable.Range(0, 4).Select(impact => SkillPresentation.ImpactSoundFor(chain, impact, 1, 5, random)).ToArray();
        Assert.True(played[0].EndsWith("ChainLance 01.wav", StringComparison.Ordinal) && played[2].EndsWith("ChainLance 03.wav", StringComparison.Ordinal)
            && played[3] == played[0], $"chain lance order {string.Join(", ", played)}");
        Assert.True(SkillPresentation.ImpactSoundFor(chain, 0, 0, 5, random).Length == 0, "a miss played a per-hit sound");
        var shadow = Rule(ShadowMaster);
        var takes = Enumerable.Range(0, 30).Select(_ => SkillPresentation.ImpactSoundFor(shadow, 0, 1, 5, random)).Distinct().Count();
        Assert.True(takes == 3, $"shadow master played {takes} of 3 takes");
        var piercing = Rule(DivinePiercing);
        var hits = SkillFormulas.HitCount(piercing, 5);
        // The hit count every view shows is the one the motion's timing strikes, ranked frames and loops included.
        foreach (var rule in SkillRuleCatalog.All)
        {
            foreach (var rank in new[] { 1, 5, 10 })
            {
                var struck = SkillFormulas.Timing(rule, rank, 1f, (500f, 250f)).HitsAtMs.Length;
                Assert.True(struck == 0 || SkillFormulas.HitCount(rule, rank) == struck,
                    $"{rule.Code:X8} rank {rank}: {SkillFormulas.HitCount(rule, rank)} hits counted, {struck} struck");
            }
        }
        Assert.True(SkillPresentation.ImpactSoundFor(piercing, hits - 2, 1, 5, random).Length == 0
            && SkillPresentation.ImpactSoundFor(piercing, hits - 1, 1, 5, random).Length > 0, "divine piercing did not sound on its final hit only");
        var cross = Rule(GrandCross);
        var start = Cast(Self, GrandCross);
        Assert.True(SkillPresentation.ImpactEffectOf(cross, start, 0) == "vfx.knight.grandcross.hit"
            && SkillPresentation.ImpactEffectOf(cross, start, 1) == "vfx.knight.grandcross.hit2"
            && SkillPresentation.ImpactEffectOf(cross, start, 5) == "vfx.knight.grandcross.hit2", "grand cross per-hit effects");
        // Meteo's meteors fall, and sound, on both of its clip's events.
        var meteo = Rule(Meteo);
        Assert.True(SkillPresentation.ImpactEffectOf(meteo, start, 0) == "vfx.magician.meteo.fall" && SkillPresentation.ImpactEffectOf(meteo, start, 1) == "vfx.magician.meteo.fall"
            && SkillPresentation.ImpactSoundFor(meteo, 0, 1, 5, random).Length > 0 && SkillPresentation.ImpactSoundFor(meteo, 1, 1, 5, random).Length > 0,
            "meteo's meteors or sound are missing on one of its two strikes");
        return Task.CompletedTask;
    }

    private static Task SoundsAndWingsAsync()
    {
        var sound = VfxDefinition.ParseFile("""
            [{ "name": "sounded", "sounds": [{ "path": "wav/a 1.wav", "variants": ["wav/a 2.wav", "wav/a 3.wav"] }] }]
            """).Single().Sounds.Single();
        Assert.True(sound.AllPaths.SequenceEqual(["wav/a 1.wav", "wav/a 2.wav", "wav/a 3.wav"]), "sound takes lost");
        Assert.True(CostumeWings.EffectFor("CA166") == "vfx.costume.wing.blue" && CostumeWings.EffectFor("CA269") == "vfx.costume.wing.blue"
            && CostumeWings.EffectFor("CA185") == "vfx.costume.wing.white" && CostumeWings.EffectFor("ca292") == "vfx.costume.wing.red",
            "costume wing colours");
        Assert.True(CostumeWings.EffectFor("CA170").Length == 0 && CostumeWings.EffectFor("DA166").Length == 0 && CostumeWings.EffectFor("").Length == 0,
            "a body item without wings showed some");
        return Task.CompletedTask;
    }
}
