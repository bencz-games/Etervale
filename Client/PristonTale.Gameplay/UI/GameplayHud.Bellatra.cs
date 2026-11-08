using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Bellatra;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.UI;

// Bellatra on the HUD: the board while the server shows it, a notice when a round starts, when the player's team goes on
// or is out and when the game is over, the secretary's window (a refused request shows its reason in the notice strip)
// and the result window: the game's final result once it is over, and a team's live result beside its booth, whose
// ticket is the NPC panel's one destination. Bellatra's show (HudBellatraShow) plays over the screen from the board and
// the team's seal notices.
public sealed partial class GameplayHud
{
    private readonly HudBellatraBoard _bellatraBoard = new();
    private readonly HudBellatraWindow _bellatraWindow = new();
    private readonly HudBellatraResult _bellatraResult = new();
    private HudBellatraShow _bellatraShow;
    private Func<string, Texture2D> _bellatraArt;
    private bool _bellatraArtAsked;

    // Plays Bellatra's sounds and music and shakes the camera; set by the screen.
    public IBellatraStage BellatraStage
    {
        set
        {
            if (_bellatraShow is not null)
            {
                _bellatraShow.Stage = value;
            }
        }
    }

    // Sends the secretary window's requests; set by the screen.
    public Action<BellatraClanAction, ulong, long> SendBellatraClan
    {
        set => _bellatraWindow.Send = value;
    }

    // Asks a result window (BellatraResultRequestPacket.OwnGame the player's game, 1 to 4 a team booth's); set by the screen.
    public Action<int> SendBellatraResult
    {
        set => _bellatraResult.Ask = value;
    }

    public void ObserveBellatra(PresenceUpdate update)
    {
        if (update.BellatraClan is { } clan)
        {
            _bellatraWindow.Observe(clan);
        }

        if (update.BellatraClanResult is { } result)
        {
            _bellatraWindow.Result(result);
            if (result.Status != Contracts.Social.BellatraClanStatus.Ok)
            {
                _notice.Show(ClientText.Get("Bellatra.Status." + result.Status));
            }
        }

        if (update.BellatraResult is { } shown)
        {
            _bellatraResult.Observe(shown);
        }

        foreach (var line in update.Chat)
        {
            _bellatraShow?.Observe(line.Line.Id);
        }

        if (update.Bellatra is not { } board)
        {
            return;
        }

        var was = _bellatraBoard.Board;
        _bellatraBoard.Apply(board);
        if (board.Shown && !_bellatraArtAsked && _bellatraShow is not null)
        {
            _bellatraArtAsked = true;
            _sprites.Request(BellatraEffects.Art);
        }

        _bellatraShow?.Observe(was, board);
        if (board.Shown && board.Outcome != was.Outcome && board.Outcome != Contracts.Social.BellatraOutcome.Playing)
        {
            _notice.Show(ClientText.Get("Bellatra.Outcome." + board.Outcome));
            if (board.Outcome == Contracts.Social.BellatraOutcome.Cleared)
            {
                _bellatraResult.RequestFinal(_clock.Elapsed.TotalMilliseconds);
            }
        }
        else if (board.Shown && board.Round > 0 && board.Round != was.Round)
        {
            _notice.Show(ClientText.Format("Bellatra.Round", board.Round.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private void OpenBellatra(ulong npcId)
    {
        _npcOpen = false;
        _bellatraWindow.Open(npcId);
    }

    // The show's table, read once, its art asked when the player first stands in the arena; without the table the board
    // and the windows still work.
    private void SetUpBellatraShow()
    {
        _bellatraArt = _sprites.Get;
        try
        {
            var table = BellatraEffectTable.Read(AssetFiles.ReadAllBytes(AssetFiles.Combine(_context.AssetRoot, BellatraEffectTable.Path)));
            _bellatraShow = new HudBellatraShow(table);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _context.Log.Error("Bellatra's effect table could not be read; its show is off", ex);
        }
    }

    private void DrawBellatraShow()
    {
        if (_bellatraShow is not { Showing: true } show)
        {
            return;
        }

        var batch = _context.SpriteBatch;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        show.Draw(batch, _bellatraArt, _painter.Pixel, _context.Font, HudPainter.TextScale);
        batch.End();
    }

    private void AskRedemption(long maximum, Action<int> accepted) =>
        _countBox.Open(ClientText.Get("Bellatra.HowMuch"), ClientText.Get("Bellatra.ToRedeem"), maximum, accepted);

    // A team booth's panel shows its team's live result beside it while a game is played.
    private void OpenBellatraBooth(int team)
    {
        _npc.ShowDestinations(_services.Destinations);
        _bellatraResult.RequestLive(team, _clock.Elapsed.TotalMilliseconds);
    }

    private bool ClickBellatra(Point pointer) => _bellatraWindow.Click(pointer) || _bellatraResult.Click(pointer);

    private bool CapturesBellatra(Point pointer) => _bellatraWindow.Contains(pointer) || _bellatraResult.Contains(pointer);
}
