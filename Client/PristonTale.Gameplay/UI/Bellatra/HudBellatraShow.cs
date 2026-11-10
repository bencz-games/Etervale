using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.World.Bellatra;

namespace PristonTale.Gameplay.UI.Bellatra;

// What Bellatra's show asks of the scene: a sound, the arena's music (empty: none, at once) and a room seal's quake.
public interface IBellatraStage
{
    void Play(EventCue cue);

    void Music(string code);

    void Quake();
}

// Bellatra's show on the screen, as the board and the team's notices tell it. Entering the arena with a ticket shows its
// loading screen; coming back to a game in play (a relog) only plays its music. A round start shows the round's number
// and START with its call, the music changing at rounds 1, 4 and 7. Before rounds 4 and 7 a team going on hears its
// fanfare under STAGE COMPLETE, the screen closes over the scores of the teams that played and who goes on, and opens
// again after the server's move (BellatraRules.BannerMs) for the round's start; a team out hears its fall under STAGE
// FAIL and the screen stays closed until it leaves the arena; the end shows CONGRATULATIONS. A seal broken in the team
// shows its emblem and name (the latest over an earlier one), the room's seals quaking.
public sealed class HudBellatraShow
{
    private const double RoundDelay = 3;
    private const double LoadingSeconds = 3;
    private const double Banner = BellatraRules.BannerMs / 1000.0;
    private const double CloseGoingOn = Banner - 3;
    private const double CloseOut = Banner - 2;
    private const double Open = Banner + 1;
    private const double RoundAfterOpen = 1;

    private static readonly Color TableText = new(255, 246, 196);
    private static readonly Color TableAdvance = new(255, 216, 0);

    private readonly BellatraEffects _effects;
    private readonly string[] _sealNames;
    private readonly List<(double At, Action Run)> _due = [];
    private (string Name, string Score)[] _table = [];
    private string[] _advance = [];
    private double _now;

    public HudBellatraShow(BellatraEffectTable table)
    {
        _sealNames = [.. Enum.GetValues<BellatraSeal>().Select(static seal =>
            ClientText.Format("Bellatra.Seal." + seal, BellatraRules.PointsBonus.ToString("N0", CultureInfo.InvariantCulture)))];
        _effects = new BellatraEffects(table) { TextOf = SealName };
    }

    public IBellatraStage Stage { get; set; }

    public bool Showing => _effects.Running;

    public void Observe(BellatraBoardInfo was, BellatraBoardInfo board)
    {
        if (!board.Shown)
        {
            if (was.Shown)
            {
                Leave();
            }

            return;
        }

        if (!was.Shown)
        {
            Arrive(board);
            return;
        }

        if (board.Outcome != was.Outcome)
        {
            switch (board.Outcome)
            {
                case BellatraOutcome.GoesOn:
                    GoOn(board);
                    return;
                case BellatraOutcome.Out:
                    Fall(board);
                    return;
                case BellatraOutcome.Cleared:
                    Stage?.Music("");
                    Stage?.Play(EventCue.Ending);
                    _effects.Start(BellatraEffect.Congratulations);
                    return;
            }
        }

        if (board.Round > 0 && board.Round != was.Round && board.Outcome == BellatraOutcome.Playing)
        {
            var round = board.Round;
            At(RoundDelay, () => Round(round));
        }
    }

    // A chat line naming a seal broken in the player's team; a seal over an earlier one replaces it.
    public void Observe(GameTextId text)
    {
        if (!BellatraRules.TrySealOfNotice(text, out var seal))
        {
            return;
        }

        foreach (var shown in Enum.GetValues<BellatraSeal>())
        {
            _effects.Stop(BellatraEffect.QuakeSeal + (int)shown);
            _effects.Stop(BellatraEffect.QuakeSealName + (int)shown);
        }

        _effects.Start(BellatraEffect.QuakeSeal + (int)seal);
        _effects.Start(BellatraEffect.QuakeSealName + (int)seal);
        Stage?.Play(EventCue.Item);
        if (seal is BellatraSeal.Quake or BellatraSeal.Stun or BellatraSeal.Freeze or BellatraSeal.Rabbit)
        {
            Stage?.Quake();
        }
    }

    public void Update(double elapsedSeconds)
    {
        _now += elapsedSeconds;
        for (var index = 0; index < _due.Count; index++)
        {
            if (_due[index].At <= _now)
            {
                var run = _due[index].Run;
                _due.RemoveAt(index--);
                run();
            }
        }

        _effects.Advance(elapsedSeconds);
    }

    public void Draw(SpriteBatch batch, Func<string, Texture2D> texture, Texture2D pixel, SpriteFont font, float textScale)
    {
        _effects.Draw(batch, texture, pixel, font, textScale);
        if (_table.Length > 0 && _effects.IsHolding(BellatraEffect.CloseScreen))
        {
            DrawTable(batch, font, textScale);
        }
    }

    // The arena's first board: a new entrant's loading screen, or the music of a game the player comes back to.
    private void Arrive(BellatraBoardInfo board)
    {
        if (board.Round == 0 && board.Outcome == BellatraOutcome.Playing)
        {
            _effects.Start(BellatraEffect.Loading);
            At(LoadingSeconds, () => _effects.Stop(BellatraEffect.Loading));
        }
        else if (board.Round > 0 && board.Outcome is BellatraOutcome.Playing or BellatraOutcome.GoesOn)
        {
            Stage?.Music(MusicOf(board.Round));
        }
    }

    private void Round(int round)
    {
        if (round is 1 or 4 or 7)
        {
            Stage?.Music(MusicOf(round));
        }

        var count = Math.Clamp(round, 1, BellatraRounds.Count) - 1;
        _effects.Start(BellatraEffect.Round);
        _effects.Start(BellatraEffect.Count1 + count);
        _effects.Start(BellatraEffect.Start);
        Stage?.Play(EventCue.Round1 + count);
    }

    // Rounds 1 to 3 play the first stage's music, 4 to 6 the second's and 7 and 8 the top room's.
    private static string MusicOf(int round) => round <= 3 ? "BGM_CODE_SOD1" : round <= 6 ? "BGM_CODE_SOD2" : "BGM_CODE_SOD3";

    private void GoOn(BellatraBoardInfo board)
    {
        Stage?.Music("");
        Stage?.Play(EventCue.StageComplete);
        _effects.Start(BellatraEffect.Stage);
        _effects.Start(BellatraEffect.Complete);
        Tabulate(board);
        At(CloseGoingOn, Close);
        At(Open, OpenScreen);
        var round = board.Round;
        At(Open + RoundAfterOpen, () => Round(round));
    }

    private void Fall(BellatraBoardInfo board)
    {
        Stage?.Music("");
        Stage?.Play(EventCue.StageFail);
        _effects.Start(BellatraEffect.Stage);
        _effects.Start(BellatraEffect.Fail);
        Tabulate(board);
        At(CloseOut, Close);
    }

    private void Close()
    {
        _effects.Start(BellatraEffect.CloseScreen);
        Stage?.Play(EventCue.Turn);
    }

    private void OpenScreen()
    {
        if (!_effects.IsRunning(BellatraEffect.CloseScreen))
        {
            return;
        }

        _effects.Stop(BellatraEffect.CloseScreen);
        _effects.Start(BellatraEffect.OpenScreen);
        Stage?.Play(EventCue.Turn);
    }

    // Leaving the arena drops what was still to come and what plays, opening a closed screen.
    private void Leave()
    {
        var closed = _effects.IsRunning(BellatraEffect.CloseScreen);
        _due.Clear();
        _effects.Clear();
        if (closed)
        {
            _effects.Start(BellatraEffect.OpenScreen);
            Stage?.Play(EventCue.Turn);
        }
    }

    // The scores of the teams that played the stage, best first, and the teams going on (BellatraBoardInfo.StageTeams,
    // GoingOn).
    private void Tabulate(BellatraBoardInfo board)
    {
        var ranked = Enumerable.Range(0, board.TeamScores.Count).OrderByDescending(team => board.TeamScores[team]).ToArray();
        _table = [.. ranked.Where(team => (board.StageTeams >> team & 1) != 0)
            .Select(team => (TeamName(team), board.TeamScores[team].ToString("N0", CultureInfo.InvariantCulture)))];
        var going = ranked.Where(team => (board.GoingOn >> team & 1) != 0).Select(TeamName).ToArray();
        _advance = going switch
        {
            [var one] => ClientText.Format("Bellatra.AdvanceOne", one).Split('\n'),
            [var first, var second, ..] => ClientText.Format("Bellatra.AdvanceTwo", first, second).Split('\n'),
            _ => []
        };
    }

    private static string TeamName(int team) => ClientText.Format("Bellatra.Team", (team + 1).ToString(CultureInfo.InvariantCulture));

    // The table over the closed screen, where the original drew it: four teams from 170, fewer from 200, a row each 30.
    private void DrawTable(SpriteBatch batch, SpriteFont font, float textScale)
    {
        var (scale, offset) = BellatraEffects.Space(batch.GraphicsDevice.Viewport);
        var size = textScale * TableTextSize * scale;
        var top = _table.Length > 2 ? 170 : 200;
        for (var row = 0; row < _table.Length; row++)
        {
            var (name, score) = _table[row];
            var y = top + row * 30;
            Text(batch, font, name, offset + new Vector2(490, y) * scale, size, TableText);
            Text(batch, font, score, offset + new Vector2(680, y) * scale - new Vector2(font.MeasureString(score).X * size, 0), size, TableText);
        }

        for (var line = 0; line < _advance.Length; line++)
        {
            Text(batch, font, _advance[line], offset + new Vector2(490, 320 + line * 30) * scale, size, TableAdvance);
        }
    }

    private const float TableTextSize = 16f / 14f;

    private static void Text(SpriteBatch batch, SpriteFont font, string text, Vector2 at, float size, Color color)
    {
        batch.DrawString(font, text, at + Vector2.One, Color.Black, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);
        batch.DrawString(font, text, at, color, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);
    }

    private void At(double seconds, Action run) => _due.Add((_now + seconds, run));

    // A seal's name effect shows its name, made once.
    private string SealName(BellatraEffect effect) =>
        effect - BellatraEffect.QuakeSealName is var seal && seal >= 0 && seal < _sealNames.Length ? _sealNames[seal] : null;
}
