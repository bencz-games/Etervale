using PristonTale.Application.Chat;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using static PristonTale.Application.GameMasters.GmInformationCommands;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.GameMasters;

// Commands that make items, gold and experience, and the overrides for the items they make.
internal static class GmItemCommands
{
    public const int MaxGold = 1_000_000;

    public static Task Specialization(GmCommandContext context)
    {
        var job = ChatCommandText.ToNumber(context.Rest);
        context.Commands.ItemOverrides.SetSpecialization(job, context.Commands.NowMs());
        context.Reply(Format(">Specific ({0}) 1 minute operation", job));
        return Task.CompletedTask;
    }

    public static Task Performance(GmCommandContext context)
    {
        var value = ChatCommandText.ToNumber(context.Rest);
        context.Commands.ItemOverrides.SetPerformance(value, context.Commands.NowMs());
        context.Reply(Format(">Item Performance setting ({0}) effective 1 minutes", value));
        return Task.CompletedTask;
    }

    // Ends one of the game master's premium timers: 1 Third Eyes to 13 Stamina Reduce Potion, in the timer numbering.
    public static async Task EndPremium(GmCommandContext context)
    {
        var rest = context.Line[context.Start..];
        if (rest.Length < 2 || !char.IsAsciiDigit(rest[1]))
        {
            return;
        }

        var kind = ChatCommandText.ToNumber(rest);
        if (!Enum.IsDefined((PremiumEffectKind)kind) || kind == 0)
        {
            return;
        }

        if (await context.Commands.World.EndPremiumAsync(context.Issuer.EntityId, (PremiumEffectKind)kind))
        {
            context.Reply(Format("({0})Item Init Success", ItemStatuses.PremiumName((PremiumEffectKind)kind)));
        }
    }

    // The best copy of an item code dropped at the game master, optionally for a class: a class name, a short name
    // (MS FS PS AS KS ATA PRS MG), its number 1 to 8, or none.
    public static async Task Perfect(GmCommandContext context)
    {
        var code = context.Word();
        var specName = context.Word();
        var job = SpecJobCode(specName);
        if (job < 0)
        {
            context.Reply(Format("Unknown spec {0} (a class name, MS FS PS AS KS ATA PRS MG, 1-8 or none)", specName));
            return;
        }

        var specBit = job == 0 ? 0u : (uint)JobBits.Of((PlayerClass)job);
        var className = job == 0 ? "no spec" : ((PlayerClass)job).ToString();
        if (FindByCode(context, code) is not { } definition)
        {
            context.Reply(Format("Unknown item code {0}", code));
            return;
        }

        if (!ItemRoller.CanTakeSpec(definition, specBit))
        {
            context.Reply(Format("{0} cannot take the {1} spec", code, className));
            return;
        }

        var dropped = await context.Commands.World.DropAsync(context.Issuer.EntityId, _ => ItemRoller.CreatePerfect(definition, 1, specBit), 0);
        context.Reply(dropped ? Format("Perfect {0} created ({1})", code, className) : Format("No field to drop {0} on", code));
    }

    // An item copy by code, or by a double-quoted name (which also finds unique items), dropped at the game master.
    public static async Task Create(GmCommandContext context)
    {
        var argument = context.Rest;
        if (Find(context, argument) is not { } definition)
        {
            return;
        }

        var overrides = context.Commands.ItemOverrides.Current(context.Commands.NowMs());
        if (await context.Commands.World.DropAsync(context.Issuer.EntityId, rng => ItemRoller.Create(definition, 1, rng, overrides), 0))
        {
            context.Reply(Format("{0} items have been created", argument));
        }
    }

    // /AgingSetting code level [class number]
    public static async Task Aged(GmCommandContext context)
    {
        var code = context.Word();
        var level = Math.Clamp(ChatCommandText.ToNumber(context.Word()), 0, Simulation.Items.Aging.AgingRules.MaxLevel);
        var job = ChatCommandText.ToNumber(context.Word());
        if (FindByCode(context, code) is not { } definition)
        {
            context.Reply(Format("Unknown item code {0}", code));
            return;
        }

        var spec = Enum.IsDefined((PlayerClass)job) && ClassProfiles.Specializes((PlayerClass)job) ? (uint)JobBits.Of((PlayerClass)job) : 0u;
        var overrides = spec != 0 && ItemRoller.CanTakeSpec(definition, spec) ? new ItemRollOverrides(spec) : ItemRollOverrides.None;
        var dropped = await context.Commands.World.DropAsync(context.Issuer.EntityId, rng =>
        {
            var item = ItemRoller.Create(definition, 1, rng, overrides);
            Simulation.Items.Aging.AgingRules.AgeTo(item, level);
            return item;
        }, 0);
        context.Reply(dropped ? Format("+{0} aged item {1} created", level, code) : Format("No field to drop {0} on", code));
    }

    // An item copy into another player's bag, written to storage before the player receives it.
    public static async Task Gift(GmCommandContext context)
    {
        var name = context.Word();
        var target = name.Length == 0 ? null : context.Commands.Directory.FindByName(name);
        var argument = context.Rest;
        if (target is null || Find(context, argument) is not { } definition)
        {
            return;
        }

        var overrides = context.Commands.ItemOverrides.Current(context.Commands.NowMs());
        if (await GmGifts.GiveAsync(context.Commands, context.Issuer, target, rng => ItemRoller.Create(definition, 1, rng, overrides)))
        {
            context.Reply(Format("{0} items have been sent to {1}", argument, target.Name));
        }
    }

    // Raises the game master to the experience that starts a level (2 to the level cap).
    public static async Task Experience(GmCommandContext context)
    {
        var word = context.Word();
        if (word.Length == 0)
        {
            return;
        }

        if (await context.Commands.World.RaiseExperienceAsync(context.Issuer.EntityId, ChatCommandText.ToNumber(word)) is { } experience)
        {
            context.Reply(Format(">Here you go~( {0} )", experience));
        }
    }

    // Adds experience points to the game master, as a monster's award would.
    public static async Task ExperienceUp(GmCommandContext context)
    {
        // Experience is unsigned 64-bit: amounts past the 32-bit range are accepted.
        if (!ulong.TryParse(context.Word(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            || amount == 0)
        {
            return;
        }

        if (await context.Commands.World.AddExperienceAsync(context.Issuer.EntityId, amount) is not null)
        {
            context.Reply(Format(">( {0} )", amount));
        }
    }

    // Drops 1 to 1,000,000 gold at the game master.
    public static async Task Gold(GmCommandContext context)
    {
        var word = context.Word();
        var amount = ChatCommandText.ToNumber(word);
        if (word.Length == 0 || amount is <= 0 or > MaxGold)
        {
            return;
        }

        if (await context.Commands.World.DropAsync(context.Issuer.EntityId, null, amount))
        {
            context.Reply(Format(">What the hell~!!( {0} )", amount));
        }
    }

    // On a debug server, a slash and an item code drop that item at the game master.
    public static async Task<bool> DebugDrop(GmCommandContext context)
    {
        if (FindByCode(context, context.Line[1..].Trim()) is not { } definition)
        {
            return false;
        }

        var overrides = context.Commands.ItemOverrides.Current(context.Commands.NowMs());
        return await context.Commands.World.DropAsync(context.Issuer.EntityId, rng => ItemRoller.Create(definition, 1, rng, overrides), 0);
    }

    // 0 for none, a class number 1 to 8, else -1.
    public static int SpecJobCode(string name)
    {
        if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.Length == 1 && name[0] - '0' is var digit && Enum.IsDefined((PlayerClass)digit) && ClassProfiles.Specializes((PlayerClass)digit))
        {
            return digit;
        }

        var shortNames = new[] { ("FS", 1), ("MS", 2), ("AS", 3), ("PS", 4), ("ATA", 5), ("KS", 6), ("MG", 7), ("PRS", 8) };
        foreach (var (alias, code) in shortNames)
        {
            if (alias.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            if (ClassProfiles.Specializes(playerClass) && playerClass.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return (int)playerClass;
            }
        }

        return -1;
    }

    // A code names a non-unique item; a double-quoted phrase names any item by its name.
    private static ItemDefinition? Find(GmCommandContext context, string argument)
    {
        if (argument.StartsWith('"'))
        {
            var close = argument.IndexOf('"', 1);
            var name = close < 0 ? argument[1..] : argument[1..close];
            var items = context.Commands.WorldData.Current.Items;
            return items.All.Concat(items.NamedUniques).FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        return FindByCode(context, argument.Split(' ', 2)[0]);
    }

    // A catalog key, "<key>#U<n>" for a named unique (Vega, Ancient Rage...) or "<key>#Q" for a quest variant.
    private static ItemDefinition? FindByCode(GmCommandContext context, string code)
    {
        var items = context.Commands.WorldData.Current.Items;
        var mark = code.IndexOf('#');
        if (mark > 0 && ItemCodes.TryParse(code[..mark], out var itemCode))
        {
            var variant = code[(mark + 1)..];
            if (variant.Equals("Q", StringComparison.OrdinalIgnoreCase))
            {
                return items.TryGetQuestVariant(itemCode, out var quest) ? quest : null;
            }

            return variant.Length > 1 && char.ToUpperInvariant(variant[0]) == 'U' && int.TryParse(variant[1..], out var number)
                && items.TryGetNamedUnique(itemCode, number, out var unique) ? unique : null;
        }

        return code.Length == 0 ? null : items.All.FirstOrDefault(item =>
            item.UniqueItem == 0 && item.CatalogKey.Equals(code, StringComparison.OrdinalIgnoreCase));
    }
}
