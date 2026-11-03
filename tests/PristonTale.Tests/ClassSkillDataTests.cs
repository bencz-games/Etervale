using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using System.Text.Json;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ClassSkillDataTests
{
    private sealed record SourceSkill(uint Code, string Name, string Description, string Icon, int Level, int Stamina, int Step, string Use, int[] Mana,
        string[] Weapons);

    // The ported classes have their first four tiers' sixteen skills ported; the fifth tier holds definitions. The Shaman
    // keeps definitions only: its first tier has costs but no castable rules, its later tiers neither.
    private static readonly PlayerClass[] Ported =
    [
        PlayerClass.Mechanician, PlayerClass.Fighter, PlayerClass.Pikeman, PlayerClass.Archer, PlayerClass.Knight, PlayerClass.Atalanta,
        PlayerClass.Priestess, PlayerClass.Magician, PlayerClass.Assassin
    ];

    private const int PortedSkills = 16;

    // Names whose original spelling was a typo or an abbreviation; the window shows the corrected one.
    private static readonly Dictionary<string, string> Renamed = new()
    {
        ["Completion"] = "Compulsion",
        ["Bone crash"] = "Bone Crash",
        ["Weapon DEF Mastery"] = "Weapon Defence Mastery",
        ["Venom sphere"] = "Venom Spear",
        ["Chain Lancer"] = "Chain Lance",
        ["Bum shot"] = "Bomb Shot"
    };

    private static readonly Dictionary<string, ItemKind> WeaponCodes = new()
    {
        ["WA1"] = ItemKind.Axe, ["WC1"] = ItemKind.Claw, ["WD1"] = ItemKind.Dagger, ["WH1"] = ItemKind.Hammer, ["WM1"] = ItemKind.Staff,
        ["WN1"] = ItemKind.Phantom, ["WP1"] = ItemKind.Pole, ["WS1"] = ItemKind.Bow, ["WS2"] = ItemKind.Sword, ["WT1"] = ItemKind.ThrowingWeapon,
        ["DS1"] = ItemKind.Shield
    };

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("class skill data keeps the original codes, window order, levels, mouse sides, weapons and ten-rank costs", MetadataAsync);
        yield return ("every skill of the ported classes has ten-rank costs and a complete rule on every rank", PortedRulesAsync);
        yield return ("class skill files reject unknown properties and brood names at any depth", StrictDataAsync);
        yield return ("the Assassin's skills keep the original hit frames, areas, party reach and dagger rules", AssassinAsync);
    }

    private static Task AssassinAsync()
    {
        static SkillRule Rule(uint code) => SkillRuleCatalog.TryGet(code, out var rule) ? rule : throw new InvalidOperationException($"{code:X8} has no rule");

        var stinger = Rule(0x10010001);
        Assert.True(stinger.ForRank(1).HitFramesByRank[0].SequenceEqual([26]) && stinger.Damage.BonusPercent == 30, "Stinger strikes once, at its second event");
        var gust = Rule(0x10040002);
        Assert.True(gust is { Kind: SkillKind.Area, AreaCenter: SkillAreaCenter.Target, AreaAccuracy: true } && gust.Damage is { AreaPercent: 50, MainPercent: 50 },
            "Gust Slash does not split its power between the target and the enemies near it");
        Assert.True(Rule(0x10020002).Allies is { Party: true } alas && alas.Radius[0] == 350, "Alas does not reach the party within 350");
        Assert.True(Rule(0x10020003).Buff is { Control: Simulation.Modifiers.ControlFlags.Stun, ResistedByBionic: true }, "Soul Shock does not stun");
        Assert.True(Rule(0x10080003).MaxTargetsByRank[9] == 5 && Rule(0x10080003).Damage.Element == Simulation.Combat.Element.Poison, "Polluted lost its target cap or poison");
        Assert.True(Rule(0x10080002).Passive is { Effect: PassiveEffect.CriticalChance } && ClassSkillCatalog.TryGet(0x10080002, out var mastery)
            && mastery.AllowsWeapon(ItemKind.Dagger) && !mastery.AllowsWeapon(ItemKind.Sword), "the Assassin's mastery does not need daggers");
        Assert.True(Rule(0x10080004).ForRank(1).HitFramesByRank[0].SequenceEqual([1, 19]), "Ninja Shadow does not strike at the cast and at its event");
        return Task.CompletedTask;
    }

    private static Task StrictDataAsync()
    {
        var assembly = typeof(ClassSkillCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("PristonTale.Simulation.Skills.Data.Classes.Archer.json")!;
        var json = new StreamReader(stream).ReadToEnd();
        Assert.True(ClassSkillCatalog.Parse(json, out _).Count == 20, "the shipped Archer file did not parse");
        const string brood = "\"bonusBroods\": [\"Normal\"]";
        const string percent = "\"bonusPercent\": 30}";
        Assert.True(json.Contains(brood, StringComparison.Ordinal) && json.Contains(percent, StringComparison.Ordinal), "the Archer sample changed");
        foreach (var broken in new[]
        {
            json.Replace(brood, "\"bonusBroods\": [\"Normall\"]", StringComparison.Ordinal),
            json.Replace(percent, "\"bonusPercent\": 30, \"bonusPercnt\": 30}", StringComparison.Ordinal),
            json.Replace("\"code\":", "\"cdoe\": \"0x0\", \"code\":", StringComparison.Ordinal)
        })
        {
            var threw = false;
            try
            {
                ClassSkillCatalog.Parse(broken, out _);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            Assert.True(threw, "a misspelled skill property or brood was accepted");
        }

        return Task.CompletedTask;
    }

    private static Task MetadataAsync()
    {
        var seen = new HashSet<uint>();
        var classes = new CharacterClassRegistry();
        var tiers = new[] { JobTier.Job1, JobTier.Job2, JobTier.Job3, JobTier.Job4, JobTier.Job5 };
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            var skills = ClassSkillCatalog.For(playerClass);
            var character = classes.CreatePlayer(playerClass, "Metadata");
            Assert.True(skills.Count == 20 && character.Class.Skills.Count == 20, $"{playerClass} must expose twenty skills");
            var levels = new[] { 10, 12, 14, 17 };
            for (var index = 0; index < skills.Count; index++)
            {
                var skill = skills[index];
                Assert.True(seen.Add(skill.Code), "duplicate original skill identity");
                Assert.True(ReferenceEquals(character.Class.Skills[index], skill), "class retained a parallel skill definition");
                Assert.True(skill.Tier == tiers[index / 4] && (skill.Code >> 16 & 0xFF) == (uint)skill.Tier, $"{skill.Name} is out of window order");
                Assert.True(skill.IconKey.Length > 0 && (skill.Description.Length > 0 || skill.ManaByRank.Count == 0),
                    $"{skill.Name} lost its presentation metadata");
                if (index < 4)
                {
                    Assert.True(skill.RequiredLevel == levels[index] && skill.ManaByRank.Count == 10, $"{skill.Name} level or rank count changed");
                }

                if (skill.ManaByRank.Count != 10)
                {
                    Assert.True(skill.ManaByRank.Count == 0, $"{skill.Name} has a partial cost table");
                    continue;
                }

                for (var rank = 1; rank <= 10; rank++)
                {
                    Assert.True(skill.ManaForRank(rank) >= 0 && skill.StaminaForRank(rank) >= 0, "negative resource cost");
                    if (skill.Category == SkillCategory.Passive)
                    {
                        Assert.True(skill.MouseUse == SkillMouseUse.None && skill.ManaForRank(rank) == 0 && skill.StaminaForRank(rank) == 0,
                            "passive skill is assignable or consumes cast resources");
                    }
                }
            }
        }

        Assert.True(seen.Count == 200, "class skill catalog is incomplete");
        Assert.True(Enum.GetValues<PlayerClass>().All(playerClass => ClassSkillCatalog.For(playerClass).Skip(16)
                .All(skill => skill.RequiredLevel is 80 or 83 or 86 or 90 && skill.ManaByRank.Count == 10)),
            "a fifth-tier skill lost its level or its costs");
        VerifySourceSnapshot();
        var mechanic = ClassSkillCatalog.For(PlayerClass.Mechanician);
        Assert.True(mechanic[2].Code == 0x01010004 && mechanic[3].Code == 0x01010003, "display order was incorrectly used as skill identity");
        Assert.True(mechanic[0].ManaForRank(1) == 12 && mechanic[0].ManaForRank(10) == 44 && mechanic[0].StaminaForRank(10) == 52,
            "Extreme Shield costs disagree with original arrays");
        Assert.True(mechanic[0].AllowsWeapon(ItemKind.Shield) && !mechanic[0].AllowsWeapon(ItemKind.Axe), "shield restriction lost");
        Assert.True(ClassSkillCatalog.For(PlayerClass.Knight)[3].MouseUse == SkillMouseUse.Right, "Double Crash incorrectly allowed on left mouse");
        Assert.True(ClassSkillCatalog.For(PlayerClass.Priestess)[2].MouseUse == SkillMouseUse.Right, "Multi Spark incorrectly allowed on left mouse");
        Assert.True(ClassSkillCatalog.For(PlayerClass.Magician)[0].ManaForRank(10) == 14, "Agony rank 10 mana cost changed");
        Assert.True(ClassSkillCatalog.For(PlayerClass.Assassin)[2].AllowsWeapon(ItemKind.Dagger), "Dual Wield Mastery lost its dagger restriction");
        Assert.True(ClassSkillCatalog.For(PlayerClass.Shaman)[0].AllowsWeapon(ItemKind.Phantom), "Dark Bolt lost its phantom restriction");
        Assert.True(ClassSkillCatalog.TryGet(0x04020002, out var falcon) && falcon.Name == "Falcon" && falcon.ManaForRank(10) == 52,
            "Falcon costs disagree with the original arrays");
        return Task.CompletedTask;
    }

    private static void VerifySourceSnapshot()
    {
        using var stream = typeof(ClassSkillDataTests).Assembly.GetManifestResourceStream("PristonTale.Tests.Fixtures.class-skill-source.json")
            ?? throw new InvalidOperationException("Original skill fixture is missing.");
        var rows = JsonSerializer.Deserialize<SourceSkill[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(rows.Length == 88, $"the source snapshot holds the forty first-tier and forty-eight later skills, got {rows.Length}");
        foreach (var source in rows)
        {
            Assert.True(ClassSkillCatalog.TryGet(source.Code, out var skill), $"source skill {source.Code:X8} is missing");
            var name = Renamed.GetValueOrDefault(source.Name, source.Name);
            Assert.True(skill.Name == name && skill.Description == source.Description && skill.IconKey == source.Icon,
                $"{name} identity or presentation changed");
            Assert.True(skill.RequiredLevel == source.Level, $"{name} required level changed");
            var mouseUse = source.Use switch
            {
                "Both" => SkillMouseUse.Both,
                "Right" => SkillMouseUse.Right,
                "Left" => SkillMouseUse.Left,
                "None" => SkillMouseUse.None,
                _ => throw new InvalidDataException(source.Use)
            };
            Assert.True(skill.MouseUse == mouseUse, $"{name} mouse use changed");
            var mask = source.Weapons.Aggregate(0u, (current, code) => current | 1u << (int)WeaponCodes[code]);
            Assert.True(skill.AllowedWeaponsMask == mask, $"{name} weapons changed");
            for (var rank = 1; rank <= 10; rank++)
            {
                Assert.True(skill.ManaForRank(rank) == source.Mana[rank - 1], $"{name} mana cost changed");
                Assert.True(skill.StaminaForRank(rank) == source.Stamina + source.Step * (rank - 1), $"{name} stamina cost changed");
            }
        }
    }

    private static Task PortedRulesAsync()
    {
        var count = 0;
        foreach (var playerClass in Ported)
        {
            var skills = ClassSkillCatalog.For(playerClass).Take(PortedSkills).ToArray();
            foreach (var skill in skills)
            {
                Assert.True(skill.ManaByRank.Count == 10, $"{skill.Name} has no ten-rank costs");
                Assert.True(SkillRuleCatalog.TryGet(skill.Code, out var rule), $"{skill.Name} has no rule");
                Assert.True(rule.Name == skill.Name, $"rule name {rule.Name} disagrees with {skill.Name}");
                Assert.True((rule.Kind == SkillKind.Passive) == (skill.Category == SkillCategory.Passive), $"{skill.Name} passive mismatch");
                count++;
                if (rule.Kind == SkillKind.Passive)
                {
                    continue;
                }

                Assert.True(rule.MasteryBase > 0 && (rule.MotionIndex > 0 || rule.Motion != SkillMotion.Skill), $"{skill.Name} has no mastery or motion");
                Assert.True(skill.MouseUse != SkillMouseUse.None, $"{skill.Name} cannot be put on a mouse button");
                for (var rank = 1; rank <= 10; rank++)
                {
                    var timing = SkillFormulas.Timing(rule, rank, 1f, (800f, 400f));
                    Assert.True(timing.DurationMs > 0 && timing.HitsAtMs.Length > 0 && timing.HitsAtMs.All(hit => hit > 0 && hit <= timing.DurationMs + 1),
                        $"{skill.Name} rank {rank} timing is invalid");
                    var cooldown = SkillFormulas.CooldownMs(rule, rank, 0, 0, 0);
                    Assert.True(cooldown is >= 490 and <= 17150, $"{skill.Name} rank {rank} cooldown {cooldown} left the gauge");
                }

                if (rule.Buff is { } buff)
                {
                    Assert.True(buff.DurationTenths.Length is 0 or 10, $"{skill.Name} buff duration is not ranked");
                }
            }

            var finalTier = skills.Skip(12).Select(skill => SkillRuleCatalog.TryGet(skill.Code, out var rule) ? rule : null).ToArray();
            Assert.True(finalTier.All(rule => rule is not null && (rule.Kind == SkillKind.Passive || SkillFormulas.IsFullyTrained(rule.Code))),
                $"{playerClass} final-tier rules are not recognised");
        }

        Assert.True(count == 144, $"expected 144 ported skills with rules, got {count}");
        Assert.True(SkillRuleCatalog.Count == count, $"rules exist for skills that are not ported: {SkillRuleCatalog.Count}");
        foreach (var playerClass in new[] { PlayerClass.Shaman })
        {
            Assert.True(ClassSkillCatalog.For(playerClass).All(skill => !SkillRuleCatalog.TryGet(skill.Code, out var rule) || rule.Kind == SkillKind.Passive),
                $"{playerClass} gained castable rules");
        }
        return Task.CompletedTask;
    }
}
