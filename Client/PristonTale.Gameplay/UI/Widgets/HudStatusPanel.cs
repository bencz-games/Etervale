using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Gameplay.UI.Clans;

namespace PristonTale.Gameplay.UI.Widgets;

// Original status window: Status.bmp with its text columns, the attribute point arrows and the organic, poison, fire,
// lightning and ice resistance boxes.
public sealed class HudStatusPanel
{
    private const int TextShift = 2;
    // The experience and the next level's take a smaller face to fit their boxes; past level 79 they read in millions.
    private const float ExperienceTextScale = 12f / HudPainter.BakePixels;
    private const int ExperienceIndex = 3;
    private const int NextLevelIndex = 4;

    // The clan's row, 20 px under the name's, its mark and name centred together; the mark 1 px above the name's top.
    private const int NameIndex = 1;
    private const int ClanRowStep = 20;
    private const int ClanMarkLift = TextShift + 1;
    private const int MillionsAboveLevel = 79;
    private const ulong Million = 1_000_000;
    private static readonly int[] PointButtonY = [431, 452, 472, 491, 511];
    private static readonly (int Left, int Right, int Y)[] Values =
    [
        (75, 160, 442 + TextShift), (75, 160, 463 + TextShift), (75, 128, 503 + TextShift), (75, 160, 543 + TextShift), (75, 160, 563 + TextShift),
        (391, 482, 429 + TextShift), (391, 482, 454 + TextShift), (391, 482, 479 + TextShift),
        (585, 632, 433 + TextShift), (585, 632, 454 + TextShift), (585, 632, 473 + TextShift), (585, 632, 493 + TextShift), (585, 632, 513 + TextShift),
        (710, 769, 455 + TextShift - 1), (710, 769, 474 + TextShift - 1), (710, 769, 495 + TextShift - 1), (710, 769, 515 + TextShift - 1),
        (710, 769, 534 + TextShift - 1), (585, 632, 533 + TextShift - 1),
        (220, 248, 457 + TextShift), (289, 315, 456 + TextShift), (220, 248, 494 + TextShift), (289, 315, 494 + TextShift), (220, 248, 531 + TextShift)
    ];

    private readonly string[] _text = new string[Values.Length];
    private OwnerState _state;

    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";

    // The local player's clan, empty without one.
    public string ClanName { get; set; } = "";

    private string _clanFitted = "";
    private string _clanShown = "";

    public static Rectangle AllocateButton(Point origin, int index) => new(origin.X + 566, origin.Y + PointButtonY[index] + 1, 18, 17);
    public static Rectangle Close(Point origin) => new(origin.X + 251, origin.Y + 565, 20, 20);
    public static Rectangle Window(Point origin) => new(origin.X, origin.Y + 400, HudLayout.DesignWidth, 200);

    public void Draw(HudPainter painter, Point origin, OwnerState state, Point pointer, bool pressed, bool interactive)
    {
        painter.Sprite(HudArt.Status, origin.X, origin.Y + 400);
        if (state is null)
        {
            return;
        }

        Refresh(state);
        if (state.StatPoints > 0)
        {
            painter.Sprite(HudArt.StatusPoint, origin.X + 520, origin.Y + 531);
            for (var index = 0; index < PointButtonY.Length; index++)
            {
                var button = AllocateButton(origin, index);
                painter.Sprite(HudArt.StatusArrow, button.X, button.Y);
                if (interactive && pressed && button.Contains(pointer))
                {
                    painter.Sprite(HudArt.StatusArrowDown, button.X + 1, button.Y + 1);
                }
            }
        }

        for (var index = 0; index < Values.Length; index++)
        {
            var (left, right, y) = Values[index];
            if (index is ExperienceIndex or NextLevelIndex)
            {
                painter.TextCentered(_text[index], origin.X + left, origin.X + right, origin.Y + y, ExperienceTextScale);
            }
            else
            {
                painter.TextCentered(_text[index], origin.X + left, origin.X + right, origin.Y + y);
            }
        }

        if (ClanName.Length > 0)
        {
            var (nameLeft, nameRight, _) = Values[NameIndex];
            if (!ReferenceEquals(ClanName, _clanShown))
            {
                (_clanShown, _clanFitted) = (ClanName, painter.Fit(ClanName, nameRight - nameLeft - ClanMarks.Span));
            }

            var left = origin.X + (nameLeft + nameRight - painter.Measure(_clanFitted) - ClanMarks.Span) / 2;
            var clanY = origin.Y + Values[NameIndex].Y + ClanRowStep;
            painter.ClanMark(left, clanY - ClanMarkLift);
            painter.Text(_clanFitted, left + ClanMarks.Span, clanY);
        }
    }

    private void Refresh(OwnerState state)
    {
        if (ReferenceEquals(state, _state))
        {
            return;
        }

        _state = state;
        _text[0] = ClassName;
        _text[1] = Name;
        _text[2] = state.Level.ToString();
        _text[ExperienceIndex] = ExperienceText(state.Experience, state.Level);
        _text[NextLevelIndex] = ExperienceText(state.NextLevel, state.Level);
        _text[5] = $"{state.Life}/{state.MaxLife}";
        _text[6] = $"{state.Mana}/{state.MaxMana}";
        _text[7] = $"{state.Stamina}/{state.MaxStamina}";
        _text[8] = state.Strength.ToString();
        _text[9] = state.Spirit.ToString();
        _text[10] = state.Talent.ToString();
        _text[11] = state.Agility.ToString();
        _text[12] = state.Health.ToString();
        _text[13] = state.AttackRating.ToString();
        _text[14] = $"{Math.Max(0, state.DamageMin)}-{Math.Max(0, state.DamageMax)}";
        _text[15] = state.Defence.ToString();
        _text[16] = state.Absorption.ToString();
        _text[17] = state.MoveSpeed.ToString();
        _text[18] = state.StatPoints > 0 ? state.StatPoints.ToString() : "";
        var resistances = state.Resistances;
        _text[19] = resistances.Bionic.ToString();
        _text[20] = resistances.Poison.ToString();
        _text[21] = resistances.Fire.ToString();
        _text[22] = resistances.Lighting.ToString();
        _text[23] = resistances.Ice.ToString();
    }

    public void Invalidate() => _state = null;

    public static string ExperienceText(ulong experience, int level) =>
        level > MillionsAboveLevel ? $"{experience / Million}M" : experience.ToString(CultureInfo.InvariantCulture);
}
