using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Units;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

// The skill sockets of the bottom bar (the selected left and right skills with their cooldown sweep) and the bar of
// the local player's running effects in the top-left corner, with the original keep icons and duration gauges.
public sealed class HudSkillHud : IDisposable
{
    private const int BuffOrigin = 10;
    private const int BuffIcon = 32;
    private const int BuffStep = 32;
    private const int BuffsPerRow = 10;
    // Two rows: a player keeping every crystal pet (MonsterCrystals.PetSlots) still sees the other effects.
    private const int MaxBuffs = BuffsPerRow * 2;
    private const int GaugeSteps = 72;
    private const float GaugeRadius = 14.14f;
    private const double FrameMs = GameUnits.TickMs;
    private const double SlidePixelsPerFrame = 2d;
    private const double FadeOutFrames = 180d;
    private const double BlinkFrames = 4d * 70d;
    private const double BlinkPeriodFrames = 31d;
    private static readonly string GaugeBackground = KeepIcons.Path("GA_.tga");
    private static readonly CharacterClassRegistry Classes = new();

    private readonly HudSprites _sprites;
    private readonly Dictionary<uint, string> _buttons = new();
    private readonly Dictionary<uint, string> _keepIcons = new();
    private readonly string[] _seconds = new string[600];
    private readonly List<(Rectangle Bounds, uint Code)> _drawnBuffs = new();
    private readonly List<StatusEntry> _ordered = new();
    private readonly HashSet<uint> _visibleCodes = new();
    private readonly Dictionary<uint, float> _buffX = new();
    // The bar's sort and prune, made once: the newest status first, at the time of the frame being drawn.
    private readonly List<uint> _gone = new();
    // The hovered tooltip's lines, made again only when what they show changes.
    private static readonly Color TipTitle = new(255, 230, 150);
    private readonly List<string> _tipLines = new();
    private (Tip Kind, long Subject, long Second) _tipKey = (Tip.Quest, -1, -1);
    private Comparison<StatusEntry> _newestFirst;
    private double _sortMs;
    private Texture2D[] _gauge;
    private double _lastBuffDrawMs = -1;
    private Rectangle _questTimer;
    private PlayerClass _class;

    public HudSkillHud(HudSprites sprites, GraphicsDevice device)
    {
        _sprites = sprites;
        _gauge = BuildGauge(device);
        _sprites.Request([GaugeBackground]);
        for (var second = 0; second < _seconds.Length; second++)
        {
            _seconds[second] = second < 60 ? second.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{second / 60}m";
        }
    }

    public void SetClass(PlayerClass characterClass)
    {
        _class = characterClass;
        _buttons.Clear();
        var requests = new List<string>();
        foreach (var skill in Classes.Get(characterClass).Skills)
        {
            if (skill.Code == 0 || skill.IconKey.Length == 0)
            {
                continue;
            }

            var button = HudArt.SkillButton(characterClass, skill.IconKey, false);
            _buttons[skill.Code] = button;
            requests.Add(button);
            if (SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.KeepIcon.Length > 0)
            {
                var keep = KeepIcons.Path(rule.KeepIcon + ".tga");
                _keepIcons[skill.Code] = keep;
                requests.Add(keep);
            }
        }

        _sprites.Request(requests);
    }

    // Icons over the bottom bar's sockets; y is the bar's vertical offset in the design space.
    public void DrawSockets(HudPainter painter, int y, uint leftSkill, uint rightSkill, SkillCooldownBook cooldowns, double nowMs)
    {
        DrawSocket(painter, HudBar.Socket(false), y, leftSkill, cooldowns, nowMs);
        DrawSocket(painter, HudBar.Socket(true), y, rightSkill, cooldowns, nowMs);
    }

    // The status whose icon is under the pointer in the buff bar, as last drawn (0 for none).
    public uint BuffAt(Point pointer)
    {
        foreach (var (bounds, code) in _drawnBuffs)
        {
            if (bounds.Contains(pointer))
            {
                return code;
            }
        }

        return 0;
    }

    // Whether a status's icon was drawn in the last frame.
    public bool Shows(uint code)
    {
        foreach (var (_, drawn) in _drawnBuffs)
        {
            if (drawn == code)
            {
                return true;
            }
        }

        return false;
    }

    // The original's continuous-skill bar: from (10, 10), one 32 px icon per running effect, the one running longest
    // first, ten to a row and a second row under the first. Each icon is the gauge background, a fan that fills clockwise
    // from 12 o'clock as the time runs out, and the keep icon over it. Icons fade in over 255 frames, fade out over the
    // last 180, blink every 31 frames in the last four seconds and slide 2 px per frame to their place; frames are the
    // original's 70 Hz.
    public void DrawBuffs(HudPainter painter, IReadOnlyList<StatusEntry> statuses, double nowMs)
    {
        var frames = _lastBuffDrawMs < 0 ? 0d : Math.Max(0d, nowMs - _lastBuffDrawMs) / FrameMs;
        _lastBuffDrawMs = nowMs;
        _drawnBuffs.Clear();
        _ordered.Clear();
        foreach (var status in statuses)
        {
            if (Icon(status.Code) is not null)
            {
                _ordered.Add(status);
            }
        }

        _sortMs = nowMs;
        _ordered.Sort(_newestFirst ??= (a, b) => Elapsed(b, _sortMs).CompareTo(Elapsed(a, _sortMs)));
        var shown = Math.Min(_ordered.Count, MaxBuffs);
        _visibleCodes.Clear();
        for (var index = 0; index < shown; index++)
        {
            var status = _ordered[index];
            _visibleCodes.Add(status.Code);
            var target = (float)(index % BuffsPerRow * BuffStep);
            var x = _buffX.TryGetValue(status.Code, out var current) ? current : target;
            var step = (float)(SlidePixelsPerFrame * frames);
            x = x < target ? Math.Min(target, x + step) : Math.Max(target, x - step);
            _buffX[status.Code] = x;
            var bounds = new Rectangle(BuffOrigin + (int)x, BuffRowY(index), BuffIcon, BuffIcon);
            _drawnBuffs.Add((bounds, status.Code));
            DrawBuff(painter, status, bounds, nowMs);
        }

        _gone.Clear();
        foreach (var code in _buffX.Keys)
        {
            if (!_visibleCodes.Contains(code))
            {
                _gone.Add(code);
            }
        }

        foreach (var code in _gone)
        {
            _buffX.Remove(code);
        }
    }

    private static int BuffRowY(int index) => BuffOrigin + index / BuffsPerRow * BuffStep;

    // Without a status book there are no effect icons to hit or to follow.
    public void ClearBuffs()
    {
        _drawnBuffs.Clear();
        _buffX.Clear();
    }

    // A running hunt adds the quest timer icon after the effects; a hunt against the clock fills its gauge as time runs out.
    public void DrawQuestTimer(HudPainter painter, QuestView quest, long remainingMs)
    {
        _questTimer = Rectangle.Empty;
        if (quest is null || quest.Timer == QuestTimerKind.None)
        {
            return;
        }

        var slot = _drawnBuffs.Count;
        var bounds = new Rectangle(BuffOrigin + slot % BuffsPerRow * BuffStep, BuffRowY(slot), BuffIcon, BuffIcon);
        _questTimer = bounds;
        painter.Stretch(GaugeBackground, bounds);
        if (quest.Timer == QuestTimerKind.Expiring && _gauge is not null)
        {
            var fraction = Math.Clamp(1d - (double)remainingMs / RankUpByMasterTrial.HuntTimeMs, 0d, 1d);
            painter.Image(_gauge[Math.Clamp((int)(fraction * GaugeSteps), 0, GaugeSteps)], bounds, new Color(0, 255, 200));
        }

        if (painter.Texture(HudQuestArt.Timer) is { } texture)
        {
            painter.Image(texture, bounds);
        }
    }

    public bool QuestTimerAt(Point pointer) => _questTimer.Contains(pointer);

    // The running quest, its hunt count and, for a hunt against the clock, the time left.
    public void DrawQuestTimerTooltip(HudPainter painter, QuestView quest, long remainingMs, Point pointer)
    {
        if (quest is null || !_questTimer.Contains(pointer))
        {
            return;
        }

        // The lines are made again only when the quest, its kills or the second left change.
        var key = (Tip.Quest, quest.Revision, Math.Max(0, remainingMs) / 1000);
        if (_tipKey != key)
        {
            _tipKey = key;
            _tipLines.Clear();
            _tipLines.Add(quest.Progress.Count > 0 ? quest.Progress[0].Title : ClientText.Get("Hud.Quest"));
            if (quest.KillsNeeded > 0)
            {
                _tipLines.Add($"{quest.TargetName}  {Math.Min(quest.Kills, quest.KillsNeeded)}/{quest.KillsNeeded}");
            }

            if (quest.Timer == QuestTimerKind.Expiring)
            {
                var left = TimeSpan.FromMilliseconds(Math.Max(0, remainingMs));
                _tipLines.Add(ClientText.Format("Hud.TimeLeft", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}")));
            }
        }

        DrawTip(painter, _questTimer);
    }

    private enum Tip
    {
        Quest,
        Buff
    }

    // The tooltip under an icon: its first line in the title colour, the others in white.
    private void DrawTip(HudPainter painter, Rectangle icon)
    {
        var widest = 0;
        foreach (var line in _tipLines)
        {
            widest = Math.Max(widest, painter.Measure(line));
        }

        var bounds = new Rectangle(icon.X, icon.Bottom + 2, widest + 16, _tipLines.Count * HudPainter.LineHeight + 8);
        painter.InfoBox(bounds);
        for (var line = 0; line < _tipLines.Count; line++)
        {
            painter.Text(_tipLines[line], bounds.X + 8, bounds.Y + 4 + line * HudPainter.LineHeight, line == 0 ? TipTitle : Color.White);
        }
    }

    // Name, remaining time and, for a summon, whether it fights the owner's target (manual) or any enemy (automatic).
    public void DrawBuffTooltip(HudPainter painter, IReadOnlyList<StatusEntry> statuses, Point pointer, double nowMs, Func<uint, bool?> summonAutomatic)
    {
        var code = BuffAt(pointer);
        StatusEntry status = null;
        for (var index = 0; code != 0 && index < statuses.Count && status is null; index++)
        {
            status = statuses[index].Code == code ? statuses[index] : null;
        }

        if (status is null)
        {
            return;
        }

        var crystalPet = MonsterCrystals.TryGetByPetStatus(code, out _);
        var seconds = !status.Permanent && status.DurationMs > 0 ? (int)Math.Ceiling(status.RemainingMs(nowMs) / 1000d) : -1;
        var automatic = summonAutomatic?.Invoke(code);
        // The lines are made again only when the status, the second left or the summon's mode change.
        var key = (Tip.Buff, (long)code << 2 | (automatic is null ? 0L : automatic.Value ? 1L : 2L), (long)seconds);
        if (_tipKey != key)
        {
            _tipKey = key;
            _tipLines.Clear();
            _tipLines.Add(Name(code));
            if (DataText.StatusEffect(code) is { } effect)
            {
                _tipLines.Add(effect);
            }

            if (seconds >= 0)
            {
                _tipLines.Add(crystalPet
                    ? ClientText.Format("Hud.TimeLeft", HudClock.MinutesSeconds(seconds))
                    : ClientText.Format("Hud.TimeLeftSeconds", seconds));
            }

            if (crystalPet)
            {
                _tipLines.Add(ClientText.Get("Hud.RightClickDismiss"));
            }

            if (automatic is { } auto)
            {
                _tipLines.Add(ClientText.Get(auto ? "Hud.AttackAuto" : "Hud.AttackManual"));
                _tipLines.Add(ClientText.Get("Hud.DoubleClickSwitch"));
            }
        }

        foreach (var (bounds, drawn) in _drawnBuffs)
        {
            if (drawn == code)
            {
                DrawTip(painter, bounds);
                return;
            }
        }
    }

    private void DrawBuff(HudPainter painter, StatusEntry status, Rectangle bounds, double nowMs)
    {
        var timed = !status.Permanent && status.DurationMs > 0;
        var elapsedFrames = Elapsed(status, nowMs) / FrameMs;
        var remainingFrames = timed ? status.RemainingMs(nowMs) / FrameMs : double.PositiveInfinity;
        var alpha = Math.Min(255d, elapsedFrames);
        if (remainingFrames < FadeOutFrames)
        {
            alpha = Math.Min(alpha, 255d - (FadeOutFrames - remainingFrames));
        }

        if (remainingFrames < BlinkFrames && (int)((BlinkFrames - remainingFrames) / BlinkPeriodFrames) % 2 == 1)
        {
            return;
        }

        var a = (int)Math.Clamp(alpha, 0d, 255d);
        var tint = new Color(a, a, a, a);
        painter.Stretch(GaugeBackground, bounds, null, tint);
        if (timed && _gauge is not null)
        {
            var fraction = Math.Clamp(1d - status.RemainingMs(nowMs) / status.DurationMs, 0d, 1d);
            var frame = Math.Clamp((int)(fraction * GaugeSteps), 0, GaugeSteps);
            var color = status.Harmful ? new Color(255, 70, 50) : new Color(0, 255, 200);
            painter.Image(_gauge[frame], bounds, Color.FromNonPremultiplied(color.R, color.G, color.B, a));
        }

        if (Icon(status.Code) is { } icon && painter.Texture(icon) is { } texture)
        {
            painter.Image(texture, bounds, tint);
        }
    }

    private static string ItemStatusIcon(uint code) => ItemStatuses.Icon(code);

    private static double Elapsed(StatusEntry status, double nowMs) =>
        status.Permanent || status.DurationMs <= 0 ? double.MaxValue : Math.Max(0d, status.DurationMs - status.RemainingMs(nowMs));

    private static string Name(uint code) =>
        SkillRuleCatalog.TryGet(code, out var rule) ? DataText.SkillName(code, rule.Name) : ClassSkillCatalog.TryGet(code, out var skill) ? DataText.SkillName(skill)
            : ItemStatuses.Name(code) ?? ClientText.Format("Hud.Effect", code.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));

    // The gauge frames: a white fan of k of the 72 steps (k = 0..72), antialiased, with premultiplied alpha.
    private static Texture2D[] BuildGauge(GraphicsDevice device)
    {
        const int samples = 4;
        var frames = new Texture2D[GaugeSteps + 1];
        var pixels = new Color[BuffIcon * BuffIcon];
        for (var step = 0; step <= GaugeSteps; step++)
        {
            var sweep = step * MathHelper.TwoPi / GaugeSteps;
            for (var py = 0; py < BuffIcon; py++)
            {
                for (var px = 0; px < BuffIcon; px++)
                {
                    var inside = 0;
                    for (var sy = 0; sy < samples; sy++)
                    {
                        for (var sx = 0; sx < samples; sx++)
                        {
                            var dx = px + (sx + 0.5f) / samples - BuffIcon / 2f;
                            var dy = py + (sy + 0.5f) / samples - BuffIcon / 2f;
                            // Clockwise from 12 o'clock with y pointing down.
                            var angle = MathF.Atan2(dx, -dy);
                            if (angle < 0f)
                            {
                                angle += MathHelper.TwoPi;
                            }

                            if (dx * dx + dy * dy <= GaugeRadius * GaugeRadius && angle <= sweep)
                            {
                                inside++;
                            }
                        }
                    }

                    var value = (byte)(inside * 255 / (samples * samples));
                    pixels[py * BuffIcon + px] = new Color(value, value, value, value);
                }
            }

            frames[step] = new Texture2D(device, BuffIcon, BuffIcon, false, SurfaceFormat.Color) { Name = "Buff gauge" };
            frames[step].SetData(pixels);
        }

        return frames;
    }

    public void Dispose()
    {
        if (_gauge is null)
        {
            return;
        }

        foreach (var frame in _gauge)
        {
            frame.Dispose();
        }

        _gauge = null;
    }

    private void DrawSocket(HudPainter painter, Rectangle socket, int y, uint skillCode, SkillCooldownBook cooldowns, double nowMs)
    {
        if (skillCode is 0 or HudSkillBindings.BasicAttackId || !_buttons.TryGetValue(skillCode, out var button) || painter.Texture(button) is not { } texture)
        {
            return;
        }

        var bounds = new Rectangle(socket.X + (socket.Width - 40) / 2, socket.Y + y + (socket.Height - 40) / 2, 40, 40);
        painter.Image(texture, bounds);
        var fraction = cooldowns?.Fraction(skillCode, nowMs) ?? 0f;
        if (fraction <= 0f)
        {
            return;
        }

        // The sweep drains from the top as the gauge refills, like the original's mastery gauge.
        var shade = (int)MathF.Ceiling(bounds.Height * fraction);
        painter.ColorBox(new Rectangle(bounds.X, bounds.Y, bounds.Width, shade), 0, 0, 0, 160);
        painter.ColorBox(new Rectangle(bounds.X, bounds.Y + shade - 1, bounds.Width, 1), 255, 220, 120, 200);
        var seconds = (int)Math.Ceiling(cooldowns.RemainingMs(skillCode, nowMs) / 1000d);
        painter.TextCentered(_seconds[Math.Clamp(seconds, 0, _seconds.Length - 1)], bounds.X, bounds.Right, bounds.Y + 13, new Color(255, 240, 200));
    }

    private string Icon(uint code)
    {
        // Buffs another class's skill put on the player (an ally's blessing) bring their own keep icon.
        if (!_keepIcons.ContainsKey(code) && SkillRuleCatalog.TryGet(code, out var rule) && rule.KeepIcon.Length > 0)
        {
            var path = KeepIcons.Path(rule.KeepIcon + ".tga");
            _keepIcons[code] = path;
            _sprites.Request([path]);
        }

        // Item effects (scrolls, force orbs) show the item's own buff icon.
        if (!_keepIcons.ContainsKey(code) && ItemStatusIcon(code) is { } itemIcon)
        {
            _keepIcons[code] = itemIcon;
            _sprites.Request([itemIcon]);
        }

        if (_keepIcons.TryGetValue(code, out var keep) && !_sprites.IsMissing(keep))
        {
            return keep;
        }

        return _buttons.GetValueOrDefault(code);
    }
}
