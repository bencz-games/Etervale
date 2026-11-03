using System.Collections.Concurrent;

namespace PristonTale.Client.Net.WorldCatalog;

// Filled by the network thread from entity descriptions and read by the game thread; a template is registered
// before any spawn that references it is published.
public sealed class WorldRenderCatalog
{
    public static readonly WorldRenderCatalog Empty = new(new Dictionary<uint, MonsterRenderInfo>());

    private readonly ConcurrentDictionary<uint, MonsterRenderInfo> _monsters;
    private readonly ConcurrentDictionary<uint, NpcRenderInfo> _npcs;

    public WorldRenderCatalog()
        : this(new Dictionary<uint, MonsterRenderInfo>())
    {
    }

    public WorldRenderCatalog(IReadOnlyDictionary<uint, MonsterRenderInfo> monsters, IReadOnlyDictionary<uint, NpcRenderInfo>? npcs = null)
    {
        _monsters = new ConcurrentDictionary<uint, MonsterRenderInfo>(monsters);
        _npcs = new ConcurrentDictionary<uint, NpcRenderInfo>(npcs ?? new Dictionary<uint, NpcRenderInfo>());
    }

    public IReadOnlyDictionary<uint, MonsterRenderInfo> Monsters => _monsters;
    public IReadOnlyDictionary<uint, NpcRenderInfo> Npcs => _npcs;
    public NpcRenderInfo? GetNpc(uint templateId) => _npcs.GetValueOrDefault(templateId);

    public MonsterRenderInfo? GetMonster(uint templateId) =>
        _monsters.TryGetValue(templateId, out var info) ? info : null;

    public void Register(MonsterRenderInfo monster) => _monsters[monster.TemplateId] = monster;

    public void Register(NpcRenderInfo npc) => _npcs[npc.TemplateId] = npc;
}
