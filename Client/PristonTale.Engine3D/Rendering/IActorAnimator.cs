using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

// An actor's skeleton posed at a frame of its timeline (ticks): the transforms its batches' palettes index, in actor
// space. rotationEnd is the playing motion's last tick (PristonModelAnimator does not rotate toward a key past it). The
// returned array is the animator's own and changes with the next evaluation.
internal interface IActorAnimator
{
    int ObjectCount { get; }

    Matrix[] Evaluate(float frame, float rotationEnd);
}
