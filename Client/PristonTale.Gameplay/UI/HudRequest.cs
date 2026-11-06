using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

// The one request a HUD controller keeps in flight. A send that throws becomes the controller's failure outcome
// (logged unless it was cancelled), so the controller always takes an outcome back, once, on a frame after it finished.
public static class HudRequest
{
    // What a controller shows when its send failed.
    public static string ConnectionError => ClientText.Get("Request.ConnectionError");
}

public sealed class HudRequest<TOutcome>(IClientLog log, string what)
{
    private Task<TOutcome> _pending;

    public bool Pending => _pending is not null;

    // failed receives whether the send was cancelled.
    public void Start(Func<Task<TOutcome>> send, Func<bool, TOutcome> failed) => _pending = RunAsync(send, failed);

    public bool TryTake(out TOutcome outcome)
    {
        if (_pending is not { IsCompleted: true } done)
        {
            outcome = default;
            return false;
        }

        _pending = null;
        outcome = done.Result;
        return true;
    }

    private async Task<TOutcome> RunAsync(Func<Task<TOutcome>> send, Func<bool, TOutcome> failed)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return failed(true);
        }
        catch (Exception exception)
        {
            log.Error(what, exception);
            return failed(false);
        }
    }
}
