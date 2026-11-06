using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Progression;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudProgressionController(Func<ProgressionCommand, Task<ProgressionOutcome>> apply, IClientLog log)
{
    private readonly HudRequest<ProgressionOutcome> _request = new(log, "Character progression request failed");
    public bool Pending => _request.Pending;
    public string Message { get; private set; } = "";

    public bool LastOk { get; private set; }
    public int Completions { get; private set; }

    public void Submit(ProgressionCommand command)
    {
        if (Pending || apply is null)
        {
            return;
        }

        Message = ClientText.Get("Progression.Saving");
        _request.Start(() => apply(command),
            _ => new ProgressionOutcome(false, command.ExpectedRevision, GameText.None));
    }

    public void Update()
    {
        if (!_request.TryTake(out var outcome))
        {
            return;
        }

        // A refusal without a line is the client's own: the server was not reached.
        Message = outcome.Ok ? ClientText.Get("Progression.Saved")
            : outcome.Message.IsNone ? ClientText.Get("Progression.ConnectionError") : ClientText.Format(outcome.Message);
        LastOk = outcome.Ok;
        Completions++;
    }
}
