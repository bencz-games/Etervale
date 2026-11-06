using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World;
using PristonTale.Assets;

namespace PristonTale.Gameplay.World.Streaming;

internal sealed class FieldResourceStreamer : IDisposable
{
    private readonly string _assetRoot;
    private readonly PristonSmdReader _reader;
    private readonly PristonRenderer _renderer;
    private readonly IClientLog _log;
    private readonly Action<FieldLighting> _configureLighting;

    public FieldResourceStreamer(
        string assetRoot,
        PristonSmdReader reader,
        PristonRenderer renderer,
        IClientLog log,
        Action<FieldLighting> configureLighting)
    {
        _assetRoot = assetRoot;
        _reader = reader;
        _renderer = renderer;
        _log = log;
        _configureLighting = configureLighting;
    }

    private sealed record PreparedField(
        FieldDef Definition,
        PristonStage Stage,
        PristonModel[] Models,
        PristonCollisionWorld Collision,
        long Started);
    private readonly Dictionary<string, Task<PreparedField>> _fieldLoads = new();
    private readonly Dictionary<string, CancellationTokenSource> _fieldCancellations = new();
    private readonly HashSet<string> _failedFieldLoads = new();
    // The destination a teleport or warp waits for; it prepares and uploads ahead of the neighbours, of which at most one is
    // in flight beside it.
    private string _priorityFieldId;

    public void Request(FieldDef field)
    {
        if (InFlight(field.Id)
            || _failedFieldLoads.Contains(field.Id)
            || NeighbourInFlight()
            || _fieldLoads.Count + (_stageUpload is null ? 0 : 1) >= FieldStreamingPolicy.MaximumPendingFields)
        {
            return;
        }

        Start(field);
    }

    public void Prioritize(FieldDef field)
    {
        if (_failedFieldLoads.Contains(field.Id))
        {
            return;
        }

        _priorityFieldId = field.Id;
        CancelOtherLoads(field.Id);
        if (!InFlight(field.Id))
        {
            Start(field);
        }
    }

    private bool InFlight(string fieldId) => _fieldLoads.ContainsKey(fieldId) || _uploadingField?.Definition.Id == fieldId;

    private bool NeighbourInFlight()
    {
        if (_uploadingField is not null && _uploadingField.Definition.Id != _priorityFieldId)
        {
            return true;
        }

        foreach (var id in _fieldLoads.Keys)
        {
            if (id != _priorityFieldId)
            {
                return true;
            }
        }

        return false;
    }

    private void Start(FieldDef field)
    {
        _log.Info($"[Stream] preparing {field.Id}.");
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var root = _assetRoot;
        var renderer = _renderer;
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _fieldCancellations.Add(field.Id, cancellation);
        _fieldLoads.Add(field.Id, Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var stage = _reader.ReadStage(AssetFiles.Combine(root, field.StageSmd));
            var models = field.StageObjectSmds.Select(path => _reader.ReadModel(AssetFiles.Combine(root, path))).ToArray();
            var collision = new PristonCollisionWorld(stage);
            token.ThrowIfCancellationRequested();
            await renderer.PrepareStageTexturesAsync(stage, models, token);
            token.ThrowIfCancellationRequested();
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _log.Info($"[Stream] CPU ready {field.Id}: {elapsed:F1} ms; {stage.Faces.Count} faces, {models.Length} models.");
            return new PreparedField(field, stage, models, collision, started);
        }));
    }

    private void CancelOtherLoads(string keepFieldId)
    {
        foreach (var id in _fieldLoads.Keys.ToArray())
        {
            if (id != keepFieldId)
            {
                CancelLoad(id);
            }
        }

        if (_stageUpload is not null && _uploadingField.Definition.Id != keepFieldId)
        {
            AbandonUpload();
        }
    }

    private void CancelLoad(string fieldId)
    {
        if (!_fieldLoads.Remove(fieldId, out var preparation))
        {
            return;
        }

        var cancellation = _fieldCancellations[fieldId];
        _fieldCancellations.Remove(fieldId);
        cancellation.Cancel();
        _ = ObservePreparationAsync(preparation, cancellation);
    }

    private PreparedField _uploadingField;
    private IEnumerator<RenderStageHandle> _stageUpload;
    private double _maximumFieldUploadMilliseconds;
    private int _fieldUploadFrames;

    public LoadedField Update()
    {
        // A destination that is no longer preparing or uploading (published, failed or cancelled) holds nothing back.
        if (_priorityFieldId is not null && !InFlight(_priorityFieldId))
        {
            _priorityFieldId = null;
        }

        if (_stageUpload is null)
        {
            string completed = null;
            if (_priorityFieldId is not null)
            {
                if (_fieldLoads.TryGetValue(_priorityFieldId, out var priorityPreparation) && priorityPreparation.IsCompleted)
                {
                    completed = _priorityFieldId;
                }
            }
            else
            {
                foreach (var (id, preparation) in _fieldLoads)
                {
                    if (preparation.IsCompleted)
                    {
                        completed = id;
                        break;
                    }
                }
            }

            if (completed is not null)
            {
                var preparation = _fieldLoads[completed];
                _fieldLoads.Remove(completed);
                var cancellation = _fieldCancellations[completed];
                _fieldCancellations.Remove(completed);
                cancellation.Dispose();
                if (preparation.IsCompletedSuccessfully)
                {
                    _uploadingField = preparation.Result;
                    _configureLighting(_uploadingField.Definition.Lighting);
                    _maximumFieldUploadMilliseconds = 0d;
                    _fieldUploadFrames = 0;
                    _stageUpload = _renderer.AddStageIncrementally(_uploadingField.Stage, _uploadingField.Models);
                }
                else if (!preparation.IsCanceled)
                {
                    _failedFieldLoads.Add(completed);
                    _log.Error($"[Stream] field preparation failed for {completed}: {preparation.Exception}");
                }
            }
        }

        if (_stageUpload is null)
        {
            return null;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _fieldUploadFrames++;
        try
        {
            // A soft budget between indivisible GPU calls, plus a hard cap on steps per frame.
            for (var step = 0; step < FieldStreamingPolicy.MaximumUploadSteps; step++)
            {
                if (!_stageUpload.MoveNext())
                {
                    throw new InvalidOperationException("Stage upload ended without publishing its handle.");
                }

                var handle = _stageUpload.Current;
                if (handle is not null)
                {
                    var loaded = new LoadedField(_uploadingField.Definition, _uploadingField.Stage, _uploadingField.Collision, handle);
                    _maximumFieldUploadMilliseconds = Math.Max(
                        _maximumFieldUploadMilliseconds,
                        System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_uploadingField.Started).TotalMilliseconds;
                    _log.Info($"[Stream] resident {_uploadingField.Definition.Id}: {elapsed:F1} ms total; " +
                        $"{_fieldUploadFrames} upload frames; max Update {_maximumFieldUploadMilliseconds:F2} ms.");
                    _stageUpload.Dispose();
                    _stageUpload = null;
                    _uploadingField = null;
                    return loaded;
                }

                if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= FieldStreamingPolicy.UploadBudgetMilliseconds)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            var failedId = _uploadingField.Definition.Id;
            _failedFieldLoads.Add(failedId);
            _log.Error($"[Stream] GPU publication failed for {failedId}: {ex}");
            _stageUpload.Dispose();
            _stageUpload = null;
            _uploadingField = null;
        }
        finally
        {
            _maximumFieldUploadMilliseconds = Math.Max(
                _maximumFieldUploadMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return null;
    }

    private void AbandonUpload()
    {
        _stageUpload.Dispose();
        _stageUpload = null;
        _uploadingField = null;
    }

    private async Task ObservePreparationAsync(Task preparation, CancellationTokenSource cancellation)
    {
        try
        {
            await preparation;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"[Stream] abandoned preparation failed: {ex}");
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        _stageUpload?.Dispose();
        _stageUpload = null;
        _uploadingField = null;
        foreach (var fieldId in _fieldLoads.Keys.ToArray())
        {
            CancelLoad(fieldId);
        }

        _fieldLoads.Clear();
    }
}
