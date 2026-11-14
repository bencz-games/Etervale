using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World;

internal sealed class LoadedField
{
    public LoadedField(
        FieldDef definition,
        PristonStage stage,
        PristonCollisionWorld collision,
        RenderStageHandle renderHandle,
        PristonActorInstance[] modernScenery = null)
    {
        Definition = definition;
        Stage = stage;
        Collision = collision;
        RenderHandle = renderHandle;
        ModernScenery = modernScenery ?? [];
    }

    public FieldDef Definition { get; }

    public PristonStage Stage { get; }

    public PristonCollisionWorld Collision { get; }

    public RenderStageHandle RenderHandle { get; }

    public PristonActorInstance[] ModernScenery { get; }
}
