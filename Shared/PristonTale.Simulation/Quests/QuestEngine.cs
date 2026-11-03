namespace PristonTale.Simulation.Quests;

// Entry point of quest requests: dispatches a command to the quest families and returns whether it succeeded. The
// caller runs it on a candidate copy of the character and stores the result when the journal or the items changed.
public static class QuestEngine
{
    private const int FuryGiverCode = 1013;

    public static bool Run(QuestContext context, QuestCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Action switch
        {
            QuestAction.Talk => Talk(context),
            QuestAction.Proceed => Proceed(context, command.Code, command.State),
            QuestAction.Accept => Accept(context, command.Code, command.State, command.Choice),
            QuestAction.ChangeClass => RankUpRules.ChangeClass(context),
            QuestAction.ChooseReward => command.Code == QuestCode.FuryOfPhantom ? FuryOfPhantomQuest.Choose(context, command.State, command.Choice)
                : QuestSteps.Stale(context),
            QuestAction.ResetSkills => RankUpRules.ResetSkills(context),
            QuestAction.DeclineSkillReset => RankUpRules.DeclineSkillReset(context),
            _ => context.Fail(QuestStatus.Rejected, "unknown quest action")
        };
    }

    // Every quest the NPC takes part in answers the click; the first dialog shown wins. Without a dialog, a quest that
    // was blocked by the running one says so.
    public static bool Talk(QuestContext context)
    {
        var npc = context.Npc;
        if (npc.ScriptCode != 0)
        {
            RankUpByPotion.TalkAtVillager(context, npc.ScriptCode, npc.ScriptParam);
        }

        if (npc.GiverCode != 0)
        {
            LevelQuests.Talk(context, npc.GiverCode);
            LevelBonusQuests.Talk(context, npc.GiverCode);
            if (npc.GiverCode == FuryGiverCode)
            {
                FuryOfPhantomQuest.Talk(context);
            }

            ElementaryQuests.Talk(context, npc.GiverCode);
        }

        RankUpRules.TalkAtMaster(context);
        if (npc.ItemAging)
        {
            RankUpByPotion.TalkAtAging(context);
        }

        RankUpByHestian.TalkAtElder(context);
        WingQuest.Talk(context);
        if (context.Prompt is null && context.Blocked)
        {
            context.Notice(QuestNoticeKind.Message, QuestTexts.QuestInProgress);
        }

        return true;
    }

    public static bool Proceed(QuestContext context, QuestCode code, int state)
    {
        if (LevelQuests.IsLevelQuest(code))
        {
            return LevelQuests.Proceed(context, code, state);
        }

        if (QuestCodes.IsElementary(code))
        {
            return ElementaryQuests.Proceed(context, code, state);
        }

        return code switch
        {
            QuestCode.Level80Bonus or QuestCode.Level90Bonus => LevelBonusQuests.Proceed(context, code, state),
            QuestCode.FuryOfPhantom => FuryOfPhantomQuest.Proceed(context, state),
            QuestCode.RankUpQuestWeapon => RankUpByQuestWeapon.Proceed(context, state),
            QuestCode.RankUpMasterTrial => RankUpByMasterTrial.Proceed(context, state),
            QuestCode.RankUpHestian => RankUpByHestian.Proceed(context, state),
            _ => QuestSteps.Stale(context)
        };
    }

    public static bool Accept(QuestContext context, QuestCode code, int state, int choice)
    {
        if (LevelQuests.IsLevelQuest(code))
        {
            return LevelQuests.Accept(context, code, state);
        }

        if (QuestCodes.IsElementary(code))
        {
            return ElementaryQuests.Accept(context, code, state);
        }

        return code switch
        {
            QuestCode.None => WingQuest.Accept(context, state),
            QuestCode.Level80Bonus or QuestCode.Level90Bonus => LevelBonusQuests.Accept(context, code, state),
            QuestCode.FuryOfPhantom => FuryOfPhantomQuest.Accept(context, state, choice),
            QuestCode.RankUpTrophies => RankUpByTrophies.Accept(context, state),
            QuestCode.RankUpHoney or QuestCode.RankUpOil => RankUpByPotion.Accept(context, code, state),
            QuestCode.RankUpQuestWeapon => RankUpByQuestWeapon.Accept(context, state),
            QuestCode.RankUpMasterTrial => RankUpByMasterTrial.Accept(context, state),
            QuestCode.RankUpHestian => RankUpByHestian.Accept(context, state),
            _ => QuestSteps.Stale(context)
        };
    }
}
