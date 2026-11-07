using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Inventory;

// The item box of a usable item: its name, what it does, how long it lasts and how to use it.
public static class HudUsableTooltip
{
    private static readonly string[] Attributes = ["", "Attribute.Strength", "Attribute.Spirit", "Attribute.Talent", "Attribute.Agility", "Attribute.Health"];
    private static readonly string[] AgingTargets = ["Usable.AgingWeapon", "Usable.AgingShield", "Usable.AgingArmor", "Usable.AgingArmlet", "Usable.AgingGloves", "Usable.AgingBoots"];

    // link: a bound Union Core's clan member.
    public static IReadOnlyList<string> Lines(string catalogKey, string link = "")
    {
        if (!UsableItemCatalog.TryGet(catalogKey, out var rule))
        {
            return [];
        }

        var lines = new List<string>();
        switch (rule.Action)
        {
            case UsableItemAction.PremiumTimers:
                foreach (var grant in rule.Grants)
                {
                    lines.Add(HudPremiumTimers.Describe(grant.Kind, grant.Value));
                }

                lines.Add(ClientText.Format("Usable.Duration", Duration(rule.Grants[0].DurationSeconds)));
                if (rule.MinimumLevel > 0)
                {
                    lines.Add(ClientText.Format("Usable.LevelFrom", rule.MinimumLevel));
                }

                break;
            case UsableItemAction.ResetAttributes when rule.Argument > 0:
                lines.Add(ClientText.Format("Usable.ResetAttribute", ClientText.Get(Attributes[rule.Argument])));
                lines.Add(ClientText.Get("Usable.ResetAttribute2"));
                break;
            case UsableItemAction.ResetAttributes:
                lines.Add(ClientText.Get("Usable.ResetStats"));
                lines.Add(ClientText.Get("Usable.Redistributed"));
                break;
            case UsableItemAction.ResetSkills:
                lines.Add(ClientText.Get("Usable.ResetSkills"));
                lines.Add(ClientText.Get("Usable.Redistributed"));
                break;
            case UsableItemAction.ResetAttributesAndSkills:
                lines.Add(ClientText.Get("Usable.ResetBoth"));
                lines.Add(ClientText.Get("Usable.Redistributed"));
                break;
            case UsableItemAction.ScrollEffect:
            {
                var scroll = ItemScrolls.For((ScrollEffectKind)rule.Argument);
                lines.AddRange(scroll.Kind switch
                {
                    ScrollEffectKind.Invulnerability => [ClientText.Get("Usable.Invulnerability"), ClientText.Get("Usable.Invulnerability2")],
                    ScrollEffectKind.Critical => [ClientText.Format("Usable.CriticalScroll", ItemScrolls.Bonus), ClientText.Get("Usable.CriticalScroll2")],
                    _ => [ClientText.Format("Usable.EvasionScroll", ItemScrolls.Bonus), ClientText.Get("Usable.EvasionScroll2")]
                });
                lines.Add(ClientText.Format("Usable.Duration", Duration(scroll.DurationSeconds)));
                break;
            }
            case UsableItemAction.TeleportCore:
                lines.Add(ClientText.Get("Usable.TeleportCore"));
                break;
            case UsableItemAction.ReturnToTown when rule.Argument >= 0:
                lines.Add(ClientText.Format("Usable.Recall", Town(rule.Argument)));
                break;
            case UsableItemAction.HairTint:
                lines.Add(ClientText.Format("Usable.Hair", (char)('A' + rule.Argument)));
                break;
            case UsableItemAction.BigHead:
                lines.Add(ClientText.Format("Usable.BigHead", UsableItemEffects.BigHeadSeconds / 60));
                break;
            case UsableItemAction.Firecracker:
                lines.Add(ClientText.Get("Usable.Fireworks"));
                break;
            case UsableItemAction.SkillMastery:
                lines.Add(ClientText.Format("Usable.TrainTier", rule.Argument));
                break;
            case UsableItemAction.AgingMastery:
                lines.Add(ClientText.Format("Usable.Mature", ClientText.Get(AgingTargets[Math.Clamp(rule.Argument, 0, AgingTargets.Length - 1)])));
                break;
            case UsableItemAction.GravityScroll:
                lines.Add(ClientText.Format("Usable.Gravity", UsableItemCatalog.GravityScrollWeight));
                lines.Add(ClientText.Format("Usable.GravityUses", UsableItemCatalog.MaxGravityScrolls));
                break;
            case UsableItemAction.MobileShop:
                lines.Add(ClientText.Get("Usable.Merchant"));
                break;
            case UsableItemAction.MonsterCrystal when MonsterCrystals.TryGet(catalogKey, out var crystal):
                lines.AddRange(CrystalLines(crystal));
                break;
            case UsableItemAction.CastleItem when CastleItems.TryGetByIndex(rule.Argument, out var castle):
                lines.Add(DataText.CastleEffect(castle));
                lines.Add(ClientText.Format("Usable.Duration", Duration(castle.DurationSeconds)));
                lines.Add(ClientText.Get("Usable.CastleOnly"));
                break;
            case UsableItemAction.Restoration:
                lines.Add(ClientText.Get("Usable.Restoration"));
                lines.Add(ClientText.Format("Usable.Restoration2", CastleItems.RestorationDelayMs / 1000));
                break;
            case UsableItemAction.Food:
                lines.Add(ClientText.Get("Usable.Restores"));
                break;
            case UsableItemAction.GiftBox:
                lines.Add(ClientText.Get("Usable.Gift"));
                break;
            case UsableItemAction.SoccerHead:
                lines.Add(ClientText.Format("Usable.SoccerHead", Duration(EventFoods.SoccerHeadSeconds)));
                break;
            case UsableItemAction.Chocolate when Chocolates.TryGet(catalogKey, out var chocolate):
                lines.Add(ClientText.Format("Usable.VirtualLife", chocolate.Level));
                lines.Add(ClientText.Format("Usable.Duration", Duration((long)(Chocolates.DurationMs(chocolate.Level) / 1000f))));
                lines.Add(ClientText.Get(chocolate.ForFemales ? "Usable.FemaleOnly" : "Usable.MaleOnly"));
                break;
            case UsableItemAction.PuzzleBag when catalogKey.Equals(Puzzles.LuckyBagIcon, StringComparison.OrdinalIgnoreCase):
                lines.Add(ClientText.Format("Usable.PuzzleBag", Puzzles.RevealCost));
                break;
            case UsableItemAction.PuzzleBag:
                lines.Add(ClientText.Format("Usable.PuzzlePiece", Puzzles.Number(catalogKey), Puzzles.PiecesPerSet));
                lines.Add(ClientText.Get("Usable.PuzzleBoard"));
                break;
            case UsableItemAction.UnionCore:
                lines.Add(ClientText.Get("Usable.ClanTeleport"));
                if (link.Length > 0)
                {
                    lines.Add(ClientText.Format("Usable.UnionCoreName", link));
                }

                break;
            case UsableItemAction.ForceOrb when ForceOrbs.TryGet(catalogKey, out var orb):
                lines.Add(orb.MinimumLevel <= 1 && orb.MaximumLevel < 200 ? ClientText.Format("Usable.LevelRange", orb.MinimumLevel, orb.MaximumLevel) : ClientText.Format("Usable.LevelFrom", orb.MinimumLevel));
                lines.Add(ClientText.Format("Usable.OrbDamage", orb.Damage));
                if (orb.Percent > 0)
                {
                    lines.Add(ClientText.Format("Usable.OrbPercent", orb.Percent));
                }

                lines.Add(ClientText.Format("Usable.Duration", Duration(orb.DurationSeconds)));
                break;
        }

        return lines;
    }

    private static IEnumerable<string> CrystalLines(MonsterCrystalDefinition crystal) => crystal.Kind switch
    {
        CrystalKind.ClanPet => [ClientText.Format("Usable.ClanPet", crystal.Name), ClientText.Get("Usable.ClanOnly")],
        CrystalKind.CastleMercenary => [ClientText.Format("Usable.Mercenary", crystal.Name), ClientText.Get("Usable.Mercenary2")],
        CrystalKind.SoulStone => [ClientText.Format("Usable.SoulStone", PetMinutes)],
        _ => [ClientText.Format("Usable.Pet", crystal.Name), ClientText.Format("Usable.Pet2", PetMinutes)]
    };

    private const int PetMinutes = (int)(MonsterCrystals.PetBaseLifetimeMs / 60_000);

    // The towns a return scroll recalls to.
    private static string Town(int fieldCode) => fieldCode switch
    {
        FieldCodes.Ricarten => "Ricarten",
        FieldCodes.Navisko => "Navisko",
        FieldCodes.Pillai => "Pillai",
        _ => ""
    };

    public static string Duration(long seconds) => seconds switch
    {
        >= 86400 when seconds % 86400 == 0 => seconds / 86400 == 1 ? ClientText.Get("Time.OneDay") : ClientText.Format("Time.Days", seconds / 86400),
        >= 3600 when seconds % 3600 == 0 => seconds / 3600 == 1 ? ClientText.Get("Time.OneHour") : ClientText.Format("Time.Hours", seconds / 3600),
        _ => ClientText.Format("Time.Seconds", seconds)
    };

    // An empty action adds no line.
    public static void Draw(HudPainter painter, HudLayout layout, Point pointer, string name, IReadOnlyList<string> lines, string action)
    {
        var hasAction = !string.IsNullOrEmpty(action);
        var count = lines.Count + (hasAction ? 1 : 0);
        var textWidth = Math.Max(painter.Measure(name), hasAction ? painter.Measure(action) : 0);
        for (var index = 0; index < lines.Count; index++)
        {
            textWidth = Math.Max(textWidth, painter.Measure(lines[index]));
        }

        var width = Math.Min(layout.Width - 8, textWidth + 24);
        var bounds = painter.TooltipBox(layout, pointer, width, 31 + count * 17);
        painter.TextCentered(painter.Fit(name, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + HudPainter.TooltipNameY, HudTextColors.BoxTitle);
        for (var index = 0; index < count; index++)
        {
            var isAction = index == lines.Count;
            var text = isAction ? action : lines[index];
            var color = isAction ? HudTextColors.Hint : new Color(150, 220, 255);
            painter.TextCentered(painter.Fit(text, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + HudPainter.TooltipLinesY + 17 * index, color);
        }
    }
}
