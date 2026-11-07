using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Manufacture;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// The manufacture window: a recipe in the left panel and, in the right panel's three boxes, the runes it names (each box
// takes only its own rune, A or B). The server lists what the placed recipe makes, priced at its fee; the window shows the
// item the runes pick (or "random") under the recipe and its fee on the plate.
public sealed class HudManufactureWindow : HudWorkbench
{
    private static readonly Point[] RuneCells = [new(240, 159), new(215, 190), new(265, 190)];

    // The products of the recipe the server last listed, and the recipe they belong to.
    public IReadOnlyList<ShopOffer> Products { get; set; } = [];

    private readonly string[] _runeKeys = new string[ManufactureWork.Runes];

    public Guid ProductsFor { get; set; }

    public override NpcServiceKind Service => NpcServiceKind.Manufacture;

    public override NpcServiceAction WorkAction => NpcServiceAction.Manufacture;

    public void Show()
    {
        Open(100);
        Products = [];
        ProductsFor = Guid.Empty;
    }

    public override string ResultText(int result) =>
        (ManufactureResult)result == ManufactureResult.Made ? ClientText.Get("Manufacture.Done") : ClientText.Get("Manufacture.Failed");

    public override bool Succeeded(int result) => (ManufactureResult)result == ManufactureResult.Made;

    public override bool Ready(OwnerState state) => Chosen(state, out _, out _) > 0;

    // The fee of the item the runes pick; for a random pick the highest of its candidates, as the server charges.
    public override long Price(OwnerState state) => Chosen(state, out _, out var fee) > 0 ? fee : 0;

    protected override Rectangle SlotBounds(Point origin, int slot) =>
        slot < RuneCells.Length ? HudCraftLayout.Cell(origin, RuneCells[slot]) : Rectangle.Empty;

    protected override int SlotAt(Point origin, Point pointer) => HudCraftLayout.CellAt(origin, RuneCells, pointer);

    protected override string PlaceItem(OwnedItemView item, Point origin, Point pointer, OwnerState state)
    {
        var key = item.CatalogKey;
        if (ManufactureWork.RecipeOf(key, out _) > 0)
        {
            if (AnySlot())
            {
                return ClientText.Get("Manufacture.RunesPlaced");
            }

            ItemId = item.InstanceId;
            return "";
        }

        var rune = ManufactureWork.RuneOf(key, out _);
        var recipe = Recipe(state);
        if (rune == 0 || recipe == 0)
        {
            return ClientText.Get("Manufacture.WrongRune");
        }

        var slot = SlotAt(origin, pointer);
        if (slot < 0 || Slots[slot] != Guid.Empty || ManufactureWork.RuneFor(recipe, slot) != rune)
        {
            slot = Enumerable.Range(0, ManufactureWork.Runes).FirstOrDefault(index => Slots[index] == Guid.Empty && ManufactureWork.RuneFor(recipe, index) == rune, -1);
        }

        if (slot < 0)
        {
            return ClientText.Get("Manufacture.WrongRune");
        }

        Slots[slot] = item.InstanceId;
        return "";
    }

    protected override void DrawPanel(HudPainter painter, Point origin, OwnerState state)
    {
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(HudCraftArt.ManufactureTitle, title.X, title.Y);
        var panel = HudCraftLayout.Panel(origin);
        painter.Sprite(HudCraftArt.ManufacturePanel, panel.X, panel.Y);
        if (Chosen(state, out var first, out _) is var count and > 0)
        {
            var name = count == 1 ? first.Name : ClientText.Get("Manufacture.Random");
            var box = ItemBounds(origin);
            painter.TextCentered(painter.Fit(name, 110), box.Center.X - 55, box.Center.X + 55, box.Bottom + 6);
        }

        var plate = HudCraftLayout.PricePlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
    }

    protected override void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state)
    {
        var key = incoming.CatalogKey;
        var slot = SlotAt(origin, pointer);
        if (slot >= 0)
        {
            var fits = Slots[slot] == Guid.Empty && ManufactureWork.RuneOf(key, out _) is var rune && rune > 0
                && rune == ManufactureWork.RuneFor(Recipe(state), slot);
            painter.Fill(SlotBounds(origin, slot), fits ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
            return;
        }

        var box = ItemBounds(origin);
        if (box.Contains(pointer))
        {
            var fits = ManufactureWork.RecipeOf(key, out _) > 0 && !AnySlot();
            painter.Fill(box, fits ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
        }
    }

    protected override void DrawStartInfo(HudPainter painter, Point info) => painter.Sprite(HudCraftArt.ManufactureStartInfo, info.X, info.Y);

    protected override bool ShowsStart(OwnerState state) => Ready(state) && StartHovered;

    protected override bool ShowsPrice(OwnerState state) => Ready(state);

    protected override void MainItemLeft()
    {
        ItemId = Guid.Empty;
        Array.Clear(Slots);
    }

    private int Recipe(OwnerState state) =>
        MainItem(state) is { } recipe ? ManufactureWork.RecipeOf(recipe.CatalogKey, out _) : 0;

    // How many listed products the three placed runes pick from (none until all three are placed), the first of them and
    // the dearest fee among them.
    private int Chosen(OwnerState state, out ShopOffer first, out long fee)
    {
        (first, fee) = (default, 0);
        var recipe = Recipe(state);
        if (recipe == 0 || ProductsFor != ItemId)
        {
            return 0;
        }

        for (var slot = 0; slot < ManufactureWork.Runes; slot++)
        {
            if (OwnedItemView.Find(state.Items, Slots[slot]) is not { } rune)
            {
                return 0;
            }

            _runeKeys[slot] = rune.CatalogKey;
        }

        var combination = ManufactureWork.Combination(recipe, _runeKeys);
        var count = 0;
        for (var index = 0; index < Products.Count; index++)
        {
            if (Products[index] is var offer && offer.Ordinal == combination)
            {
                first = count++ == 0 ? offer : first;
                fee = Math.Max(fee, offer.Price);
            }
        }

        return count;
    }
}
