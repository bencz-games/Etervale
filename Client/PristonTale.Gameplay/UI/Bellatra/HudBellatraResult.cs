using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.UI.Bellatra;

// A Bellatra result in a help box, best score first: rank, class, name, score and kills. The game's final result opens
// when the game ends; the live result of a team opens beside a team booth's panel while a game is played, is asked again
// every RefreshMs and closes with the panel or once the game has nobody left in it. Only the answer to the request the
// window still waits for opens it, and a live answer never replaces the final result.
public sealed class HudBellatraResult
{
    private const double RefreshMs = 12_000;

    // The final result's box sits this far right of the NPC panel's, as the original's.
    private const int FinalOffset = 170;
    private const int LiveGap = 4;

    private const string Help = "image/Sinimage/help/";
    private const string Header = Help + "sod_list.bmp";
    private const string Row = Help + "sod_list-line.bmp";
    private const int SizeW = HudNpcPanel.SizeW;
    private const int SizeH = HudNpcPanel.SizeH;
    private const int RowsTop = 59;
    private const int RowStep = 24;
    private const int TextDrop = 5;

    // The rank's column; a two-digit rank starts a little further left.
    private const int RankX = 25;
    private const int WideRankX = 20;

    private static readonly Color RankColor = new(255, 200, 100);
    private static readonly Color NameColor = new(222, 231, 255);
    private static readonly Color ScoreColor = new(128, 255, 128);
    private static readonly Color KillsColor = new(110, 165, 250);

    // A class's face, in PlayerClass order.
    private static readonly string[] Faces = [.. Enum.GetValues<PlayerClass>().Select(static face => $"{Help}sod_cr-{(int)face}.bmp")];

    public static readonly string[] Art = [Header, Row, .. Faces];

    private enum Wait
    {
        None,
        Final,
        Live
    }

    private Wait _wait;
    private BellatraResultInfo _result;
    private (string Rank, string Score, string Kills)[] _texts = [];
    private int _x;
    private int _team;
    private double _askedMs;

    public bool IsOpen => _result is not null;

    // Asks the server for a result (BellatraResultRequestPacket.OwnGame or a team booth's team); set by the HUD.
    public Action<int> Ask { get; set; }

    private static int Y => HudNpcPanel.BoxY;

    private Rectangle Bounds => HudPainter.HelpBoxBounds(_x, Y, SizeW, SizeH);

    private Rectangle Ok => HudPainter.HelpBoxOk(_x, Y, SizeW, SizeH);

    public void RequestFinal(double nowMs) => Request(Wait.Final, BellatraResultRequestPacket.OwnGame, nowMs);

    // A team booth's live result, unless the final result is shown.
    public void RequestLive(int team, double nowMs)
    {
        if (_result is not { Final: true })
        {
            Request(Wait.Live, team, nowMs);
        }
    }

    public void Observe(BellatraResultInfo result)
    {
        if (_wait == Wait.None || result.Final != (_wait == Wait.Final))
        {
            return;
        }

        if (!result.Final && result.Lines.Count == 0)
        {
            Close();
            return;
        }

        _result = result;
        _x = result.Final ? HudNpcPanel.BoxX + FinalOffset : HudNpcPanel.Box.Right + LiveGap;
        _texts = [.. result.Lines.Select(static (line, index) => ((index + 1).ToString(CultureInfo.InvariantCulture),
            line.Score.ToString(CultureInfo.InvariantCulture), line.Kills.ToString(CultureInfo.InvariantCulture)))];
    }

    // panelOpen: whether the NPC panel a live result stands beside is still open.
    public void Update(double nowMs, bool panelOpen)
    {
        if (_wait != Wait.Live)
        {
            return;
        }

        if (!panelOpen)
        {
            Close();
        }
        else if (_result is not null && nowMs - _askedMs >= RefreshMs)
        {
            Request(Wait.Live, _team, nowMs);
        }
    }

    public void Close()
    {
        _result = null;
        _wait = Wait.None;
    }

    public bool Contains(Point pointer) => IsOpen && Bounds.Contains(pointer);

    public bool Click(Point pointer)
    {
        if (!Contains(pointer))
        {
            return false;
        }

        if (Ok.Contains(pointer))
        {
            Close();
        }

        return true;
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        if (_result is not { } result)
        {
            return;
        }

        painter.HelpBox(_x, Y, SizeW, SizeH, separator: false);
        painter.Sprite(Header, _x + 18, Y + 22);
        for (var row = 0; row < BellatraLimits.ResultLines; row++)
        {
            var top = Y + RowsTop + row * RowStep;
            painter.Sprite(Row, _x + 18, top);
            if (row >= result.Lines.Count)
            {
                continue;
            }

            var line = result.Lines[row];
            var face = (int)line.Class - 1;
            if (face >= 0 && face < Faces.Length)
            {
                painter.Sprite(Faces[face], _x + 52, top - 1);
            }

            var (rank, score, kills) = _texts[row];
            var y = top + TextDrop;
            painter.Text(rank, _x + (rank.Length > 1 ? WideRankX : RankX), y, RankColor);
            painter.TextCentered(line.Name, _x + 81, _x + 201, y, NameColor);
            painter.TextCentered(score, _x + 198, _x + 278, y, ScoreColor);
            painter.TextCentered(kills, _x + 278, _x + 338, y, KillsColor);
        }

        var ok = Ok;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    private void Request(Wait wait, int team, double nowMs)
    {
        _wait = wait;
        _team = team;
        _askedMs = nowMs;
        Ask?.Invoke(team);
    }
}
