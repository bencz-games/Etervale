using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Contracts.Units;

namespace PristonTale.Effects.Vfx;

// Effect models of the component layers, drawn as baked frames (30 per second) with their tint and blend.
public sealed partial class VfxRenderer
{

    public int LastModelCount { get; private set; }

    private void DrawModels(VfxSystem system, Matrix view, Matrix projection)
    {
        LastModelCount = 0;
        foreach (var instance in system.Instances)
        {
            DrawPartModels(instance, view, projection);
            foreach (var state in instance.ComponentStates)
            {
                var source = state.Component.Source;
                var model = state.Component.Model;
                if (source.Kind != VfxComponentKind.Model || model is null || !state.Drawn)
                {
                    continue;
                }

                ref readonly var element = ref state.Element;
                var world = ModelFront * instance.Orientation * Matrix.CreateTranslation(element.Position);
                var frame = (int)(element.Frame / GameUnits.FrameTicks) % Math.Max(1, model.FrameCount);
                model.Draw(frame, world, view, projection, Decode(element.Color), ModelBlend(source.Blend));
                LastModelCount++;
            }
        }
    }
}
