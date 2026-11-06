using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.HelpPet;

// What the help pet needs to know of the local player each frame. Kind is the helper pet beside it (1 to 4, 0 for none);
// ExperiencePercent is the level's experience in whole percent; Fresh is a character at level 1 with no experience.
public readonly record struct HelpPetState(int Kind, int Level, int ExperiencePercent, bool Fresh, PlayerClass Class, bool InTown, bool Attacking);

// The local player's helper pet as the scene draws it: its kind (0 for none) and the screen point it speaks from.
public readonly record struct HudHelperPetView(int Kind, HudOverheadPoint? Point);

// The beginner's helper pet's talk. It speaks over itself ("Name : message", four seconds) when the player points at,
// clicks or presses the key of a part of the interface, while it is shown and the player is below level 10; it speaks
// when the pointer comes onto a part and keeps that part's line up while the pointer stays. Its help boxes queue at the top left and each stays fifteen seconds or until
// OK: the greeting of a fresh character, the guide of each level from 2 to 10, and the class guide at 90% of level 8 and
// the farewell at 90% of level 9 while fighting. In town it says one of its idle lines every thirty seconds, and while
// fighting below level 8 one of its battle lines as the level's experience passes a step (one line however many steps
// it passed at once); neither list repeats until it is spent. The server takes the pet away at level 10 in the tick that raises the level, and the two can arrive in
// either order: a level reached within two seconds of the pet leaving still has its guide. Given a loader, the texts
// are read again in a new language when the shown one changes.
public sealed class HudHelpPet(Task<HelpPetMessages> messages, Random random, IClientLog log, Func<string, Task<HelpPetMessages>> load = null)
{
    private Task<HelpPetMessages> _messages = messages;
    private string _language = ClientText.Language;

    public const double BubbleMs = 4000;
    public const double BoxMs = 15000;
    public const double IdleEveryMs = 30000;
    public const double LeaveGraceMs = 2000;
    public const int TextWidth = 440;
    // The original's steps also end at 100%, which the level's experience never passes.
    private static readonly int[] BattleSteps = [5, 10, 20, 30, 35, 40, 50, 60, 70, 80, 90];
    // The box (HudPainter.GuideBox).
    private const int BoxX = 3;
    private const int BoxY = 3;
    private const int BoxColumns = 32;
    private const int MinRows = 4;
    // The text height the four rows hold (six lines); each further 13 pixels adds a row.
    private const int MinRowsText = 90;
    private const int TextX = 75;
    private const int TextY = 22;
    private const int LineHeight = 15;
    private static readonly Rectangle Portrait = new(10, 20, 64, 64);
    // The OK button's left edge and its place below the top of the bottom edge.
    private const int OkX = 522;
    private const int OkBelowEdge = 9;
    private static readonly Point OkSize = new(47, 12);
    private const string BoxFolder = "image/Sinimage/Help/";
    private const string PetFolder = "image/Sinimage/PetData/";
    private const string OkArt = BoxFolder + "bltr_bt3_.bmp";
    private const string OkLitArt = BoxFolder + "bltr_bt3.bmp";
    private static readonly string[] Portraits =
        [PetFolder + "Pet_terry-pose.tga", PetFolder + "Pet_nepsys-pose.tga", PetFolder + "Pet_io-pose.tga", PetFolder + "Pet-mute-pose.tga"];
    public static readonly string[] Art = [.. Portraits, OkArt, OkLitArt];

    private readonly Queue<Box> _queue = new();
    private readonly List<int> _idleLeft = new();
    private readonly List<int> _battleLeft = new();
    private HelpPetMessages _book;
    private Box _box;
    private double _boxSinceMs;
    private string _bubbleTopic;
    private string _pointed;
    private double _bubbleUntilMs;
    private int _kind;
    private int _lastKind;
    private double _leftAtMs = double.NegativeInfinity;
    private int _level;
    private int _battleStep;
    private bool _greeted;
    private bool _classGuide;
    private bool _farewell;
    private double _idleSinceMs;

    // The line over the pet ("Name : message"), or null.
    public string Bubble { get; private set; }

    // The help box shown: the pet's kind and the topic's lines; null when none is.
    public (int Kind, IReadOnlyList<string> Lines)? Shown => _box is null ? null : (_box.Kind, _box.Lines);

    public int Queued => _queue.Count;

    public void Update(HelpPetState state, double nowMs)
    {
        if (load is not null && _language != ClientText.Language)
        {
            _language = ClientText.Language;
            _messages = load(_language);
            _book = null;
        }

        if (_book is null)
        {
            if (!_messages.IsCompleted)
            {
                return;
            }

            if (!_messages.IsCompletedSuccessfully)
            {
                log.Error("[HelpPet] the pet's texts did not load", _messages.Exception?.GetBaseException());
            }

            _book = _messages.IsCompletedSuccessfully ? _messages.Result : HelpPetMessages.Empty;
            _idleSinceMs = nowMs;
        }

        if (state.Kind != _kind)
        {
            if (state.Kind == 0)
            {
                _lastKind = _kind;
                _leftAtMs = nowMs;
            }

            _kind = state.Kind;
        }

        if (_kind != 0 && !_greeted)
        {
            _greeted = true;
            if (state.Fresh)
            {
                Greet(state.Class);
            }
        }

        if (state.Level != _level)
        {
            var speaker = _kind != 0 ? _kind : nowMs - _leftAtMs <= LeaveGraceMs ? _lastKind : 0;
            if (_level != 0 && state.Level > _level && speaker != 0)
            {
                foreach (var topic in LevelTopics(state.Level))
                {
                    Queue(topic, speaker);
                }
            }

            _level = state.Level;
            _battleStep = StepsPassed(state.ExperiencePercent);
        }

        if (_bubbleTopic is not null && nowMs >= _bubbleUntilMs)
        {
            _bubbleTopic = null;
            Bubble = null;
        }

        if (_box is not null && nowMs - _boxSinceMs >= BoxMs)
        {
            NextBox(nowMs);
        }
        else if (_box is null && _queue.Count > 0)
        {
            NextBox(nowMs);
        }

        if (!Speaks)
        {
            return;
        }

        if (state.InTown && nowMs - _idleSinceMs >= IdleEveryMs)
        {
            _idleSinceMs = nowMs;
            Speak("Normal_Message", Pick(_idleLeft, "Normal_Message"), nowMs);
        }

        if (state.Attacking)
        {
            Fight(state, nowMs);
        }
    }

    // The part of the interface under the pointer each frame (null for none): its message 0 when the pointer comes onto
    // it, its line kept up while the pointer stays.
    public void Point(string topic, double nowMs)
    {
        if (topic != _pointed)
        {
            _pointed = topic;
            Speak(topic, 0, nowMs);
        }
        else if (topic is not null && _bubbleTopic == topic)
        {
            _bubbleUntilMs = nowMs + BubbleMs;
        }
    }

    // A part of the interface clicked (index 1), right clicked (2) or its key pressed (3 and on).
    public void Say(string topic, int index, double nowMs) => Speak(topic, index, nowMs);

    private void Speak(string topic, int index, double nowMs)
    {
        if (!Speaks || topic is null || index < 0)
        {
            return;
        }

        var lines = _book.Lines(topic);
        if (index >= lines.Length)
        {
            return;
        }

        _bubbleTopic = topic;
        _bubbleUntilMs = nowMs + BubbleMs;
        Bubble = $"{_book.Name(_kind)} : {lines[index]}";
    }

    public bool Captures(Point pointer) => _box is not null && OkButton(_box).Contains(pointer);

    // OK closes the box shown; true when the click was on it.
    public bool Click(Point pointer, double nowMs)
    {
        if (!Captures(pointer))
        {
            return false;
        }

        NextBox(nowMs);
        return true;
    }

    // The box spans the original's four rows and grows a row per 13 pixels its wrapped text needs beyond them.
    public void Draw(HudPainter painter, Point pointer)
    {
        if (_box is null)
        {
            return;
        }

        _box.Wrapped ??= [.. _box.Lines.SelectMany(line => HudTextLayout.Wrap(line, TextWidth, text => painter.Measure(text)))];
        _box.Rows = MinRows + Math.Max(0, (int)Math.Ceiling((_box.Wrapped.Length * LineHeight - MinRowsText) / (double)HudPainter.GuideRowStep));
        painter.GuideBox(BoxX, BoxY, BoxColumns, _box.Rows);
        painter.Stretch(Portraits[_box.Kind - 1], Portrait);
        var ok = OkButton(_box);
        painter.Sprite(ok.Contains(pointer) ? OkLitArt : OkArt, ok.X, ok.Y);
        painter.Flush();
        for (var index = 0; index < _box.Wrapped.Length; index++)
        {
            painter.Text(_box.Wrapped[index], TextX, TextY + index * LineHeight, Color.White);
        }
    }

    private bool Speaks => _book is not null && _kind != 0 && _level < HelpPets.LeaveLevel;

    // The top of the box's bottom edge: 85, as the original's, for four rows.
    private static int Bottom(int rows) => HudPainter.GuideBoxBottom(BoxY, rows);

    private static Rectangle OkButton(Box box) => new(OkX, Bottom(Math.Max(MinRows, box.Rows)) + OkBelowEdge, OkSize.X, OkSize.Y);

    private static int StepsPassed(int percent)
    {
        var passed = 0;
        while (passed < BattleSteps.Length && percent > BattleSteps[passed])
        {
            passed++;
        }

        return passed;
    }

    private void Greet(PlayerClass playerClass)
    {
        var welcome = _book.Lines("Welcom_ALL").ToArray();
        if (welcome.Length > 1)
        {
            welcome[0] = welcome[0].Replace("%s", _book.Lineage(_kind));
            welcome[1] = welcome[1].Replace("%s", _book.Name(_kind));
        }

        Queue(welcome, _kind);
        Queue(DeathPenalty.TownField(playerClass) == FieldCodes.Ricarten ? "Welcom_Tems" : "Welcom_Mora", _kind);
    }

    private static string[] LevelTopics(int level) => level switch
    {
        2 or 6 or 9 or 10 => [$"Level_Message{level}-1", $"Level_Message{level}-2"],
        >= 3 and <= 8 => [$"Level_Message{level}"],
        _ => []
    };

    private void Fight(HelpPetState state, double nowMs)
    {
        if (state.Level < 8)
        {
            var passed = StepsPassed(state.ExperiencePercent);
            if (passed > _battleStep)
            {
                _battleStep = passed;
                Speak("Battle_Message", Pick(_battleLeft, "Battle_Message"), nowMs);
            }

            return;
        }

        if (state.Level == 8 && state.ExperiencePercent >= 90 && !_classGuide)
        {
            _classGuide = true;
            Queue($"Special_Message8_{ClassTopic(state.Class)}", _kind);
        }
        else if (state.Level == 9 && state.ExperiencePercent >= 90 && !_farewell)
        {
            _farewell = true;
            Queue("Special_Message9", _kind);
        }
    }

    private static string ClassTopic(PlayerClass playerClass) => playerClass == PlayerClass.Mechanician ? "Mecha" : playerClass.ToString();

    // One of a list's messages not said since the list was last spent.
    private int Pick(List<int> left, string topic)
    {
        if (left.Count == 0)
        {
            left.AddRange(Enumerable.Range(0, _book.Lines(topic).Length));
            if (left.Count == 0)
            {
                return -1;
            }
        }

        var pick = random.Next(left.Count);
        var index = left[pick];
        left.RemoveAt(pick);
        return index;
    }

    private void Queue(string topic, int kind) => Queue(_book.Lines(topic), kind);

    private void Queue(string[] lines, int kind)
    {
        if (lines.Length > 0)
        {
            _queue.Enqueue(new Box(kind, lines));
        }
    }

    private void NextBox(double nowMs)
    {
        _box = _queue.Count > 0 ? _queue.Dequeue() : null;
        _boxSinceMs = nowMs;
    }

    private sealed class Box(int kind, string[] lines)
    {
        public int Kind { get; } = kind;
        public string[] Lines { get; } = lines;
        public string[] Wrapped { get; set; }
        public int Rows { get; set; }
    }
}
