using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Npc;

// Asset-relative paths of the original shop and warehouse window images.
public static class HudNpcArt
{
    private const string Shop = "image/Sinimage/shopall/";

    public const string Window = Shop + "shop-1.bmp";
    public const string Gold = Shop + "my_money.bmp";
    public const string Weight = Shop + "weight.bmp";
    public const string WarehouseTitle = Shop + "Warehouse.bmp";
    public const string TitleEquipment = Shop + "title-shop.bmp";
    public const string TitleGoods = Shop + "title-TabEtc.bmp";
    public const string TabWeapons = Shop + "TabAttack.bmp";
    public const string TabWeaponsIdle = Shop + "TabAttack0.bmp";
    public const string TabArmor = Shop + "TabDefense.bmp";
    public const string TabArmorIdle = Shop + "TabDefense0.bmp";
    public const string TabGoods = Shop + "TabEtc.bmp";
    public const string Buy = Shop + "Buy.bmp";
    public const string BuyIdle = Shop + "Buy0.bmp";
    public const string Sell = Shop + "Sell.bmp";
    public const string SellIdle = Shop + "Sell0.bmp";
    public const string Repair = Shop + "Repair.bmp";
    public const string RepairIdle = Shop + "DefaultRepair.bmp";
    public const string RepairAll = Shop + "AllRepair.bmp";
    public const string RepairAllIdle = Shop + "DefaultAllRepair.bmp";
    public const string BuyInfo = Shop + "ButtonInfo/BuyInfo.bmp";
    public const string SellInfo = Shop + "ButtonInfo/SellInfo.bmp";
    public const string RepairInfo = Shop + "ButtonInfo/RepairInfo.bmp";
    public const string RepairAllInfo = Shop + "ButtonInfo/RepairAllInfo.bmp";
    public const string LeftArrow = Shop + "LeftArrow.bmp";
    public const string RightArrow = Shop + "RightArrow.bmp";
    public const string Exit = Shop + "ButtonExit.bmp";
    public const string Grid = Shop + "Grid.bmp";
    public const string NoUse = Shop + "NoUseBox.bmp";

    public static readonly string[] All =
    [
        Window, Gold, Weight, WarehouseTitle, TitleEquipment, TitleGoods, TabWeapons, TabWeaponsIdle, TabArmor, TabArmorIdle, TabGoods,
        Buy, BuyIdle, Sell, SellIdle, Repair, RepairIdle, RepairAll, RepairAllIdle, BuyInfo, SellInfo, RepairInfo, RepairAllInfo,
        LeftArrow, RightArrow, Exit, Grid, NoUse
    ];

    public static string Label(NpcServiceKind kind, int value) => kind switch
    {
        NpcServiceKind.Shop => ClientText.Get("NpcService.Shop"),
        NpcServiceKind.SkillMaster => ClientText.Get("NpcService.SkillMaster"),
        NpcServiceKind.Warehouse => ClientText.Get("NpcService.Warehouse"),
        NpcServiceKind.QuestGiver or NpcServiceKind.QuestScript => ClientText.Get("NpcService.Quest"),
        NpcServiceKind.QuestEvent => value == Simulation.Items.Usable.Puzzles.CollectorEvent ? ClientText.Get("NpcService.PuzzleExchange") : ClientText.Get("NpcService.EventQuest"),
        NpcServiceKind.WarpGate => ClientText.Get("NpcService.WarpGate"),
        NpcServiceKind.Teleporter => ClientText.Get("NpcService.Teleport"),
        NpcServiceKind.ItemAging => ClientText.Get("NpcService.Aging"),
        NpcServiceKind.ItemMix => value == Simulation.Items.Mixing.MixingWork.AlchemyMode ? ClientText.Get("NpcService.Alchemy") : ClientText.Get("NpcService.Mixing"),
        NpcServiceKind.Smelting => ClientText.Get("NpcService.Smelting"),
        NpcServiceKind.Manufacture => ClientText.Get("NpcService.Crafting"),
        NpcServiceKind.MixtureReset => ClientText.Get("NpcService.MixtureReset"),
        NpcServiceKind.Clan => ClientText.Get("NpcService.Clan"),
        NpcServiceKind.BlessCastle => ClientText.Get("NpcService.BlessCastle"),
        NpcServiceKind.EventBooth => ClientText.Get("NpcService.Event"),
        NpcServiceKind.EventGift => ClientText.Get("NpcService.PrizeDraw"),
        NpcServiceKind.GiftExpress => ClientText.Get("NpcService.ItemDelivery"),
        NpcServiceKind.StarPoint => ClientText.Get("NpcService.StarPoints"),
        NpcServiceKind.CollectMoney => ClientText.Get("NpcService.Donations"),
        NpcServiceKind.GiveMoney => ClientText.Get("NpcService.DonationBox"),
        NpcServiceKind.Polling => ClientText.Get("NpcService.Survey"),
        NpcServiceKind.Media => ClientText.Get("NpcService.Broadcast"),
        NpcServiceKind.EventMonster => ClientText.Get("NpcService.Event"),
        _ => ClientText.Get("NpcService.Service")
    };
}
