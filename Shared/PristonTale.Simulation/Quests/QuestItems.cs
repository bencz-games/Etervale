using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// Item codes the quests hand out, ask for and take back.
public static class QuestItems
{
    public static readonly uint SilverWolverineTrophy = ItemCodes.Parse("QT101");
    public static readonly uint BronzeWolverineTrophy = ItemCodes.Parse("QT102");
    public static readonly uint GoldenWolverineTrophy = ItemCodes.Parse("QT103");
    public static readonly uint RoyalJelly = ItemCodes.Parse("QT104");
    public static readonly uint HairTonic = ItemCodes.Parse("QT105");
    public static readonly uint Vamp = ItemCodes.Parse("QT106");
    public static readonly uint Level90Token = ItemCodes.Parse("QT107");
    public static readonly uint KhaliaTear = ItemCodes.Parse("QT108");
    public static readonly uint BonusVampOne = ItemCodes.Parse("QT109");
    public static readonly uint BonusVampTwo = ItemCodes.Parse("QT110");
    public static readonly uint BonusVampThree = ItemCodes.Parse("QT111");
    public static readonly uint Recommendation = ItemCodes.Parse("QT112");
    public static readonly uint RoyalAmulet = ItemCodes.Parse("QT113");
    public static readonly uint Candy = ItemCodes.Parse("QT114");
    public static readonly uint Cake = ItemCodes.Parse("QT115");
    public static readonly uint LostHammer = ItemCodes.Parse("QT116");
    public static readonly uint MoonStone = ItemCodes.Parse("QT125");
    public static readonly uint GemOfHestian = ItemCodes.Parse("QT126");
    public static readonly uint GlassBottle = ItemCodes.Parse("MA101");
    public static readonly uint Honey = ItemCodes.Parse("MA201");
    public static readonly uint Oil = ItemCodes.Parse("MA202");

    public static readonly uint ArcaneRing = ItemCodes.Parse("OR202");
    public static readonly uint EmperorRing = ItemCodes.Parse("OR203");
    public static readonly uint FocusRing = ItemCodes.Parse("OR204");

    // Royal jelly takes the bottle and nine honey, hair tonic the bottle and seven oil.
    public const int HoneyForJelly = 9;
    public const int OilForTonic = 7;

    private static readonly uint QuestFamily = ItemCodes.Family("QT1");
    private static readonly uint BottleFamily = ItemCodes.Family("MA1");
    private static readonly uint IngredientFamily = ItemCodes.Family("MA2");

    // Quest and ingredient drops go straight into the killer's pack, as the original server put them there; they never lie
    // on the ground.
    public static bool IsQuestDrop(uint code) => ItemCodes.Family(code) == QuestFamily || IsIngredient(code);

    // The original's special item box keeps these, with the wings (QuestItemBox): the quest items through the lost
    // hammer and the ingredients.
    private static readonly uint[] BoxedCodes =
    [
        SilverWolverineTrophy, BronzeWolverineTrophy, GoldenWolverineTrophy, RoyalJelly, HairTonic, Vamp, Level90Token, KhaliaTear,
        BonusVampOne, BonusVampTwo, BonusVampThree, Recommendation, RoyalAmulet, Candy, Cake, LostHammer, GlassBottle, Honey, Oil
    ];

    public static bool Boxed(uint code) => Array.IndexOf(BoxedCodes, code) >= 0;

    public static bool IsIngredient(uint code) => ItemCodes.Family(code) == IngredientFamily || ItemCodes.Family(code) == BottleFamily;
}
