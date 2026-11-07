using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.Items.Mixing;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Npc;

// The mixing window: a mixable item, the sheltoms and seals of a formula in any slot and the respec stone. The alchemist
// takes only sheltoms, which it turns into force orbs. Taking the item back keeps the materials; the start button and
// the price show once the window is ready.
public sealed class HudMixWindow : HudWorkbench
{
    private MixFormula _startedFormula;

    public bool Alchemy { get; private set; }

    public override NpcServiceKind Service => NpcServiceKind.ItemMix;

    public override NpcServiceAction WorkAction => Alchemy ? NpcServiceAction.Refine : NpcServiceAction.Mix;

    // The formula the started mix matches, for its result line.
    public override void Started(OwnerState state) => _startedFormula = Formula(state);

    public override string ResultText(int result) => (MixResult)result switch
    {
        MixResult.Mixed => ClientText.Format("Mix.Result", string.Join(", ", (_startedFormula?.Bonuses ?? []).Select(ClientText.MixBonus))),
        MixResult.Reconstructed => ClientText.Get("Mix.Reconstructed"),
        MixResult.Refined => ClientText.Get("Mix.Refined"),
        _ => ClientText.Get("Mix.Failed")
    };

    public override bool Succeeded(int result) => (MixResult)result != MixResult.Failed;

    public void Show(bool alchemy, int pricePercent)
    {
        Open(pricePercent);
        Alchemy = alchemy;
    }

    public static bool Mixable(OwnedItemView item) =>
        item?.Details is { } details && MixingWork.Mixable(item.CatalogKey, item.Kind, IsWearable(item.Kind), details.UpgradeLevel,
            details.AgingGoal > 0, details.MixSheltom);

    public override bool Ready(OwnerState state) => AnySlotFilled() && (Alchemy || MainItem(state) is not null);

    private bool AnySlotFilled()
    {
        foreach (var id in Slots)
        {
            if (id != Guid.Empty)
            {
                return true;
            }
        }

        return false;
    }

    public override long Price(OwnerState state)
    {
        if (!Alchemy)
        {
            return MainItem(state) is { Details: { } details } ? MixingWork.Price(details.Roll.Price, new MixingSettings(PricePercent, Tax: Tax)) : 0;
        }

        var settings = new MixingSettings(AlchemyPricePercent: PricePercent, Tax: Tax);
        long total = 0;
        var items = state?.Items ?? [];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (Array.IndexOf(Slots, item.InstanceId) >= 0)
            {
                total += Math.Max(0, MixingWork.AlchemyPrice(AgingRules.SheltomOrdinal(item.CatalogKey), settings));
            }
        }

        return MixingWork.AlchemyTotal(total, settings);
    }

    public MixFormula Formula(OwnerState state)
    {
        if (MainItem(state) is not { } item)
        {
            return null;
        }

        var counts = new int[MixFormulas.SheltomKinds];
        foreach (var material in state.Items.Where(entry => Array.IndexOf(Slots, entry.InstanceId) >= 0))
        {
            var ordinal = AgingRules.SheltomOrdinal(material.CatalogKey);
            if (ordinal is >= 1 and <= MixFormulas.SheltomKinds)
            {
                counts[ordinal - 1]++;
            }
        }

        return MixFormulas.Find(ItemCodes.FamilyOf(item.CatalogKey), counts);
    }

    protected override string PlaceItem(OwnedItemView item, Point origin, Point pointer, OwnerState state)
    {
        var key = item.CatalogKey;
        if (Accepts(key))
        {
            var slot = HudCraftLayout.SheltomAt(origin, pointer);
            if (slot < 0 || Slots[slot] != Guid.Empty)
            {
                slot = Array.IndexOf(Slots, Guid.Empty);
            }

            if (slot < 0)
            {
                return ClientText.Get("Mix.CannotMix");
            }

            Slots[slot] = item.InstanceId;
            return "";
        }

        if (Alchemy)
        {
            return ClientText.Get("Mix.CannotMix");
        }

        if (key.Equals(MixingWork.RespecStone, StringComparison.OrdinalIgnoreCase))
        {
            StoneId = item.InstanceId;
            return "";
        }

        if (!Mixable(item))
        {
            return ClientText.Get("Mix.CannotMix");
        }

        ItemId = item.InstanceId;
        return "";
    }

    protected override void DrawPanel(HudPainter painter, Point origin, OwnerState state)
    {
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(Alchemy ? HudCraftArt.AlchemyTitle : HudCraftArt.MixTitle, title.X, title.Y);
        if (Alchemy)
        {
            painter.Sprite(HudCraftArt.AlchemyPanel, origin.X + 17, origin.Y + 84);
        }
        else
        {
            var panel = HudCraftLayout.Panel(origin);
            painter.Sprite(HudCraftArt.Panel, panel.X, panel.Y);
        }

        var plate = HudCraftLayout.PricePlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
    }

    protected override void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state)
    {
        var key = incoming.CatalogKey;
        var slot = HudCraftLayout.SheltomAt(origin, pointer);
        if (slot >= 0)
        {
            painter.Fill(HudCraftLayout.Sheltom(origin, slot), Accepts(key) && Slots[slot] == Guid.Empty ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
            return;
        }

        if (Alchemy)
        {
            return;
        }

        var stone = HudCraftLayout.Stone(origin);
        if (stone.Contains(pointer))
        {
            painter.Fill(stone, key.Equals(MixingWork.RespecStone, StringComparison.OrdinalIgnoreCase) ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
            return;
        }

        var box = HudCraftLayout.ItemBox(origin);
        if (box.Contains(pointer))
        {
            painter.Fill(box, Mixable(incoming) ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
        }
    }

    protected override void DrawStartInfo(HudPainter painter, Point info) =>
        painter.Sprite(Alchemy ? HudCraftArt.AlchemyStartInfo : HudCraftArt.MixStartInfo, Alchemy ? info.X - 10 : info.X, info.Y);

    protected override bool ShowsStart(OwnerState state) => Ready(state) && StartHovered;

    protected override bool ShowsPrice(OwnerState state) => Ready(state);

    protected override void MainItemLeft() => ItemId = Guid.Empty;

    private bool Accepts(string key)
    {
        var ordinal = AgingRules.SheltomOrdinal(key);
        if (Alchemy)
        {
            return MixingWork.AlchemyPrice(ordinal) >= 0;
        }

        return ordinal > 0 || MixingWork.SealOf(key) is not null;
    }

    private static bool IsWearable(Simulation.Items.ItemKind kind) => kind is not (Simulation.Items.ItemKind.Potion or
        Simulation.Items.ItemKind.Material or Simulation.Items.ItemKind.Quest or Simulation.Items.ItemKind.Misc or Simulation.Items.ItemKind.Sheltom
        or Simulation.Items.ItemKind.ForceOrb);
}
