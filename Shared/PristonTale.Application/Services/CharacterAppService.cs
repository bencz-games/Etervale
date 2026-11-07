using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.Services;

public sealed class CharacterAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly Persistence.Abstractions.Clans.IClanRepository? _clans;
    private readonly ICharacterRepository _characters;
    private readonly CharacterClassRegistry _classes;
    private readonly WorldDataHolder _worldData;
    private readonly WorldCatalog _world;

    public CharacterAppService(
        ISessionRegistry sessions,
        ICharacterRepository characters,
        CharacterClassRegistry classes,
        WorldDataHolder worldData,
        WorldCatalog world,
        Persistence.Abstractions.Clans.IClanRepository? clans = null)
    {
        _clans = clans;
        _sessions = sessions;
        _characters = characters;
        _classes = classes;
        _worldData = worldData;
        _world = world;
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = Authenticate(sessionToken);

        var rows = await _characters.ListByAccountAsync(session.AccountId, ct);
        var clans = _clans is null ? null : await _clans.ClanNamesAsync(session.AccountId, ct);
        var summaries = new List<CharacterSummary>(rows.Count);
        foreach (var row in rows)
        {
            var summary = BuildSummary(row);
            summaries.Add(clans is not null && clans.TryGetValue(row.Id, out var clan) ? summary with { ClanName = clan } : summary);
        }

        return summaries;
    }

    public async Task<CreateCharacterOutcome> CreateCharacterAsync(
        string sessionToken, string name, PlayerClass playerClass, int faceIndex, CancellationToken ct = default)
    {
        var session = Authenticate(sessionToken);

        var trimmed = name?.Trim() ?? string.Empty;
        if (!CharacterNames.IsValidLength(trimmed))
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.NameLength);

        if (!Enum.IsDefined(playerClass))
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.UnknownClass);

        if (!CharacterFace.IsValidIndex(faceIndex))
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.InvalidFace);

        if (await _characters.NameExistsAsync(trimmed, ct))
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.NameTaken);

        var usedSlots = await _characters.UsedSlotsAsync(session.AccountId, ct);
        if (CharacterSlots.FirstFree(usedSlots) is not { } slot)
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.NoFreeSlot);

        var record = CharacterRecordFactory.NewStarter(
            _classes,
            session.AccountId,
            trimmed,
            slot,
            playerClass,
            Simulation.Progression.DeathPenalty.TownField(playerClass),
            faceIndex);

        var created = await _characters.CreateAsync(record, ct);
        return CreateCharacterOutcome.Success(BuildSummary(created));
    }

    public async Task<DeleteCharacterOutcome> DeleteCharacterAsync(
        string sessionToken, string name, CancellationToken ct = default)
    {
        var session = Authenticate(sessionToken);

        var trimmed = name?.Trim() ?? string.Empty;
        var found = await _characters.FindByNameForAccountAsync(session.AccountId, trimmed, ct);
        if (found is null)
        {
            return DeleteCharacterOutcome.Denied("character not found");
        }

        // A clan keeps its members: a member leaves (a chief entrusts or disbands) before the character can be deleted.
        if (_clans is not null && await _clans.IsMemberAsync(found.Id, ct))
        {
            return DeleteCharacterOutcome.Denied("the character is in a clan");
        }

        // A character in play (by this session or another of the account) is claimed: it cannot be deleted under its saves.
        if (!_sessions.TryClaimCharacter(found.Id, sessionToken))
        {
            return DeleteCharacterOutcome.Denied("the character is in play");
        }

        try
        {
            return await _characters.DeleteByNameAsync(session.AccountId, trimmed, ct)
                ? DeleteCharacterOutcome.Success()
                : DeleteCharacterOutcome.Denied("character not found");
        }
        finally
        {
            _sessions.ReleaseCharacter(found.Id, sessionToken);
        }
    }

    private PlayerSession Authenticate(string token)
    {
        if (!_sessions.TryGet(token, out var session))
            throw new InvalidSessionException();
        return session;
    }

    // A character whose saved equipment cannot be read is listed without its looks, so the rest of the account still logs
    // in; entering it is refused as an unsupported saved state.
    private CharacterSummary BuildSummary(CharacterRecord character)
    {
        var (armorKey, weaponKey, offHandKey) = (string.Empty, string.Empty, string.Empty);
        try
        {
            (armorKey, weaponKey, offHandKey) = VisualKeys(character);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
        }

        var inVillage = _world.TryGetFieldByCode(character.FieldId, out var field) && field.State == FieldState.Village;
        return new CharacterSummary(
            character.Name,
            (PlayerClass)character.JobCode,
            character.Level,
            character.FaceIndex,
            character.Tier,
            character.HairIndex,
            armorKey,
            weaponKey,
            offHandKey,
            inVillage);
    }

    private (string Armor, string Weapon, string OffHand) VisualKeys(CharacterRecord character)
    {
        var equipment = CharacterStateCodec.ReadEquipment(character.Equipment);
        var occupiedSlots = new HashSet<EquipmentSlot>();
        var armorKey = string.Empty;
        var weaponKey = string.Empty;
        var offHandKey = string.Empty;

        foreach (var saved in equipment.Items)
        {
            // The stored weapon pair shares the hand slots with the held one and is not shown.
            if (saved is { Hidden: true })
            {
                continue;
            }

            if (saved is null || !saved.Slot.HasValue || saved.Anchor != -1 || !occupiedSlots.Add(saved.Slot.Value))
            {
                throw new InvalidDataException($"Character '{character.Name}' has invalid persisted equipment placement.");
            }

            if (!_worldData.Current.Items.TryGet(saved.Code, out var definition))
            {
                throw new InvalidDataException($"Character '{character.Name}' references unknown equipped item 0x{saved.Code:X8}.");
            }

            if (!ItemEquipmentMetadata.Fits(definition.Kind, definition.EquipSlot, saved.Slot.Value))
            {
                throw new InvalidDataException($"Character '{character.Name}' has an item persisted in the wrong equipment slot.");
            }

            AssignVisualKey(saved.Slot.Value, definition, ref armorKey, ref weaponKey, ref offHandKey);
        }

        return (armorKey, weaponKey, offHandKey);
    }

    private static void AssignVisualKey(
        EquipmentSlot slot,
        ItemDefinition definition,
        ref string armorKey,
        ref string weaponKey,
        ref string offHandKey)
    {
        if (slot is EquipmentSlot.Armor or EquipmentSlot.Weapon or EquipmentSlot.OffHand &&
            string.IsNullOrWhiteSpace(definition.CatalogKey))
        {
            throw new InvalidDataException($"Equipped item 0x{definition.Code:X8} has no visual catalog key.");
        }

        switch (slot)
        {
            case EquipmentSlot.Armor:
                armorKey = definition.CatalogKey;
                break;
            case EquipmentSlot.Weapon:
                weaponKey = definition.CatalogKey;
                break;
            case EquipmentSlot.OffHand:
                offHandKey = definition.CatalogKey;
                break;
        }
    }
}
