using PristonTale.Simulation.Social;
using System.Collections.Concurrent;
using PristonTale.Application.Sessions;

namespace PristonTale.Application.Chat;

// A player in the world as the chat sees it. The game-master level starts at the account's level and may be raised for
// this session by another game master.
public sealed class ChatMember
{
    private int _gmLevel;
    private int _whisperRefused;
    private int _noticeSeen;

    public ChatMember(PlayerSession session, string name, IChatLink link, long joinedMs, long startingMoney)
    {
        Session = session;
        Name = name;
        Link = link;
        JoinedMs = joinedMs;
        StartingMoney = startingMoney;
        _gmLevel = session.GmLevel;
    }

    public PlayerSession Session { get; }
    public ulong EntityId => Session.EntityId;
    public string Name { get; }
    public string UserId => Session.UserId;
    public long AccountId => Session.AccountId;
    public IChatLink Link { get; }
    public long JoinedMs { get; }

    // The character's gold when it entered the world.
    public long StartingMoney { get; }

    public int GmLevel
    {
        get => Volatile.Read(ref _gmLevel);
        set => Volatile.Write(ref _gmLevel, value);
    }

    // Fed only by this member's connection, one line at a time.
    public ChatFloodGuard Flood { get; } = new();

    public bool WhisperRefused
    {
        get => Volatile.Read(ref _whisperRefused) != 0;
        set => Volatile.Write(ref _whisperRefused, value ? 1 : 0);
    }

    // The player's friends and the players it ignores (ContactAppService); a whisper from an ignored player is refused.
    public ContactBook Contacts { get; init; } = new([]);

    // The last server notice this player received; a newer notice is sent with the next heartbeat.
    public int NoticeSeen
    {
        get => Volatile.Read(ref _noticeSeen);
        set => Volatile.Write(ref _noticeSeen, value);
    }
}

// Every player in the world, by entity id, character name and account. Thread-safe; members join when the world
// accepted them and leave before the world forgets them.
public sealed class ChatDirectory
{
    private readonly ConcurrentDictionary<ulong, ChatMember> _byEntity = new();
    private readonly ConcurrentDictionary<string, ChatMember> _byName = new(StringComparer.OrdinalIgnoreCase);
    private string _notice = "";
    private int _noticeCounter;
    private readonly object _noticeGate = new();

    public IEnumerable<ChatMember> Members => _byEntity.Values;

    public void Join(ChatMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        member.NoticeSeen = NoticeCounter;
        _byEntity[member.EntityId] = member;
        _byName[member.Name] = member;
    }

    public void Leave(ulong entityId, IChatLink link)
    {
        if (_byEntity.TryGetValue(entityId, out var member) && ReferenceEquals(member.Link, link))
        {
            _byEntity.TryRemove(new KeyValuePair<ulong, ChatMember>(entityId, member));
            _byName.TryRemove(new KeyValuePair<string, ChatMember>(member.Name, member));
        }
    }

    public bool TryGet(ulong entityId, out ChatMember member) => _byEntity.TryGetValue(entityId, out member!);

    public ChatMember? FindByName(string name) => _byName.TryGetValue(name, out var member) ? member : null;

    public ChatMember? FindByUserId(string userId)
    {
        foreach (var member in _byEntity.Values)
        {
            if (string.Equals(member.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                return member;
            }
        }

        return null;
    }

    public int NoticeCounter
    {
        get
        {
            lock (_noticeGate)
            {
                return _noticeCounter;
            }
        }
    }

    // The standing notice every player in the world receives once with the next heartbeat; players who enter later do
    // not receive the notice that was set before they came.
    public void SetNotice(string text)
    {
        lock (_noticeGate)
        {
            _notice = text;
            _noticeCounter++;
        }
    }

    public bool TryTakeNotice(ChatMember member, out string text)
    {
        lock (_noticeGate)
        {
            text = _notice;
            if (member.NoticeSeen >= _noticeCounter)
            {
                return false;
            }

            member.NoticeSeen = _noticeCounter;
            return true;
        }
    }

    public void Broadcast(ChatLine line)
    {
        foreach (var member in _byEntity.Values)
        {
            member.Link.Deliver(line);
        }
    }

    // The lines a world change answered (a party or a trade), to those of their players still in the chat.
    public void Deliver(IReadOnlyList<World.WorldNotice>? notices)
    {
        foreach (var notice in notices ?? [])
        {
            if (TryGet(notice.To, out var member))
            {
                member.Link.Deliver(new ChatLine(notice.Kind, 0, notice.Text));
            }
        }
    }
}
