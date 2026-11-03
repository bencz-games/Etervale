using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Quests;

// The class tier advances only through the rank-up quests of the skill master in the class's home town (Ricarten for
// the Tempskron classes, Pillai for the Morion ones). The class-change button opens the next one once the level is
// reached and every skill the current tier opened has a rank: level 20 for the first, 40 for the second, 60 for the
// third. A master's class-change group 1 offers only the first rank-up; 2 and above offer all three.
public static class RankUpRules
{
    public static int RequiredLevel(int tier) => tier switch
    {
        0 => 20,
        1 => 40,
        2 => 60,
        3 => 80,
        _ => int.MaxValue
    };

    public static bool TierSkillsLearned(PlayerCharacter character)
    {
        var skills = character.Class.Skills;
        var opened = Math.Min(SkillLearning.OpenedBy(character.Tier), skills.Count);
        for (var index = 0; index < opened; index++)
        {
            if (skills[index].Code != 0 && character.Skills.RankOf(skills[index].Code) < 1)
            {
                return false;
            }
        }

        return true;
    }

    // The rank-up quest the class-change button starts now, or None.
    public static QuestCode Offer(QuestContext context)
    {
        var character = context.Character;
        var group = context.Npc.ClassChangeGroup;
        if (group <= 0 || !context.Npc.HomeMaster || character.Tier >= PlayerCharacter.LastTier || character.Level < RequiredLevel(character.Tier)
            || !TierSkillsLearned(character) || group == 1 && character.Tier > 0
            || character.Tier == PlayerCharacter.UncappedTier && group < RankUpByHestian.MasterGroup)
        {
            return QuestCode.None;
        }

        var journal = context.Journal;
        var active = journal.Active;
        switch (character.Tier)
        {
            case 0 when QuestClasses.RanksUpByTrophies(character):
                return active.Code == QuestCode.RankUpTrophies ? QuestCode.None : QuestCode.RankUpTrophies;
            case 0:
                if (journal.HasFinished(QuestCode.RankUpHoney) || journal.HasFinished(QuestCode.RankUpOil)
                    || active.Code is QuestCode.RankUpHoney or QuestCode.RankUpOil)
                {
                    return QuestCode.None;
                }

                return RankUpByPotion.Branch(character.Name);
            case 1:
                return active.Code == QuestCode.RankUpQuestWeapon || journal.HasFinished(QuestCode.RankUpQuestWeapon)
                    ? QuestCode.None
                    : QuestCode.RankUpQuestWeapon;
            case 2:
                return active.Code == QuestCode.RankUpMasterTrial ? QuestCode.None : QuestCode.RankUpMasterTrial;
            case 3:
                return active.Code == RankUpByHestian.Code ? QuestCode.None : RankUpByHestian.Code;
            default:
                return QuestCode.None;
        }
    }

    // The class-change button: the start dialog of the rank-up quest, or the quest-in-progress refusal.
    public static bool ChangeClass(QuestContext context)
    {
        var code = Offer(context);
        if (code == QuestCode.None)
        {
            return context.Fail(QuestStatus.Rejected, "the master has no class change for you now");
        }

        if (!context.Journal.Active.IsIdle)
        {
            return context.Busy();
        }

        context.Show(code switch
        {
            QuestCode.RankUpTrophies => QuestSteps.Confirm(code, 0, "ChangeJob1", QuestTexts.CarryOut),
            QuestCode.RankUpHoney => QuestSteps.Confirm(code, 0, "ChangeJob2M_M0", QuestTexts.CarryOut),
            QuestCode.RankUpOil => QuestSteps.Confirm(code, 0, "ChangeJob2M_D0", QuestTexts.CarryOut),
            QuestCode.RankUpQuestWeapon => QuestSteps.Confirm(code, 0, "ChangeJob3_0", QuestTexts.TheQuest),
            QuestCode.RankUpHestian => RankUpByHestian.StartDialog(0),
            _ => QuestSteps.Confirm(code, 0, "ChangeJob4_1Start", QuestTexts.SearchQuest)
        });
        return true;
    }

    // The home-town master answers the running rank-up quest; without one it trains skills and offers the next class change.
    public static void TalkAtMaster(QuestContext context)
    {
        if (context.Npc.ClassChangeGroup <= 0 || !context.Npc.HomeMaster)
        {
            return;
        }

        if (!RankUpByTrophies.TalkAtMaster(context) && !RankUpByPotion.TalkAtMaster(context) && !RankUpByMasterTrial.TalkAtMaster(context)
            && !RankUpByHestian.TalkAtMaster(context))
        {
            RankUpByQuestWeapon.TalkAtMaster(context);
        }

        context.ClassChangeOffered = Offer(context) != QuestCode.None;
    }

    public static QuestPrompt RankUpPrompt(QuestContext context, QuestCode code, int state, string document) =>
        QuestSteps.Confirm(code, state, document, QuestTexts.RankUp, ClassProfiles.Of(QuestClasses.ClassOf(context.Character)).TitleAt(context.Character.Tier + 1));

    // Moves the class one tier up; the master then offers one free redistribution of the learned skills.
    public static void RankUp(QuestContext context)
    {
        var character = context.Character;
        character.ApplyQuestProgress((short)(character.Tier + 1), 0, 0, 0);
        context.Journal.SkillResetOffered = true;
        context.Notice(QuestNoticeKind.Fanfare, "");
    }

    // The original asks once, right after the rank-up: No ends the offer.
    public static bool DeclineSkillReset(QuestContext context)
    {
        if (!ResetOffered(context))
        {
            return false;
        }

        context.Journal.SkillResetOffered = false;
        return true;
    }

    public static bool ResetSkills(QuestContext context)
    {
        if (!ResetOffered(context))
        {
            return false;
        }

        context.Character.ResetLearnedSkills();
        context.Journal.SkillResetOffered = false;
        context.Notice(QuestNoticeKind.Message, QuestTexts.SkillsRedistributed);
        return true;
    }

    private static bool ResetOffered(QuestContext context) =>
        context.Journal.SkillResetOffered || context.Fail(QuestStatus.Rejected, "no skill redistribution is offered");
}
