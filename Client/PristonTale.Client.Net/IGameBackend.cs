using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Client.Net;

public interface IGameBackend : IAsyncDisposable
{
    string? SessionToken { get; }

    // The protocol allows one login per connection: after a successful login or a closed connection, log in again
    // through a new backend.
    bool RequiresNewConnection { get; }

    Task<LoginOutcome> LoginAsync(string account, string password, string language, CancellationToken ct = default);

    Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken ct = default);

    Task<CreateCharacterOutcome> CreateCharacterAsync(string name, PlayerClass playerClass, int faceIndex, CancellationToken ct = default);

    Task<DeleteCharacterOutcome> DeleteCharacterAsync(string name, CancellationToken ct = default);

    Task<EnterWorldOutcome> EnterWorldAsync(string characterName, CancellationToken ct = default);

    IPlayChannel OpenPlayChannel(CancellationToken ct = default);

    // Live catalog: monster and NPC templates appear as their descriptions arrive.
    Task<WorldRenderCatalog> GetWorldCatalogAsync(CancellationToken ct = default);
}
