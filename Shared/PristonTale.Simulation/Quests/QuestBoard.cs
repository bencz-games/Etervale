using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Quests;

// One line of the quest window: a title and the description file it opens (under the client's quest board folder).
public sealed record QuestBoardEntry(string Title, string Description);

// The quest window lists the running quest's steps so far and the finished quests, newest first.
public static class QuestBoard
{
    private static readonly string[][] Rows =
    [
        ["Rank up quest", "Rank up quest(Completed)", "ChangeJob1a.Msg", "ChangeJob1b.Msg"],
        ["Rank up quest", "Rank-up Exam (Done)", "ChangeJob2a.Msg", "ChangeJob1b.Msg"],
        ["Rank up quest", "Rank up quest (Completed)", "ChangeJob3a.Msg", "ChangeJob1b.Msg"],
        ["Rank up quest [Second]", "Rank up quest [Second]", "Quest3\\ChangeJob3-1.msg", "Quest3\\ChangeJob3-2.msg"],
        ["Rank up quest [Second]", "Rank up quest (Completed)", "Quest3\\ChangeJob3-3.msg", "Quest3\\ChangeJob3-4.msg"],
        ["Rank up quest second[1]", "Rank up quest second[2]", "Quest3\\ChangeJob3-1.msg", "Quest3\\ChangeJob3-W1-2.msg"],
        ["Rank up quest second[3]", "Rank up quest second[4]", "Quest3\\ChangeJob3-W2-2.msg", "Quest3\\ChangeJob3-W3-2.msg"],
        ["Rank up quest second[5]", "Rank up quest second[Completed]", "Quest3\\ChangeJob3-3.msg", "Quest3\\ChangeJob3-4.msg"],
        ["Rank up quest second[1]", "Rank up quest second[2]", "Quest3\\ChangeJob3-1.msg", "Quest3\\ChangeJob3-R1-2.msg"],
        ["Rank up quest second[3]", "Rank up quest second[4]", "Quest3\\ChangeJob3-R2-2.msg", "Quest3\\ChangeJob3-R3-2.msg"],
        ["Rank up quest second[5]", "Rank up quest second[Completed]", "Quest3\\ChangeJob3-3.msg", "Quest3\\ChangeJob3-4.msg"],
        ["Rank up quest second[1]", "Rank up quest second[2]", "Quest3\\ChangeJob3-1.msg", "Quest3\\ChangeJob3-M1-2.msg"],
        ["Rank up quest second[3]", "Rank up quest second[4]", "Quest3\\ChangeJob3-M2-2.msg", "Quest3\\ChangeJob3-M3-2.msg"],
        ["Rank up quest second[5]", "Rank up quest second[Completed]", "Quest3\\ChangeJob3-3.msg", "Quest3\\ChangeJob3-4.msg"],
        ["For her", "For her(completed)", "LevelQuest\\Quest30.Msg", "LevelQuest\\Quest30a.Msg"],
        ["The Cave", "The Cave(completed)", "LevelQuest\\Quest55.Msg", "LevelQuest\\Quest55a.Msg"],
        ["The Cave", "The Cave(completed)", "LevelQuest\\Quest55_2.Msg", "LevelQuest\\Quest55_2a.Msg"],
        ["Helping Michelle", "Helping Michelle(completed)", "LevelQuest\\Quest70.Msg", "LevelQuest\\Quest70a.Msg"],
        ["Enclosed Fury", "Enclosed Fury(completed)", "LevelQuest\\Quest80.Msg", "LevelQuest\\Quest80a.Msg"],
        ["Tears of Khalia", "Tears of Khalia(completed)", "LevelQuest\\Quest85.Msg", "LevelQuest\\Quest85a.Msg"],
        ["Village of eura", "Village of Eura(completed)", "LevelQuest\\Quest90.Msg", "LevelQuest\\Quest90a.Msg"],
        ["Test of Kingdom", "Test of Kingdom(Complete)", "LevelQuest\\Quest_7State_1.msg", "LevelQuest\\Quest_7State_end.msg"],
        ["Test of Kingdom", "Test of Kingdom(Complete)", "LevelQuest\\Quest_7State_2.msg", "LevelQuest\\Quest_7State_end.msg"],
        ["Test of Kingdom", "Test of Kingdom(Complete)", "LevelQuest\\Quest_7State_3.msg", "LevelQuest\\Quest_7State_end.msg"],
        ["Bitter ordeal", "Bitter ordeal(Complete)", "LevelQuest\\Quest_10State_1.msg", "LevelQuest\\Quest_10State_end.msg"],
        ["Bitter ordeal", "Bitter ordeal(Complete)", "LevelQuest\\Quest_10State_2_1.msg", "LevelQuest\\Quest_10State_end.msg"],
        ["Bitter ordeal", "Bitter ordeal(Complete)", "LevelQuest\\Quest_10State_2_2.msg", "LevelQuest\\Quest_10State_end.msg"],
        ["Bitter ordeal", "Bitter ordeal(Complete)", "LevelQuest\\Quest_10State_2_3.msg", "LevelQuest\\Quest_10State_end.msg"],
        ["Job Quest 3th[1]", "Job Quest 3th(Complete)", "Quest4\\ChangeJob4_1.msg", "Quest4\\ChangeJob4_5.msg"],
        ["Job Quest 3th[2]", "Job Quest 3th(Complete)", "Quest4\\ChangeJob4_2.msg", "Quest4\\ChangeJob4_5.msg"],
        ["Job Quest 3th[3]", "Job Quest 3th(Complete)", "Quest4\\ChangeJob4_3_1.msg", "Quest4\\ChangeJob4_3_2.msg"],
        ["Job Quest 3th[3]", "Job Quest 3th(Complete)", "Quest4\\ChangeJob4_3_3.msg", "Quest4\\ChangeJob4_3_4.msg"],
        ["Job Quest 3th[3]", "Job Quest 3th(Complete)", "Quest4\\ChangeJob4_3_5.msg", "Quest4\\ChangeJob4_3_6.msg"],
        ["Job Quest 3th[4]", "Job Quest 3th(Complete)", "Quest4\\ChangeJob4_4.msg", "Quest4\\ChangeJob4_5.msg"],
        ["4th Rank-Up Test[1]", "4th Rank-Up Test(Finished)", "Quest5\\ChangeJob5_1.msg", "Quest5\\ChangeJob5_5.msg"],
        ["4th Rank-Up Test[2]", "4th Rank-Up Test(Finished)", "Quest5\\ChangeJob5_1.msg", "Quest5\\ChangeJob5_5.msg"],
        ["4th Rank-Up Test[3]", "4th Rank-Up Test(Finished)", "Quest5\\ChangeJob5_1.msg", "Quest5\\ChangeJob5_5.msg"],
        ["4th Rank-Up Test[4]", "4th Rank-Up Test(Finished)", "Quest5\\ChangeJob5_2.msg", "Quest5\\ChangeJob5_5.msg"],
        ["4th Rank-Up Test[5]", "4th Rank-Up Test(Finished)", "Quest5\\ChangeJob5_3.msg", "Quest5\\ChangeJob5_5.msg"],
        ["4th Rank-Up Test[6]", "4th Rank-Up Test(Finished)", "Quest5\\ChangeJob5_4.msg", "Quest5\\ChangeJob5_5.msg"]
    ];

    private const int FifthRankUpRow = 34;

    private static readonly string[][] BeginnerRows =
    [
        ["A tired warehouse keeper", "A tired warehouse keeper(Complete)", "HaQuest\\Quest_tired_1start_T.msg", "HaQuest\\Quest_tired_1end.msg"],
        ["A tired warehouse keeper", "A tired warehouse keeper(Complete)", "HaQuest\\Quest_tired_1start_M.msg", "HaQuest\\Quest_tired_1end.msg"],
        ["Signs of fear", "Signs of fear(Complete)", "HaQuest\\Quest_terrified_1start_T.msg", "HaQuest\\Quest_terrified_1end.msg"],
        ["Signs of fear", "Signs of fear(Complete)", "HaQuest\\Quest_terrified_1start_M.msg", "HaQuest\\Quest_terrified_1end.msg"],
        ["Candy Delivery", "Candy Delivery(Complete)", "HaQuest\\Quest_candy_1start_T.msg", "HaQuest\\Quest_candy_1end.msg"],
        ["Candy Delivery", "Candy Delivery(Complete)", "HaQuest\\Quest_candy_1start_M.msg", "HaQuest\\Quest_candy_1end.msg"],
        ["Goblin Hill", "Goblin Hill(Complete)", "HaQuest\\Quest_goblin_1start_T.msg", "HaQuest\\Quest_goblin_1end.msg"],
        ["Goblin Hill", "Goblin Hill(Complete)", "HaQuest\\Quest_goblin_1start_M.msg", "HaQuest\\Quest_goblin_1end.msg"],
        ["Threats of the Undead", "Threats of the Undead(Complete)", "HaQuest\\Quest_undead_1start_T.msg", "HaQuest\\Quest_undead_1end.msg"],
        ["Threats of the Undead", "Threats of the Undead(Complete)", "HaQuest\\Quest_undead_1start_M.msg", "HaQuest\\Quest_undead_1end.msg"],
        ["One-eyed Monsters", "One-eyed Monsters(Complete)", "HaQuest\\Quest_cyclops_1start.msg", "HaQuest\\Quest_cyclops_1end.msg"],
        ["A lost Hammer", "A lost Hammer(Complete)", "HaQuest\\Quest_hammer_1start.msg", "HaQuest\\Quest_hammer_1end.msg"],
        ["Fury's phantasma[1]", "Fury's phantasma[2]", "HaQuest\\Quest_100State_1.msg", "HaQuest\\Quest_100State_2.msg"],
        ["Fury's phantasma[3]", "Fury's phantasma[4]", "HaQuest\\Quest_100State_3.msg", "HaQuest\\Quest_100State_4.msg"],
        ["Fury's phantasma[5]", "Fury's phantasma(Complete)", "HaQuest\\Quest_100State_5.msg", "HaQuest\\Quest_100State_6.msg"]
    ];

    // Every description file the window can open, under the client's quest board folder.
    public static IEnumerable<string> Documents => Rows.Concat(BeginnerRows).SelectMany(static row => row.Skip(2)).Distinct(StringComparer.Ordinal);

    // Every title the window can show, open and finished.
    public static IEnumerable<string> Titles => Rows.Concat(BeginnerRows).SelectMany(static row => row.Take(2)).Distinct(StringComparer.Ordinal);

    private static QuestBoardEntry Open(string[] row) => new(row[0], row[2]);

    private static QuestBoardEntry Done(string[] row) => new(row[1], row[3]);

    public static IReadOnlyList<QuestBoardEntry> Progress(PlayerCharacter character)
    {
        var active = character.Quests.Active;
        var tempskron = QuestClasses.IsTempskron(character);
        var entries = new List<QuestBoardEntry>();
        switch (active.Code)
        {
            case QuestCode.RankUpTrophies:
                entries.Add(Open(Rows[0]));
                break;
            case QuestCode.RankUpHoney:
                entries.Add(Open(Rows[1]));
                break;
            case QuestCode.RankUpOil:
                entries.Add(Open(Rows[2]));
                break;
            case QuestCode.RankUpQuestWeapon:
                AddWeaponSteps(entries, character, active);
                break;
            case QuestCode.Level30:
            case QuestCode.Level55:
            case QuestCode.Level55Morion:
            case QuestCode.Level70:
            case QuestCode.Level80:
            case QuestCode.Level85:
            case QuestCode.Level90:
                entries.Add(Open(Rows[LevelRow(active.Code)]));
                break;
            case QuestCode.Level80Bonus:
                entries.Add(Open(Rows[active.State switch { 1 => 21, 2 => 22, _ => 23 }]));
                break;
            case QuestCode.Level90Bonus:
                entries.Add(Open(Rows[active.State == 2 && active.Variant is >= 1 and <= 3 ? 24 + active.Variant : 24]));
                break;
            case QuestCode.RankUpMasterTrial:
                AddTrialStep(entries, active);
                break;
            case QuestCode.RankUpHestian when active.State > 0:
                // The three hunts, Hosean, the fight and the gem.
                entries.Add(Open(Rows[FifthRankUpRow + active.State switch { 1 or 2 => 0, 3 or 4 => 1, 5 or 6 => 2, 7 => 3, 8 => 4, _ => 5 }]));
                break;
            case QuestCode.FuryOfPhantom:
                AddFuryStep(entries, active.State);
                break;
            default:
                if (QuestCodes.IsElementary(active.Code))
                {
                    entries.Add(Open(BeginnerRows[BeginnerRow(active.Code, tempskron)]));
                }

                break;
        }

        return entries;
    }

    public static IReadOnlyList<QuestBoardEntry> Finished(PlayerCharacter character)
    {
        var finished = character.Quests.Finished;
        var entries = new List<QuestBoardEntry>(finished.Count);
        for (var index = finished.Count - 1; index >= 0; index--)
        {
            var code = finished[index];
            var row = code switch
            {
                QuestCode.RankUpTrophies or QuestCode.RankUpHoney or QuestCode.RankUpOil => Done(Rows[0]),
                QuestCode.RankUpQuestWeapon => Done(Rows[7]),
                QuestCode.Level80Bonus => Done(Rows[21]),
                QuestCode.Level90Bonus => Done(Rows[24]),
                QuestCode.RankUpMasterTrial => Done(Rows[33]),
                QuestCode.RankUpHestian => Done(Rows[FifthRankUpRow]),
                QuestCode.FuryOfPhantom => Done(BeginnerRows[14]),
                _ when LevelQuests.IsLevelQuest(code) => Done(Rows[LevelRow(code)]),
                _ when QuestCodes.IsElementary(code) => Done(BeginnerRows[BeginnerRow(code, true)]),
                _ => null
            };
            if (row is not null)
            {
                entries.Add(row);
            }
        }

        return entries;
    }

    private static int LevelRow(QuestCode code) => code switch
    {
        QuestCode.Level30 => 14,
        QuestCode.Level55 => 15,
        QuestCode.Level55Morion => 16,
        QuestCode.Level70 => 17,
        QuestCode.Level80 => 18,
        QuestCode.Level85 => 19,
        _ => 20
    };

    private static int BeginnerRow(QuestCode code, bool tempskron) => code switch
    {
        QuestCode.ElementaryF => 10,
        QuestCode.ElementaryG => 11,
        _ => QuestCodes.ElementaryIndex(code) * 2 + (tempskron ? 0 : 1)
    };

    // The second rank-up adds a line for each grown stage of the weapon.
    private static void AddWeaponSteps(List<QuestBoardEntry> entries, PlayerCharacter character, ActiveQuest active)
    {
        var first = QuestClasses.WeaponHunt(QuestClasses.ClassOf(character)) switch
        {
            QuestWeaponHunt.Ranged => 8,
            QuestWeaponHunt.Field => 11,
            _ => 5
        };
        var steps = active.State switch
        {
            3 => Math.Clamp(active.WeaponStage, 1, 4),
            4 => 4,
            >= 5 => 5,
            _ => 0
        };
        entries.Add(Open(Rows[first]));
        if (steps >= 1)
        {
            entries.Add(Done(Rows[first]));
        }

        if (steps >= 2)
        {
            entries.Add(Open(Rows[first + 1]));
        }

        if (steps >= 3)
        {
            entries.Add(Done(Rows[first + 1]));
        }

        if (steps >= 5)
        {
            entries.Add(Open(Rows[first + 2]));
        }
    }

    private static void AddTrialStep(List<QuestBoardEntry> entries, ActiveQuest active)
    {
        switch (active.State)
        {
            case 1 or 2:
                entries.Add(Open(Rows[28]));
                break;
            case 3 or 4:
                entries.Add(Open(Rows[29]));
                break;
            case 5:
                var description = active.Target switch
                {
                    QuestMonsters.SolidSnail => Rows[30][2],
                    QuestMonsters.IllusionKnight => Rows[30][3],
                    QuestMonsters.AveliskLord => Rows[31][2],
                    QuestMonsters.Dusk => Rows[31][3],
                    QuestMonsters.ChainGolem => Rows[32][2],
                    _ => Rows[32][3]
                };
                entries.Add(new QuestBoardEntry(Rows[30][0], description));
                break;
            case 6 or 7 or 8:
                entries.Add(Open(Rows[33]));
                break;
        }
    }

    private static void AddFuryStep(List<QuestBoardEntry> entries, int state)
    {
        switch (state)
        {
            case 1 or 2 or 3:
                entries.Add(Open(BeginnerRows[12]));
                break;
            case 4:
                entries.Add(Done(BeginnerRows[12]));
                break;
            case 5 or 6 or 7 or 8:
                entries.Add(Open(BeginnerRows[13]));
                break;
            case 9 or 10:
                entries.Add(Done(BeginnerRows[13]));
                break;
            case 11 or 12:
                entries.Add(Open(BeginnerRows[14]));
                break;
        }
    }
}
