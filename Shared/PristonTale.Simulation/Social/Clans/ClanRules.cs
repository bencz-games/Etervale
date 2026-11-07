namespace PristonTale.Simulation.Social.Clans;

// Why a clan request was refused (Ok when it was carried out).
public enum ClanStatus : byte
{
    Ok = 0,
    NotInClan = 1,
    AlreadyInClan = 2,
    NotChief = 3,
    NotRecruiter = 4,
    LevelTooLow = 5,
    NotEnoughGold = 6,
    NameInvalid = 7,
    NameTaken = 8,
    ClanFull = 9,
    JoinCooldown = 10,
    ChiefOnAccount = 11,
    TargetNotFound = 12,
    TargetInClan = 13,
    NotRegistered = 14,
    TargetLevelTooLow = 15,
    CapacityTooLow = 16,
    ViceExists = 17,
    NotVice = 18,
    ChiefCannotLeave = 19,
    Unavailable = 20,
    NoInvitation = 21,
    InviteRefused = 22,
    NotMember = 23,
    OutOfReach = 24,
    Busy = 25,
    MarkUnavailable = 26
}

// The clan rules: founding at level 40 for 500,000 gold at the Clan Master; six members (the chief included) within ten
// days to be registered, and a registered clan that falls below six has ten days to recruit again before it is
// dissolved; as many members as the chief's level allows (20 at 40, one more per level, at most 80); one vice chief; the
// chief hands the clan to a member of at least his level for 300,000 gold, only once it is registered, and cannot leave
// it otherwise. A character whose clan was dissolved cannot join one for four days, and an account keeps one chief.
public static class ClanRules
{
    public const long FoundingCost = 500_000;
    public const int FoundingLevel = 40;
    public const long EntrustCost = 300_000;
    public const int RegisteredMembers = 6;
    public const long DaySeconds = 24 * 3600;
    public const int GraceDays = 10;
    public const long GraceSeconds = GraceDays * DaySeconds;
    public const int JoinCooldownDays = 4;
    public const long JoinCooldownSeconds = JoinCooldownDays * DaySeconds;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 16;
    public const int MaxIntroLength = 80;
    public const int BaseCapacity = 20;
    public const int MaxCapacity = 80;
    public const long InvitationSeconds = 30;

    // A clan's monsters near a member: a cheer heals at most this many within this range, and a clan crystal is refused
    // when this many already stand there.
    public const int NearbyMonsters = 3;
    public const float NearbyRangeSquared = 3_276_800f;

    public static int Capacity(int chiefLevel) => chiefLevel < FoundingLevel ? 0 : Math.Min(MaxCapacity, BaseCapacity + chiefLevel - FoundingLevel);

    // Two to sixteen letters, digits, spaces, '-' or '_' (no surrogate pairs: at most 48 UTF-8 bytes), neither starting nor
    // ending with a space nor holding two in a row.
    public static bool IsValidName(string name) =>
        name is { Length: >= MinNameLength and <= MaxNameLength } && name[0] != ' ' && name[^1] != ' ' && !name.Contains("  ", StringComparison.Ordinal)
        && name.All(static c => !char.IsSurrogate(c) && (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'));

    public static bool CanRecruit(ClanRank rank) => rank is ClanRank.Chief or ClanRank.Vice;

    // The clan's state and deadline once it counts this many members: six or more register it (no deadline); fewer start
    // the ten-day countdown unless one is running.
    public static (ClanState State, long DeadlineSeconds) Counted(Clan clan, int count, long nowSeconds) =>
        count >= RegisteredMembers ? (ClanState.Registered, 0)
        : (clan.State, clan.DeadlineSeconds == 0 ? nowSeconds + GraceSeconds : clan.DeadlineSeconds);

    public static bool Expired(Clan clan, long nowSeconds) => clan.DeadlineSeconds != 0 && clan.DeadlineSeconds <= nowSeconds;

    public static ClanStatus CheckRoom(Clan clan) => clan.Count >= clan.Capacity ? ClanStatus.ClanFull : ClanStatus.Ok;

    // The chief hands the clan to the member: registered clans only, to a member of at least the chief's level whose
    // capacity holds the clan, for EntrustCost.
    public static ClanStatus CheckEntrust(Clan clan, ClanMember chief, ClanMember heir, long money)
    {
        if (clan.State != ClanState.Registered)
        {
            return ClanStatus.NotRegistered;
        }

        if (heir is null || heir.CharacterId == chief.CharacterId)
        {
            return ClanStatus.NotMember;
        }

        if (heir.Level < chief.Level)
        {
            return ClanStatus.TargetLevelTooLow;
        }

        if (Capacity(heir.Level) < clan.Count)
        {
            return ClanStatus.CapacityTooLow;
        }

        return money < EntrustCost ? ClanStatus.NotEnoughGold : ClanStatus.Ok;
    }
}
