namespace PristonTale.Simulation.Quests;

// First rank-up of the Morion classes. The master sends the player to a villager: one branch wants royal jelly (a
// glass bottle and nine honey brewed at the aging NPC), the other hair tonic (the bottle and seven oil). The villager
// hands out the bottle, takes the brew and finishes the quest; the master then promotes the class. The branch follows
// the character's name, so it never changes for a character.
public static class RankUpByPotion
{
    private const int Collecting = 1;
    private const int Delivering = 2;
    private const int Delivered = 3;

    public static QuestCode Branch(string name)
    {
        var sum = 0;
        foreach (var character in name ?? "")
        {
            sum += Windows1252(character);
        }

        return (sum & 2) == 0 ? QuestCode.RankUpHoney : QuestCode.RankUpOil;
    }

    // The byte of a name character in the classic client's code page (names are limited to it).
    private static int Windows1252(char character) => character switch
    {
        '€' => 0x80, '‚' => 0x82, 'ƒ' => 0x83, '„' => 0x84, '…' => 0x85, '†' => 0x86, '‡' => 0x87,
        'ˆ' => 0x88, '‰' => 0x89, 'Š' => 0x8A, '‹' => 0x8B, 'Œ' => 0x8C, 'Ž' => 0x8E, '‘' => 0x91,
        '’' => 0x92, '“' => 0x93, '”' => 0x94, '•' => 0x95, '–' => 0x96, '—' => 0x97, '˜' => 0x98,
        '™' => 0x99, 'š' => 0x9A, '›' => 0x9B, 'œ' => 0x9C, 'ž' => 0x9E, 'Ÿ' => 0x9F,
        _ => character & 0xFF
    };

    private static bool IsBranch(QuestCode code) => code is QuestCode.RankUpHoney or QuestCode.RankUpOil;

    private static string Documents(QuestCode code) => code == QuestCode.RankUpHoney ? "ChangeJob2M_M" : "ChangeJob2M_D";

    public static bool TalkAtMaster(QuestContext context)
    {
        var journal = context.Journal;
        var character = context.Character;
        if (QuestClasses.RanksUpByTrophies(character) || character.Tier != 0
            || !journal.HasFinished(QuestCode.RankUpHoney) && !journal.HasFinished(QuestCode.RankUpOil))
        {
            return false;
        }

        var finished = journal.HasFinished(QuestCode.RankUpHoney) ? QuestCode.RankUpHoney : QuestCode.RankUpOil;
        context.Show(RankUpRules.RankUpPrompt(context, finished, Delivered, "ChangeJob2M_Success"));
        return true;
    }

    // The villager of the branch: hands out the bottle, takes the brew and ends the quest.
    public static void TalkAtVillager(QuestContext context, int scriptCode, int scriptParam)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (!IsBranch(active.Code) || scriptCode != (int)active.Code)
        {
            return;
        }

        var character = context.Character;
        if (scriptParam == 1 && !QuestInventory.Has(character, QuestItems.GlassBottle) && !QuestInventory.Has(character, QuestItems.RoyalJelly)
            && !QuestInventory.Has(character, QuestItems.HairTonic))
        {
            QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.GlassBottle)]);
        }

        var state = active.State;
        if (QuestInventory.Remove(character, QuestItems.RoyalJelly, 1) > 0 || QuestInventory.Remove(character, QuestItems.HairTonic, 1) > 0)
        {
            state = Delivered;
        }

        var documents = Documents(active.Code);
        switch (state)
        {
            case Collecting:
                journal.Active = active with { State = Delivering };
                context.Show(QuestSteps.Close(active.Code, Delivering, documents + "1"));
                break;
            case Delivering:
                context.Show(QuestSteps.Close(active.Code, Delivering, documents + "2"));
                break;
            case Delivered:
                context.Show(QuestSteps.Close(active.Code, Delivered, documents + "3"));
                QuestSteps.Finish(context, active.Code);
                break;
        }
    }

    // At the aging NPC the bottle and the ingredients of the branch become the brew.
    public static void TalkAtAging(QuestContext context)
    {
        var active = context.Journal.Active;
        if (IsBranch(active.Code) && CanBrew(context, active.Code))
        {
            context.Show(QuestSteps.Confirm(active.Code, active.State, "", QuestTexts.Exchange,
                QuestTexts.Name(active.Code == QuestCode.RankUpHoney ? "Royal Jelly" : "Hair Tonic")));
        }
    }

    private static bool CanBrew(QuestContext context, QuestCode code)
    {
        var character = context.Character;
        return QuestInventory.Has(character, QuestItems.GlassBottle) && (code == QuestCode.RankUpHoney
            ? QuestInventory.Count(character, QuestItems.Honey) >= QuestItems.HoneyForJelly
            : QuestInventory.Count(character, QuestItems.Oil) >= QuestItems.OilForTonic);
    }

    public static bool Accept(QuestContext context, QuestCode code, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (state == 0)
        {
            if (!active.IsIdle || RankUpRules.Offer(context) != code)
            {
                return QuestSteps.Stale(context);
            }

            // Leftover ingredients of an earlier attempt are thrown away.
            QuestInventory.Remove(context.Character, QuestItems.Honey);
            QuestInventory.Remove(context.Character, QuestItems.Oil);
            journal.Active = new ActiveQuest { Code = code, State = Collecting };
            return true;
        }

        if (state == Delivered && active.Code != code && journal.HasFinished(code))
        {
            // The rank-up the master offers once the villager's quest is finished.
            if (QuestClasses.RanksUpByTrophies(context.Character) || context.Character.Tier != 0 || !context.Npc.HomeMaster || context.Npc.ClassChangeGroup <= 0)
            {
                return QuestSteps.Stale(context);
            }

            RankUpRules.RankUp(context);
            return true;
        }

        if (active.Code != code || active.State != state || !context.Npc.ItemAging || !CanBrew(context, code))
        {
            return QuestSteps.Stale(context);
        }

        var character = context.Character;
        var ingredient = code == QuestCode.RankUpHoney ? QuestItems.Honey : QuestItems.Oil;
        var needed = code == QuestCode.RankUpHoney ? QuestItems.HoneyForJelly : QuestItems.OilForTonic;
        QuestInventory.Remove(character, QuestItems.GlassBottle, 1);
        QuestInventory.Remove(character, ingredient, needed);
        if (!QuestInventory.TryGrant(context, [new QuestGrant(code == QuestCode.RankUpHoney ? QuestItems.RoyalJelly : QuestItems.HairTonic)]))
        {
            return QuestInventory.Refused(context);
        }

        return true;
    }

    // Ingredients come only while the branch runs, with the bottle in the pack and before the brew is made: at most nine
    // honey or seven oil.
    public static bool Accepts(QuestContext context, uint code)
    {
        var active = context.Journal.Active;
        var character = context.Character;
        if (code == QuestItems.Honey)
        {
            return active.Code == QuestCode.RankUpHoney && QuestInventory.Has(character, QuestItems.GlassBottle)
                && !QuestInventory.Has(character, QuestItems.RoyalJelly) && QuestInventory.Count(character, QuestItems.Honey) < QuestItems.HoneyForJelly;
        }

        if (code == QuestItems.Oil)
        {
            return active.Code == QuestCode.RankUpOil && QuestInventory.Has(character, QuestItems.GlassBottle)
                && !QuestInventory.Has(character, QuestItems.HairTonic) && QuestInventory.Count(character, QuestItems.Oil) < QuestItems.OilForTonic;
        }

        return false;
    }
}
