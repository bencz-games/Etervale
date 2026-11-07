namespace PristonTale.Gameplay.UI.Clans;

// Asset-relative paths of the party window's clan page and of its titles for a clan's members (a third tab, "Clan").
public static class HudClanArt
{
    private const string Party = "image/party/";

    public const string Title = Party + "title-clan.tga";
    public const string PartyTitle = Party + "title-Party.tga";
    public const string FriendsTitle = Party + "title-chatting.tga";
    public const string Member = Party + "party_man_0.bmp";
    public const string Vice = Party + "party_man_jang.bmp";
    public const string Chat = Party + "ButtonChatting.bmp";
    public const string ChatIdle = Party + "ButtonChatting_.bmp";
    public const string ChatInfo = Party + "ButtonInfo-chatting.bmp";

    public static readonly string[] All = [Title, PartyTitle, FriendsTitle, Member, Vice, Chat, ChatIdle, ChatInfo];
}
