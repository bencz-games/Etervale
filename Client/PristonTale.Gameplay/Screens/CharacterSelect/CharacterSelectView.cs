using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal readonly record struct CharSelectInfo(string Name, int Level, string BroodName, string ClassName, string ClanName);
internal readonly record struct CharSelectJobView(PlayerClass Class, Vector3 HeadWorld, bool Hovered, bool Selected);

internal sealed class CharSelectViewModel
{
    public int StateIndex;
    public string ErrorMessage = string.Empty;

    public double ElapsedSeconds;

    public bool RosterHasChars;
    public bool HasSelectedRoster;
    public bool ShowNewCharacterPrompt;
    public CharSelectInfo? Info;

    public int HoveredRace = -1;
    public int JobCount;
    public CharSelectJobView[] Jobs = new CharSelectJobView[5];

    public int FaceIndex;
    public int HoveredFace = -1;
    public CharacterBrood FaceBrood;
    public string NameDisplay = string.Empty;
    public bool CaretVisible;
    public Texture2D FacePortrait;
}

internal sealed class CharacterSelectView
{
    // The selected character's level as text, made when the selection's level changes.
    private int _levelShown = -1;
    private string _levelText = "";

    private const int InfoPanelValueColumnX = 102;
    private const float InfoPanelValueScale = 0.45f;
    private const float InfoPanelValueBaselineAdjustment = -2f;

    private readonly SpriteBatch _sb;
    private readonly SpriteFont _font;
    private readonly CharSelectAssets _assets;
    private readonly CharacterSelectCamera _camera;

    public CharacterSelectView(SpriteBatch spriteBatch, SpriteFont font, CharSelectAssets assets, CharacterSelectCamera camera)
    {
        _sb = spriteBatch;
        _font = font;
        _assets = assets;
        _camera = camera;
    }

    public void Draw(Viewport viewport, CharSelectViewModel m)
    {
        _sb.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp);

        DrawFrame(viewport);
        DrawTitle(viewport, m.StateIndex);
        switch (m.StateIndex)
        {
            case 0:
                DrawCharacterSelect(viewport, m);
                break;
            case 1:
                DrawRaceSelect(viewport, m);
                break;
            case 2:
                DrawJobSelect(viewport, m);
                break;
            default:
                DrawFaceSelect(viewport, m);
                break;
        }

        if (!string.IsNullOrEmpty(m.ErrorMessage))
        {
            _sb.DrawString(_font, m.ErrorMessage,
                new Vector2(viewport.Width / 2f - 128f, viewport.Height / 2f + viewport.Height / 6f), Color.Gold);
        }

        _sb.End();
    }

    private void DrawFrame(Viewport viewport)
    {
        var w = viewport.Width;
        var h = viewport.Height;

        var bar = new Color(0, 0, 0, 150);
        _sb.Draw(_assets.Pixel, new Rectangle(0, 0, w, 70), bar);
        _sb.Draw(_assets.Pixel, new Rectangle(0, h - 50, w, 70), bar);

        if (_assets.FrameT != null && _assets.FrameD != null)
        {
            for (var x = 4; x < w; x += 64)
            {
                _sb.Draw(_assets.FrameT, new Rectangle(x, 68, 64, 8), Color.White);
                _sb.Draw(_assets.FrameD, new Rectangle(x, h - 54, 64, 8), Color.White);
            }
        }

        if (_assets.FrameL != null && _assets.FrameR != null)
        {
            for (var y = 70; y < h; y += 64)
            {
                _sb.Draw(_assets.FrameL, new Rectangle(0, y, 8, 64), Color.White);
                _sb.Draw(_assets.FrameR, new Rectangle(w - 8, y, 8, 64), Color.White);
            }
        }

        if (_assets.FrameCn1 != null)
        {
            _sb.Draw(_assets.FrameCn1, new Rectangle(0, 68, 130, 64), Color.White);
        }

        if (_assets.FrameCn2 != null)
        {
            _sb.Draw(_assets.FrameCn2, new Rectangle(w - 130, 68, 130, 64), Color.White);
        }

        if (_assets.FrameCn3 != null)
        {
            _sb.Draw(_assets.FrameCn3, new Rectangle(0, h - (64 + 48), 130, 64), Color.White);
        }

        if (_assets.FrameCn4 != null)
        {
            _sb.Draw(_assets.FrameCn4, new Rectangle(w - 130, h - (64 + 48), 130, 64), Color.White);
        }
    }

    private void DrawTitle(Viewport viewport, int stateIndex)
    {
        if (_assets.TitleBox != null)
        {
            _sb.Draw(_assets.TitleBox, new Rectangle(viewport.Width / 2 - 128, 0, 256, 128), Color.White);
        }

        if (_assets.TitleTxt != null)
        {
            var row = stateIndex switch { 0 => 0, 1 => 1, 2 => 2, _ => 3 };
            var src = new Rectangle(0, row * 30, 128, 30);
            _sb.Draw(_assets.TitleTxt, new Rectangle(viewport.Width / 2 - 64, 53, 128, 30), src, Color.White);
        }
    }

    private void DrawCharacterSelect(Viewport viewport, CharSelectViewModel m)
    {
        var y = CharSelectLayout.ButtonRowY(viewport);
        var primary = m.RosterHasChars ? _assets.BtSelect : _assets.BtNewChar;
        if (primary != null)
        {
            _sb.Draw(primary, new Vector2(CharSelectLayout.TwoButtonPrimaryX(viewport), y), Color.White);
        }

        if (_assets.BtExit != null)
        {
            _sb.Draw(_assets.BtExit, new Vector2(CharSelectLayout.TwoButtonQuitX(viewport), y), Color.White);
        }

        if (m.HasSelectedRoster && _assets.BtDelete != null)
        {
            _sb.Draw(_assets.BtDelete, new Vector2(CharSelectLayout.TwoButtonDeleteX, y), Color.White);
        }

        if (m.ShowNewCharacterPrompt && _assets.NewCharBig != null)
        {
            var opacity = m.RosterHasChars
                ? 1f
                : 0.5f + 0.5f * (0.5f + 0.5f * MathF.Sin((float)m.ElapsedSeconds * 3f));
            _sb.Draw(_assets.NewCharBig, CharSelectLayout.NewCharPrompt(viewport), Color.White * opacity);
        }

        if (m.Info is { } info)
        {
            DrawInfoPanel(viewport, info);
        }
    }

    private void DrawRaceSelect(Viewport viewport, CharSelectViewModel model)
    {
        var temp = CharSelectLayout.TempscronMark(viewport);
        var mor = CharSelectLayout.MorionMark(viewport);
        DrawRaceMark(temp, _assets.MarkTempscron, _assets.LogoTempscron);
        DrawRaceMark(mor, _assets.MarkMorion, _assets.LogoMorion);

        if (model.HoveredRace == 0)
        {
            DrawRaceDescription(temp.X - 175, temp.Y + 150, _assets.TempscronDescription);
        }
        else if (model.HoveredRace == 1)
        {
            DrawRaceDescription(mor.X, mor.Y + 150, _assets.MorionDescription);
        }

        DrawThreeButtons(viewport);
    }

    private void DrawJobSelect(Viewport viewport, CharSelectViewModel m)
    {
        for (var index = 0; index < m.JobCount; index++)
        {
            var job = m.Jobs[index];
            if (_assets.TryClassLabel(job.Class, job.Hovered || job.Selected, out var label) && label != null)
            {
                var head = viewport.Project(job.HeadWorld, _camera.Projection, _camera.View, Matrix.Identity);
                _sb.Draw(label, new Vector2(head.X - 64f, head.Y - 30f), Color.White);
            }

            if (job.Hovered && !job.Selected)
            {
                DrawJobDescription(viewport, job.Class);
            }
        }

        DrawThreeButtons(viewport);
    }

    private void DrawFaceSelect(Viewport viewport, CharSelectViewModel m)
    {
        DrawFaceBackdrop(viewport);
        if (m.FacePortrait != null)
        {
            _sb.Draw(m.FacePortrait, CharSelectLayout.FacePortrait(viewport), Color.White);
        }

        for (var i = 0; i < CharSelectLayout.FacesPerJob; i++)
        {
            var rect = CharSelectLayout.FaceThumbDraw(viewport, i);
            var selected = i == m.FaceIndex;
            var hovered = i == m.HoveredFace;
            var box = selected || hovered ? _assets.FaceBoxSelected : _assets.FaceBoxNormal;
            var opacity = 0.59f;
            var boxOpacity = 1f;
            if (selected)
            {
                opacity = 1f;
            }
            else if (hovered)
            {
                opacity = 0.78f;
                boxOpacity = 0.71f;
            }

            if (box != null)
            {
                _sb.Draw(box, rect, Color.White * boxOpacity);
            }

            var thumb = _assets.FaceThumb(i, selected);
            if (thumb != null)
            {
                var yOffset = m.FaceBrood == CharacterBrood.Tempskron ? 4 : 0;
                _sb.Draw(thumb, new Rectangle(rect.X, rect.Y + yOffset, rect.Width, rect.Height), Color.White * opacity);
            }
        }

        var nameBoxX = viewport.Width / 2 - 95;
        var nameY = CharSelectLayout.FaceNameDecorationY(viewport);
        if (_assets.FaceNameBox != null)
        {
            _sb.Draw(_assets.FaceNameBox, new Rectangle(nameBoxX, nameY, 64, 16), Color.White);
            _sb.Draw(_assets.FaceNameBox, new Rectangle(nameBoxX + 128, nameY, 64, 16), Color.White);
        }

        var shown = m.NameDisplay + (m.CaretVisible ? "_" : string.Empty);
        var inputPosition = CharSelectLayout.FaceInputPosition(viewport);
        SmallValue(shown, inputPosition.X, inputPosition.Y, Color.White);

        DrawThreeButtons(viewport);
    }

    private void DrawThreeButtons(Viewport viewport)
    {
        var y = CharSelectLayout.ButtonRowY(viewport);
        if (_assets.BtSelect != null)
        {
            _sb.Draw(_assets.BtSelect, new Vector2(CharSelectLayout.ThreeButtonOkX(viewport), y), Color.White);
        }

        if (_assets.BtBack != null)
        {
            _sb.Draw(_assets.BtBack, new Vector2(CharSelectLayout.ThreeButtonBackX(viewport), y), Color.White);
        }

        if (_assets.BtExit != null)
        {
            _sb.Draw(_assets.BtExit, new Vector2(CharSelectLayout.ThreeButtonQuitX(viewport), y), Color.White);
        }
    }

    private void DrawInfoPanel(Viewport viewport, CharSelectInfo info)
    {
        var boxX = viewport.Width / 2 - 128;
        var boxY = viewport.Height / 2 + viewport.Height / 5;
        var rect = new Rectangle(boxX, boxY, 256, 128);
        if (_assets.InfoBackPanel != null)
        {
            _sb.Draw(_assets.InfoBackPanel, rect, Color.White);
        }

        // The overlay's top 33 rows hold the clan line: a character in a clan shows them and its rows move 20 px down; its
        // mark fills the line's box from 3 px left of the values' column.
        var inClan = info.ClanName.Length > 0;
        if (_assets.InfoOverlay != null)
        {
            if (inClan)
            {
                _sb.Draw(_assets.InfoOverlay, rect, Color.White);
            }
            else
            {
                _sb.Draw(_assets.InfoOverlay, new Rectangle(boxX, boxY + 13, 256, 95), new Rectangle(0, 33, 256, 95), Color.White);
            }
        }

        var valueX = boxX + InfoPanelValueColumnX;
        var top = boxY + (inClan ? 20 : 0) + InfoPanelValueBaselineAdjustment;
        if (inClan)
        {
            _sb.Draw(_assets.ClanMark, new Vector2(valueX - 3, boxY + 17), Color.White);
            SmallValue(info.ClanName, valueX + 17, top, new Color(230, 240, 255));
        }

        SmallValue(info.Name, valueX, top + 20, new Color(255, 250, 235));
        if (info.Level != _levelShown)
        {
            (_levelShown, _levelText) = (info.Level, info.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        SmallValue(_levelText, valueX, top + 40, new Color(255, 220, 100));
        SmallValue(info.BroodName, valueX, top + 60, new Color(255, 250, 235));
        SmallValue(info.ClassName, valueX, top + 80, new Color(255, 250, 235));
    }

    private void SmallValue(string text, float x, float y, Color color) =>
        _sb.DrawString(_font, text, new Vector2(x, y), color, 0f, Vector2.Zero, InfoPanelValueScale, SpriteEffects.None, 0f);

    private void DrawRaceMark(Rectangle rectangle, Texture2D mark, Texture2D logo)
    {
        if (mark != null)
        {
            _sb.Draw(mark, rectangle, Color.White);
        }

        if (logo != null)
        {
            _sb.Draw(logo, new Rectangle(rectangle.X - 6, rectangle.Y + 118, 128, 32), Color.White);
        }
    }

    private void DrawRaceDescription(int x, int y, Texture2D description)
    {
        if (_assets.RaceBoxMain != null)
        {
            _sb.Draw(_assets.RaceBoxMain, new Rectangle(x, y, 256, 256), Color.White);
        }

        if (_assets.RaceBoxEnd != null)
        {
            _sb.Draw(_assets.RaceBoxEnd, new Rectangle(x + 256, y, 32, 256), Color.White);
        }

        if (description != null)
        {
            _sb.Draw(description, new Vector2(x + 19, y + 12), Color.White);
        }
    }

    private void DrawJobDescription(Viewport viewport, PlayerClass playerClass)
    {
        var x = viewport.Width / 2 - 140;
        var y = viewport.Height / 5;
        if (_assets.JobBoxMain != null)
        {
            _sb.Draw(_assets.JobBoxMain, new Rectangle(x, y, 256, 256), Color.White);
        }

        if (_assets.JobBoxEnd != null)
        {
            _sb.Draw(_assets.JobBoxEnd, new Rectangle(x + 256, y, 32, 256), Color.White);
        }

        if (_assets.TryClassDescription(playerClass, out var description) && description != null)
        {
            _sb.Draw(description, new Vector2(x + 10, y + 4), Color.White);
        }
    }

    private void DrawFaceBackdrop(Viewport viewport)
    {
        if (_assets.FaceBigBoxTop != null)
        {
            _sb.Draw(_assets.FaceBigBoxTop, CharSelectLayout.FaceBackdropTop(viewport), Color.White);
        }

        if (_assets.FaceBigBoxBottom != null)
        {
            _sb.Draw(_assets.FaceBigBoxBottom, CharSelectLayout.FaceBackdropBottom(viewport), Color.White);
        }
    }
}
