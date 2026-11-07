using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.World;

// Runs on the world tick. Every request is checked against the NPC in the player's field: alive, within reach, offering
// the service and the service running in Etervale. Prices, stock and totals come from the catalogs, never the client.
internal static class NpcServiceTransactions
{
    public static bool CanReach(WorldPlayer player, WorldNpc npc) =>
        !player.IsDead && player.Shop is null && NpcInteraction.WithinReach(player.Position, npc.Position);

    public static NpcServiceOutcome Check(WorldPlayer player, FieldInstance field, NpcServiceCommand command, out WorldNpc? npc,
        out NpcService? service)
    {
        service = null;
        if (!field.TryGetNpc(command.NpcEntityId, out npc) || !CanReach(player, npc))
        {
            return NpcServiceOutcome.Failed(NpcServiceStatus.OutOfReach, GameText.Of(GameTextId.NpcOutOfReach));
        }

        service = npc.Definition.Find(command.Service);
        if (service is null || !Enum.IsDefined(command.Action) || !Accepts(command.Service, command.Action))
        {
            return NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, GameText.Of(GameTextId.NpcNoSuchService));
        }

        if (!NpcServiceAvailability.IsAvailable(service))
        {
            return NpcServiceOutcome.Failed(NpcServiceStatus.NotAvailable, GameText.Of(GameTextId.ServiceNotAvailable));
        }

        // A skill master trains only the classes of its own town: Ricarten's the Tempskron, Pillai's the Morion.
        if (command.Service == NpcServiceKind.SkillMaster && !Simulation.Quests.QuestClasses.IsHomeTown(player.Character, field.FieldId))
        {
            return NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, GameText.Of(GameTextId.OtherTownMaster));
        }

        // A character over the weight limit cannot open the warehouse.
        if (command.Service == NpcServiceKind.Warehouse && command.Action == NpcServiceAction.Open && CarriedWeight.IsOverweight(player.Character))
        {
            return NpcServiceOutcome.Failed(NpcServiceStatus.OverWeight, GameText.Of(GameTextId.OverWeight));
        }

        return new NpcServiceOutcome(true, NpcServiceStatus.Ok, player.Character.InventoryRevision, 0, GameText.None);
    }

    // booth: Bellatra as it stands, for its team booths.
    public static PreparedNpcOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, NpcServiceCommand command,
        ItemCatalog items, WorldCatalog world, BellatraBooth? booth = null)
    {
        if (MobileShopTransactions.Handles(command))
        {
            return MobileShopTransactions.Prepare(session, player, field, command);
        }

        var original = player.Character;
        var storage = session.Warehouse;
        var check = Check(player, field, command, out var npc, out var service);
        if (!check.Ok)
        {
            return Final(original, check with { Revision = original.InventoryRevision });
        }

        if (command.Service == NpcServiceKind.Warehouse && storage is null && command.Action != NpcServiceAction.Close)
        {
            return Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.NotOpen, GameText.Of(GameTextId.WarehouseNotOpen), original.InventoryRevision));
        }

        if (!NpcServiceActions.IsDurable(command.Action))
        {
            var team = Simulation.World.Bellatra.BellatraRules.TeamOfBooth(service!.Value);
            TeleportOffer? ticket = null;
            var answer = command.Service == NpcServiceKind.EventBooth && command.Action == NpcServiceAction.Open && booth is not null && team >= 0
                ? booth.Answer(original, team, world, out ticket)
                : GameText.None;
            var shown = Show(command, npc!, service!, original, storage, items, world, Done(original, storage, answer), field.CastleTax);
            if (ticket is not null)
            {
                shown = shown with { Destinations = [ticket] };
            }

            return new PreparedNpcOperation(original, null, null, shown);
        }

        return Settled(original, storage, command) ?? Apply(session, player, field, command, npc!, service!, items, world, booth);
    }

    // A durable request already answered: its replay (the same command) or an identity conflict, and a request made
    // against an older inventory or warehouse. Null when the request can be applied.
    internal static PreparedNpcOperation? Settled(PlayerCharacter original, WarehouseStorage? storage, NpcServiceCommand command)
    {
        if (original.ServiceOperations.Find(command.OperationId) is { } applied)
        {
            if (applied.Command != command)
            {
                return Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Conflict, GameText.Of(GameTextId.OperationConflict), original.InventoryRevision));
            }

            var replayed = State(command, original, storage, Done(original, storage, GameText.Of(GameTextId.AlreadyApplied)));
            return new PreparedNpcOperation(original, null, null, replayed);
        }

        if (command.OperationId == Guid.Empty || command.ExpectedRevision != original.InventoryRevision
            || storage is not null && command.Service == NpcServiceKind.Warehouse && command.ExpectedStorageRevision != storage.Revision)
        {
            return Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Stale, GameText.Of(GameTextId.ServiceStale),
                original.InventoryRevision, storage?.Revision ?? 0));
        }

        return null;
    }

    // A shop's sale, repair or repair of everything worn and carried.
    internal static NpcServiceStatus SellOrRepair(PlayerCharacter candidate, NpcServiceCommand command) => command.Action switch
    {
        NpcServiceAction.Sell => NpcShopRules.TrySell(candidate, command.ItemId, command.Count, out _),
        NpcServiceAction.Repair => NpcShopRules.TryRepair(candidate, command.ItemId, out _),
        NpcServiceAction.RepairAll => NpcShopRules.TryRepairAll(candidate, out _),
        _ => NpcServiceStatus.Rejected
    };

    private static PreparedNpcOperation Apply(PlayerSession session, WorldPlayer player, FieldInstance field, NpcServiceCommand command,
        WorldNpc npc, NpcService service, ItemCatalog items, WorldCatalog world, BellatraBooth? booth)
    {
        var original = player.Character;
        var candidate = original.CloneForInventoryOperation();
        var storage = command.Service == NpcServiceKind.Warehouse ? session.Warehouse!.Clone() : null;
        var fieldId = field.FieldId;
        var position = player.Position;
        TeleportArrival? arrival = null;
        ItemWorkResult? work = null;
        NpcServiceStatus status;
        switch (command.Action)
        {
            case NpcServiceAction.Buy:
                status = npc.Definition.Shop.Contains(command.ItemCode) && items.TryGet(command.ItemCode, out var definition)
                    ? NpcShopRules.TryBuy(candidate, definition, command.Count, command.Anchor, out _, field.CastleTax)
                    : NpcServiceStatus.Rejected;
                break;
            case NpcServiceAction.Sell or NpcServiceAction.Repair or NpcServiceAction.RepairAll:
                status = SellOrRepair(candidate, command);
                break;
            case NpcServiceAction.Deposit:
                status = storage!.TryDeposit(candidate, command.ItemId, command.Anchor, command.Covered);
                break;
            case NpcServiceAction.Withdraw:
                status = storage!.TryWithdraw(candidate, command.ItemId, command.Anchor, command.Covered);
                break;
            case NpcServiceAction.Move:
                status = storage!.TryMove(command.ItemId, command.Anchor, command.Covered);
                break;
            case NpcServiceAction.DepositGold:
                status = storage!.TryDepositGold(candidate, command.Amount);
                break;
            case NpcServiceAction.WithdrawGold:
                status = storage!.TryWithdrawGold(candidate, command.Amount);
                break;
            case NpcServiceAction.Teleport:
                status = TryTeleport(candidate, service, command.Anchor, world, field.CastleTax, out arrival);
                if (arrival is not null)
                {
                    fieldId = arrival.FieldCode;
                    position = arrival.Position;
                }

                break;
            case NpcServiceAction.Exchange:
                status = Simulation.Items.Usable.PuzzleExchange.TryExchange(candidate, items, field.ItemRng, out _);
                break;
            case NpcServiceAction.Age:
                status = Simulation.Items.Aging.AgingWork.TryAge(candidate, command.ItemId, command.Materials ?? [], field.ItemRng, out work, out _,
                    field.Aging);
                break;
            case NpcServiceAction.Mix:
                status = Simulation.Items.Mixing.MixingWork.TryMix(candidate, command.ItemId, command.Materials ?? [], field.ItemRng, out work, out _,
                    field.Mixing);
                break;
            case NpcServiceAction.Refine:
                status = Simulation.Items.Mixing.MixingWork.TryRefine(candidate, command.Materials ?? [], items, field.ItemRng, out work, out _,
                    field.Mixing);
                break;
            case NpcServiceAction.Smelt:
                status = Simulation.Items.Smelting.SmeltingWork.TrySmelt(candidate, command.Materials ?? [], items, field.ItemRng, out work, out _);
                break;
            case NpcServiceAction.Manufacture:
                status = Simulation.Items.Manufacture.ManufactureWork.TryManufacture(candidate, command.ItemId, command.Materials ?? [], items,
                    field.ItemRng, out work, out _);
                break;
            case NpcServiceAction.ResetMix:
                status = Simulation.Items.Mixing.MixtureReset.TryReset(candidate, command.ItemId, command.Materials ?? [], field.ItemRng, out work);
                break;
            case NpcServiceAction.EnterBellatra:
                status = Simulation.World.Bellatra.BellatraRules.TeamOfBooth(service.Value) is var team and >= 0 && booth is not null
                    ? booth.Admit(candidate, team, out arrival)
                    : NpcServiceStatus.Rejected;
                if (arrival is not null)
                {
                    fieldId = arrival.FieldCode;
                    position = arrival.Position;
                }

                break;
            default:
                status = NpcServiceStatus.Rejected;
                break;
        }

        if (status != NpcServiceStatus.Ok)
        {
            return Final(original, NpcServiceOutcome.Failed(status, Describe(status), original.InventoryRevision, session.Warehouse?.Revision ?? 0));
        }

        candidate.RecordServiceOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, fieldId, position);
        var outcome = Done(candidate, storage ?? session.Warehouse, GameText.None);
        var reply = State(command, candidate, storage, outcome) with { Arrival = arrival, Work = work };
        return new PreparedNpcOperation(original, candidate, snapshot, reply, candidate.Money - original.Money, storage,
            session.Warehouse?.Revision ?? 0, field.CastleTax,
            command.Action == NpcServiceAction.EnterBellatra ? Simulation.World.Bellatra.BellatraRules.TeamOfBooth(service.Value) : -1);
    }

    private static NpcServiceStatus TryTeleport(PlayerCharacter candidate, NpcService service, int index, WorldCatalog world, int tax, out TeleportArrival? arrival)
    {
        arrival = null;
        if (!TeleportRoutes.TryGet(service.Value, index, out var destination) || !world.TryGetFieldByCode(destination.FieldCode, out var target))
        {
            return NpcServiceStatus.Rejected;
        }

        if (candidate.Level < target.LimitLevel)
        {
            return NpcServiceStatus.Rejected;
        }

        if (!candidate.TryChangeMoney(-CastleRules.WithTax(destination.PriceFor(candidate.Level), tax)))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        var start = target.ArrivalFor(Random.Shared);
        arrival = new TeleportArrival(target.FieldCode, new Vector3(start.X, 0f, start.Z));
        return NpcServiceStatus.Ok;
    }

    private static NpcServiceReply Show(NpcServiceCommand command, WorldNpc npc, NpcService service, PlayerCharacter character,
        WarehouseStorage? storage, ItemCatalog items, WorldCatalog world, NpcServiceOutcome outcome, int tax)
    {
        if (command.Action == NpcServiceAction.Close)
        {
            return new NpcServiceReply(outcome);
        }

        return command.Service switch
        {
            NpcServiceKind.Shop => new NpcServiceReply(outcome)
            {
                Catalog = command.Action == NpcServiceAction.Open ? Catalog(npc.Definition.Shop, items, tax) : null,
                Quotes = Quotes(character)
            },
            NpcServiceKind.Warehouse => new NpcServiceReply(outcome) { Warehouse = storage!.View() },
            NpcServiceKind.Teleporter => new NpcServiceReply(outcome) { Destinations = Destinations(service, world, character.Level, tax) },
            NpcServiceKind.Manufacture when command.Action == NpcServiceAction.Refresh => new NpcServiceReply(outcome)
            {
                Catalog = ManufactureProducts(character, command.ItemId, items)
            },
            _ => new NpcServiceReply(outcome)
        };
    }

    // What the recipe the player holds makes, one offer per product with its rune combination as Ordinal (the random
    // armour of all B runes lists every candidate under one combination) and its fee as Price.
    private static IReadOnlyList<ShopOffer> ManufactureProducts(PlayerCharacter character, Guid recipeId, ItemCatalog items)
    {
        var inventory = character.Inventory;
        var at = inventory.Find(recipeId);
        var armour = false;
        var recipe = at >= 0 && inventory[at] is { } held ? Simulation.Items.Manufacture.ManufactureWork.RecipeOf(held.Definition.CatalogKey, out armour) : 0;
        var offers = new List<ShopOffer>();
        for (var combination = 0; recipe > 0 && combination < 1 << Simulation.Items.Manufacture.ManufactureWork.Runes; combination++)
        {
            foreach (var key in Simulation.Items.Manufacture.ManufactureWork.Products(recipe, armour, combination, character.Class.Class))
            {
                if (items.TryGet(ItemCodes.Parse(key), out var made))
                {
                    offers.Add(new ShopOffer(made.Code, 0, combination, made.Name, made.Kind, Simulation.Items.Manufacture.ManufactureWork.Fee(made),
                        made.GridWidth, made.GridHeight, made.MaxStack, made.CatalogKey));
                }
            }
        }

        return offers;
    }

    private static NpcServiceReply State(NpcServiceCommand command, PlayerCharacter character, WarehouseStorage? storage, NpcServiceOutcome outcome) =>
        command.Service switch
        {
            NpcServiceKind.Shop => new NpcServiceReply(outcome) { Quotes = Quotes(character) },
            NpcServiceKind.Warehouse when storage is not null => new NpcServiceReply(outcome) { Warehouse = storage.View() },
            _ => new NpcServiceReply(outcome)
        };

    public static IReadOnlyList<ShopOffer> Catalog(NpcShopStock stock, ItemCatalog items, int tax = 0)
    {
        var offers = new List<ShopOffer>();
        for (var tab = 0; tab < 3; tab++)
        {
            var codes = stock.Tab(tab);
            for (var ordinal = 0; ordinal < codes.Count; ordinal++)
            {
                if (items.TryGet(codes[ordinal], out var definition))
                {
                    offers.Add(new ShopOffer(definition.Code, tab, ordinal, definition.Name, definition.Kind, NpcShopRules.BuyPrice(definition, 1, tax),
                        definition.GridWidth, definition.GridHeight, definition.MaxStack, definition.CatalogKey));
                }
            }
        }

        return offers;
    }

    // Equipped items are quoted for repair only; a sell price of -1 marks an item the shop does not buy.
    public static ShopQuotes Quotes(PlayerCharacter character)
    {
        var quotes = new List<ShopQuote>();
        foreach (var item in character.Inventory.Items())
        {
            var sell = NpcShopRules.CanSell(item) ? NpcShopRules.SellPrice(item, item.Count) : -1;
            quotes.Add(new ShopQuote(item.InstanceId, sell, NpcShopRules.RepairCost(item), item.DurabilityCurrent, item.DurabilityMax));
        }

        foreach (var equipped in character.Equipment.Values)
        {
            quotes.Add(new ShopQuote(equipped.InstanceId, -1, NpcShopRules.RepairCost(equipped), equipped.DurabilityCurrent, equipped.DurabilityMax));
        }

        return new ShopQuotes(character.InventoryRevision, quotes);
    }

    private static IReadOnlyList<TeleportOffer> Destinations(NpcService service, WorldCatalog world, int level, int tax)
    {
        var offers = new List<TeleportOffer>();
        foreach (var destination in TeleportRoutes.For(service.Value))
        {
            var limit = world.TryGetFieldByCode(destination.FieldCode, out var field) ? field.LimitLevel : 0;
            offers.Add(new TeleportOffer(destination.Index, destination.FieldCode, destination.Name, CastleRules.WithTax(destination.PriceFor(level), tax), limit));
        }

        return offers;
    }

    private static bool Accepts(NpcServiceKind service, NpcServiceAction action) => action switch
    {
        NpcServiceAction.Open or NpcServiceAction.Close => true,
        NpcServiceAction.Refresh => service is NpcServiceKind.Shop or NpcServiceKind.Warehouse or NpcServiceKind.Manufacture,
        NpcServiceAction.Buy or NpcServiceAction.Sell or NpcServiceAction.Repair or NpcServiceAction.RepairAll => service == NpcServiceKind.Shop,
        NpcServiceAction.Deposit or NpcServiceAction.Withdraw or NpcServiceAction.Move or NpcServiceAction.DepositGold
            or NpcServiceAction.WithdrawGold => service == NpcServiceKind.Warehouse,
        NpcServiceAction.Teleport => service == NpcServiceKind.Teleporter,
        NpcServiceAction.Exchange => service == NpcServiceKind.QuestEvent,
        NpcServiceAction.Age => service == NpcServiceKind.ItemAging,
        NpcServiceAction.Mix or NpcServiceAction.Refine => service == NpcServiceKind.ItemMix,
        NpcServiceAction.Smelt => service == NpcServiceKind.Smelting,
        NpcServiceAction.Manufacture => service == NpcServiceKind.Manufacture,
        NpcServiceAction.ResetMix => service == NpcServiceKind.MixtureReset,
        NpcServiceAction.EnterBellatra => service == NpcServiceKind.EventBooth,
        _ => false
    };

    internal static GameText Describe(NpcServiceStatus status, GameTextId refusal = GameTextId.NpcRefused) => status switch
    {
        NpcServiceStatus.BellatraLevel => GameText.Of(GameTextId.BellatraLevel, Simulation.World.Bellatra.BellatraRules.MinLevel),
        NpcServiceStatus.BellatraNoTickets => GameText.Of(GameTextId.BellatraNoTickets,
            Simulation.World.Bellatra.BellatraRules.TicketHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (Simulation.World.Bellatra.BellatraRules.TicketHour + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        _ => GameText.Of(RefusalText(status, refusal))
    };

    private static GameTextId RefusalText(NpcServiceStatus status, GameTextId refusal) => status switch
    {
        NpcServiceStatus.NotEnoughGold => GameTextId.NotEnoughGold,
        NpcServiceStatus.NoSpace => GameTextId.NoSpace,
        NpcServiceStatus.GoldLimit => GameTextId.GoldLimit,
        NpcServiceStatus.OverWeight => GameTextId.OverWeight,
        NpcServiceStatus.WrongPuzzle => GameTextId.WrongPuzzle,
        NpcServiceStatus.WrongMaterials => GameTextId.WrongMaterials,
        NpcServiceStatus.BellatraClosed => GameTextId.BellatraClosed,
        NpcServiceStatus.BellatraTeamFull => GameTextId.BellatraTeamFull,
        NpcServiceStatus.BellatraEntered => GameTextId.BellatraEntered,
        _ => refusal
    };

    internal static NpcServiceOutcome Done(PlayerCharacter character, WarehouseStorage? storage, GameText message) =>
        new(true, NpcServiceStatus.Ok, character.InventoryRevision, storage?.Revision ?? 0, message);

    internal static PreparedNpcOperation Final(PlayerCharacter original, NpcServiceOutcome outcome) =>
        new(original, null, null, new NpcServiceReply(outcome));
}
