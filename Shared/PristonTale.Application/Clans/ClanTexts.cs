using System.Globalization;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Application.Clans;

// The line a refused clan request answers with; the rules' numbers travel as its arguments.
public static class ClanTexts
{
    public static GameText Of(ClanStatus status) => status switch
    {
        ClanStatus.LevelTooLow => GameText.Of(GameTextId.ClanLevelTooLow, Number(ClanRules.FoundingLevel)),
        ClanStatus.NameInvalid => GameText.Of(GameTextId.ClanNameInvalid, Number(ClanRules.MinNameLength), Number(ClanRules.MaxNameLength)),
        ClanStatus.JoinCooldown => GameText.Of(GameTextId.ClanJoinCooldown, Number(ClanRules.JoinCooldownDays)),
        _ => GameText.Of(Id(status))
    };

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static GameTextId Id(ClanStatus status) => status switch
    {
        ClanStatus.NotInClan => GameTextId.ClanNotInClan,
        ClanStatus.AlreadyInClan => GameTextId.ClanAlreadyInClan,
        ClanStatus.NotChief => GameTextId.ClanNotChief,
        ClanStatus.NotRecruiter => GameTextId.ClanNotRecruiter,
        ClanStatus.NotEnoughGold => GameTextId.NotEnoughGold,
        ClanStatus.NameTaken => GameTextId.ClanNameTaken,
        ClanStatus.ClanFull => GameTextId.ClanFull,
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
        ClanStatus.Busy => GameTextId.ClanBusy,
        ClanStatus.MarkUnavailable => GameTextId.ClanMarkUnavailable,
        _ => GameTextId.ClanUnavailable
    };
}
