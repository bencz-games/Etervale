using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// The lines a change on the command phase answers (parties, trades), taken by the call that made it.
public sealed partial class WorldServer
{
    private readonly List<WorldNotice> _notices = new();

    private void Notify(ulong to, ChatKind kind, GameText text) => _notices.Add(new WorldNotice(to, kind, text));

    // A reply to the player who acted.
    private void Reply(WorldPlayer player, GameText text) => Notify(player.EntityId, ChatKind.Server, text);

    // A request that answers only lines: the action runs for a player still playing.
    private WorldNotice[] NoticeAction(PlayerSession session, Action<Connection> action)
    {
        if (TryGetPlaying(session, out var connection))
        {
            action(connection);
        }

        return TakeNotices();
    }

    // The tick's caller, after Tick: the lines no request took (a change of place ends a trade).
    public WorldNotice[] TakeTickNotices() => TakeNotices();

    private WorldNotice[] TakeNotices()
    {
        if (_notices.Count == 0)
        {
            return [];
        }

        var notices = _notices.ToArray();
        _notices.Clear();
        return notices;
    }
}
