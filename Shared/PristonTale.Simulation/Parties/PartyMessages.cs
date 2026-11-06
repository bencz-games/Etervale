using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Parties;

// The original's party lines: replies to the player who acted and notices every member receives.
public static class PartyMessages
{
    public static readonly GameText LevelGap = GameText.Of(GameTextId.PartyLevelGap);
    public static readonly GameText YouLeft = GameText.Of(GameTextId.PartyYouLeft);
    public static readonly GameText Disbanded = GameText.Of(GameTextId.PartyDisbanded);
    public static readonly GameText Released = GameText.Of(GameTextId.PartyReleased);
    public static readonly GameText CannotEnter = GameText.Of(GameTextId.PartyCannotEnter);

    // The request box's second line, under the name of who asks; a member's recommendation names the player instead.
    public static readonly GameText Request = GameText.Of(GameTextId.PartyRequest);

    public static GameText Recommends(string player) => GameText.Of(GameTextId.PartyRecommends, player);

    public static GameText Recommended(string leader, string player) => GameText.Of(GameTextId.PartyRecommended, leader, player);

    public static GameText Requested(string player) => GameText.Of(GameTextId.PartyRequested, player);

    public static GameText AlreadyMember(string player) => GameText.Of(GameTextId.PartyAlreadyMember, player);

    public static GameText InOtherParty(string player) => GameText.Of(GameTextId.PartyInOther, player);

    public static GameText NotFound(string player) => GameText.Of(GameTextId.PlayerNotFound, player);

    public static GameText Expelled(string player) => GameText.Of(GameTextId.PartyExpelled, player);

    public static GameText Delegated(string player) => GameText.Of(GameTextId.PartyDelegated, player);

    public static GameText LeaderNow(string player) => GameText.Of(GameTextId.PartyLeaderNow, player);

    public static GameText Entered(string player) => GameText.Of(GameTextId.PartyEntered, player);

    public static GameText Left(string player) => GameText.Of(GameTextId.PartyLeft, player);
}
