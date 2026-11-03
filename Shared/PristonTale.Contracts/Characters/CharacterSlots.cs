namespace PristonTale.Contracts.Characters;

public static class CharacterSlots
{
    // Characters per account.
    public const int Maximum = 6;

    // The lowest slot no character of the account holds; null when all Maximum are taken.
    public static short? FirstFree(IEnumerable<short> used)
    {
        var taken = used as ISet<short> ?? used.ToHashSet();
        for (short slot = 0; slot < Maximum; slot++)
        {
            if (!taken.Contains(slot))
            {
                return slot;
            }
        }

        return null;
    }
}
