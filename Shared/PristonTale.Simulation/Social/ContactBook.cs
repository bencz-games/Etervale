using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Social;

// Why a contact change was refused.
public enum ContactStatus : byte
{
    Ok = 0,
    Rejected = 1,
    Full = 2,
    Listed = 3,
    NotListed = 4,
    Unavailable = 5
}

// A character's friends and the players it ignores, as the original's friends page keeps them: each list at most
// ContactLimits.MaxEntries names in the order added, a name once whatever its case, never the character's own. A whisper
// from an ignored player is refused. The chat reads it from any thread while its owner's requests change it.
public sealed class ContactBook
{
    private readonly object _gate = new();
    private string[] _friends = [];
    private string[] _ignored = [];

    public ContactBook(IEnumerable<(ContactList List, string Name)> entries)
    {
        foreach (var (list, name) in entries)
        {
            if (Check(list, name, "") == ContactStatus.Ok)
            {
                Add(list, name);
            }
        }
    }

    // Lists storage could not read: empty, and never changed, so nothing stored is overwritten or listed twice.
    public static ContactBook Unavailable() => new([]) { Available = false };

    public bool Available { get; private init; } = true;

    public IReadOnlyList<string> Friends => Volatile.Read(ref _friends);
    public IReadOnlyList<string> Ignored => Volatile.Read(ref _ignored);

    public bool Ignores(string name) => Find(ContactList.Ignored, name) is not null;

    public ContactStatus Check(ContactList list, string name, string self)
    {
        if (!Available)
        {
            return ContactStatus.Unavailable;
        }

        if (name is null || !CharacterNames.IsValidLength(name) || string.Equals(name, self, StringComparison.OrdinalIgnoreCase)
            || !Enum.IsDefined(list))
        {
            return ContactStatus.Rejected;
        }

        return Find(list, name) is not null ? ContactStatus.Listed : Of(list).Count >= ContactLimits.MaxEntries ? ContactStatus.Full : ContactStatus.Ok;
    }

    public void Add(ContactList list, string name)
    {
        lock (_gate)
        {
            ref var names = ref Names(list);
            Volatile.Write(ref names, [.. names, name]);
        }
    }

    // The name as the list keeps it (its case), or null when it is not on the list.
    public string Find(ContactList list, string name)
    {
        foreach (var listed in Of(list))
        {
            if (string.Equals(listed, name, StringComparison.OrdinalIgnoreCase))
            {
                return listed;
            }
        }

        return null;
    }

    public void Remove(ContactList list, string name)
    {
        lock (_gate)
        {
            ref var names = ref Names(list);
            Volatile.Write(ref names, Array.FindAll(names, listed => !string.Equals(listed, name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private IReadOnlyList<string> Of(ContactList list) => list == ContactList.Friends ? Friends : Ignored;

    private ref string[] Names(ContactList list) => ref list == ContactList.Friends ? ref _friends : ref _ignored;
}

// The texts a player sees.
public static class ContactMessages
{
    public static readonly GameText WhisperRefused = GameText.Of(GameTextId.WhisperRefused);

    public static GameText Of(ContactStatus status, string name) => status switch
    {
        ContactStatus.Ok => GameText.None,
        ContactStatus.Full => GameText.Of(GameTextId.ContactListFull),
        ContactStatus.Listed => GameText.Of(GameTextId.ContactListed, name),
        ContactStatus.NotListed => GameText.Of(GameTextId.ContactNotListed, name),
        ContactStatus.Unavailable => GameText.Of(GameTextId.ContactsUnavailable),
        _ => GameText.Of(GameTextId.ContactRefused)
    };
}
