using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;

namespace PristonTale.Gameplay.UI.Widgets;

// The target frame regions in logical HUD pixels. Portrait is the viewport inside cw.tga that holds the live 3D portrait;
// Brood is the 16x16 race icon of monsters; StatsY is the text line under the life bar (level and life percentage).
public readonly record struct HudTargetFrame(Rectangle Box, Rectangle Portrait, Rectangle HealthBar, Rectangle Brood, int NameY, int StatsY);

// Target inspection box (centred at 0.92/0.1 of the screen with cw.tga, the name 16 pixels above its bottom edge, the
// monster race icon in its top right corner and the original life bar under it). Hits are kept as history lines; the
// original shows them as floating numbers only, so the chat window does not list them.
public sealed partial class HudCombat
{
    private const int StateBarWidth = 100;
    private const int StateBarSize = 92;
    private const int StateBarHeight = 16;
    private const int NameLift = 21;
    private static readonly Color LevelColor = new(220, 220, 220);
    private static readonly Color MonsterColor = new(255, 160, 120);
    private static readonly Color NpcColor = HudTextColors.BoxTitle;
    // The original name shadow sits one pixel right and one pixel up.
    private static readonly Point NameShadow = new(1, -1);

    private sealed class Entity(EntitySpawnInfo spawn)
    {
        public EntitySpawnInfo Spawn { get; } = spawn;
        public int Life { get; set; } = spawn.Life;
        public Vector3 Position { get; set; } = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
        // Set when the local player starts attacking this monster at full life, as in the original.
        public bool StateBarEnabled { get; set; }
        // Set when the local player swings at it at all: its name box over the scene then carries the life bar.
        public bool Attacked { get; set; }
        public string OwnerLabel { get; set; }
    }

    private readonly Dictionary<ulong, Entity> _entities = new();
    private WorldRenderCatalog _catalog = WorldRenderCatalog.Empty;
    private ulong _self;
    private int _levelValue = -1;
    private string _levelText = "";
    private int _percentValue = -1;
    private string _percentText = "";

    public void SetCatalog(WorldRenderCatalog catalog) => _catalog = catalog;

    // What the client knows of an entity in view: its kind and a summon's owner (Unspecified when not in view).
    public EntityKind KindOf(ulong id, out ulong ownerId)
    {
        ownerId = 0;
        if (!_entities.TryGetValue(id, out var entity))
        {
            return EntityKind.None;
        }

        ownerId = entity.Spawn.OwnerId;
        return entity.Spawn.Kind;
    }

    // Fills the caller's list (cleared first) so the minimap reads the NPC positions without allocating.
    public void CollectNpcPositions(List<Vector3> positions)
    {
        positions.Clear();
        foreach (var entity in _entities.Values)
        {
            if (entity.Spawn.Kind == EntityKind.Npc)
            {
                positions.Add(entity.Position);
            }
        }
    }

    public void Observe(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            var known = _entities.TryGetValue(spawn.EntityId, out var previous);
            _entities[spawn.EntityId] = new Entity(spawn) { StateBarEnabled = known && previous.StateBarEnabled, Attacked = known && previous.Attacked };
            if (spawn.IsSelf)
            {
                _self = spawn.EntityId;
            }
        }

        for (var index = 0; index < update.States.Count; index++)
        {
            var state = update.States[index];
            if (_entities.TryGetValue(state.EntityId, out var entity))
            {
                entity.Life = state.Life;
                entity.Position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
            }
        }

        foreach (var combat in update.Events)
        {
            if (combat.Kind == CombatEventKind.SwingBegin && combat.AttackerId == _self && _self != 0)
            {
                NoteAttack(combat.TargetId);
            }
        }

        foreach (var id in update.Removed)
        {
            _entities.Remove(id);
        }
    }

    // As in the original, only an attack that finds the monster at full life enables the classic bar.
    public void NoteAttack(ulong target)
    {
        if (_entities.TryGetValue(target, out var attacked) && attacked.Spawn.Kind == EntityKind.Monster)
        {
            attacked.Attacked = true;
        }

        if (_entities.TryGetValue(target, out var entity) && entity.Spawn.Kind == EntityKind.Monster && entity.Spawn.MaxLife > 0
            && entity.Life == entity.Spawn.MaxLife)
        {
            entity.StateBarEnabled = true;
        }
    }

    public static Rectangle TargetBox(HudLayout layout)
    {
        var height = layout.Height / 6 + 10;
        var width = (int)(layout.Height * 1.34f / 8) + 10;
        var centerX = (int)(layout.Width * 0.92f);
        var centerY = (int)(layout.Height * 0.1f);
        return new Rectangle(centerX - (width >> 1), centerY - (height >> 1), width, height);
    }

    // The life bar starts 5 pixels in and 2 pixels below the box, 10 pixels narrower; the text line under it is where the
    // original printed the target's statistics, 22 pixels below the box.
    public static HudTargetFrame TargetFrame(HudLayout layout)
    {
        var box = TargetBox(layout);
        var centerX = box.X + (box.Width >> 1);
        var centerY = box.Y + (box.Height >> 1);
        var portrait = new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
        var bar = new Rectangle(box.X + 5, box.Bottom + 2, box.Width - 10, StateBarHeight);
        var brood = new Rectangle(centerX + box.Width / 2 - 24, centerY - box.Height / 2 + 8, 16, 16);
        // The UI font is taller than the original's; the name sits higher so its descenders end inside the frame's border.
        return new HudTargetFrame(box, portrait, bar, brood, box.Bottom - NameLift, box.Bottom + 22);
    }

    public static int HealthPercent(int life, int maximum)
    {
        if (maximum <= 0 || life <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp((long)Math.Min(life, maximum) * 100 / maximum, 1, 100);
    }

    public bool ShowsHealthBar(ulong target, bool classic)
    {
        // NPCs show only their name, as in the original: they cannot be fought, so a life bar would mislead.
        if (!_entities.TryGetValue(target, out var entity) || entity.Spawn.MaxLife <= 0 || entity.Spawn.Kind == EntityKind.Npc)
        {
            return false;
        }

        return !classic || entity.StateBarEnabled;
    }

    // Original life bar: red to yellow to green over the squared life fraction.
    public static Color StateColor(int life, int maximum)
    {
        if (maximum <= 0)
        {
            return Color.Red;
        }

        var percent = (int)((long)Math.Clamp(life, 0, maximum) * 511 / maximum);
        percent = percent * percent / 512;
        var low = percent & 0xFF;
        return percent < 256 ? new Color(255, low, 0) : new Color(255 - low, 255, 0);
    }

    public bool TryGetPortraitSubject(ulong target, out HudPortraitSubject subject)
    {
        subject = default;
        if (target == 0 || !_entities.TryGetValue(target, out var entity))
        {
            return false;
        }

        var (y, z) = entity.Spawn.Kind switch
        {
            EntityKind.Monster when _catalog.GetMonster(entity.Spawn.TemplateId) is { } monster => (monster.PortraitOffsetY, monster.PortraitOffsetZ),
            EntityKind.Npc when _catalog.GetNpc(entity.Spawn.TemplateId) is { } npc => (npc.PortraitOffsetY, npc.PortraitOffsetZ),
            _ => (0, 0)
        };
        subject = new HudPortraitSubject(target, y, z);
        return true;
    }

    // The race icon of monsters; the brood of player-like monsters has none.
    public static string BroodIcon(MonsterBrood brood) => brood switch
    {
        MonsterBrood.Normal => HudArt.BroodNormal,
        MonsterBrood.Mutant => HudArt.BroodMutant,
        MonsterBrood.Undead => HudArt.BroodUndead,
        MonsterBrood.Demon => HudArt.BroodDemon,
        MonsterBrood.Mechanic => HudArt.BroodMechanic,
        _ => null
    };

    // Modern mode (default) always shows the bar with the level and life percentage under it; classic mode draws only
    // what the original shows: the name, the race icon and the life bar once StateBarEnabled.
    // playerButtons: the trade, party and friend buttons take the life bar's place under the frame (HudPlayerButtons).
    public void DrawTarget(HudPainter painter, HudLayout layout, ulong target, bool classicHealthBar = false, Texture2D portrait = null,
        bool playerButtons = false)
    {
        if (target == 0 || !_entities.TryGetValue(target, out var entity))
        {
            return;
        }

        var frame = TargetFrame(layout);
        var box = frame.Box;
        // cw.tga's centre is a black gradient at roughly half opacity; the slate fill behind it shows through as the
        // frame's grey backdrop, and the actor is drawn over both.
        painter.ColorBox(frame.Portrait, 60, 68, 74, 255);
        painter.Stretch(HudArt.TargetBox, box);
        painter.Image(portrait, frame.Portrait);
        if (entity.Spawn.Kind == EntityKind.Monster && BroodIcon(_catalog.GetMonster(entity.Spawn.TemplateId)?.Brood ?? MonsterBrood.Normal) is { } icon)
        {
            painter.Stretch(icon, frame.Brood);
        }

        var color = entity.Spawn.Kind switch
        {
            EntityKind.Monster => MonsterColor,
            EntityKind.Npc => NpcColor,
            _ => Color.White
        };
        // The original centres the name on the frame without clipping it; it only has to stay on screen.
        var centerX = box.X + (box.Width >> 1);
        var room = Math.Max(box.Width - 8, 2 * Math.Min(centerX, layout.Width - centerX) - 8);
        painter.TextCentered(painter.Fit(Name(target), room), centerX - room / 2, centerX + room / 2, frame.NameY, color, true, NameShadow);
        if (playerButtons || !ShowsHealthBar(target, classicHealthBar))
        {
            return;
        }

        var bar = frame.HealthBar;
        DrawHealthBar(painter, bar, entity.Life, entity.Spawn.MaxLife);
        if (classicHealthBar)
        {
            return;
        }

        var percent = PercentText(HealthPercent(entity.Life, entity.Spawn.MaxLife));
        painter.Text(LevelText(Level(entity)), bar.X, frame.StatsY, LevelColor);
        painter.Text(percent, bar.Right - painter.Measure(percent), frame.StatsY, Color.White);
    }

    private static void DrawHealthBar(HudPainter painter, Rectangle bar, int life, int maximum)
    {
        painter.Stretch(HudArt.EnergyBack, bar, new Rectangle(0, 0, StateBarWidth, 15));
        // A maximum of 0 (a spawn without one) draws the empty bar.
        if (life > 0 && maximum > 0)
        {
            var percent = (int)((long)Math.Min(life, maximum) * StateBarSize / maximum);
            painter.Stretch(HudArt.EnergyFill, new Rectangle(bar.X, bar.Y, (percent + 3) * bar.Width / StateBarWidth, bar.Height),
                new Rectangle(0, 0, percent + 3, 15), StateColor(life, maximum));
        }
    }

    private static int Level(Entity entity) => entity.Spawn.Level > 0 ? entity.Spawn.Level : entity.Spawn.Player?.Level ?? 0;

    // The frame redraws every frame; its numbers are formatted only when they change.
    private string LevelText(int level)
    {
        if (level != _levelValue)
        {
            _levelValue = level;
            _levelText = level > 0 ? $"Lv. {level}" : "";
        }

        return _levelText;
    }

    private string PercentText(int percent)
    {
        if (percent != _percentValue)
        {
            _percentValue = percent;
            _percentText = $"{percent}%";
        }

        return _percentText;
    }

    // A player's name while it is in view; null otherwise.
    public string PlayerName(ulong id) => _entities.TryGetValue(id, out var entity) && entity.Spawn.Player is { } player ? player.Name : null;

    private string Name(ulong id)
    {
        if (!_entities.TryGetValue(id, out var entity))
        {
            return "Target";
        }

        if (entity.Spawn.Player is { } player)
        {
            return player.Name;
        }

        if (!string.IsNullOrEmpty(entity.Spawn.Name))
        {
            return entity.Spawn.Name;
        }

        return entity.Spawn.Kind == EntityKind.Npc
            ? _catalog.GetNpc(entity.Spawn.TemplateId)?.Name ?? "NPC"
            : _catalog.GetMonster(entity.Spawn.TemplateId)?.Name ?? "Monster";
    }
}
