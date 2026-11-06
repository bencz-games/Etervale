using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Chat;

// One line for one receiver. SpeakerId is the entity that said it (0 for server lines): the receiver shows it in a
// bubble over that entity when it sees it. A server line the client words in its language is Line; Text is shown as it
// is (what players say, game-master lines) when Line is none.
public readonly record struct ChatLine(ChatKind Kind, ulong SpeakerId, string Text, GameText Line = default)
{
    public static ChatLine Reply(string text) => new(ChatKind.Server, 0, text);

    public static ChatLine Reply(GameText line) => new(ChatKind.Server, 0, "", line);
}
