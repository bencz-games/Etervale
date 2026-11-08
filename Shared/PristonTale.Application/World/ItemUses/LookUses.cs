using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// Items that change how the player looks or cheers.

// A hair tint changes the hair the character row stores and every viewer draws; the tint already worn refuses.
internal sealed class HairTintUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use) =>
        use.Context.Session.Loaded!.HairIndex == use.Rule.Argument ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;

    public override CharacterSaveSnapshot Store(CharacterSaveSnapshot snapshot, UsableItemRule rule) =>
        snapshot with { Scalars = snapshot.Scalars with { HairIndex = rule.Argument } };

    public override void Commit(PlayerSession session, UsableItemRule rule) => session.Loaded = session.Loaded! with { HairIndex = rule.Argument };

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.ChangeHair(use.Rule.Argument);
        return new ItemUseEffect(ItemEffectKind.Learn);
    }
}

// A firecracker bursts over its user, who cheers.
internal sealed class FirecrackerUse : ItemUseAction
{
    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.QueueYahoo(heals: true);
        return new ItemUseEffect(ItemEffectKind.Firecracker);
    }
}

// The soccer head is refused while a big head still runs, and guards its user while the head changes.
internal sealed class SoccerHeadUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        var premium = use.Character.Premium;
        return premium.BigHeadMode != 0 && premium.BigHeadExpiresAtSeconds > use.Context.NowSeconds ? ItemUseStatus.SameAttribute : ItemUseStatus.Used;
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.Guard(EventFoods.HeadChangeGuardMs);
        return new ItemUseEffect(ItemEffectKind.HeadChange);
    }
}
