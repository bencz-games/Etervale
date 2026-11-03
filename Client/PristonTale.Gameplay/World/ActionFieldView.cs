namespace PristonTale.Gameplay.World;

// An action field is seen side-on: an actor facing anywhere toward +x is drawn facing +x, any other way -x.
public static class ActionFieldView
{
    public static float Facing(float renderYaw) => MathF.Sin(renderYaw) >= 0f ? MathF.PI / 2f : -MathF.PI / 2f;
}
