namespace PristonTale.Simulation.Items.Usable;

// The visible effect an item use plays around its user for everyone nearby. Wire values; append only.
public enum ItemEffectKind : byte
{
    None = 0,
    Firecracker = 1,
    Mastery = 2,
    ForceOrb = 3,
    Learn = 4,
    PetAttack = 5,
    // The return sparkle with the learn sound: ether cores, crystal summons and the Restoration revive.
    Return = 6,
    // The level-up burst with the learn sound: a head change starting or ending.
    HeadChange = 7,
    // The drink sound of a booster or a puzzle reveal.
    Drink = 8,
    // A gift box or capsule opened: the orange light of the eat event.
    GiftBox = 9,
    // A new character's starter gift came (its order in Variant, its item in ItemCode); its owner's client shows the
    // beginner's guide to it.
    StarterGift = 10,
    // An aged copy reached its level (the upkeep): the aging light on its owner, with the level-up sound lowered.
    AgingUp = 11
}
