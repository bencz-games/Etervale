using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World.ItemUses;

// One use being prepared on the tick: the context, the command, the item's rule, the carried item and the field's
// definition where the player stands.
internal readonly record struct ItemUseRequest(ItemUseContext Context, ItemUseCommand Command, UsableItemRule Rule, ItemInstance Item, FieldDef? Here)
{
    public PlayerCharacter Character => Context.Player.Character;
}

// A stored use reaching the world on the tick.
internal readonly record struct ItemUseInWorld(WorldPlayer Player, FieldInstance Field, PreparedItemUseOperation Operation, long NowMs)
{
    public UsableItemRule Rule => Operation.Rule!;
}

// What every viewer sees of a use: the effect and where it plays (the player when At is null).
internal readonly record struct ItemUseEffect(ItemEffectKind Kind, Vector3? At = null)
{
    public static ItemUseEffect None => default;
}

// One kind of usable item, end to end. The shared rules (dead or alive, villages, level, the inventory revision) and the
// character part (UsableItemEffects) run for every kind; a kind overrides only what differs.
internal abstract class ItemUseAction
{
    // Only a dead player may use it; every other item refuses the dead.
    public virtual bool ForTheDead => false;

    // A use takes one of the item from the inventory before its effect.
    public virtual bool Consumes => true;

    // The character rules run again on the live character at commit, with the prepared clock and roll.
    public virtual bool ReappliesAtCommit => true;

    public virtual ItemUseStatus Check(in ItemUseRequest use) =>
        UsableItemEffects.Check(use.Character, use.Rule, use.Context.NowSeconds);

    // Where a use that moves the player takes it; asked only of a use that passed Check.
    public virtual TeleportArrival? Arrival(in ItemUseRequest use) => null;

    // What the use adds to the candidate beyond the character rules: prizes, a crystal's monster, gold, food.
    public virtual ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate) => ItemUseExtra.None;

    public virtual GameText Message(ItemDefinition item) => GameText.Of(GameTextId.ItemNamedUsed, item.Name);

    // The stored record of a use that changes what the character row holds outside the character state.
    public virtual CharacterSaveSnapshot Store(CharacterSaveSnapshot snapshot, UsableItemRule rule) => snapshot;

    // The session's part of a stored use, at commit.
    public virtual void Commit(PlayerSession session, UsableItemRule rule)
    {
    }

    public virtual ItemUseEffect Apply(in ItemUseInWorld use) => ItemUseEffect.None;
}

// The kind behind each UsableItemAction.
internal static class ItemUseActions
{
    private static readonly ItemUseAction Plain = new PlainUse();

    private static readonly Dictionary<UsableItemAction, ItemUseAction> ByAction = new()
    {
        [UsableItemAction.PremiumTimers] = new PremiumTimersUse(),
        [UsableItemAction.ResetAttributes] = new ResetAttributesUse(),
        [UsableItemAction.ResetAttributesAndSkills] = new ResetAttributesUse(),
        [UsableItemAction.ResetSkills] = new ResetSkillsUse(),
        [UsableItemAction.SkillMastery] = new SkillMasteryUse(),
        [UsableItemAction.AgingMastery] = new AgingMasteryUse(),
        [UsableItemAction.GravityScroll] = new GravityScrollUse(),
        [UsableItemAction.ScrollEffect] = new ScrollEffectUse(),
        [UsableItemAction.ForceOrb] = new ForceOrbUse(),
        [UsableItemAction.TeleportCore] = new TeleportCoreUse(),
        [UsableItemAction.ReturnToTown] = new ReturnToTownUse(),
        [UsableItemAction.MobileShop] = new MobileShopUse(),
        [UsableItemAction.UnionCore] = new UnionCoreUse(),
        [UsableItemAction.HairTint] = new HairTintUse(),
        [UsableItemAction.Firecracker] = new FirecrackerUse(),
        [UsableItemAction.SoccerHead] = new SoccerHeadUse(),
        [UsableItemAction.MonsterCrystal] = new MonsterCrystalUse(),
        [UsableItemAction.CastleItem] = new CastleItemUse(),
        [UsableItemAction.Restoration] = new RestorationUse(),
        [UsableItemAction.Food] = new FoodUse(),
        [UsableItemAction.GiftBox] = new GiftBoxUse(),
        [UsableItemAction.Chocolate] = new ChocolateUse(),
        [UsableItemAction.PuzzleBag] = new PuzzleBagUse()
    };

    public static ItemUseAction For(UsableItemAction action) => ByAction.GetValueOrDefault(action, Plain);

    // A kind whose whole effect is the character rules (the big-head potion).
    private sealed class PlainUse : ItemUseAction;
}
