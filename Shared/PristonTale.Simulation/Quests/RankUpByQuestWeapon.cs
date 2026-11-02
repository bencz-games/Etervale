using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// Second rank-up: three vamp sheltoms buy a quest weapon of the class. Wielded, it grows three times by hunting (melee
// classes and archers count one monster per stage, casters every monster of one field per stage); each growth adds
// life, damage and attack rating (mana regeneration for staves). Grown, it earns the rank-up and a ring; five days
// after the weapon was made both vanish and the master grants fifteen life for good.
public static class RankUpByQuestWeapon
{
    public const QuestCode Code = QuestCode.RankUpQuestWeapon;
    public const int VampsNeeded = 3;
    public const long WeaponLifetimeMs = 5L * 24 * 60 * 60 * 1000;
    public const int FinalStage = 4;

    private const int Collecting = 1;
    private const int Paying = 2;
    private const int Growing = 3;
    private const int Grown = 4;
    private const int Waiting = 5;
    private const int WeaponGone = 6;

    // Kills of each stage, indexed from the hunt's first entry: melee from 0, ranged from 3, field from 6.
    private static readonly int[] StageKills = [30, 40, 50, 25, 35, 45, 50, 70, 100];
    private static readonly int[] StageFields = [8, 10, 13];
    // What a stage hunts, as the weapon's box names it, indexed like StageKills.
    private static readonly string[] StageTargets =
    [
        "Bargon", "Skeleton Warrior", "Head Cutter", "Armored Beetle", "Skeleton Ranger", "Titan", "Forbidden Land's Monster", "Oasis' Monster",
        "Ancient Prison 1F's Monster"
    ];

    private static readonly uint StaffFamily = ItemCodes.Family("WM1");

    private static int HuntBase(QuestWeaponHunt hunt) => hunt switch
    {
        QuestWeaponHunt.Ranged => 3,
        QuestWeaponHunt.Field => 6,
        _ => 0
    };

    public static int KillsForStage(QuestWeaponHunt hunt, int stage) => StageKills[Math.Clamp(HuntBase(hunt) + stage - 1, 0, StageKills.Length - 1)];

    // The monster quest code a melee or ranged stage hunts (Bargon, Skeleton Warrior, Head Cutter; Armored Beetle, Skeleton
    // Ranger, Titan), or the field a caster's stage hunts in.
    public static int StagePrey(QuestWeaponHunt hunt, int stage) => hunt == QuestWeaponHunt.Field ? 0 : HuntBase(hunt) + stage;

    public static int StageField(QuestWeaponHunt hunt, int stage) => hunt == QuestWeaponHunt.Field ? StageFields[Math.Clamp(stage - 1, 0, 2)] : 0;

    // The growing weapon's box line: the stage's prey or field, the kills made and the kills the stage needs
    // ("Bargon:12/30"); empty once grown.
    public static string HuntLine(QuestWeaponHunt hunt, int stage, int killsLeft)
    {
        if (stage is < 1 or >= FinalStage)
        {
            return "";
        }

        var index = Math.Clamp(HuntBase(hunt) + stage - 1, 0, StageKills.Length - 1);
        var needed = StageKills[index];
        return $"{StageTargets[index]}:{Math.Clamp(needed - killsLeft, 0, needed)}/{needed}";
    }

    public static bool TalkAtMaster(QuestContext context)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != Code)
        {
            return false;
        }

        switch (active.State)
        {
            case Collecting when QuestInventory.Count(context.Character, QuestItems.Vamp) >= VampsNeeded:
                journal.Active = active with { State = Paying };
                context.Show(QuestSteps.Proceed(Code, Paying, "ChangeJob3_2"));
                return true;
            case Paying:
                context.Show(QuestSteps.Proceed(Code, Paying, "ChangeJob3_2"));
                return true;
            case Grown:
                context.Show(RankUpRules.RankUpPrompt(context, Code, Grown, "ChangeJob3_4"));
                return true;
            case WeaponGone when context.Character.Tier == 2:
                context.Show(QuestSteps.Proceed(Code, WeaponGone, "ChangeJob3_5"));
                return false;
            default:
                return false;
        }
    }

    public static bool Accept(QuestContext context, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (state == 0)
        {
            if (!active.IsIdle || RankUpRules.Offer(context) != Code)
            {
                return QuestSteps.Stale(context);
            }

            journal.Active = new ActiveQuest { Code = Code, State = Collecting };
            return true;
        }

        if (state != Grown || active.Code != Code || active.State != Grown || context.Character.Tier != 1 || !context.Npc.HomeMaster
            || context.Npc.ClassChangeGroup < 2)
        {
            return QuestSteps.Stale(context);
        }

        // The rank-up comes with the ring of the class, bound to the quest like the weapon.
        var ringCode = QuestClasses.RankUpRing(QuestClasses.ClassOf(context.Character));
        var ringId = Guid.Empty;
        if (!QuestInventory.Has(context.Character, ringCode))
        {
            if (!context.Items.TryCreateQuestVariant(ringCode, 0, out var ring) || !QuestInventory.TryGiveAll(context, [ring]))
            {
                return QuestInventory.Refused(context);
            }

            ringId = ring.InstanceId;
        }

        journal.Active = active with { State = Waiting, RingId = ringId };
        RankUpRules.RankUp(context);
        return true;
    }

    public static bool Proceed(QuestContext context, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != Code || active.State != state)
        {
            return QuestSteps.Stale(context);
        }

        if (state == WeaponGone)
        {
            if (context.Character.Tier != 2)
            {
                return QuestSteps.Stale(context);
            }

            context.Notice(QuestNoticeKind.Message, QuestTexts.LifeFifteen);
            QuestSteps.Finish(context, Code);
            return true;
        }

        if (state != Paying)
        {
            return QuestSteps.Stale(context);
        }

        var character = context.Character;
        var weaponCode = QuestClasses.QuestWeapon(QuestClasses.ClassOf(character));
        if (QuestInventory.Count(character, QuestItems.Vamp) < VampsNeeded || weaponCode == 0
            || !context.Items.TryCreateQuestVariant(weaponCode, 0, out var weapon))
        {
            journal.Active = active with { State = Collecting };
            return context.Fail(QuestStatus.Rejected, "the master cannot forge a quest weapon for this class");
        }

        QuestInventory.Remove(character, QuestItems.Vamp, VampsNeeded);
        if (!QuestInventory.TryGiveAll(context, [weapon]))
        {
            journal.Active = active with { State = Collecting };
            return QuestInventory.Refused(context);
        }

        var hunt = QuestClasses.WeaponHunt(QuestClasses.ClassOf(character));
        journal.Active = active with
        {
            State = Growing,
            WeaponId = weapon.InstanceId,
            WeaponCreatedUnixMs = context.NowUnixMs,
            WeaponStage = 1,
            WeaponKillsLeft = KillsForStage(hunt, 1),
            StartLevel = character.Level
        };
        return true;
    }

    // Kill credit while the quest weapon is wielded; returns true when the stage's kills are done and the weapon should grow.
    public static bool OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        var character = context.Character;
        if (active.Code != Code || active.State != Growing || active.WeaponStage is < 1 or >= FinalStage || active.WeaponKillsLeft <= 0
            || character.GetEquipped(EquipmentSlot.Weapon)?.InstanceId != active.WeaponId)
        {
            return false;
        }

        var hunt = QuestClasses.WeaponHunt(QuestClasses.ClassOf(character));
        var counts = hunt == QuestWeaponHunt.Field
            ? context.FieldCode == StageField(hunt, active.WeaponStage)
            : monsterCode == StagePrey(hunt, active.WeaponStage);
        if (!counts)
        {
            return false;
        }

        context.Journal.Active = active with { WeaponKillsLeft = active.WeaponKillsLeft - 1 };
        return active.WeaponKillsLeft - 1 <= 0;
    }

    // Grows the weapon once its stage's kills are done.
    public static bool Grow(QuestContext context)
    {
        var journal = context.Journal;
        var active = journal.Active;
        var character = context.Character;
        if (active.Code != Code || active.State != Growing || active.WeaponKillsLeft > 0 || active.WeaponStage is < 1 or >= FinalStage
            || character.FindOwnedItem(active.WeaponId) is not { } weapon)
        {
            return false;
        }

        var damage = context.Rng.NextInclusive(1, 2);
        var roll = weapon.Roll with { DamageMin = weapon.Roll.DamageMin + damage, DamageMax = weapon.Roll.DamageMax + damage };
        var manaRegen = 0;
        if (ItemCodes.Family(weapon.Definition.Code) == StaffFamily)
        {
            manaRegen = context.Rng.NextInclusive(3, 6);
        }
        else
        {
            roll = roll with { AttackRating = roll.AttackRating + context.Rng.NextInclusive(10, 15) };
        }

        weapon.Roll = roll;
        character.TouchInventory();
        var stage = active.WeaponStage + 1;
        var hunt = QuestClasses.WeaponHunt(QuestClasses.ClassOf(character));
        journal.Active = active with
        {
            WeaponStage = stage,
            WeaponKillsLeft = stage >= FinalStage ? 0 : KillsForStage(hunt, stage),
            WeaponLifeBonus = active.WeaponLifeBonus + 4,
            WeaponManaRegenTenths = active.WeaponManaRegenTenths + manaRegen,
            State = stage >= FinalStage ? Grown : Growing
        };
        character.RefreshQuestEffects();
        context.Notice(QuestNoticeKind.Message, QuestTexts.QuestItemLevelUp);
        context.Notice(QuestNoticeKind.Fanfare, "");
        return true;
    }

    // Levelling more than two levels past the weapon without finishing its hunt slows it for the session.
    public static bool CheckLevelSkip(QuestContext context)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != Code || journal.WeaponSlowed || !QuestInventory.Holds(context.Character, active.WeaponId)
            || context.Character.Level <= active.StartLevel + 2)
        {
            return false;
        }

        journal.Active = active with { StartLevel = context.Character.Level };
        journal.WeaponSlowed = true;
        context.Notice(QuestNoticeKind.Message, QuestTexts.QuestItemSlowed);
        return true;
    }

    public static bool Expired(QuestJournal journal, long nowUnixMs)
    {
        var active = journal.Active;
        return active.Code == Code && active.State == Waiting && active.WeaponCreatedUnixMs > 0 && nowUnixMs > active.WeaponCreatedUnixMs + WeaponLifetimeMs;
    }

    // Five days after it was made the weapon and the ring vanish; the master then finishes the quest.
    public static bool Expire(QuestContext context)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != Code || active.State != Waiting || context.Character.Tier < 2)
        {
            return false;
        }

        var character = context.Character;
        var weaponName = character.FindOwnedItem(active.WeaponId)?.Definition.Name ?? "";
        if (Expired(journal, context.NowUnixMs))
        {
            QuestInventory.RemoveInstance(character, active.WeaponId);
            QuestInventory.RemoveInstance(character, active.RingId);
        }

        if (QuestInventory.Holds(character, active.WeaponId) || QuestInventory.Holds(character, active.RingId))
        {
            return false;
        }

        journal.Active = active with { State = WeaponGone, WeaponId = Guid.Empty, RingId = Guid.Empty };
        context.Notice(QuestNoticeKind.Message, QuestTexts.QuestItemGone(weaponName));
        return true;
    }

    // Three vamps while the sheltoms are being gathered.
    public static bool Accepts(QuestContext context, uint code)
    {
        var active = context.Journal.Active;
        return code == QuestItems.Vamp && active.Code == Code && active.State < Growing && context.Character.Tier == 1
            && QuestInventory.Count(context.Character, QuestItems.Vamp) < VampsNeeded;
    }

    // The weapon and the ring stay with the character while the quest holds them: they are not sold or stored.
    public static bool IsBound(PlayerCharacter character, ItemInstance item)
    {
        var active = character.Quests.Active;
        return item is not null && active.Code == Code && (item.InstanceId == active.WeaponId || item.InstanceId == active.RingId);
    }
}
