using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;
using PristonTale.Protocol.Transport;

// smoke [address] [account] [password] [--character NAME] [--skill CODE] [--chat TEXT]
// Without --character a new Mechanician is created and its skill cast must be answered (refused: unlearned, in town).
// With --character the existing character casts CODE on itself and the cast must be accepted, start on the
// presence timeline and, for buffs, add its status.
var positional = args.Where((arg, index) => !arg.StartsWith("--", StringComparison.Ordinal)
    && (index == 0 || !args[index - 1].StartsWith("--", StringComparison.Ordinal))).ToArray();
var address = positional.Length > 0 ? positional[0] : TlsTransport.LocalAddress;
var account = positional.Length > 1 ? positional[1] : "smoke";
var password = positional.Length > 2 ? positional[2] : "smoke-pw";
var existing = Option("--character");
var skillCode = Option("--skill") is { } code ? Convert.ToUInt32(code.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16) : 0x01010001u;
Console.WriteLine($"Connecting to {address}…");
await using var conn = new GameServerConnection(address);

var login = await conn.LoginAsync(account, password);
Console.WriteLine($"Login            : ok={login.Ok} msg=\"{login.Message}\"");
if (!login.Ok)
{
    return 1;
}

var before = await conn.ListCharactersAsync();
Console.WriteLine($"List (before)    : {before.Count} character(s)");

var name = existing ?? "Smoke" + Guid.NewGuid().ToString("N")[..6];
if (existing is null)
{
    var created = await conn.CreateCharacterAsync(name, PlayerClass.Mechanician, faceIndex: 0);
    Console.WriteLine($"Create {name,-11}: ok={created.Ok} msg=\"{created.Message}\"");
    if (!created.Ok)
    {
        return 1;
    }
}

var after = await conn.ListCharactersAsync();
Console.WriteLine($"List (after)     : {after.Count} character(s)");
foreach (var c in after)
{
    Console.WriteLine($"    - {c.Name} (class {c.Class}, level {c.Level})");
}

var enter = await conn.EnterWorldAsync(name);
Console.WriteLine($"EnterWorld       : ok={enter.Ok} field={enter.FieldId} entity={enter.EntityId} msg=\"{enter.Message}\"");
if (!enter.Ok)
{
    return 1;
}

var catalog = await conn.GetWorldCatalogAsync();
await using (var play = conn.OpenPlayChannel())
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var spawns = 0;
    var owner = false;
    QuestView? journal = null;
    try
    {
        await foreach (var update in play.ReadAllAsync(timeout.Token))
        {
            spawns += update.Spawns.Count;
            owner |= update.Owner is not null;
            journal ??= update.Quest;
            if (spawns > 1 && owner && journal is not null)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }

    Console.WriteLine($"Presence         : {spawns} spawn(s), owner status={owner}, catalog {catalog.Monsters.Count} monsters / {catalog.Npcs.Count} NPCs");
    if (spawns == 0 || !owner)
    {
        return 1;
    }

    // The journal arrives with the first snapshots; a quest talk without an NPC must be refused as out of reach.
    var talk = await play.RequestQuestAsync(new QuestCommand(Guid.NewGuid(), 0, QuestAction.Talk, QuestCode.None, 0, 0));
    Console.WriteLine($"Quest            : journal revision {journal?.Revision.ToString() ?? "none"} ({journal?.Code}), talk without NPC -> {talk.Status}");
    if (journal is null || talk.Ok || talk.Status != QuestStatus.OutOfReach)
    {
        return 1;
    }

    // A whisper to a player who is not online must be answered with a server line naming that player.
    var whisper = await ChatAsync(play, "/:SmokeNobody hi", line => line.Text.StartsWith("SmokeNobody ", StringComparison.Ordinal));
    if (whisper is null)
    {
        return 1;
    }

    if (Option("--chat") is { } command)
    {
        await ChatAsync(play, command, _ => true);
    }

    await play.SendMovementAsync(new System.Numerics.Vector3(enter.SpawnX + 20, 0, enter.SpawnZ), 0f, false, false, 0, enter.FieldId);
    if (!await CastAsync(play, existing is not null))
    {
        return 1;
    }
}

Console.WriteLine("SMOKE OK");
return 0;

async Task<bool> CastAsync(IPlayChannel play, bool mustSucceed)
{
    await play.SendSkillCastAsync(1, skillCode, 0, null);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    SkillCastResultInfo? result = null;
    SkillEventInfo? start = null;
    SkillEventInfo? impact = null;
    StatusEffectInfo? status = null;
    try
    {
        await foreach (var update in play.ReadAllAsync(timeout.Token))
        {
            result ??= update.CastResult;
            start ??= update.SkillEvents.FirstOrDefault(value => value.Phase == SkillEventPhase.Start && value.SkillCode == skillCode);
            impact ??= update.SkillEvents.FirstOrDefault(value => value.Phase == SkillEventPhase.Impact && value.SkillCode == skillCode);
            status ??= update.StatusEffects.FirstOrDefault(value => value.Op == StatusEffectOp.Add && value.StatusCode == skillCode);
            if (result is { Accepted: false } || result is not null && impact is not null && status is not null)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }

    Console.WriteLine($"SkillCast {skillCode:X8}: result={result?.Status.ToString() ?? "none"} rank={result?.Rank} cooldown={result?.CooldownMs} ms "
        + $"mana={result?.Mana} stamina={result?.Stamina}");
    Console.WriteLine($"Skill timeline   : start={(start is null ? "none" : $"{start.DurationMs:0} ms, hit at {start.HitAtMs:0} ms")}, "
        + $"impact={(impact is null ? "none" : $"{impact.TargetCount} target(s)")}, status={(status is null ? "none" : $"{status.RemainingMs} ms value {status.Value}")}");
    return mustSucceed ? result is { Accepted: true } && start is not null && impact is not null : result is not null;
}

async Task<ChatLineInfo?> ChatAsync(IPlayChannel play, string text, Func<ChatLineInfo, bool> expected)
{
    await play.SendChatAsync(text, false);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    ChatLineInfo? answer = null;
    try
    {
        await foreach (var update in play.ReadAllAsync(timeout.Token))
        {
            foreach (var line in update.Chat)
            {
                Console.WriteLine($"Chat {line.Kind,-12}: {line.Text}");
                answer ??= expected(line) ? line : null;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }

    Console.WriteLine($"Chat \"{text}\" -> {(answer is null ? "no answer" : "answered")}");
    return answer;
}

string? Option(string option)
{
    var index = Array.IndexOf(args, option);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
