namespace PristonTale.Gameplay.UI.Npc;

public static class HudCraftArt
{
    private const string Shop = "image/Sinimage/shopall/";
    private const string Aging = Shop + "Aging/";
    private const string Craft = Shop + "CraftItem/";

    public const string Panel = Craft + "CraftItemMain.bmp";
    public const string StartButton = Craft + "Button.bmp";
    public const string MixStartInfo = Craft + "ButtonInfo.bmp";
    public const string MixTitle = Craft + "Title.bmp";
    public const string AgingTitle = Aging + "Aging_Title.bmp";
    public const string AgingStartInfo = Aging + "ButtonInfo.bmp";
    public const string AlchemyTitle = Shop + "title_alchemy.bmp";
    public const string AlchemyPanel = Shop + "shop_alchemy.bmp";
    public const string AlchemyStartInfo = Shop + "Bt_alchemy.bmp";

    public const string AgingBar = Aging + "Bar.bmp";
    public const string AgingFill = Aging + "Bar_.bmp";
    public const string AgingPlate = Aging + "Aging.bmp";

    // The hint of each sheltom an aging step asks for, by sheltom ordinal (1 to 14).
    private static readonly string[] SheltomHints = Enumerable.Range(0, 15).Select(static ordinal => $"{Aging}itos{100 + ordinal}_.bmp").ToArray();

    public static string SheltomHint(int ordinal) => SheltomHints[ordinal];

    public static readonly string[] All =
    [
        Panel, StartButton, MixStartInfo, MixTitle, AgingTitle, AgingStartInfo, AlchemyTitle, AlchemyPanel, AlchemyStartInfo, AgingBar, AgingFill,
        AgingPlate,
        .. SheltomHints[1..]
    ];
}
