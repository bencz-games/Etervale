using PristonTale.Application.Chat;
using PristonTale.Application.Saves;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Services;

// Pays a Bellatra prize through the path of every character change (the save gate, the copy made and checked on the
// tick, the commit, the background save), trying again a few times while the player is busy with another change, and
// tells the player the prize, the gold limit that refused it or why it failed. A prize still waiting when the server
// stops, or owed to a player who left, is lost.
// A paid prize's points and tax are queued for Bellatra's clans.
public sealed class BellatraAppService(WorldServer world, ICharacterSaves saves, ChatDirectory chat, Bellatra.BellatraClanAppService? clans = null)
{
    private const int Attempts = 3;
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(10);

    private readonly DurableOperations _durable = new(saves);

    public async Task PayAsync(BellatraPrize prize, CancellationToken ct)
    {
        GameText? told = null;
        var paid = false;
        for (var attempt = 0; attempt < Attempts && told is null; attempt++)
        {
            using var lease = await _durable.AdmitAsync(prize.Session, GateWait, ct);
            told = await lease.RunAsync(
                session => world.PrepareBellatraPrizeAsync(session, prize.Gold),
                world.CommitBellatraPrizeAsync,
                prepared =>
                {
                    paid = prepared.Outcome > 0 || prize.Gold <= 0;
                    return (GameText?)(paid ? prize.Text : GameText.Of(GameTextId.GoldLimit));
                },
                static failure => failure == DurableFailure.Busy ? (GameText?)null : DurableFailures.Text(failure));
        }

        if (chat.TryGet(prize.Session.EntityId, out var member))
        {
            member.Link.Deliver(new ChatLine(ChatKind.System, 0, "", told ?? DurableFailures.Text(DurableFailure.Busy)));
        }

        if (paid)
        {
            clans?.Queue(prize);
        }
    }
}
