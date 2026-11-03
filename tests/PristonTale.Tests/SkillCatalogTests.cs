using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The class skill files: every skill of the Knight, Atalanta, Priestess and Magician files has its ten-rank costs, a
// rule of a consistent kind, the original slots, levels and weapons, and the per-rank values the server uses.
internal static class SkillCatalogTests
{
    private static readonly PlayerClass[] Morion = [PlayerClass.Knight, PlayerClass.Atalanta, PlayerClass.Priestess, PlayerClass.Magician];

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every Knight, Atalanta, Priestess and Magician skill has ten-rank costs, a rule and its original slot", CoverageAsync);
        yield return ("class skill files keep the original levels, mouse sides, weapons and stamina of the later tiers", MetadataAsync);
        yield return ("class skill rules carry the original per-rank tables of the later tiers", TablesAsync);
        yield return ("motion variants, extra hits, end hits and clip loops time the later-tier strikes", TimingAsync);
        yield return ("passive class skills feed the derived stats through permanent effects", PassivesAsync);
        yield return ("exclusive and replaced effects follow the original combination rules", ExclusiveAsync);
    }

    private static Task CoverageAsync()
    {
        var classes = new CharacterClassRegistry();
        var total = 0;
        foreach (var playerClass in Morion)
        {
            var skills = classes.Get(playerClass).Skills.Take(16).ToArray();
            Assert.True(classes.Get(playerClass).Skills.Count == 20, $"{playerClass} does not list twenty skills from its class file");
            var group = (uint)skills[0].Code & 0xFF000000u;
            for (var index = 0; index < skills.Length; index++)
            {
                var skill = skills[index];
                var tier = index / 4;
                var expected = group | (1u << tier) << 16 | (uint)(index % 4 + 1);
                Assert.True(skill.Code == expected, $"{skill.Name} has code {skill.Code:X8}, expected {expected:X8}");
                Assert.True(skill.ManaByRank.Count == 10, $"{skill.Name} has no ten-rank costs");
                Assert.True(SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.Name == skill.Name, $"{skill.Name} has no rule");
                Assert.True((rule.Kind == SkillKind.Passive) == (skill.Category == SkillCategory.Passive), $"{skill.Name} passive kind disagrees");
                for (var rank = 1; rank <= 10; rank++)
                {
                    Assert.True(skill.ManaForRank(rank) >= 0 && skill.StaminaForRank(rank) >= 0, $"{skill.Name} has a negative cost");
                    if (skill.Category != SkillCategory.Passive)
                    {
                        Assert.True(skill.ManaForRank(rank) > 0 && skill.StaminaForRank(rank) > 0, $"{skill.Name} rank {rank} costs nothing");
                    }
                }

                if (rule.Kind != SkillKind.Passive)
                {
                    Assert.True(rule.MasteryBase > 0, $"{skill.Name} has no recharge");
                    var timing = SkillFormulas.Timing(rule, 10, 1f, (800f, 400f));
                    Assert.True(timing.DurationMs > 0 && timing.HitsAtMs.Length > 0 && timing.HitsAtMs.All(hit => hit > 0 && hit <= timing.DurationMs + 1),
                        $"{skill.Name} timing is invalid");
                }
                else
                {
                    Assert.True(rule.Passive is not null, $"{skill.Name} passive has no effect");
                }

                total++;
            }
        }

        Assert.True(total == 64, $"expected 64 skills, found {total}");
        return Task.CompletedTask;
    }

    private static Task MetadataAsync()
    {
        var classes = new CharacterClassRegistry();
        // Level, stamina base and per rank, mouse sockets and rank 1 and 10 mana of tiers 2-4 in slot order.
        var expected = new Dictionary<PlayerClass, (int Level, int Stamina, int Step, SkillMouseUse Mouse, int First, int Last)[]>
        {
            [PlayerClass.Knight] =
            [
                (20, 36, 2, SkillMouseUse.Right, 23, 50), (23, 38, 2, SkillMouseUse.Both, 22, 45), (26, 41, 2, SkillMouseUse.Both, 24, 42),
                (30, 44, 2, SkillMouseUse.Right, 25, 54), (40, 0, 0, SkillMouseUse.None, 0, 0), (43, 46, 2, SkillMouseUse.Right, 30, 65),
                (46, 50, 2, SkillMouseUse.Right, 36, 70), (50, 45, 2, SkillMouseUse.Right, 28, 55), (60, 53, 2, SkillMouseUse.Right, 50, 77),
                (63, 50, 2, SkillMouseUse.Right, 47, 74), (66, 51, 2, SkillMouseUse.Right, 57, 84), (70, 51, 2, SkillMouseUse.Right, 45, 63)
            ],
            [PlayerClass.Atalanta] =
            [
                (20, 34, 2, SkillMouseUse.Right, 24, 48), (23, 36, 2, SkillMouseUse.Right, 20, 48), (26, 42, 2, SkillMouseUse.Right, 25, 60),
                (30, 40, 2, SkillMouseUse.Both, 25, 50), (40, 43, 2, SkillMouseUse.Right, 30, 57), (43, 50, 2, SkillMouseUse.Right, 32, 65),
                (46, 46, 2, SkillMouseUse.Both, 30, 56), (50, 48, 2, SkillMouseUse.Right, 34, 59), (60, 52, 2, SkillMouseUse.Right, 36, 70),
                (63, 48, 2, SkillMouseUse.Right, 34, 62), (66, 50, 2, SkillMouseUse.Right, 33, 65), (70, 55, 2, SkillMouseUse.Right, 33, 51)
            ],
            [PlayerClass.Priestess] =
            [
                (20, 0, 0, SkillMouseUse.None, 0, 0), (23, 21, 1, SkillMouseUse.Right, 24, 53), (26, 23, 1, SkillMouseUse.Right, 24, 57),
                (30, 27, 1, SkillMouseUse.Right, 24, 59), (40, 24, 1, SkillMouseUse.Both, 26, 51), (43, 31, 1, SkillMouseUse.Right, 70, 34),
                (46, 28, 1, SkillMouseUse.Right, 30, 60), (50, 31, 1, SkillMouseUse.Right, 60, 97), (60, 35, 1, SkillMouseUse.Right, 65, 92),
                (63, 41, 1, SkillMouseUse.Right, 100, 136), (66, 41, 1, SkillMouseUse.Right, 76, 103), (70, 47, 1, SkillMouseUse.Right, 110, 146)
            ],
            [PlayerClass.Magician] =
            [
                (20, 0, 0, SkillMouseUse.None, 0, 0), (23, 21, 1, SkillMouseUse.Right, 25, 55), (26, 28, 1, SkillMouseUse.Right, 30, 64),
                (30, 26, 1, SkillMouseUse.Both, 26, 59), (40, 40, 1, SkillMouseUse.Right, 34, 72), (43, 33, 1, SkillMouseUse.Right, 30, 67),
                (46, 35, 1, SkillMouseUse.Right, 36, 63), (50, 30, 1, SkillMouseUse.Right, 35, 66), (60, 120, 2, SkillMouseUse.Right, 52, 88),
                (63, 32, 1, SkillMouseUse.Right, 75, 111), (66, 43, 1, SkillMouseUse.Right, 105, 150), (70, 39, 1, SkillMouseUse.Right, 100, 136)
            ]
        };
        foreach (var (playerClass, rows) in expected)
        {
            var skills = classes.Get(playerClass).Skills;
            for (var index = 0; index < rows.Length; index++)
            {
                var skill = skills[index + 4];
                var row = rows[index];
                Assert.True(skill.RequiredLevel == row.Level && skill.StaminaBase == row.Stamina && skill.StaminaPerRank == row.Step && skill.MouseUse == row.Mouse
                    && skill.ManaForRank(1) == row.First && skill.ManaForRank(10) == row.Last, $"{skill.Name} metadata differs from the original");
                Assert.True(skill.IconKey.Length > 0 && skill.Description.Length > 0, $"{skill.Name} lost its icon or text");
            }
        }

        var knight = classes.Get(PlayerClass.Knight).Skills;
        Assert.True(knight[5].AllowsWeapon(ItemKind.Pole) && knight[5].AllowsWeapon(ItemKind.Sword) && !knight[5].AllowsWeapon(ItemKind.Axe),
            "Brandish weapons changed");
        Assert.True(knight[9].AllowsWeapon(ItemKind.Shield) && knight[13].AllowsWeapon(ItemKind.Shield), "shield skills lost their shield");
        var atalanta = classes.Get(PlayerClass.Atalanta).Skills;
        Assert.True(atalanta[12].AllowsWeapon(ItemKind.Bow) && !atalanta[12].AllowsWeapon(ItemKind.Sword), "Hall of Valhalla weapons changed");
        Assert.True(atalanta[9].AllowsWeapon(ItemKind.Staff) && atalanta[9].AllowsWeapon(ItemKind.Sword), "Triumph of Valhalla weapons changed");
        Assert.True(classes.Get(PlayerClass.Magician).Skills.Skip(4).All(skill => skill.AllowedWeaponsMask == 0 || skill.AllowsWeapon(ItemKind.Staff)),
            "magician skills lost their staff");
        return Task.CompletedTask;
    }

    private static Task TablesAsync()
    {
        Rule(0x05020001, out var valor);
        Assert.True(valor.Buff.Modifiers[0].Stat == StatKind.DamageVsUndead && valor.At(valor.Buff.Modifiers[0].Values, 10) == 54
            && valor.At(valor.Buff.DurationTenths, 1) == 600 && valor.At(valor.Allies.Radius, 1) == 200, "Holy Valor tables changed");
        Rule(0x05020003, out var piercing);
        Assert.True(piercing.RollsAccuracy && piercing.At(piercing.AttackRating, 10) == 130 && piercing.At(piercing.Damage.Percent, 10) == 73, "Piercing");
        Rule(0x05080001, out var justice);
        Assert.True(justice.BoxWidth == 140 && justice.At(justice.Radius, 10) == 215 && justice.At(justice.Damage.Percent, 1) == 100, "Sword of Justice");
        Rule(0x06040003, out var lightning);
        Assert.True(lightning.Damage.Element == Element.Lighting && lightning.Damage.Main is { AttackPower: true } && lightning.At(lightning.Damage.Max, 10) == 96,
            "Lightning Javelin");
        Rule(0x06080003, out var frost);
        Assert.True(frost.Buff.Enchant is { BasicOnly: true } enchant && SkillFormulas.RankValue(enchant.Choices[0].Max, 10) == 86
            && SkillFormulas.RankValue(enchant.Choices[0].SlowTenths, 10) == 90, "Frost Javelin");
        Rule(0x07020002, out var divine);
        Assert.True(divine.At(divine.MaxTargetsByRank, 10) == 6 && divine.Damage.ResistanceDivisor == 10, "Divine Lightning");
        Rule(0x07080003, out var chain);
        Assert.True(chain.AreaShape == SkillAreaShape.Chain && chain.At(chain.MaxTargetsByRank, 10) == 9 && chain.At(chain.Radius, 10) == 185, "Chain Lightning");
        Rule(0x07040002, out var resurrection);
        Assert.True(resurrection.Kind == SkillKind.Resurrect && SkillFormulas.RankValue(resurrection.Resurrect.Chance, 10) == 94, "Resurrection");
        Rule(0x08040001, out var shield);
        Assert.True(shield.Buff.Absorb is { Resource: ResourceKind.Mana } absorb && SkillFormulas.RankValue(absorb.Percent, 10) == 30, "Energy Shield");
        Rule(0x08080001, out var elemental);
        Assert.True(elemental.Kind == SkillKind.Summon && elemental.At(elemental.Summon.Life, 10) == 3800 && elemental.Summon.Monster == "Fire Elemental",
            "Fire Elemental");
        Rule(0x08080004, out var meteo);
        Assert.True(meteo.EndHit && meteo.Damage.ResistanceDivisor == 2 && meteo.Damage.SpellPower && meteo.At(meteo.Damage.Min, 10) == 224, "Meteo");
        Rule(0x07080004, out var muspell);
        Assert.True(muspell.Buff.Companion is { } companion && muspell.At(companion.IntervalMs, 10) == 4000
            && SkillFormulas.RankValue(companion.Choices[0].Max, 10) == 140 && muspell.Buff.Guard is { OnBlock: false }, "Summon Muspell");
        return Task.CompletedTask;
    }

    private static Task TimingAsync()
    {
        Rule(0x05080004, out var piercing);
        Assert.True(SkillFormulas.Timing(piercing, 1, 1f, default).HitsAtMs.Length == 3 && SkillFormulas.Timing(piercing, 5, 1f, default).HitsAtMs.Length == 5
            && SkillFormulas.Timing(piercing, 9, 1f, default).HitsAtMs.Length == 7, "Divine Piercing hits do not follow its rank's motion");
        Assert.True(piercing.ForRank(10).MotionIndex == 174 && piercing.ForRank(4).MotionIndex == 172, "Divine Piercing motion variant");
        Rule(0x06040004, out var storm);
        var stormTiming = SkillFormulas.Timing(storm, 1, 1f, default);
        Assert.True(stormTiming.HitsAtMs.Length == 2 && stormTiming.HitsAtMs[1] > stormTiming.DurationMs / 2, "Storm Javelin does not strike once per loop");
        Rule(0x08040002, out var diastrophism);
        var quake = SkillFormulas.Timing(diastrophism, 1, 1f, default);
        Assert.True(quake.HitsAtMs.Length == 2 && Math.Abs(quake.HitsAtMs[1] - quake.DurationMs) < 1f, "Diastrophism lost its end hit");
        Rule(0x06080002, out var rage);
        Assert.True(SkillFormulas.Timing(rage, 1, 1f, default).HitsAtMs.Length == 1, "X-Rage strikes more than once");
        Rule(0x05040004, out var cross);
        Assert.True(SkillFormulas.SkillClipRate(cross, 1f) > 1f, "Grand Cross lost its attack speed step");
        return Task.CompletedTask;
    }

    private static Task PassivesAsync()
    {
        var classes = new CharacterClassRegistry();
        var priest = classes.CreatePlayer(PlayerClass.Priestess, "Meditation", 40);
        var before = priest.Effects.Totals.Flat(StatKind.ManaRegen);
        priest.Skills.Restore(0x07020001, 10);
        Assert.True(priest.Effects.Totals.Flat(StatKind.ManaRegen) == before + 42 && Regeneration.ManaPerSecond(priest) > 0, "Meditation regeneration");
        var mage = classes.CreatePlayer(PlayerClass.Magician, "Mental", 40);
        var mana = mage.MaxMana;
        mage.Skills.Restore(0x08020001, 10);
        Assert.True(mage.MaxMana == mana * 132 / 100, $"Mental Mastery max mana {mage.MaxMana} from {mana}");
        var knight = classes.CreatePlayer(PlayerClass.Knight, "Mastery", 50);
        knight.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Sword, 100, 200));
        var plain = WeaponDamage.Compute(knight);
        knight.Skills.Restore(0x05040001, 10);
        var mastered = WeaponDamage.Compute(knight);
        Assert.True(mastered.Min == plain.Min + 38 && mastered.Max == plain.Max + 76, $"Sword Mastery {plain} -> {mastered}");
        knight.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Axe, 100, 200));
        Assert.True(!knight.Effects.TryGet(0x05040001, out _), "Sword Mastery stayed with an axe");
        return Task.CompletedTask;
    }

    private static Task ExclusiveAsync()
    {
        var field = SkillFixtures.Field();
        var knight = SkillFixtures.Player(field, 1, PlayerClass.Knight, 80, default, SkillFixtures.Weapon(ItemKind.Sword), SkillFixtures.Shield());
        knight.Character.Skills.Restore(0x05010002, 5);
        knight.Character.Skills.Restore(0x05020001, 5);
        knight.Character.Skills.Restore(0x05040002, 5);
        knight.Character.Skills.Restore(0x05080002, 5);
        SkillFixtures.Accept("Holy Body", field, knight, 0x05010002);
        SkillFixtures.Finish(field, knight);
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "Holy Valor stacked on Holy Body", field, knight, 0x05020001);
        SkillFixtures.Accept("Divine Inhalation", field, knight, 0x05040002);
        SkillFixtures.Finish(field, knight);
        Assert.True(knight.Character.Effects.TryGet(0x05040002, out _), "Divine Inhalation not applied");
        SkillFixtures.Wait(field, 20f);
        SkillFixtures.Accept("Godly Shield", field, knight, 0x05080002);
        Assert.True(!knight.Character.Effects.TryGet(0x05040002, out _), "Godly Shield did not replace Divine Inhalation");
        knight.Character.Equip(EquipmentSlot.OffHand, null);
        SkillFixtures.Finish(field, knight);
        Assert.True(!knight.Character.Effects.TryGet(0x05080002, out _), "Godly Shield outlived its shield");
        return Task.CompletedTask;
    }

    private static void Rule(uint code, out SkillRule rule) => Assert.True(SkillRuleCatalog.TryGet(code, out rule), $"rule {code:X8} missing");
}
