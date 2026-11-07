using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Client.Net.Presence;

public sealed record PresenceUpdate(
    long ServerTick,
    IReadOnlyList<EntitySpawnInfo> Spawns,
    IReadOnlyList<EntityStateInfo> States,
    IReadOnlyList<ulong> Removed,
    IReadOnlyList<CombatEventInfo> Events,
    int? FieldId = null,
    OwnerState? Owner = null)
{
    public IReadOnlyList<PristonTale.Simulation.World.Loot.GroundDropView> GroundAdded { get; init; } = [];
    public IReadOnlyList<Guid> GroundRemoved { get; init; } = [];

    // FieldWarp for the local player: a forced correction after a rejected report, otherwise a field confirmation.
    public WarpInfo? Warp { get; init; }

    // Server simulation time of a freshly received group or event list (PresenceClock extension); null otherwise.
    public double? ServerTimeMs { get; init; }

    public IReadOnlyList<SkillEventInfo> SkillEvents { get; init; } = [];
    public IReadOnlyList<StatusEffectInfo> StatusEffects { get; init; } = [];
    public SkillCooldownSnapshot? Cooldowns { get; init; }
    public SkillCastResultInfo? CastResult { get; init; }

    public PremiumTimerSnapshot? PremiumTimers { get; init; }

    // What standing up again would cost the local player while it is dead.
    public PristonTale.Simulation.Progression.RestartOffer? RestartOffer { get; init; }

    // The weather of the local player's field, when it entered the world or the weather changed.
    public PristonTale.Contracts.World.WeatherState? Weather { get; init; }
    public IReadOnlyList<ItemEffectInfo> ItemEffects { get; init; } = [];

    // The quest journal when it changed, quest notices and a quest dialog the world opened on its own.
    public QuestView? Quest { get; init; }
    public IReadOnlyList<QuestNotice> QuestNotices { get; init; } = [];
    public QuestPrompt? QuestPrompt { get; init; }

    // Chat lines from the server, and the server's request to save and quit (its shutdown).
    public IReadOnlyList<ChatLineInfo> Chat { get; init; } = [];
    public bool CloseRequested { get; init; }

    // The local player's party when it changed (no member once it has none), and a player's request to group.
    public PartyInfo? Party { get; init; }
    public PartyRequestInfo? PartyRequest { get; init; }

    // The local player's clan (ClanInfo.None when it has none), an invitation to one and a request's result.
    public ClanInfo? Clan { get; init; }
    public ClanInvitationInfo? ClanInvitation { get; init; }
    public ClanResultInfo? ClanResult { get; init; }

    // Bless Castle, at world entry and on every change.
    public CastleInfo? Castle { get; init; }

    // The owner's window at the castle's butler and how a request of it ended.
    public CastleOwnerInfo? CastleOwner { get; init; }
    public CastleOwnerResultInfo? CastleOwnerResult { get; init; }

    // The local player's trade window when it changed (TradeInfo.None once it ended), and a player's request to trade.
    public TradeInfo? Trade { get; init; }
    public TradeRequestInfo? TradeRequest { get; init; }

    // One of the local player's contact lists, as the server answered a change or a refresh.
    public ContactListInfo? Contacts { get; init; }
}

// A contact list in the order added; Online tells a friend playing now.
public sealed record ContactListInfo(ContactList List, IReadOnlyList<(string Name, bool Online)> Entries);

// The trade window as the server holds it: the partner, the local player's own offer, the partner's items (anchored at
// their trade grid cells, with their values for the local player), the partner's gold and both locks.
public sealed record TradeInfo(ulong PartnerId, string PartnerName, PristonTale.Simulation.Trades.TradeOffer Own, bool OwnLocked,
    IReadOnlyList<OwnedItemView> PartnerItems, long PartnerMoney, bool PartnerLocked)
{
    public static readonly TradeInfo None = new(0, "", PristonTale.Simulation.Trades.TradeOffer.Empty, false, [], 0, false);

    public bool Open => PartnerId != 0;
}

public sealed record TradeRequestInfo(ulong FromId, string FromName);

// The local player's party, the leader first; Position is the member's planar place (x, z) in world units.
public sealed record PartyInfo(IReadOnlyList<PartyMemberInfo> Members)
{
    public static readonly PartyInfo None = new([]);

    public bool Leads(ulong entityId) => Members.Count > 0 && Members[0].EntityId == entityId;

    public bool Contains(ulong entityId)
    {
        for (var index = 0; index < Members.Count; index++)
        {
            if (Members[index].EntityId == entityId)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record PartyMemberInfo(ulong EntityId, string Name, int Level, int Life, int MaxLife, System.Numerics.Vector2 Position);

// Recommended: the player a member asks its leader to take (empty for a request to join).
public sealed record PartyRequestInfo(ulong FromId, string FromName, string Recommended = "");

// One chat line: Kind is the server's colour and tab code, SpeakerId the entity that said it (0 for server lines).
// Direct lines (the plain chat message) go to the log as they are; the others show over a speaker the client sees.
// Line is a server line the client words in its language; Text is shown as it is when Line is none.
public sealed record ChatLineInfo(ChatKind Kind, ulong SpeakerId, string Text, GameText Line = default);

public sealed record WarpInfo(int FieldId, System.Numerics.Vector3 Position, bool Forced);
