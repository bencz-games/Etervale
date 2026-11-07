namespace PristonTale.Gameplay.UI.Npc;

public static class HudCraftArt
{
    private const string Shop = "image/Sinimage/shopall/";
    private const string Aging = Shop + "Aging/";
    private const string Craft = Shop + "CraftItem/";
    private const string Smelting = Shop + "SmeltingItem/";
    private const string Manufacture = Shop + "Manufacture/";

    public const string Panel = Craft + "CraftItemMain.bmp";
    public const string StartButton = Craft + "Button.bmp";
    public const string MixStartInfo = Craft + "ButtonInfo.bmp";
    public const string MixTitle = Craft + "Title.bmp";
    public const string AgingTitle = Aging + "Aging_Title.bmp";
    public const string AgingStartInfo = Aging + "ButtonInfo.bmp";
    public const string AlchemyTitle = Shop + "title_alchemy.bmp";
    public const string AlchemyPanel = Shop + "shop_alchemy.bmp";
    public const string AlchemyStartInfo = Shop + "Bt_alchemy.bmp";
    public const string SmeltingPanel = Smelting + "SmeltingMain.bmp";
    public const string SmeltingTitle = Smelting + "SmeltingTitle.bmp";
    public const string SmeltingStartInfo = Smelting + "ButtonInfo.bmp";
    public const string ManufacturePanel = Manufacture + "ManufactureMain.bmp";
    public const string ManufactureTitle = Manufacture + "ManufactureTitle.bmp";
    public const string ManufactureStartInfo = Manufacture + "ButtonInfo.bmp";
    public const string ResetPanel = Craft + "MResetMain.bmp";
    public const string ResetTitle = Craft + "MResetTitle.bmp";
    public const string ResetButton = Craft + "MResetButton.bmp";
    public const string ResetStartInfo = Craft + "MResetButtonInfo.bmp";

    public const string AgingBar = Aging + "Bar.bmp";
    public const string AgingFill = Aging + "Bar_.bmp";
    public const string AgingPlate = Aging + "Aging.bmp";

    // The hint of each sheltom an aging step asks for, by sheltom ordinal (1 to 14).
    private static readonly string[] SheltomHints = Enumerable.Range(0, 15).Select(static ordinal => $"{Aging}itos{100 + ordinal}_.bmp").ToArray();

    public static string SheltomHint(int ordinal) => SheltomHints[ordinal];

    public static readonly string[] All =
    [
        Panel, StartButton, MixStartInfo, MixTitle, AgingTitle, AgingStartInfo, AlchemyTitle, AlchemyPanel, AlchemyStartInfo, AgingBar, AgingFill,
        AgingPlate, SmeltingPanel, SmeltingTitle, SmeltingStartInfo, ManufacturePanel, ManufactureTitle, ManufactureStartInfo, ResetPanel, ResetTitle,
        ResetButton, ResetStartInfo,
        .. SheltomHints[1..]
    ];
}
