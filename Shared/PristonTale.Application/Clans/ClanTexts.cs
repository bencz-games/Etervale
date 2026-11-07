using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Application.Clans;

// The line a refused clan request answers with.
public static class ClanTexts
{
    public static GameText Of(ClanStatus status) => GameText.Of(status switch
    {
        ClanStatus.NotInClan => GameTextId.ClanNotInClan,
        ClanStatus.AlreadyInClan => GameTextId.ClanAlreadyInClan,
        ClanStatus.NotChief => GameTextId.ClanNotChief,
        ClanStatus.NotRecruiter => GameTextId.ClanNotRecruiter,
        ClanStatus.LevelTooLow => GameTextId.ClanLevelTooLow,
        ClanStatus.NotEnoughGold => GameTextId.NotEnoughGold,
        ClanStatus.NameInvalid => GameTextId.ClanNameInvalid,
        ClanStatus.NameTaken => GameTextId.ClanNameTaken,
        ClanStatus.ClanFull => GameTextId.ClanFull,
        ClanStatus.JoinCooldown => GameTextId.ClanJoinCooldown,
        ClanStatus.ChiefOnAccount => GameTextId.ClanChiefOnAccount,
        ClanStatus.TargetNotFound => GameTextId.ClanTargetNotFound,
        ClanStatus.TargetInClan => GameTextId.ClanTargetInClan,
        ClanStatus.NotRegistered => GameTextId.ClanNotRegistered,
        ClanStatus.TargetLevelTooLow => GameTextId.ClanTargetLevelTooLow,
        ClanStatus.CapacityTooLow => GameTextId.ClanCapacityTooLow,
        ClanStatus.ViceExists => GameTextId.ClanViceExists,
        ClanStatus.NotVice => GameTextId.ClanNotVice,
        ClanStatus.ChiefCannotLeave => GameTextId.ClanChiefCannotLeave,
        ClanStatus.NoInvitation => GameTextId.ClanNoInvitation,
        ClanStatus.NotMember => GameTextId.ClanNotMember,
        ClanStatus.OutOfReach => GameTextId.NpcOutOfReach,
        ClanStatus.Busy => GameTextId.CloseTradeFirst,
        _ => GameTextId.ClanUnavailable
    });
}
