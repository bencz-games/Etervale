using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Inventory;

// The item box of a usable item: its name, what it does, how long it lasts and how to use it.
public static class HudUsableTooltip
{
    private static readonly string[] Attributes = ["", "Strength", "Spirit", "Talent", "Agility", "Health"];
    private static readonly string[] AgingTargets = ["weapon", "shield or orb", "armor or robe", "armlet", "gloves", "boots"];

    public static IReadOnlyList<string> Lines(string catalogKey)
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

                lines.Add("Duration: " + Duration(rule.Grants[0].DurationSeconds));
                if (rule.MinimumLevel > 0)
                {
                    lines.Add($"Req. Level: {rule.MinimumLevel}+");
                }

                break;
            case UsableItemAction.ResetAttributes when rule.Argument > 0:
                lines.Add($"Returns {Attributes[rule.Argument]} to its starting value");
                lines.Add("and refunds its stat points.");
                break;
            case UsableItemAction.ResetAttributes:
                lines.Add("It makes stat points");
                lines.Add("to be redistributed.");
                break;
            case UsableItemAction.ResetSkills:
                lines.Add("It makes skill points");
                lines.Add("to be redistributed.");
                break;
            case UsableItemAction.ResetAttributesAndSkills:
                lines.Add("It makes stat/skill points");
                lines.Add("to be redistributed.");
                break;
            case UsableItemAction.ScrollEffect:
            {
                var scroll = ItemScrolls.For((ScrollEffectKind)rule.Argument);
                lines.AddRange(scroll.Kind switch
                {
                    ScrollEffectKind.Invulnerability => ["Becomes invincible for the set time.", "The attack power decreases by 1/2."],
                    ScrollEffectKind.Critical => [$"{ItemScrolls.Bonus}% increase", "on the critical attack."],
                    _ => [$"{ItemScrolls.Bonus}% increase of", "evade chance."]
                });
                lines.Add($"Duration: {scroll.DurationSeconds}sec");
                break;
            }
            case UsableItemAction.TeleportCore:
                lines.Add("Teleports you to a field of your choice.");
                break;
            case UsableItemAction.ReturnToTown when rule.Argument >= 0:
                lines.Add($"Recall to {Town(rule.Argument)}");
                break;
            case UsableItemAction.HairTint:
                lines.Add($"Changes your hair to type {(char)('A' + rule.Argument)}.");
                break;
            case UsableItemAction.BigHead:
                lines.Add($"Gives you a big head for {UsableItemEffects.BigHeadSeconds / 60} minutes.");
                break;
            case UsableItemAction.Firecracker:
                lines.Add("Fireworks light up the sky around you.");
                break;
            case UsableItemAction.SkillMastery:
                lines.Add($"Fully trains the skills of class tier {rule.Argument}.");
                break;
            case UsableItemAction.AgingMastery:
                lines.Add($"Matures the aged {AgingTargets[Math.Clamp(rule.Argument, 0, AgingTargets.Length - 1)]} you wear.");
                break;
            case UsableItemAction.GravityScroll:
                lines.Add($"Carrying capacity +{UsableItemCatalog.GravityScrollWeight}");
                lines.Add($"(up to {UsableItemCatalog.MaxGravityScrolls} uses)");
                break;
            case UsableItemAction.MobileShop:
                lines.Add("Opens a shop that buys and repairs.");
                break;
            case UsableItemAction.MonsterCrystal when MonsterCrystals.TryGet(catalogKey, out var crystal):
                lines.AddRange(CrystalLines(crystal));
                break;
            case UsableItemAction.CastleItem when CastleItems.TryGetByIndex(rule.Argument, out var castle):
                lines.Add(castle.Effect);
                lines.Add($"Duration: {castle.DurationSeconds}sec");
                lines.Add("Bless Castle only");
                break;
            case UsableItemAction.Restoration:
                lines.Add("Revives you where you fell");
                lines.Add($"{CastleItems.RestorationDelayMs / 1000} seconds after death in the castle.");
                break;
            case UsableItemAction.Food:
                lines.Add("Restores life, mana and stamina.");
                break;
            case UsableItemAction.GiftBox:
                lines.Add("Open it to receive a gift.");
                break;
            case UsableItemAction.SoccerHead:
                lines.Add($"Gives you a soccer-ball head for {Duration(EventFoods.SoccerHeadSeconds)}.");
                break;
            case UsableItemAction.Chocolate when Chocolates.TryGet(catalogKey, out var chocolate):
                lines.Add($"Virtual Life Lv {chocolate.Level}");
                lines.Add($"Duration: {(int)(Chocolates.DurationMs(chocolate.Level) / 1000f)}sec");
                lines.Add(chocolate.ForFemales ? "Female characters only" : "Male characters only");
                break;
            case UsableItemAction.PuzzleBag when catalogKey.Equals(Puzzles.LuckyBagIcon, StringComparison.OrdinalIgnoreCase):
                lines.Add($"Reveal the puzzle piece inside for {Puzzles.RevealCost} gold.");
                break;
            case UsableItemAction.PuzzleBag:
                lines.Add($"Puzzle piece {Puzzles.Number(catalogKey)} of {Puzzles.PiecesPerSet}");
                lines.Add("Lay the set out on the puzzle board.");
                break;
            case UsableItemAction.UnionCore:
                lines.Add("Teleport to a clan member");
                break;
            case UsableItemAction.ForceOrb when ForceOrbs.TryGet(catalogKey, out var orb):
                lines.Add(orb.MinimumLevel <= 1 && orb.MaximumLevel < 200 ? $"Req. Level: {orb.MinimumLevel}-{orb.MaximumLevel}" : $"Req. Level: {orb.MinimumLevel}+");
                lines.Add($"AddedATKPOWER: {orb.Damage}");
                if (orb.Percent > 0)
                {
                    lines.Add($"AddATKPOWER%: {orb.Percent}%");
                }

                lines.Add($"Duration: {orb.DurationSeconds}sec");
                break;
        }

        return lines;
    }

    private static IEnumerable<string> CrystalLines(MonsterCrystalDefinition crystal) => crystal.Kind switch
    {
        CrystalKind.ClanPet => [$"Summons a {crystal.Name} for your clan.", "Clan members only"],
        CrystalKind.CastleMercenary => [$"Summons a {crystal.Name} to guard", "the castle during a siege."],
        CrystalKind.SoulStone => [$"Releases a hostile soul for {PetMinutes} minutes."],
        _ => [$"Summons a {crystal.Name} to fight for you", $"for {PetMinutes} minutes, longer at higher levels."]
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
        >= 86400 when seconds % 86400 == 0 => seconds / 86400 == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{seconds / 86400} days"),
        >= 3600 when seconds % 3600 == 0 => seconds / 3600 == 1 ? "1 hour" : string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600} hours"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{seconds}sec")
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
