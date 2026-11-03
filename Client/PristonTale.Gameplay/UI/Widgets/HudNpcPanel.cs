using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Widgets;

// NPC dialog in the original help box (at 50,45, 9x10 tiles): the NPC's name, the line it says and one button per typed
// service in the title plate. Quest roles get no button: talking to the NPC asks its quests, which answer with their own
// dialog. Skill masters also get the skill master box (ReStartMain.bmp) with the skill, class-change and exit buttons;
// the class-change button works when the master offers this player a rank-up. A teleporter's destinations replace the
// service buttons once its service is opened.
public sealed class HudNpcPanel(
    Func<ulong, Task<NpcInteraction>> interact,
    IClientLog log,
    Action<NpcInteraction> opened = null,
    Action closed = null)
{
    public const int BoxX = 50;
    public const int BoxY = 45;
    public const int SizeW = 9;
    public const int SizeH = 10;
    public const int VisibleLines = 16;
    public const int ButtonWidth = 152;
    public const int ButtonHeight = 28;
    private const int TextWidth = SizeW * 32 + 64 - 64;
    private const int ButtonColumns = 2;
    private const int MaxButtonRows = 3;
    private const int MaxDestinations = 4;
    private static readonly Color Unavailable = new(130, 130, 130);
    private static readonly Color Highlight = HudTextColors.Marked;

    private readonly HudRequest<NpcInteraction> _request = new(log, "NPC interaction failed");
    private string[] _dialog = [];
    private NpcServiceOffer[] _services = [];
    private bool _dismissed;
    private int _firstLine;
    public NpcInteraction Current { get; private set; }
    public bool ReadyToOpen { get; private set; }
    public bool CanTrain => Current?.Offers(NpcServiceKind.SkillMaster) == true;

    // The NPC gives, runs or ends quests: a quest giver or script, a skill master (rank-ups), the wing exchange or the
    // aging master (the aging brew of a rank-up).
    public bool TakesPartInQuests => Current is { Ok: true } current && current.Services.Any(IsQuestRole);

    private static bool IsQuestRole(NpcServiceOffer offer) =>
        offer.Kind is NpcServiceKind.QuestGiver or NpcServiceKind.QuestScript or NpcServiceKind.SkillMaster or NpcServiceKind.ItemAging
        || offer.Kind == NpcServiceKind.WarpGate && offer.Value <= 1;

    // As in the original, an NPC whose only working service is a shop, a warehouse, the aging or the mixing opens that
    // window at once, and a skill master opens the skill window in training mode (its class change is a button there).
    public NpcServiceOffer DirectService =>
        Current is not { Ok: true } ? null
        : CanTrain ? _services.Length == 0 ? Current.Services.First(static offer => offer.Kind == NpcServiceKind.SkillMaster) : null
        : _services.Length == 1 && _services[0].Kind is NpcServiceKind.Shop or NpcServiceKind.Warehouse or NpcServiceKind.ItemAging or NpcServiceKind.ItemMix
            ? _services[0]
            : null;
    public IReadOnlyList<TeleportOffer> Destinations { get; private set; } = [];
    public IReadOnlyList<NpcServiceOffer> Services => _services;

    public static Rectangle Box => HudPainter.HelpBoxBounds(BoxX, BoxY, SizeW, SizeH);
    public static Rectangle Title => HudPainter.HelpBoxTitle(BoxX, BoxY, SizeW);
    public static Rectangle OkButton => HudPainter.HelpBoxOk(BoxX, BoxY, SizeW, SizeH);

    // Service buttons fill two columns upward from the OK button, at most three rows; destinations take one wide row each.
    public static Rectangle ServiceButton(int index, int count, int columns = ButtonColumns)
    {
        var rows = (count + columns - 1) / columns;
        var top = OkButton.Y - 10 - rows * (ButtonHeight + 4);
        var width = columns == 1 ? ButtonWidth * 2 + 8 : ButtonWidth;
        return new Rectangle(BoxX + 20 + index % columns * (ButtonWidth + 8), top + index / columns * (ButtonHeight + 4), width, ButtonHeight);
    }

    public static Rectangle DestinationButton(int index, int count) => ServiceButton(index, count, 1);

    public void Request(ulong entityId)
    {
        if (!_request.Pending && interact is not null)
        {
            Current = null;
            _dismissed = false;
            _firstLine = 0;
            Destinations = [];
            _request.Start(() => interact(entityId), _ => NpcInteraction.Rejected(HudRequest.ConnectionError));
        }
    }

    public void Update(SpriteFont font)
    {
        ReadyToOpen = false;
        if (!_request.TryTake(out var current))
        {
            return;
        }

        Current = current;
        if (_dismissed)
        {
            Current = null;
            return;
        }

        _dialog = Current.Ok
            ? Current.Dialog.SelectMany(line => HudTextLayout.Wrap(font, line, TextWidth, HudPainter.TextScale)).ToArray()
            : HudTextLayout.Wrap(font, Current.Message, TextWidth, HudPainter.TextScale);
        _services = Current.Ok ? Buttons(Current) : [];
        _firstLine = 0;
        ReadyToOpen = true;
        opened?.Invoke(Current);
    }

    // The skill master keeps its own box; services that share a label show one button, and only working services show.
    private static NpcServiceOffer[] Buttons(NpcInteraction interaction)
    {
        return interaction.Services
            .Where(offer => offer.Kind != NpcServiceKind.SkillMaster && offer.Available && (offer.Kind == NpcServiceKind.ItemAging || !IsQuestRole(offer)))
            .GroupBy(offer => HudNpcArt.Label(offer.Kind, offer.Value))
            .Select(group => group.First())
            .Take(ButtonColumns * MaxButtonRows)
            .ToArray();
    }

    public void ShowDestinations(IReadOnlyList<TeleportOffer> destinations)
    {
        Destinations = destinations.Take(MaxDestinations).ToArray();
    }

    public void Dismiss()
    {
        if (!_dismissed)
        {
            closed?.Invoke();
        }

        _dismissed = true;
        ReadyToOpen = false;
        Destinations = [];
    }

    public void Scroll(int wheelDelta)
    {
        _firstLine = Math.Clamp(_firstLine - Math.Sign(wheelDelta) * 3, 0, Math.Max(0, _dialog.Length - LineCapacity));
    }

    public NpcServiceOffer ServiceAt(Point pointer)
    {
        if (Destinations.Count > 0)
        {
            return null;
        }

        for (var index = 0; index < _services.Length; index++)
        {
            if (ServiceButton(index, _services.Length).Contains(pointer))
            {
                return _services[index];
            }
        }

        return null;
    }

    public TeleportOffer DestinationAt(Point pointer)
    {
        for (var index = 0; index < Destinations.Count; index++)
        {
            if (DestinationButton(index, Destinations.Count).Contains(pointer))
            {
                return Destinations[index];
            }
        }

        return null;
    }

    private int ButtonCount => Destinations.Count > 0 ? Destinations.Count : _services.Length;

    private int LineCapacity
    {
        get
        {
            var top = ButtonCount == 0 ? OkButton.Y
                : Destinations.Count > 0 ? DestinationButton(0, ButtonCount).Y : ServiceButton(0, ButtonCount).Y;
            return Math.Clamp((top - 8 - (BoxY + 66)) / 15, 1, VisibleLines);
        }
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        if (Current is null)
        {
            return;
        }

        painter.HelpBox(BoxX, BoxY, SizeW, SizeH);
        var title = Title;
        painter.Sprite(HudArt.HelpTitle, title.X, title.Y);
        painter.TextCentered(painter.Fit(Current.Name, title.Width - 20), title.Left, title.Right, title.Y + 7, HudTextColors.BoxTitle);
        var capacity = LineCapacity;
        string[] lines = Destinations.Count > 0 ? ["Where do you want to go?"] : _dialog;
        for (var line = 0; line < capacity && _firstLine + line < lines.Length; line++)
        {
            painter.Text(lines[_firstLine + line], BoxX + 30, BoxY + 66 + 15 * line);
        }

        if (lines.Length > capacity)
        {
            var barX = BoxX + (SizeW - 1) * 32 + 80 - 6;
            var height = capacity * 15 + 16;
            painter.ScrollBar(barX, BoxY + 53, height, _firstLine, lines.Length - capacity);
        }

        DrawButtons(painter, pointer);
        var ok = OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    private void DrawButtons(HudPainter painter, Point pointer)
    {
        var count = ButtonCount;
        for (var index = 0; index < count; index++)
        {
            var bounds = Destinations.Count > 0 ? DestinationButton(index, count) : ServiceButton(index, count);
            string label;
            var available = true;
            if (Destinations.Count > 0)
            {
                var destination = Destinations[index];
                label = destination.MinimumLevel > 0
                    ? $"{destination.Name}  {destination.Price:N0} gold  (Lv {destination.MinimumLevel})"
                    : $"{destination.Name}  {destination.Price:N0} gold";
            }
            else
            {
                var service = _services[index];
                label = HudNpcArt.Label(service.Kind, service.Value);
                available = service.Available;
            }

            var color = !available ? Unavailable : bounds.Contains(pointer) ? Highlight : Color.White;
            var tint = available ? Color.White : Color.Gray;
            if (bounds.Width == ButtonWidth)
            {
                painter.Sprite(HudArt.HelpTitle, bounds.X, bounds.Y, tint);
            }
            else
            {
                DrawPlate(painter, bounds, tint);
            }

            painter.TextCentered(painter.Fit(label, bounds.Width - 20), bounds.Left, bounds.Right, bounds.Y + 7, color);
        }
    }

    // The title plate's ends stay unscaled; only its middle stretches to the button width.
    public static void DrawPlate(HudPainter painter, Rectangle bounds, Color tint)
    {
        if (bounds.Width == ButtonWidth)
        {
            painter.Sprite(HudArt.HelpTitle, bounds.X, bounds.Y, tint);
            return;
        }

        const int End = 40;
        painter.Sprite(HudArt.HelpTitle, bounds.X, bounds.Y, new Rectangle(0, 0, End, ButtonHeight), tint);
        painter.Stretch(HudArt.HelpTitle, new Rectangle(bounds.X + End, bounds.Y, bounds.Width - End * 2, ButtonHeight),
            new Rectangle(End, 0, ButtonWidth - End * 2, ButtonHeight), tint);
        painter.Sprite(HudArt.HelpTitle, bounds.Right - End, bounds.Y, new Rectangle(ButtonWidth - End, 0, End, ButtonHeight), tint);
    }
}
