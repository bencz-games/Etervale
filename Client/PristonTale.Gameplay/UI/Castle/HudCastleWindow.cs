using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Gameplay.UI.Castle;

// The window of Bless Castle's butler, in the NPC panel, for the members of the clan holding the castle: the treasury,
// the tax and clan skill in force and from the next day, the crystal towers and the mercenaries for the next siege. The
// chief changes them on its pages (a click picks a tax, a skill, the next element of a tower or one more mercenary;
// CastleOwnerRules gives the cost) and saves, or takes gold out of the treasury. The server answers every request with
// its result and the window as it is then; a refused save keeps the page and its changes, Back drops them.
public sealed class HudCastleWindow
{
    private enum Page
    {
        Closed,
        Main,
        Tax,
        Skill,
        Towers,
        Mercenaries
    }

    private const int LineStep = HudPanelPage.LineStep;
    private static readonly Color Highlight = HudTextColors.Marked;
    private static readonly Color Chosen = HudPanelPage.Chosen;
    private static readonly Color Muted = HudPanelPage.Muted;
    private static readonly string[] TowerNames = ["Castle.TowerOuterA", "Castle.TowerOuterB", "Castle.TowerInnerA", "Castle.TowerInnerB",
        "Castle.TowerInnerC", "Castle.TowerInnerD"];
    private static readonly string[] MercenaryNames = ["Castle.MercenaryA", "Castle.MercenaryB", "Castle.MercenaryC"];

    private Page _page;
    private ulong _npcId;
    private CastleOwnerAction _requested;
    private CastleSetup _draft = CastleSetup.Empty;
    private (string Text, Color Color, int Choice)[] _rows = [];
    private string[] _buttons = [];
    private string[] _fitted;
    private string[] _fittedButtons;

    public CastleOwnerInfo View { get; private set; } = CastleOwnerInfo.None;

    public bool IsOpen => _page != Page.Closed;

    public bool Pending { get; private set; }

    // Sends a request with the setup it carries and a withdrawal's gold; set by the HUD.
    public Action<CastleOwnerAction, ulong, CastleSetup, long> Send { get; set; }

    // Asks how much gold to take out (at most the treasury); set by the HUD.
    public Action<long, Action<int>> AskAmount { get; set; }

    public void Open(ulong npcId)
    {
        _npcId = npcId;
        _draft = View.Setup;
        Go(Page.Main);
        Request(CastleOwnerAction.Open, 0);
    }

    public void Close() => _page = Page.Closed;

    // On the first page the window takes the server's setup; on another one the chief's changes stay.
    public void Observe(CastleOwnerInfo view)
    {
        View = view;
        if (!view.HasView)
        {
            Close();
        }

        if (_page is Page.Main or Page.Closed)
        {
            _draft = view.Setup;
        }

        Refresh();
    }

    public void Result(CastleOwnerResultInfo result)
    {
        if (result.Action != _requested)
        {
            return;
        }

        Pending = false;
        if (result.Status == CastleOwnerStatus.Ok && result.Action != CastleOwnerAction.Open)
        {
            _draft = View.Setup;
            Go(Page.Main);
        }
    }

    public bool Contains(Point pointer) => IsOpen && HudNpcPanel.Box.Contains(pointer);

    public bool Click(Point pointer)
    {
        if (!Contains(pointer))
        {
            return false;
        }

        if (HudNpcPanel.OkButton.Contains(pointer))
        {
            Close();
            return true;
        }

        if (Pending)
        {
            return true;
        }

        if (HudPanelPage.ButtonAt(pointer, _buttons.Length) is var button && button >= 0)
        {
            Activate(_buttons[button]);
            return true;
        }

        if (LineAt(pointer) is var line && line >= 0 && _rows[line].Choice >= 0)
        {
            Choose(_rows[line].Choice);
        }

        return true;
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!IsOpen)
        {
            return;
        }

        _fitted ??= [.. _rows.Select(row => painter.Fit(row.Text, HudNpcPanel.TextWidth))];
        _fittedButtons ??= [.. _buttons.Select(button => painter.Fit(ClientText.Get(button), HudNpcPanel.ButtonWidth - 10))];
        HudPanelPage.Frame(painter, ClientText.Get("NpcService.BlessCastle"));
        var hovered = LineAt(pointer);
        for (var line = 0; line < _rows.Length && line < Capacity(); line++)
        {
            var (_, color, choice) = _rows[line];
            painter.Text(_fitted[line], HudNpcPanel.TextLeft, HudNpcPanel.TextTop + LineStep * line, line == hovered && choice >= 0 && !Pending ? Highlight : color);
        }

        HudPanelPage.Buttons(painter, _fittedButtons, pointer, Pending);
    }

    private void Activate(string button)
    {
        switch (button)
        {
            case "Castle.Tax":
                Go(Page.Tax);
                break;
            case "Castle.Skill":
                Go(Page.Skill);
                break;
            case "Castle.Towers":
                Go(Page.Towers);
                break;
            case "Castle.Mercenaries":
                Go(Page.Mercenaries);
                break;
            case "Castle.Withdraw":
                AskAmount?.Invoke(View.Treasury, amount => Request(CastleOwnerAction.Withdraw, amount));
                break;
            case "Castle.Save":
                Request(CastleOwnerAction.Save, 0);
                break;
            case "Castle.Reset":
                _draft = View.Setup;
                Refresh();
                break;
            default:
                _draft = View.Setup;
                Go(Page.Main);
                break;
        }
    }

    // A click on a page's line: the tax, the skill, the next element of a tower (Undo takes back an unbought one) or one
    // more mercenary of a kind.
    private void Choose(int choice)
    {
        switch (_page)
        {
            case Page.Tax:
                _draft = _draft with { Tax = choice };
                break;
            case Page.Skill:
                _draft = _draft with { Skill = (CastleClanSkill)choice };
                break;
            case Page.Towers:
                var towers = _draft.Towers.ToArray();
                towers[choice] = (CastleTower)((int)towers[choice] % (int)CastleTower.Fire + 1);
                _draft = _draft with { Towers = [.. towers] };
                break;
            case Page.Mercenaries:
                var mercenaries = _draft.Mercenaries.ToArray();
                mercenaries[choice] = Math.Min(CastleSiegeRules.MercenaryMax, mercenaries[choice] + 1);
                _draft = _draft with { Mercenaries = [.. mercenaries] };
                break;
        }

        Refresh();
    }

    private void Request(CastleOwnerAction action, long amount)
    {
        Pending = true;
        _requested = action;
        Send?.Invoke(action, _npcId, _draft, amount);
    }

    private void Go(Page page)
    {
        _page = page;
        Refresh();
    }

    private void Refresh()
    {
        _fitted = null;
        _fittedButtons = null;
        _buttons = _page switch
        {
            Page.Main when View.CanEdit => ["Castle.Tax", "Castle.Skill", "Castle.Towers", "Castle.Mercenaries", "Castle.Withdraw"],
            Page.Tax or Page.Skill => ["Castle.Save", "Castle.Back"],
            Page.Towers or Page.Mercenaries => ["Castle.Save", "Castle.Reset", "Castle.Back"],
            _ => []
        };
        _rows = _page switch
        {
            Page.Tax => [.. Enumerable.Range(0, CastleOwnerRules.MaxTaxRate + 1).Select(tax => (Percent(tax), tax == _draft.Tax ? Chosen : Color.White, tax))],
            Page.Skill => [.. CastleClanSkills.All.Select(skill => (ClientText.Format("Castle.SkillLine", CastleClanSkills.Name(skill), Effect(skill)),
                skill == _draft.Skill ? Chosen : Color.White, (int)skill))],
            Page.Towers => [.. Enumerable.Range(0, CastleSiegeRules.TowerSlots).Select(slot => (TowerLine(slot), Color.White, slot)), Cost()],
            Page.Mercenaries => [.. Enumerable.Range(0, CastleSiegeRules.MercenaryKinds).Select(kind => (MercenaryLine(kind), Color.White, kind)), Cost()],
            Page.Main => MainRows(),
            _ => []
        };
    }

    private (string, Color, int)[] MainRows()
    {
        var view = View;
        var rows = new List<(string, Color, int)>
        {
            (ClientText.Format("Castle.Treasury", Gold(view.Treasury)), Color.White, -1),
            (ClientText.Format("Castle.TaxLine", Percent(view.Tax), Percent(view.Setup.Tax)), Color.White, -1),
            (ClientText.Format("Castle.SkillNow", SkillName(view.SkillInForce)), Color.White, -1),
            (ClientText.Format("Castle.SkillTomorrow", SkillName(view.Setup.Skill)), Color.White, -1),
            (ClientText.Format("Castle.TowersLine", view.Setup.Towers.Count(static tower => tower != CastleTower.None).ToString(CultureInfo.InvariantCulture),
                CastleSiegeRules.TowerSlots.ToString(CultureInfo.InvariantCulture)), Color.White, -1),
            (ClientText.Format("Castle.MercenariesLine", string.Join(", ", view.Setup.Mercenaries.Select(static count => count.ToString(CultureInfo.InvariantCulture))),
                CastleSiegeRules.MercenaryMax.ToString(CultureInfo.InvariantCulture)), Color.White, -1)
        };
        if (!view.CanEdit)
        {
            rows.Add((ClientText.Get("Castle.ChiefOnly"), Muted, -1));
        }

        return [.. rows];
    }

    private (string, Color, int) Cost() =>
        CastleOwnerRules.Cost(View.Setup, _draft, out var cost) == CastleOwnerStatus.Ok
            ? (ClientText.Format("Castle.Cost", Gold(cost)), Chosen, -1)
            : (ClientText.Get("Castle.Status.InvalidSetup"), Muted, -1);

    private string TowerLine(int slot) => ClientText.Format("Castle.TowerLine", ClientText.Get(TowerNames[slot]), ElementName(_draft.Towers[slot]),
        View.Setup.Towers[slot] == CastleTower.None && _draft.Towers[slot] != CastleTower.None ? Gold(CastleOwnerRules.TowerPrice) : "");

    private string MercenaryLine(int kind) => ClientText.Format("Castle.MercenaryLine", ClientText.Get(MercenaryNames[kind]),
        _draft.Mercenaries[kind].ToString(CultureInfo.InvariantCulture), CastleSiegeRules.MercenaryMax.ToString(CultureInfo.InvariantCulture),
        Gold(CastleOwnerRules.MercenaryPrices[kind]));

    private static string ElementName(CastleTower tower) => ClientText.Get(tower switch
    {
        CastleTower.Ice => "Castle.Ice",
        CastleTower.Lightning => "Castle.Lightning",
        CastleTower.Fire => "Castle.Fire",
        _ => "Castle.NoTower"
    });

    private static string SkillName(CastleClanSkill skill) => skill == CastleClanSkill.None ? ClientText.Get("Castle.NoSkill") : CastleClanSkills.Name(skill);

    private static string Effect(CastleClanSkill skill) => skill switch
    {
        CastleClanSkill.Absorb => ClientText.Format("Castle.AbsorbEffect", CastleClanSkills.AbsorbBonus),
        CastleClanSkill.Damage => ClientText.Format("Castle.DamageEffect", CastleClanSkills.DamagePercent),
        _ => ClientText.Format("Castle.EvasionEffect", CastleClanSkills.EvasionPercent)
    };

    private static string Percent(int value) => string.Create(CultureInfo.InvariantCulture, $"{value}%");

    private static string Gold(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private int Capacity() => HudPanelPage.Capacity(_buttons.Length);

    private int LineAt(Point pointer) => HudPanelPage.LineAt(pointer, _rows.Length, _buttons.Length);
}
