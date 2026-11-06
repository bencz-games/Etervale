using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.Items.Mixing;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class MixingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("mix formulas match the item family and the exact sheltom counts and add their bonuses", FormulaAsync);
        yield return ("a mix spends the sheltoms and the item price, fails without a formula and refuses aged or mixed items", MixAsync);
        yield return ("two seals of the item's level band rebuild it under the same identity", ReconstructAsync);
        yield return ("the alchemist turns each sheltom up to the eleventh into its force orb for its price", RefineAsync);
        yield return ("mixed copies glow from Devine up and the dialog carries the price setting", PresentationAsync);
    }

    private static ItemTemplateRecord Record(string code) => ItemRecords.Blank(code);

    private static ItemDefinition Def(string code, Func<ItemTemplateRecord, ItemTemplateRecord>? change = null) =>
        WorldDataFactory.ToItemDefinition(change is null ? Record(code) : change(Record(code)));

    private static readonly ItemDefinition AxeDef = Def("WA101", record => record with
    {
        Kind = (int)ItemKind.Axe, Price = 1000, DamageMinLow = 10, DamageMinHigh = 10, DamageMaxLow = 20, DamageMaxHigh = 20,
        AttackRatingMin = 50, AttackRatingMax = 50, DurabilityMin = 200, DurabilityMax = 200, ReqLevel = 10
    });

    private static ItemInstance Sheltom(int ordinal) => new(Def($"OS1{ordinal:00}", record => record with { Kind = (int)ItemKind.Sheltom }));

    private static PlayerCharacter Fighter() => new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Mixer", 50);

    private static List<Guid> Place(PlayerCharacter character, int from, params ItemInstance[] items)
    {
        var ids = new List<Guid>();
        foreach (var item in items)
        {
            Assert.True(character.Inventory.TryPlace(item, from++), "a material did not fit");
            ids.Add(item.InstanceId);
        }

        return ids;
    }

    private static Task FormulaAsync()
    {
        Assert.True(MixFormulas.All.Count == 103, "the formula count differs");
        var organic = MixFormulas.Find("WA1", [5, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.True(organic is { Bonuses: [{ Stat: MixStat.Organic, Value: 5f, Percent: false }] }, "the first weapon formula differs");
        Assert.True(MixFormulas.Find("WA1", [4, 0, 0, 0, 0, 0, 0, 0, 0]) is null && MixFormulas.Find("OA1", [5, 0, 0, 0, 0, 0, 0, 0, 0]) is null,
            "a formula matched other counts or another family");

        var axe = new ItemInstance(AxeDef);
        MixFormulas.Apply(axe, organic!, [5, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.True(axe.Roll.Resistances.Bionic == 5 && axe.MixSheltom == 1 && axe.MixStats == MixStat.Organic && axe.Mixed, "the bonus was not added");

        var percent = new MixFormula(["WA1"], new int[9], [new(MixStat.DamageMax, 10f, true), new(MixStat.Critical, 2f, false)]);
        var other = new ItemInstance(AxeDef);
        MixFormulas.Apply(other, percent, [0, 0, 0, 0, 0, 0, 1, 0, 0]);
        Assert.True(other.Roll.DamageMax == 22 && other.Critical == 2 && other.MixSheltom == 7, "percent and whole bonuses differ");
        return Task.CompletedTask;
    }

    private static Task MixAsync()
    {
        var character = Fighter();
        character.TryChangeMoney(100_000);
        var axe = new ItemInstance(AxeDef);
        Place(character, 0, axe);
        var ids = Place(character, 12, Sheltom(1), Sheltom(1), Sheltom(1), Sheltom(1), Sheltom(1));
        var money = character.Money;
        Assert.True(MixingWork.TryMix(character, axe.InstanceId, ids, new FixedRng(), out var work, out var price) == NpcServiceStatus.Ok
            && work.Result == (byte)MixResult.Mixed && price == 1000 && character.Money == money - 1000, "the mix was not carried out");
        var mixed = character.Inventory[0];
        Assert.True(mixed.Roll.Resistances.Bionic == 5 && mixed.MixSheltom == 1 && ids.All(id => character.Inventory.Find(id) < 0),
            "the mix did not change the item or keep the sheltoms");
        Assert.True(!MixingWork.Mixable(mixed) && !AgingRules.Ageable(mixed), "a mixed item could be worked again");

        var plain = Fighter();
        plain.TryChangeMoney(100_000);
        var other = new ItemInstance(AxeDef);
        Place(plain, 0, other);
        var lone = Place(plain, 12, Sheltom(2));
        Assert.True(MixingWork.TryMix(plain, other.InstanceId, lone, new FixedRng(), out var failed, out _) == NpcServiceStatus.Ok
            && failed.Result == (byte)MixResult.Failed && plain.Inventory[0].MixSheltom == 0 && plain.Inventory.Find(lone[0]) < 0,
            "a mix without a formula did not fail and spend the sheltom");

        var aged = new ItemInstance(AxeDef) { UpgradeLevel = 1 };
        Assert.True(!MixingWork.Mixable(aged), "an aged item was mixable");
        var poor = Fighter();
        var cheap = new ItemInstance(AxeDef);
        Place(poor, 0, cheap);
        var five = Place(poor, 12, Sheltom(1), Sheltom(1), Sheltom(1), Sheltom(1), Sheltom(1));
        Assert.True(MixingWork.TryMix(poor, cheap.InstanceId, five, new FixedRng(), out _, out _, new MixingSettings(0)) == NpcServiceStatus.Ok,
            "a free mix needed gold");
        return Task.CompletedTask;
    }

    private static Task ReconstructAsync()
    {
        var character = Fighter();
        character.TryChangeMoney(100_000);
        var axe = new ItemInstance(AxeDef);
        Place(character, 0, axe);
        var seal = Def("SE103");
        var seals = Place(character, 12, new ItemInstance(seal), new ItemInstance(seal));
        Assert.True(MixingWork.TryMix(character, axe.InstanceId, seals[..1], new FixedRng(), out _, out _) == NpcServiceStatus.WrongMaterials,
            "one seal rebuilt the item");
        Assert.True(MixingWork.TryMix(character, axe.InstanceId, seals, new FixedRng(), out var work, out _) == NpcServiceStatus.Ok
            && work.Result == (byte)MixResult.Reconstructed && work.ItemId == axe.InstanceId && character.Inventory.Find(axe.InstanceId) == 0,
            "the item was not rebuilt under its identity");

        var high = Fighter();
        high.TryChangeMoney(100_000);
        var strong = new ItemInstance(Def("WA150", record => record with { Kind = (int)ItemKind.Axe, Price = 10, ReqLevel = 60 }));
        Place(high, 0, strong);
        var young = Place(high, 12, new ItemInstance(seal), new ItemInstance(seal));
        Assert.True(MixingWork.TryMix(high, strong.InstanceId, young, new FixedRng(), out _, out _) == NpcServiceStatus.WrongMaterials,
            "a seal rebuilt an item above its level band");
        return Task.CompletedTask;
    }

    private static Task RefineAsync()
    {
        var orbs = Enumerable.Range(1, 11).Select(rank => Def($"FO1{rank:00}", record => record with { Kind = (int)ItemKind.ForceOrb })).ToList();
        var items = new ItemCatalog(orbs);
        var character = Fighter();
        character.TryChangeMoney(100_000);
        var ids = Place(character, 0, Sheltom(1), Sheltom(1), Sheltom(11));
        var money = character.Money;
        Assert.True(MixingWork.TryRefine(character, ids, items, new FixedRng(), out var work, out var price) == NpcServiceStatus.Ok
            && work.Result == (byte)MixResult.Refined && price == 200 * 2 + 50000 && character.Money == money - price, "the refining differs");
        var made = Enumerable.Range(0, character.Inventory.Capacity).Select(index => character.Inventory[index]).Where(static item => item is not null)
            .Select(static item => item!.Definition.CatalogKey).Order().ToArray();
        Assert.True(made.SequenceEqual(["FO101", "FO101", "FO111"]), "the orbs differ: " + string.Join(",", made));
        var twelfth = Place(character, 20, Sheltom(12));
        Assert.True(MixingWork.TryRefine(character, twelfth, items, new FixedRng(), out _, out _) == NpcServiceStatus.WrongMaterials,
            "the twelfth sheltom was refined");
        return Task.CompletedTask;
    }

    private static Task PresentationAsync()
    {
        var glowing = new ItemInstance(AxeDef) { MixSheltom = 6 };
        var dull = new ItemInstance(AxeDef) { MixSheltom = 5 };
        Assert.True(HeldItemGlow.For(glowing).Glows && !HeldItemGlow.For(dull).Glows, "the mixed glow starts elsewhere than Devine");

        var buffer = new byte[4096];
        var dialog = new NpcDialogPacket(3, true, "Mixer", GameText.None, [new NpcServiceEntry((byte)NpcServiceKind.ItemMix, true, 1, 50)], []);
        var read = NpcDialogPacket.Read(buffer.AsSpan(0, dialog.Write(buffer)));
        Assert.True(read.Services[0].PricePercent == 50 && read.Services[0].Value == 1, "the price setting did not reach the dialog");
        return Task.CompletedTask;
    }

    private sealed class FixedRng : ISimRng
    {
        public int NextInclusive(int minInclusive, int maxInclusive) => minInclusive;
        public int NextExclusive(int minInclusive, int maxExclusive) => minInclusive;
        public float NextUnit() => 0f;
    }
}
