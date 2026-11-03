namespace PristonTale.Simulation.Quests;

// The bonus chains after the milestones. Level 80: the first NPC sends the player to a second one, three vamp sheltoms
// of named bosses buy a recommendation, and the first NPC turns it into two extra stat points per level from 80 on.
// Level 90: the second NPC curses the player to hunt fifty of one of three monsters with thirty percent less damage;
// dying resets the hunt; the reward is three extra stat points per level from 90 on.
public static class LevelBonusQuests
{
    private const int MinimumLevel80 = QuestRewards.FirstChainLevel;
    private const int MinimumLevel90 = QuestRewards.SecondChainLevel;
    private const int CursedHuntCount = 50;
    private static readonly int[] CursedPrey = [QuestMonsters.Omega, QuestMonsters.DMachine, QuestMonsters.Mountain];

    // 1010 and 1012 are the first level 80 NPC, 1011 the second; 1012 is also the first level 90 NPC and 1013 the second.
    public static void Talk(QuestContext context, int giverCode)
    {
        switch (giverCode)
        {
            case 1010:
                TalkLevel80(context, second: false);
                break;
            case 1011:
                TalkLevel80(context, second: true);
                break;
            case 1012:
                TalkLevel80(context, second: false);
                TalkLevel90(context, second: false);
                break;
            case 1013:
                TalkLevel90(context, second: true);
                break;
        }
    }

    private static void TalkLevel80(QuestContext context, bool second)
    {
        const QuestCode code = QuestCode.Level80Bonus;
        var journal = context.Journal;
        if ((journal.LevelLog & LevelQuestLog.Level80Bonus) != 0 || context.Character.Level < MinimumLevel80)
        {
            return;
        }

        if (QuestSteps.Blocked(context, code))
        {
            context.Block();
            return;
        }

        var active = journal.Active;
        var state = active.Code == code ? active.State : 0;
        switch (state)
        {
            case 0 when !second:
                context.Show(QuestSteps.Confirm(code, 0, "LevelQuest80_2start", QuestTexts.SearchQuest));
                break;
            case 1 when second:
                context.Show(QuestSteps.Proceed(code, 1, "LevelQuest80_2start2"));
                break;
            case 2 when second:
                if (HasVamps(context))
                {
                    journal.Active = active with { State = 3 };
                    context.Show(QuestSteps.Proceed(code, 3, "LevelQuest80_2Path2"));
                }
                else
                {
                    context.Show(QuestSteps.Close(code, 2, "LevelQuest80_2Path"));
                }

                break;
            case 3 when second:
                context.Show(QuestSteps.Proceed(code, 3, "LevelQuest80_2Path2"));
                break;
            case 4 when !second:
                if (QuestInventory.Has(context.Character, QuestItems.Recommendation))
                {
                    context.Show(QuestSteps.Proceed(code, 4, "LevelQuest80_2end"));
                }

                break;
        }
    }

    private static void TalkLevel90(QuestContext context, bool second)
    {
        const QuestCode code = QuestCode.Level90Bonus;
        var journal = context.Journal;
        if ((journal.LevelLog & LevelQuestLog.Level90Bonus) != 0 || context.Character.Level < MinimumLevel90)
        {
            return;
        }

        if (QuestSteps.Blocked(context, code))
        {
            context.Block();
            return;
        }

        var active = journal.Active;
        var state = active.Code == code ? active.State : 0;
        switch (state)
        {
            case 0 when !second:
                context.Show(QuestSteps.Confirm(code, 0, "LevelQuest90_2start", QuestTexts.SearchQuest));
                break;
            case 1 when second:
                if (active.Variant == 0)
                {
                    // One of the three prey families is drawn when the second NPC first explains the hunt.
                    journal.Active = active with { Variant = context.Rng.NextInclusive(1, CursedPrey.Length), KillsNeeded = CursedHuntCount };
                }

                context.Show(QuestSteps.Proceed(code, 1, "LevelQuest90_2start2"));
                break;
            case 2:
                context.Show(QuestSteps.Close(code, 2, second ? "LevelQuest90_2Path2" : "LevelQuest90_2Path"));
                break;
            case 3:
                context.Show(QuestSteps.Proceed(code, 3, "LevelQuest90_2end"));
                break;
        }
    }

    private static bool HasVamps(QuestContext context) =>
        QuestInventory.Has(context.Character, QuestItems.BonusVampOne) && QuestInventory.Has(context.Character, QuestItems.BonusVampTwo)
        && QuestInventory.Has(context.Character, QuestItems.BonusVampThree);

    public static bool Accept(QuestContext context, QuestCode code, int state)
    {
        var journal = context.Journal;
        if (state != 0 || !journal.Active.IsIdle)
        {
            return QuestSteps.Stale(context);
        }

        if (code == QuestCode.Level80Bonus && (journal.LevelLog & LevelQuestLog.Level80Bonus) == 0 && context.Character.Level >= MinimumLevel80
            || code == QuestCode.Level90Bonus && (journal.LevelLog & LevelQuestLog.Level90Bonus) == 0 && context.Character.Level >= MinimumLevel90)
        {
            journal.Active = new ActiveQuest { Code = code, State = 1 };
            return true;
        }

        return QuestSteps.Stale(context);
    }

    public static bool Proceed(QuestContext context, QuestCode code, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != code || active.State != state)
        {
            return QuestSteps.Stale(context);
        }

        var character = context.Character;
        if (code == QuestCode.Level80Bonus)
        {
            switch (state)
            {
                case 1:
                    journal.Active = active with { State = 2 };
                    return true;
                case 3:
                    if (!HasVamps(context))
                    {
                        return QuestSteps.Stale(context);
                    }

                    QuestInventory.Remove(character, QuestItems.BonusVampOne, 1);
                    QuestInventory.Remove(character, QuestItems.BonusVampTwo, 1);
                    QuestInventory.Remove(character, QuestItems.BonusVampThree, 1);
                    if (!QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.Recommendation)]))
                    {
                        return QuestInventory.Refused(context);
                    }

                    journal.Active = active with { State = 4 };
                    return true;
                case 4:
                    if (!QuestInventory.Has(character, QuestItems.Recommendation))
                    {
                        return QuestSteps.Stale(context);
                    }

                    QuestInventory.Remove(character, QuestItems.Recommendation);
                    journal.LevelLog |= LevelQuestLog.Level80Bonus;
                    character.ApplyQuestProgress(character.Tier, QuestRewards.BonusChainBackPay(character.Level, code), 0, 0);
                    context.Notice(QuestNoticeKind.Message, QuestTexts.SevenStatPoints);
                    QuestSteps.Finish(context, code);
                    return true;
            }

            return QuestSteps.Stale(context);
        }

        switch (state)
        {
            case 1:
                if (active.Variant is < 1 or > 3)
                {
                    return QuestSteps.Stale(context);
                }

                journal.Active = (active with { State = 2 }).Hunting(CursedPrey[active.Variant - 1], CursedHuntCount);
                return true;
            case 3:
                QuestInventory.Remove(character, QuestItems.RoyalAmulet);
                journal.LevelLog |= LevelQuestLog.Level90Bonus;
                character.ApplyQuestProgress(character.Tier, QuestRewards.BonusChainBackPay(character.Level, code), 0, 0);
                context.Notice(QuestNoticeKind.Message, QuestTexts.TenStatPoints);
                QuestSteps.Finish(context, code);
                return true;
        }

        return QuestSteps.Stale(context);
    }

    public static void OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        if (active is not { Code: QuestCode.Level90Bonus, State: 2 } || active.Target == 0 || monsterCode != active.Target)
        {
            return;
        }

        var kills = active.Kills + 1;
        context.Notice(QuestNoticeKind.Chat, QuestTexts.Count(QuestMonsters.NameOf(active.Target), kills));
        context.Journal.Active = kills >= active.KillsNeeded ? active with { Kills = kills, State = 3 } : active with { Kills = kills };
    }

    // Dying during the cursed hunt starts it over: the prey is drawn again by the second NPC.
    public static void OnDeath(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active is not { Code: QuestCode.Level90Bonus, State: 2 })
        {
            return;
        }

        context.Journal.Active = active with { State = 1, Variant = 0, Target = 0, Kills = 0, KillsNeeded = 0 };
        context.Notice(QuestNoticeKind.Message, QuestTexts.TrainingReset);
    }
}
