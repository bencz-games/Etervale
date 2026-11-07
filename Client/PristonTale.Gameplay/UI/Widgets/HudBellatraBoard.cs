using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Bellatra's board, on the right under the target frame while the server shows it (the player is in the arena, in the
// game or just out of it): the round, the player's score and the four teams' scores, the player's team in gold. The
// lines are made again when the server's board changes.
public sealed class HudBellatraBoard
{
    private static readonly Color Title = HudSideBoard.Title;
    private static readonly Color Own = HudSideBoard.Own;
    private static readonly Color Line = HudSideBoard.Line;

    private readonly List<(string Text, string Right, Color Color)> _lines = new();

    public BellatraBoardInfo Board { get; private set; } = BellatraBoardInfo.Hidden;

    public void Apply(BellatraBoardInfo board)
    {
        Board = board;
        _lines.Clear();
        if (!board.Shown)
        {
            return;
        }

        _lines.Add((ClientText.Get("Bellatra.Title"), "", Title));
        _lines.Add((board.Round > 0 ? ClientText.Format("Bellatra.Round", board.Round.ToString(CultureInfo.InvariantCulture))
            : ClientText.Get(board.Outcome == Contracts.Social.BellatraOutcome.Cleared ? "Bellatra.Over" : "Bellatra.Waiting"), "", Line));
        _lines.Add((ClientText.Get("Bellatra.MyScore"), Points(board.Score), Own));
        for (var team = 0; team < board.TeamScores.Count; team++)
        {
            _lines.Add((ClientText.Format("Bellatra.Team", (team + 1).ToString(CultureInfo.InvariantCulture)), Points(board.TeamScores[team]),
                team == board.Team ? Own : Line));
        }
    }

    public void Draw(HudPainter painter, HudLayout layout)
    {
        if (_lines.Count > 0)
        {
            HudSideBoard.Draw(painter, layout, _lines);
        }
    }

    private static string Points(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
