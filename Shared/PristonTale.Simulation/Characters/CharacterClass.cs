using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

public abstract class CharacterClass
{
    private readonly List<Skill> _skills = new();

    protected CharacterClass(CharacterClassDef definition)
    {
        Definition = definition;
        _skills.AddRange(ClassSkillCatalog.For(definition.Class));
    }

    public CharacterClassDef Definition { get; }
    public PlayerClass Class => Definition.Class;
    public string Name => Definition.Name;
    public CharacterBrood Brood => Definition.Brood;
    public IReadOnlyList<Skill> Skills => _skills;

    protected static CharacterClassDef BuildDefinition(
        PlayerClass playerClass,
        string name,
        CharacterBrood brood,
        string bodyInx,
        string headInx,
        int armorTableIndex,
        CharacterAttributes baseAttributes,
        CharacterFunctionProfile functionProfile) =>
        new(
            playerClass,
            name,
            brood,
            new CharacterVisualSet(
                new CharacterAssetRef(bodyInx),
                new CharacterAssetRef(headInx),
                FaceIndex: 0,
                HairIndex: 0,
                armorTableIndex),
            baseAttributes,
            CharacterAttributes.Empty,
            functionProfile);

    public virtual void OnSpawn(PlayerCharacter player) { }

    public virtual void OnLevelUp(PlayerCharacter player) { }
}
