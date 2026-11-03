using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

// The original skill window and its text: Skil24.bmp with the titles and skills of the first four tiers the class has
// reached (later tiers stay empty until the rank-up), Skil25.bmp with the fifth tier's title and skills on the first
// tier's boxes, skill buttons (gray "_" variant when unusable), rank gauges, F1-F8 short keys, the selected-skill hover
// frame, and for a skill master's pupil the training box: the skill and elite points left (or that there are none) and
// the class change button.
public sealed class HudSkillsPanel(SpriteFont font, HudSprites sprites)
{
    private const int TooltipWidth = 260;

    private sealed record Entry(Skill Skill, string Level, string[] Description, string[] Weapons, string Cost, string Mouse,
        int Rank = 0, string Status = "Unlearned", IReadOnlyList<string> Details = null, string Button = "", string GrayButton = "");

    private static readonly CharacterClassRegistry Classes = new();
    private readonly List<string> _classSprites = [];
    // The class's tier titles and skill buttons, made once per class instead of every frame.
    private readonly string[] _titles = new string[PlayerCharacter.LastTier + 1];
    private Entry[] _entries = [];
    private PlayerClass _class;
    private int _points = -1;
    private string _pointsText = "";
    private int _finalPoints = -1;
    private string _finalPointsText = "";
    private int _trainingIndex = -1;
    private string _trainingName = "";
    private string _trainingPrice = "";
    private IReadOnlyList<OwnedSkillView> _displayedSkills;
    private Entry _hovered;
    private bool _hoveredBasic;
    private bool _secondPage;
    private int _shownTier;

    private int _boxPoints = -1;
    private int _boxElitePoints = -1;
    private string _boxPointsText = "";
    private string _boxElitePointsText = "";

    // The training box is on screen; it takes clicks over the scene.
    public bool MasterBoxShown { get; private set; }

    public bool TrainingPending => _trainingIndex >= 0;
    public void CancelTraining() => _trainingIndex = -1;

    public static Rectangle ClassRow(Point origin, int index) => HudSkillTreeLayout.Node(origin, index);

    public void SetClass(PlayerClass characterClass)
    {
        _class = characterClass;
        _displayedSkills = null;
        CancelTraining();
        var skills = Classes.Get(characterClass).Skills;
        _entries = new Entry[skills.Count];
        var previous = _classSprites.ToArray();
        _classSprites.Clear();
        for (var tier = 0; tier < _titles.Length; tier++)
        {
            _titles[tier] = HudArt.JobTitle(characterClass, tier);
            _classSprites.Add(_titles[tier]);
        }

        for (var index = 0; index < skills.Count; index++)
        {
            var skill = skills[index];
            if (skill.Code == 0)
            {
                _entries[index] = new Entry(skill, "", [], [], "", "", Status: "Unavailable in this version");
                continue;
            }

            var button = HudArt.SkillButton(characterClass, skill.IconKey, false);
            var grayButton = HudArt.SkillButton(characterClass, skill.IconKey, true);
            _classSprites.Add(button);
            _classSprites.Add(grayButton);
            var weapons = skill.AllowedWeaponsMask == 0 ? "Any weapon" : string.Join(", ", Enum.GetValues<ItemKind>().Where(skill.AllowsWeapon));
            var mouse = skill.MouseUse switch
            {
                SkillMouseUse.Left => "Left mouse",
                SkillMouseUse.Right => "Right mouse",
                SkillMouseUse.Both => "Left or right mouse",
                _ => "Passive skill"
            };
            var cost = CostLine(skill, 1);
            _entries[index] = new Entry(skill, $"Lv {skill.RequiredLevel}",
                HudTextLayout.Wrap(font, skill.Description, TooltipWidth - 20, HudPainter.TextScale),
                HudTextLayout.Wrap(font, "Equipment: " + weapons, TooltipWidth - 20, HudPainter.TextScale), cost, mouse)
            {
                Button = button,
                GrayButton = grayButton
            };
        }

        foreach (var path in previous.Except(_classSprites, StringComparer.OrdinalIgnoreCase))
        {
            sprites.Release(path);
        }

        sprites.Request(_classSprites);
    }

    // The basic attack or a learned active skill under the pointer, for selecting it into a mouse socket or a short key.
    public uint HoveredSkill(Point origin, Point pointer)
    {
        if (HudSkillTreeLayout.BasicAttack(origin).Contains(pointer))
        {
            return HudSkillBindings.BasicAttackId;
        }

        for (var index = PageFirst; index < PageEnd; index++)
        {
            var entry = _entries[index];
            if (entry.Skill.Code != 0 && entry.Rank > 0 && entry.Skill.MouseUse != SkillMouseUse.None && Box(origin, index).Contains(pointer))
            {
                return entry.Skill.Code;
            }
        }

        return 0;
    }

    public void HandleTrainingClick(Point origin, Point pointer, OwnerState state, ulong npcId, HudProgressionController controller)
    {
        if (state is null || npcId == 0 || controller.Pending)
        {
            return;
        }

        if (_trainingIndex >= 0)
        {
            if (HudMessageBox.Accept.Contains(pointer))
            {
                AcceptTraining(state, npcId, controller);
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                _trainingIndex = -1;
            }

            return;
        }

        for (var index = PageFirst; index < PageEnd; index++)
        {
            if (!Box(origin, index).Contains(pointer))
            {
                continue;
            }

            var entry = _entries[index];
            var rank = Rank(state, entry.Skill.Code);
            if (!CanTrain(index, state))
            {
                return;
            }

            _trainingIndex = index;
            _trainingName = entry.Skill.Name;
            _trainingPrice = $"Rank {rank + 1}: {SkillLearning.Price(index, rank):N0} gold";
        }
    }

    // Yes to the skill being confirmed (its Yes button, or Enter as the original's skill box takes it).
    public void AcceptTraining(OwnerState state, ulong npcId, HudProgressionController controller)
    {
        if (_trainingIndex < 0 || state is null || npcId == 0 || controller.Pending)
        {
            return;
        }

        controller.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision, ProgressionAction.LearnSkill,
            SkillCode: _entries[_trainingIndex].Skill.Code, NpcEntityId: npcId));
        _trainingIndex = -1;
    }

    // The arrow of the page shown turns to the other page.
    public bool ClickPage(Point origin, Point pointer)
    {
        var arrow = _secondPage ? HudSkillTreeLayout.PageBack(origin) : HudSkillTreeLayout.PageForward(origin);
        if (!arrow.Contains(pointer))
        {
            return false;
        }

        TurnPage();
        return true;
    }

    // P turns the page once the class reached the fifth tier.
    public void TurnPageByKey()
    {
        if (_shownTier >= PlayerCharacter.LastTier)
        {
            TurnPage();
        }
    }

    private void TurnPage()
    {
        _secondPage = !_secondPage;
        CancelTraining();
    }

    // The skills the class tier has opened: the first tier's four, then four more with each rank-up.
    private int Shown => Math.Min(_entries.Length, SkillLearning.OpenedBy(_shownTier));

    // The first page holds the first four tiers' skills, the second the fifth tier's on the first tier's boxes.
    private int PageFirst => _secondPage ? HudSkillTreeLayout.NodeCount : 0;

    private int PageEnd => _secondPage ? Shown : Math.Min(Shown, HudSkillTreeLayout.NodeCount);

    private Rectangle Box(Point origin, int index) => ClassRow(origin, index - PageFirst);

    public void Draw(HudPainter painter, Point origin, Point pointer, OwnerState state, HudSkillBindings bindings, ulong skillMaster, bool pressed,
        bool interactive)
    {
        RefreshRanks(state);
        _hovered = null;
        _hoveredBasic = false;
        _shownTier = state?.Tier ?? 0;
        if (_secondPage)
        {
            painter.Sprite(HudArt.SkillWindowSecond, origin.X, origin.Y + 400);
            var back = HudSkillTreeLayout.PageBack(origin);
            if (interactive && back.Contains(pointer))
            {
                painter.Sprite(HudArt.SkillPageBack, back.X + 1, back.Y + 1);
            }

            if (_shownTier >= PlayerCharacter.LastTier)
            {
                var title = HudSkillTreeLayout.Title(origin, 0);
                painter.Sprite(_titles[PlayerCharacter.LastTier], title.X, title.Y);
            }
        }
        else
        {
            painter.Sprite(HudArt.SkillWindow, origin.X, origin.Y + 400);
            var forward = HudSkillTreeLayout.PageForward(origin);
            if (interactive && forward.Contains(pointer))
            {
                painter.Sprite(HudArt.SkillPageForward, forward.X + 1, forward.Y + 1);
            }

            for (var tier = 0; tier <= Math.Min(_shownTier, PlayerCharacter.LastTier - 1); tier++)
            {
                var title = HudSkillTreeLayout.Title(origin, tier);
                painter.Sprite(_titles[tier], title.X, title.Y);
            }
        }

        for (var index = PageFirst; index < PageEnd; index++)
        {
            var entry = _entries[index];
            var node = Box(origin, index);
            if (entry.Skill.Code != 0)
            {
                // A pupil sees in colour what the master can teach now (a skill at rank 10 has nothing left to learn);
                // elsewhere the learned skills are in colour.
                var usable = skillMaster != 0 ? entry.Rank < SkillLearning.MaxRank && (entry.Rank > 0 || CanTrain(index, state)) : entry.Rank > 0;
                painter.Sprite(usable ? entry.Button : entry.GrayButton, node.X, node.Y);
                var mastery = HudSkillTreeLayout.MasteryGauge(origin, index - PageFirst);
                painter.Sprite(entry.Skill.MouseUse == SkillMouseUse.None ? HudArt.SkillGaugePassive : HudArt.SkillGaugeBack, mastery.X, mastery.Y);

                var rank = HudSkillTreeLayout.RankGauge(origin, index - PageFirst);
                var length = Math.Clamp(entry.Rank, 0, SkillLearning.MaxRank) * rank.Height / SkillLearning.MaxRank;
                painter.Sprite(HudArt.SkillGauge, rank.X, rank.Bottom - length, new Rectangle(0, rank.Height - length, rank.Width, length));
                DrawShortcut(painter, node, entry.Skill.Code, bindings);
            }

            if (interactive && node.Contains(pointer))
            {
                _hovered = entry;
                painter.Sprite(HudArt.SkillSelect, node.X + (pressed ? 1 : 0), node.Y + (pressed ? 1 : 0));
            }
        }

        var basic = HudSkillTreeLayout.BasicAttack(origin);
        DrawShortcut(painter, basic, HudSkillBindings.BasicAttackId, bindings);
        if (interactive && basic.Contains(pointer))
        {
            _hoveredBasic = true;
            painter.Sprite(HudArt.SkillSelect, basic.X + (pressed ? 1 : 0), basic.Y + (pressed ? 1 : 0));
        }

        if (state is not null)
        {
            if (_points != state.SkillPoints)
            {
                _points = state.SkillPoints;
                _pointsText = _points.ToString();
            }

            if (_finalPoints != state.EliteSkillPoints)
            {
                _finalPoints = state.EliteSkillPoints;
                _finalPointsText = _finalPoints.ToString();
            }

            painter.TextCentered(_pointsText, origin.X + 165, origin.X + 253, origin.Y + 578);
            painter.TextCentered(_finalPointsText, origin.X + 250, origin.X + 253, origin.Y + 578);
        }
    }

    // The training box's lines (their tops), centred in the box, and their colours.
    private const int BoxTitleY = 145;
    private const int BoxFirstLineY = 180;
    private const int BoxSecondLineY = 200;
    private const int BoxPointsY = 230;
    private const int BoxElitePointsY = 255;
    private static readonly Color BoxElitePoints = new(147, 243, 193);
    private static readonly Color BoxShort = new(255, 64, 64);

    // A skill master's pupil, no skill being confirmed: the training box at the screen's top left with the points left
    // (or, with none, that there are none), the class change button inside it once the tier's skills all have a rank (lit
    // while the master offers the next rank-up quest, gray while the level or a running quest still holds it back). It is
    // drawn under the quest dialog and the other windows, as the original draws it with the interface.
    public void DrawMasterBox(HudPainter painter, OwnerState state, ulong skillMaster, bool classChangeOffered)
    {
        MasterBoxShown = false;
        if (skillMaster == 0 || state is null || _trainingIndex >= 0)
        {
            return;
        }

        MasterBoxShown = true;
        var box = HudSkillTreeLayout.MasterBox;
        painter.Flush();
        painter.InfoBox(box);
        painter.TextCentered("Train Skill/Magic", box.Left, box.Right, BoxTitleY, HudTextColors.Title);
        if (state.SkillPoints > 0 || state.EliteSkillPoints > 0)
        {
            if (_boxPoints != state.SkillPoints || _boxElitePoints != state.EliteSkillPoints)
            {
                _boxPoints = state.SkillPoints;
                _boxElitePoints = state.EliteSkillPoints;
                _boxPointsText = $"Skill Points : {_boxPoints}";
                _boxElitePointsText = $"Elite Skill Points : {_boxElitePoints}";
            }

            painter.TextCentered("Select a Skill or Magic", box.Left, box.Right, BoxFirstLineY);
            painter.TextCentered("To learn or train", box.Left, box.Right, BoxSecondLineY);
            painter.TextCentered(_boxPointsText, box.Left, box.Right, BoxPointsY, HudTextColors.Highlight);
            painter.TextCentered(_boxElitePointsText, box.Left, box.Right, BoxElitePointsY, BoxElitePoints);
        }
        else
        {
            painter.TextCentered("Not enough Skill Points", box.Left, box.Right, BoxFirstLineY, BoxShort);
            painter.TextCentered("to learn or practice", box.Left, box.Right, BoxSecondLineY, BoxShort);
        }

        if (classChangeOffered || _shownTier < PlayerCharacter.LastTier && TierLearned())
        {
            var button = HudSkillTreeLayout.ClassChange;
            painter.Sprite(classChangeOffered ? HudArt.SelectJob : HudArt.SelectJobGray, button.X, button.Y);
        }
    }

    private bool TierLearned()
    {
        for (var index = 0; index < Shown; index++)
        {
            if (_entries[index].Skill.Code != 0 && _entries[index].Rank < 1)
            {
                return false;
            }
        }

        return true;
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (_trainingIndex >= 0)
        {
            painter.Flush();
            HudMessageBox.Draw(painter, _trainingName, _trainingPrice, pointer);
            return;
        }

        if (_hovered is not null)
        {
            DrawDetails(painter, layout, pointer, _hovered);
        }
        else if (_hoveredBasic)
        {
            DrawLines(painter, layout, pointer, "Basic attack", ["Release LMB or RMB to select.", "Hold the button + F1-F8 to bind."]);
        }
    }

    private bool CanTrain(int index, OwnerState state)
    {
        if (state is null || index >= _entries.Length)
        {
            return false;
        }

        var skills = Classes.Get(_class).Skills;
        return SkillLearning.CanTrain(skills, index, code => Rank(state, code), state.Level, state.Tier, state.SkillPoints, state.EliteSkillPoints)
            && state.Money >= SkillLearning.Price(index, Rank(state, skills[index].Code));
    }

    private static int Rank(OwnerState state, uint code)
    {
        foreach (var skill in state.Skills)
        {
            if (skill.Code == code)
            {
                return skill.Rank;
            }
        }

        return 0;
    }

    private static void DrawLines(HudPainter painter, HudLayout layout, Point anchor, string title, string[] lines)
    {
        var width = Math.Max(painter.Measure(title), lines.Max(painter.Measure)) + 24;
        var height = 24 + lines.Length * HudPainter.LineHeight + 8;
        var bounds = painter.TooltipBox(layout, anchor, width, height);
        painter.Text(title, bounds.X + 10, bounds.Y + 8, HudTextColors.BoxTitle);
        for (var index = 0; index < lines.Length; index++)
        {
            painter.Text(lines[index], bounds.X + 10, bounds.Y + HudPainter.TooltipLinesY + index * HudPainter.LineHeight, new Color(220, 220, 220));
        }
    }

    private static void DrawDetails(HudPainter painter, HudLayout layout, Point pointer, Entry entry)
    {
        if (entry.Skill.Code == 0)
        {
            DrawLines(painter, layout, pointer, entry.Skill.Name, [entry.Status]);
            return;
        }

        var lines = new List<string>(entry.Description)
        {
            $"{entry.Level}   {entry.Mouse}",
            entry.Cost
        };
        lines.AddRange(entry.Details ?? []);
        lines.AddRange(entry.Weapons);
        lines.Add(entry.Status);
        DrawLines(painter, layout, pointer, entry.Skill.Name, lines.ToArray());
    }

    // Ranks drive both the hover test of Update and the drawing, so the HUD refreshes them before either.
    public void RefreshRanks(OwnerState state)
    {
        if (state is null || ReferenceEquals(state.Skills, _displayedSkills))
        {
            return;
        }

        _displayedSkills = state.Skills;
        for (var index = 0; index < _entries.Length; index++)
        {
            var entry = _entries[index];
            if (entry.Skill.Code == 0)
            {
                continue;
            }

            var rank = Rank(state, entry.Skill.Code);
            var shownRank = Math.Max(1, rank);
            var cost = CostLine(entry.Skill, shownRank);
            var status = rank == 0 ? "Unlearned | Rank 1 preview" : $"Learned rank {rank} / {SkillLearning.MaxRank}";
            if (rank > 0 && entry.Skill.Category == SkillCategory.Passive)
            {
                status += " | Passive";
            }

            if (entry.Skill.MouseUse != SkillMouseUse.None && SkillRuleCatalog.TryGet(entry.Skill.Code, out var rule))
            {
                var cooldown = SkillFormulas.CooldownMs(rule, shownRank, state.Talent, (int)state.MagicMastery, 0) / 1000f;
                cost += $" | Recharge {cooldown:0.#} s";
                if (rank > 0)
                {
                    status += " | Click to place in a mouse socket, F1-F8 while held to bind";
                }
            }

            var details = SkillRuleCatalog.TryGet(entry.Skill.Code, out var detailRule) ? HudSkillDetails.Lines(detailRule, shownRank) : [];
            _entries[index] = entry with { Rank = rank, Cost = cost, Status = status, Details = details };
        }
    }

    // The mana and stamina a skill costs at a rank; a passive skill, one never used by mouse, and one without its ranks'
    // costs has none.
    private static string CostLine(Skill skill, int rank) =>
        skill.Category == SkillCategory.Passive || skill.MouseUse == SkillMouseUse.None || skill.ManaByRank.Count != SkillLearning.MaxRank
            ? "No activation cost"
            : $"Rank {rank}: MP {skill.ManaForRank(rank)} | SP {skill.StaminaForRank(rank)}";

    // Short keys: ShortKey\f<n>.bmp at the skill box left + 9, top - 6.
    private static void DrawShortcut(HudPainter painter, Rectangle node, uint skillCode, HudSkillBindings bindings)
    {
        for (var index = 0; index < HudSkillBindings.ShortcutCount; index++)
        {
            if (bindings.Shortcut(index).SkillId == skillCode)
            {
                painter.Sprite(HudArt.ShortKeys[index], node.X + 9, node.Y - 6);
            }
        }
    }
}
