using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Gameplay.UI.Widgets;

// Bless Castle's siege board, on the right under the target frame while a siege runs and the player is in the castle
// field: the time left, the clan holding the castle, the castle door, the crystal towers standing, and the best clans with their share of the damage
// (the player's own clan in gold). The time left counts down from the state's arrival on the HUD clock. The lines are
// made again when the server's state changes and when the time left reaches another second.
public sealed class HudSiegeBoard
{
    private const double DriftMs = 1500;
    private static readonly Color Title = HudSideBoard.Title;
    private static readonly Color Own = HudSideBoard.Own;
    private static readonly Color Line = HudSideBoard.Line;

    private CastleInfo _castle = CastleInfo.None;
    private readonly List<(string Text, string Right, Color Color)> _lines = new();
    private long _linesSecond = -1;
    private double _endMs;
    private long _ownClan;

    public CastleInfo Castle => _castle;

    // The countdown keeps its own pace unless the server's time left drifts from it by more than a second and a half (a
    // new siege, a game master's new end).
    public void Apply(CastleInfo castle, double nowMs)
    {
        var endMs = nowMs + castle.SecondsLeft * 1000d;
        if (_castle.Siege != SiegeState.Running || Math.Abs(endMs - _endMs) > DriftMs)
        {
            _endMs = endMs;
        }

        _castle = castle;
        _linesSecond = -1;
    }

    public void Draw(HudPainter painter, HudLayout layout, bool inCastle, double nowMs, long ownClan)
    {
        if (!inCastle || _castle.Siege != SiegeState.Running)
        {
            return;
        }

        var left = (long)Math.Ceiling(Math.Max(0d, _endMs - nowMs) / 1000d);
        if (left != _linesSecond || ownClan != _ownClan)
        {
            Compose(left, ownClan);
        }

        HudSideBoard.Draw(painter, layout, _lines);
    }

    private void Compose(long left, long ownClan)
    {
        _linesSecond = left;
        _ownClan = ownClan;
        _lines.Clear();
        _lines.Add((ClientText.Get(_castle.League ? "Siege.League" : "Siege.Title"), "", Title));
        _lines.Add((ClientText.Format("Hud.TimeLeft", HudClock.MinutesSeconds(left)), "", Line));
        _lines.Add((_castle.OwnerClanId == 0 ? ClientText.Get("Siege.NoOwner") : ClientText.Format("Siege.HeldBy", _castle.OwnerName), "", Line));
        _lines.Add((ClientText.Get(_castle.DoorDown ? "Siege.DoorDown" : "Siege.DoorStands"), "", Line));
        _lines.Add((ClientText.Format("Siege.Crystals", _castle.Crystals.ToString(CultureInfo.InvariantCulture),
            CastleSiegeRules.TowerSlots.ToString(CultureInfo.InvariantCulture)), "", Line));
        for (var index = 0; index < _castle.Top.Count; index++)
        {
            var clan = _castle.Top[index];
            var share = _castle.TotalDamage > 0 ? clan.Damage * 100 / _castle.TotalDamage : 0;
            _lines.Add(($"{index + 1}. {clan.Name}", $"{share}%", clan.ClanId == ownClan ? Own : Line));
        }
    }
}
