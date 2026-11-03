using PristonTale.Simulation.Physics;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Players closer than the range (squared planar world units) to the speaker. Fields share one coordinate space, so
    // players across a field seam hear each other as they see each other.
    public Task<ulong[]> PlayersNearAsync(ulong speakerId, float rangeSquared, bool includeSpeaker = false)
    {
        return EnqueueAsync(speakerId, () =>
        {
            if (!_connections.TryGetValue(speakerId, out var speaker))
            {
                return Array.Empty<ulong>();
            }

            var origin = speaker.Player.Position;
            var near = new List<ulong>();
            foreach (var connection in _connections.Values)
            {
                if (connection.Player.EntityId == speakerId && !includeSpeaker)
                {
                    continue;
                }

                if (Planar.DistanceSquared(connection.Player.Position, origin) < rangeSquared)
                {
                    near.Add(connection.Player.EntityId);
                }
            }

            return near.ToArray();
        });
    }

    // The name of a monster, NPC or player anywhere in the world.
    public Task<string?> EntityNameAsync(ulong requesterId, ulong entityId)
    {
        return EnqueueAsync(requesterId, () =>
        {
            foreach (var field in _fields.Values)
            {
                if (field.TryGetEntityName(entityId, out var name))
                {
                    return name;
                }
            }

            return (string?)null;
        });
    }

    // The beginner's pet command: shows or hides it; false when the player has none to show.
    public Task<bool> ToggleHelpPetAsync(ulong entityId) =>
        EnqueueAsync(entityId, () => _connections.TryGetValue(entityId, out var connection) && connection.Player.Character.ToggleHelpPet());

    // A yahoo word in the chat (key false) or Space inside its window (key true); false when the player may not cheer now.
    public Task<bool> YahooAsync(ulong entityId, bool key)
    {
        return EnqueueAsync(entityId, () =>
            _connections.TryGetValue(entityId, out var connection) && connection.Player.AskYahoo(key, UnixNowMs));
    }
}
