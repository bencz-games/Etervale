using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World.ItemUses;

// Monster crystals and soul stones. Billing crystals and soul stones refuse during the three-second delay every crystal
// use starts; the item's level, towns and the quest arena refuse any crystal, and a pet crystal is refused to a user
// who keeps ten pets. Clan pets need a registered clan, one a member, while fewer than three of the clan stand near;
// castle mercenaries need a member of the clan holding the castle during a siege in the castle field, within the called
// mercenaries' limits (FieldInstance.MercenaryRoom; a siege that ends before the use's commit takes the crystal). The
// monster appears beside its caller (a mercenary where it stands), and the effect plays there.
internal sealed class MonsterCrystalUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        var item = use.Item;
        if (!MonsterCrystals.TryGet(item.Definition.CatalogKey, out var crystal))
        {
            return ItemUseStatus.NotUsable;
        }

        var context = use.Context;
        if (crystal.SharedDelay && context.Player.CrystalDelayUntilMs > context.NowMs)
        {
            return ItemUseStatus.Delay;
        }

        if (item.Definition.RequiredLevel > use.Character.Level)
        {
            return ItemUseStatus.LevelTooLow;
        }

        if (use.Here is null || use.Here.State == FieldState.Village || context.Field.FieldId == FieldCodes.QuestArena)
        {
            return ItemUseStatus.NotHere;
        }

        return crystal.Kind switch
        {
            CrystalKind.ClanPet when context.Player.Clan is null => ItemUseStatus.ClanOnly,
            CrystalKind.ClanPet when !context.Player.Clan.Registered => ItemUseStatus.RegisteredClanOnly,
            CrystalKind.ClanPet when context.Field.ClanPetRoom(context.Player) is var room && room != ItemUseStatus.Used => room,
            CrystalKind.ClanPet when !context.Field.HasCrystalPetSlot(context.Player) => ItemUseStatus.SummonLimit,
            CrystalKind.CastleMercenary => context.Field.MercenaryRoom(context.Player,
                context.Field.TryFindMonsterKey(crystal.Pick(context.Pick, context.CrystalEvent), out var mercenary) ? mercenary : null),
            CrystalKind.SoulStone when MonsterCrystals.SoulStoneBlockedFields.Contains(context.Field.FieldId) => ItemUseStatus.NotHere,
            CrystalKind.Pet when !context.Field.HasCrystalPetSlot(context.Player) => ItemUseStatus.SummonLimit,
            _ => ItemUseStatus.Used
        };
    }

    // The monster the crystal calls; a crystal whose monster the world data lacks is not usable.
    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate)
    {
        MonsterCrystals.TryGet(use.Item.Definition.CatalogKey, out var crystal);
        var key = crystal.Pick(use.Context.Pick, use.Context.CrystalEvent);
        return use.Context.Field.TryFindMonsterKey(key, out _) ? new ItemUseExtra(ItemUseStatus.Used, key) : new ItemUseExtra(ItemUseStatus.NotUsable);
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        var player = use.Player;
        player.CrystalDelayUntilMs = use.NowMs + (long)MonsterCrystals.SharedDelayMs;
        return use.Field.CallCrystalMonster(player, use.Operation.CatalogKey, use.Operation.Summon) is { } spawned
            ? new ItemUseEffect(ItemEffectKind.Return, spawned)
            : ItemUseEffect.None;
    }
}
