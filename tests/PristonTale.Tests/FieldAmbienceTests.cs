using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

// The effects the field raises around the local player: dust from running and landing, fireflies on a forest night.
internal static class FieldAmbienceTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("running on a ruin or desert field without rain puffs dust every eighth tick on alternate sides, Bellatra aside",
            DustAsync);
        yield return ("a forest night brings a swarm of fireflies every 512 ticks one time in two, never by day or elsewhere", FirefliesAsync);
    }

    private static FieldDef Field(FieldState state, int code = 1) =>
        new("test", "Test", state, FieldBackdrops.Desert, "", []) { FieldCode = code };

    private static Task DustAsync()
    {
        Assert.True(FieldDust.Kicks(Field(FieldState.Desert), 0f) && FieldDust.Kicks(Field(FieldState.Ruin), 0f), "a desert or ruin raised no dust");
        Assert.True(!FieldDust.Kicks(Field(FieldState.Desert), 0.5f) && !FieldDust.Kicks(Field(FieldState.Forest), 0f)
            && !FieldDust.Kicks(Field(FieldState.Ruin, FieldCodes.Bellatra), 0f), "rain, a forest or Bellatra raised dust");
        var puffs = Enumerable.Range(1, 32).Where(tick => FieldDust.Puffs(tick, out _)).ToArray();
        FieldDust.Puffs(8, out var first);
        FieldDust.Puffs(16, out var second);
        Assert.True(puffs.SequenceEqual([8, 16, 24, 32]) && first == -second, $"the puffs differ: {string.Join(",", puffs)}");

        var names = AuthoredEffects.Names("Common");
        Assert.True(names.Contains(FieldDust.Running) && names.Contains(FieldDust.Landing) && names.Contains(Fireflies.Effect),
            "a field effect is not defined");
        return Task.CompletedTask;
    }

    private static Task FirefliesAsync()
    {
        var forest = Field(FieldState.Forest);
        Assert.True(Fireflies.Shown(forest, 2f) && Fireflies.Shown(forest, 23.5f) && !Fireflies.Shown(forest, 4f) && !Fireflies.Shown(forest, 22.9f)
            && !Fireflies.Shown(Field(FieldState.Desert), 1f), "the forest night hours differ");

        var fireflies = new Fireflies(new Random(7));
        var swarms = 0;
        var periods = 0;
        for (var frame = 0; frame < 70 * 600; frame++)
        {
            var due = fireflies.Update(1f / 70f, forest, 1f);
            swarms += due ? 1 : 0;
            periods += frame % 512 == 511 ? 1 : 0;
        }

        Assert.True(swarms > periods / 4 && swarms < periods * 3 / 4, $"{swarms} swarms over {periods} periods");
        var day = new Fireflies(new Random(7));
        Assert.True(Enumerable.Range(0, 70 * 60).All(_ => !day.Update(1f / 70f, forest, 12f)), "fireflies came by day");
        return Task.CompletedTask;
    }
}
