using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Quests;

// Item checks and changes quests make on the carried items: counts by code, removal by code or identity, and grants
// that either fit completely or leave the pack untouched.
public static class QuestInventory
{
    public static int Count(PlayerCharacter character, uint code)
    {
        var total = 0;
        foreach (var item in character.OwnedItems())
        {
            if (item.Definition.Code == code)
            {
                total += item.Count;
            }
        }

        return total;
    }

    public static bool Has(PlayerCharacter character, uint code) => Count(character, code) > 0;

    public static bool Holds(PlayerCharacter character, Guid instanceId) => instanceId != Guid.Empty && character.FindOwnedItem(instanceId) is not null;

    // Removes up to amount units of the code from every place Count counts: both bag pages, the special item box, the
    // equipment, the stored weapon pair and the hand; returns how many were removed.
    public static int Remove(PlayerCharacter character, uint code, int amount = int.MaxValue)
    {
        var removed = RemoveFrom(character.Inventory, code, amount);
        var hidden = RemoveFrom(character.HiddenBag, code, amount - removed);
        removed += hidden;
        var boxed = 0;
        foreach (var (box, item) in character.QuestBox.Occupied().ToArray())
        {
            if (removed + boxed < amount && item.Definition.Code == code)
            {
                character.QuestBox.TakeAt(box);
                boxed++;
            }
        }

        removed += boxed;

        foreach (var pair in character.Equipment.ToArray())
        {
            if (removed < amount && pair.Value.Definition.Code == code)
            {
                character.Equip(pair.Key, null);
                removed++;
            }
        }

        // The shown page counts its own change; the hidden page and the box do not.
        if (hidden + boxed > 0)
        {
            character.TouchInventory();
        }

        return removed < amount ? removed + character.TakeStoredOrHeld(code, amount - removed) : removed;
    }

    private static int RemoveFrom(Inventory inventory, uint code, int amount)
    {
        var removed = 0;
        foreach (var (anchor, item) in inventory.Occupied().ToArray())
        {
            if (removed < amount && item.Definition.Code == code)
            {
                var take = Math.Min(item.Count, amount - removed);
                inventory.TakeFrom(anchor, take);
                removed += take;
            }
        }

        return removed;
    }

    public static bool RemoveInstance(PlayerCharacter character, Guid instanceId)
    {
        if (instanceId == Guid.Empty)
        {
            return false;
        }

        if (character.Inventory.TakeAt(character.Inventory.Find(instanceId)) is not null)
        {
            return true;
        }

        if (character.HiddenBag.TakeAt(character.HiddenBag.Find(instanceId)) is not null
            || character.QuestBox.TakeAt(character.QuestBox.Find(instanceId)) is not null)
        {
            character.TouchInventory();
            return true;
        }

        foreach (var pair in character.Equipment.ToArray())
        {
            if (pair.Value.InstanceId == instanceId)
            {
                character.Equip(pair.Key, null);
                return true;
            }
        }

        return character.TakeStoredOrHeld(instanceId);
    }

    // Whether every item fits: the placement is tried on a copy of the pack, and the items together must stay within the
    // carried-weight limit like any other item the character takes.
    public static QuestGrantCheck Check(PlayerCharacter character, IReadOnlyList<ItemInstance> items)
    {
        if (items.Count == 0)
        {
            return QuestGrantCheck.Fits;
        }

        var weight = 0;
        var trial = character.CloneForInventoryOperation();
        foreach (var item in items)
        {
            if (item is null || !trial.TryReceive(item.Copy(item.Count, preserveId: true)))
            {
                return QuestGrantCheck.NoSpace;
            }

            weight += CarriedWeight.Of(item);
        }

        return CarriedWeight.CanTake(character, weight) ? QuestGrantCheck.Fits : QuestGrantCheck.OverWeight;
    }

    // Places every item or none.
    public static bool TryGiveAll(PlayerCharacter character, IReadOnlyList<ItemInstance> items)
    {
        if (Check(character, items) != QuestGrantCheck.Fits)
        {
            return false;
        }

        foreach (var item in items)
        {
            character.TryReceive(item);
        }

        return true;
    }

    // Places every item or none, keeping the reason of a refusal for Refused.
    public static bool TryGiveAll(QuestContext context, IReadOnlyList<ItemInstance> items)
    {
        context.GrantRefusal = Check(context.Character, items);
        return context.GrantRefusal == QuestGrantCheck.Fits && TryGiveAll(context.Character, items);
    }

    // Fails the request with the reason the last grant did not fit.
    public static bool Refused(QuestContext context) => context.GrantRefusal == QuestGrantCheck.OverWeight
        ? context.Fail(QuestStatus.OverWeight, GameText.Of(GameTextId.OverWeight))
        : context.Fail(QuestStatus.NoSpace, QuestTexts.NotEnoughSpace);

    // Creates and places the items of a reward list; the codes missing from the catalog are skipped.
    public static bool TryGrant(QuestContext context, IReadOnlyList<QuestGrant> grants)
    {
        var created = new List<ItemInstance>(grants.Count);
        foreach (var grant in grants)
        {
            var spec = grant.ClassSpecific ? QuestClasses.JobMask(context.Character) : 0u;
            for (var copy = 0; copy < grant.Copies; copy++)
            {
                if (grant.QuestVariant)
                {
                    if (context.Items.TryCreateQuestVariant(grant.Code, spec, out var variant))
                    {
                        created.Add(variant);
                    }

                    continue;
                }

                if (context.Items.TryCreate(grant.Code, grant.Count, spec, out var stack))
                {
                    created.Add(stack);
                    continue;
                }

                // An item that does not stack that far comes as single units.
                for (var unit = 0; unit < grant.Count && grant.Count > 1; unit++)
                {
                    if (context.Items.TryCreate(grant.Code, 1, spec, out var single))
                    {
                        created.Add(single);
                    }
                }
            }
        }

        return TryGiveAll(context, created);
    }
}

public enum QuestGrantCheck : byte
{
    Fits = 0,
    NoSpace = 1,
    OverWeight = 2
}

// One reward line: Count units in each of Copies items, optionally specialised for the class or of the quest variant.
public readonly record struct QuestGrant(uint Code, int Count = 1, int Copies = 1, bool ClassSpecific = false, bool QuestVariant = false);
