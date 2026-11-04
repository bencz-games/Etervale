using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.Effects;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class HeldItemGlowTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("held glow: a unique's file colour, the +4..+20 aging colours with their mix textures, the class-up setting", RulesAsync);
        yield return ("held glow pulse: the light rises and falls each 2^blink ms, a steady glow and the alpha and scale offsets", PulseAsync);
        yield return ("a player's held items glow in its appearance and in its description on the wire", WireAsync);
        yield return ("class-up lights circle a point along the weapon by its kind", ClassUpAsync);
    }

    private static ItemDefinition Weapon(string key, ItemGlow glow = default, int effect = 0) =>
        new(ItemCodes.Parse(key), key, ItemKind.Sword) { CatalogKey = key, Glow = glow, DisplayEffect = effect, EquipSlot = EquipmentSlot.Weapon };

    private static Task RulesAsync()
    {
        var unique = HeldItemGlow.For(new ItemInstance(Weapon("WS210", new ItemGlow(220, 80, 0, 0, 9, 0f))));
        Assert.True(unique is { R: 220, G: 80, B: 0, Blink: 9, Mix: -1, ClassUp: false } && unique.Glows, $"a unique's glow differs: {unique}");
        Assert.True(HeldItemGlow.For(new ItemInstance(Weapon("WS201"))).IsNone && HeldItemGlow.For(null).IsNone, "a plain weapon glows");

        var three = HeldItemGlow.For(new ItemInstance(Weapon("WS201")) { UpgradeLevel = 3 });
        var four = HeldItemGlow.For(new ItemInstance(Weapon("WS201")) { UpgradeLevel = 4 });
        var seven = HeldItemGlow.For(new ItemInstance(Weapon("WS201")) { UpgradeLevel = 7 });
        var twenty = HeldItemGlow.For(new ItemInstance(Weapon("WS201", new ItemGlow(50, 100, 255, 0, 9, 0f))) { UpgradeLevel = 20 });
        Assert.True(three.IsNone && four is { R: 100, G: 40, B: 90, Blink: 9, Mix: -1 }, $"the +3 or +4 glow differs: {three} {four}");
        Assert.True(seven is { R: 0, G: 50, B: 140, Mix: 0, MixScroll: 9 } && seven.MixTexture.EndsWith("mixS_01.bmp", StringComparison.Ordinal),
            $"the +7 glow differs: {seven}");
        Assert.True(twenty is { R: 240, G: 240, B: 240, Mix: 5, MixScroll: 14 } && twenty.MixTexture.EndsWith("mixM_01.bmp", StringComparison.Ordinal),
            $"the +20 glow does not replace the unique's colour: {twenty}");

        // A file colour that is an aging colour brings that colour's mix texture.
        var borrowed = HeldItemGlow.For(new ItemInstance(Weapon("WS211", new ItemGlow(150, 10, 0, 0, 9, 0f))));
        var classUp = HeldItemGlow.For(new ItemInstance(Weapon("WS212", effect: 1)));
        Assert.True(borrowed is { Mix: 3, MixScroll: 12 } && classUp is { ClassUp: true, Glows: false } && !classUp.IsNone,
            $"a borrowed colour or the class-up setting differs: {borrowed} {classUp}");
        return Task.CompletedTask;
    }

    private static Task PulseAsync()
    {
        var glow = new HeldItemGlow(220, 80, 0, 0, 9, 0f, -1, 0, false);
        var dark = glow.Sample(511);
        var bright = glow.Sample(0);
        var rising = glow.Sample(512 + 511);
        Assert.True(dark.R == 0f && Math.Abs(bright.R - ((511 * 220) >> 9) / 255f) < 1e-6f && Math.Abs(rising.R - bright.R) < 1e-6f
            && bright.Opacity == 1f && bright.Scale == 1f, $"the pulse differs: {dark} {bright} {rising}");
        Assert.True(Math.Abs(glow.Sample(256).R - ((255 * 220) >> 9) / 255f) < 1e-6f, "the pulse is not linear");

        var steady = new HeldItemGlow(100, 0, 0, -51, -1, 0f, -1, 0, false).Sample(12345);
        Assert.True(Math.Abs(steady.R - 100 / 255f) < 1e-6f && Math.Abs(steady.Opacity - 204 / 255f) < 1e-6f, $"a steady glow differs: {steady}");
        var fading = new HeldItemGlow(0, 0, 100, -255, 9, 1.5f, -1, 0, false).Sample(0);
        var translucent = new HeldItemGlow(0, 0, 100, 128, 9, -2f, -1, 0, false).Sample(0);
        Assert.True(fading.Opacity < 0.01f && Math.Abs(fading.Scale - 255 * 1.5f / 256f) < 1e-5f
            && Math.Abs(translucent.Opacity - 127 / 255f) < 1e-6f && translucent.Scale == 2f, $"the alpha or scale offsets differ: {fading} {translucent}");
        Assert.True(HeldItemGlow.None.Sample(5) == HeldGlowSample.Plain, "no glow changed the item");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Shining", 80);
        character.Equip(EquipmentSlot.Weapon, new ItemInstance(Weapon("WS213", new ItemGlow(0, 180, 255, 0, 9, 0f), 1)) { UpgradeLevel = 9 });
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        var appearance = player.Appearance;
        Assert.True(appearance.WeaponGlow is { R: 150, G: 60, B: 0, Mix: 2, ClassUp: true } && appearance.OffHandGlow.IsNone,
            $"the appearance does not carry the +9 class-up glow: {appearance.WeaponGlow}");

        var glow = new DescribedGlow(9, 150, 60, 0, -20, 1.5f, 2, 10, true);
        var looks = new DescribedPlayer(PlayerClass.Mechanician, 3, 1, 4, "char/tmABCD/tmh-b01.inf", 1, 2, 3, false, false, 0, 0) { WeaponGlow = glow };
        var packet = new EntityDescriptionPacket(7, 4, EntityKind.Player, 80, 3, 1f, "Shining", "char/tmABCD/b001.ini", looks);
        var buffer = new byte[EntityDescriptionPacket.MaximumSize];
        var read = EntityDescriptionPacket.Read(buffer.AsSpan(0, packet.Write(buffer)));
        Assert.True(read.Player is { } described && described.WeaponGlow == glow && described.OffHandGlow == DescribedGlow.None,
            "the glows did not round-trip");
        return Task.CompletedTask;
    }

    private static Task ClassUpAsync()
    {
        Assert.True(WeaponOrbitLights.Center("WA105", 40f, false, false) == new Vector3(0f, 20f, 0f), "an axe's lights are not at its middle");
        Assert.True(WeaponOrbitLights.Center("WS205", 40f, false, false) == new Vector3(0f, 30f, 0f)
            && WeaponOrbitLights.Center("WS205", 10f, false, false) == new Vector3(0f, 8f, 0f), "a sword's lights are not near its tip");
        Assert.True(WeaponOrbitLights.Center("WT105", 40f, false, false) == new Vector3(0f, -16f, 0f)
            && WeaponOrbitLights.Center("WC105", 10f, false, false) == new Vector3(20f, 0f, 0f), "a javelin's or claw's lights differ");
        Assert.True(WeaponOrbitLights.Center("WS105", 40f, true, false) == new Vector3(30f, 0f, 0f), "a left-hand bow's lights differ");
        var leaning = WeaponOrbitLights.Center("WP105", 40f, false, true);
        Assert.True(Math.Abs(leaning.Length() - 20f) < 1e-4f && leaning.Z > 0f, $"a two-handed weapon's point does not lean: {leaning}");
        return Task.CompletedTask;
    }
}
