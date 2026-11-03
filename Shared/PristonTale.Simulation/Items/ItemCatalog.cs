namespace PristonTale.Simulation.Items;

public sealed class ItemCatalog
{
    private readonly Dictionary<uint, ItemDefinition> _byCode;
    private readonly Dictionary<uint, ItemDefinition> _questVariants;
    private readonly Dictionary<(uint Code, int Variant), ItemDefinition> _namedUniques;
    private readonly Dictionary<int, ItemSetDefinition> _sets;

    // Quest variants share the code of a regular item but carry their own name and stats (the unique quest marker of the
    // authored data); quests hand them out and saved items name them by their unique marker. Named uniques (Vega,
    // Ancient Rage...) are further variants of a code that never drop; game masters create them.
    public ItemCatalog(IEnumerable<ItemDefinition> definitions, IEnumerable<ItemDefinition> questVariants = null,
        IEnumerable<ItemDefinition> namedUniques = null, IEnumerable<ItemSetDefinition> sets = null)
    {
        _sets = (sets ?? []).ToDictionary(static set => set.SetCode);
        _byCode = definitions.Select(WithSet).ToDictionary(static d => d.Code);
        _questVariants = (questVariants ?? []).Select(WithSet).ToDictionary(static d => d.Code);
        _namedUniques = (namedUniques ?? []).Select(WithSet).ToDictionary(static d => (d.Code, d.UniqueVariant));
        All = _byCode.Values.ToList();
    }

    private ItemDefinition WithSet(ItemDefinition definition) =>
        definition.SetCode != 0 && _sets.TryGetValue(definition.SetCode, out var set) ? definition with { Set = set } : definition;

    public IReadOnlyCollection<ItemDefinition> NamedUniques => _namedUniques.Values;

    public bool TryGetNamedUnique(uint code, int variant, out ItemDefinition definition) => _namedUniques.TryGetValue((code, variant), out definition);

    public IReadOnlyList<ItemDefinition> All { get; }

    public ItemDefinition Get(uint code) => _byCode[code];

    public bool TryGet(uint code, out ItemDefinition definition) => _byCode.TryGetValue(code, out definition);

    public bool TryGetQuestVariant(uint code, out ItemDefinition definition) => _questVariants.TryGetValue(code, out definition);

    // The definition a saved item names: the quest variant for the unique quest marker (the regular one when the code
    // has no variant), the regular one otherwise.
    public bool TryGet(uint code, int unique, out ItemDefinition definition) =>
        unique > ItemDefinition.NamedUniqueBase && TryGetNamedUnique(code, unique - ItemDefinition.NamedUniqueBase, out definition)
        || unique == ItemDefinition.QuestUnique && TryGetQuestVariant(code, out definition) || TryGet(code, out definition);
}
