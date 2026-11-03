using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

// The wing exchange: the wing NPC turns the held wing, two crystals and gold into the next wing, from no wing to the
// metal wing up to the extreme wing. The NPC explains what the next wing needs, or offers the exchange when everything
// is carried.
public static class WingQuest
{
    private sealed record Recipe(uint[] Needs, int Gold, uint Result, string Documents);

    // The wings in exchange order (WingGates.Wings).
    private static readonly uint[] Wings = WingGates.Wings.Select(static wing => wing.Code).ToArray();

    private static readonly Recipe[] Recipes =
    [
        new([Crystal(1), Crystal(2)], 3000, Wings[0], "M_Wing"),
        new([Wings[0], Crystal(2), Crystal(3)], 5000, Wings[1], "S_Wing"),
        new([Wings[1], Crystal(3), Crystal(4)], 10000, Wings[2], "G_Wing"),
        new([Wings[2], Crystal(5), Crystal(6)], 20000, Wings[3], "D_Wing"),
        new([Wings[3], Crystal(6), Crystal(7)], 40000, Wings[4], "K_Wing"),
        new([Wings[4], Crystal(7), Crystal(8)], 80000, Wings[5], "E_Wing")
    ];

    private static uint Crystal(int ordinal) => ItemCodes.Parse($"GP1{ordinal:D2}");

    // The recipe of the held wing (the last one found in the pack); -1 when the extreme wing is already held.
    public static int RecipeIndex(QuestContext context)
    {
        var index = 0;
        foreach (var item in context.Character.OwnedItems())
        {
            var held = Array.IndexOf(Wings, item.Definition.Code);
            if (held >= 0)
            {
                index = held + 1;
            }
        }

        return index < Recipes.Length ? index : -1;
    }

    private static bool CanExchange(QuestContext context, Recipe recipe) =>
        context.Character.Money >= recipe.Gold && recipe.Needs.All(code => QuestInventory.Has(context.Character, code));

    public static void Talk(QuestContext context)
    {
        if (!context.Npc.WingExchange)
        {
            return;
        }

        var index = RecipeIndex(context);
        if (index < 0)
        {
            context.Show(QuestSteps.Close(QuestCode.None, 0, "Wing_End"));
            return;
        }

        var recipe = Recipes[index];
        context.Show(CanExchange(context, recipe)
            ? QuestSteps.Confirm(QuestCode.None, index, recipe.Documents + "_b", QuestTexts.ReceiveWing, WingGates.Wings[index].Name)
            : QuestSteps.Close(QuestCode.None, index, recipe.Documents + "_a"));
    }

    public static bool Accept(QuestContext context, int state)
    {
        var index = RecipeIndex(context);
        if (!context.Npc.WingExchange || index < 0 || index != state)
        {
            return QuestSteps.Stale(context);
        }

        var recipe = Recipes[index];
        if (!CanExchange(context, recipe))
        {
            return context.Character.Money < recipe.Gold ? context.Fail(QuestStatus.NotEnoughGold, QuestTexts.NotEnoughGold) : QuestSteps.Stale(context);
        }

        foreach (var code in recipe.Needs)
        {
            QuestInventory.Remove(context.Character, code, 1);
        }

        if (!context.Character.TryChangeMoney(-recipe.Gold) || !QuestInventory.TryGrant(context, [new QuestGrant(recipe.Result)]))
        {
            return QuestInventory.Refused(context);
        }

        context.Notice(QuestNoticeKind.Fanfare, "");
        return true;
    }
}
