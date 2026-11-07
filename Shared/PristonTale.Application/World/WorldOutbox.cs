using System.Threading.Channels;

namespace PristonTale.Application.World;

public sealed class WorldOutbox : Channel<WorldSnapshot>
{
    private readonly object _gate = new();
    private readonly Channel<bool> _ready = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite,
        AllowSynchronousContinuations = false
    });
    // The event lists of every snapshot published since the last read, with their ticks.
    private readonly List<EventBatch> _batches = new();
    private SkillCooldownView[]? _cooldowns;
    private readonly QuestUpdateBuffer _quest = new();
    private readonly int _eventCapacity;
    private WorldSnapshot _latest;
    private int _eventCount;
    private bool _eventsLost;
    private bool _hasState;
    private bool _completed;
    // Spreads the frame pins of many outboxes over the frame's counters.
    private readonly uint _pinKey = (uint)Random.Shared.Next();

    public const int DefaultEventCapacity = 4096;

    public WorldOutbox(int eventCapacity = DefaultEventCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _eventCapacity = eventCapacity;
        Reader = new OutboxReader(this);
        Writer = new OutboxWriter(this);
    }

    private bool Publish(WorldSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            var incoming = snapshot.Events.Length + snapshot.SkillEvents.Length + snapshot.StatusEvents.Length + snapshot.ItemEffects.Length;
            if (incoming > _eventCapacity - _eventCount)
            {
                // A reader this far behind loses the events it has not read, never its connection: the entities it shows
                // catch up with the next snapshot, and the encoder sends again the statuses running on them.
                _batches.Clear();
                _eventCount = 0;
                _eventsLost = true;
            }

            _latest = snapshot;
            _hasState = true;
            // Skill, status and item events are as reliable as combat events; the newest cooldown list replaces older ones.
            if (incoming > 0 && incoming <= _eventCapacity)
            {
                _batches.Add(new EventBatch(snapshot.ServerTick, snapshot.Events, snapshot.SkillEvents, snapshot.StatusEvents, snapshot.ItemEffects));
                _eventCount += incoming;
            }

            if (snapshot.Cooldowns is not null)
            {
                _cooldowns = snapshot.Cooldowns;
            }

            // Quest notices are kept in order up to their own bound; the newest journal view and opened dialog replace older ones.
            _quest.Add(snapshot.Quest);
            _ready.Writer.TryWrite(true);
            return true;
        }
    }

    private bool Read(out WorldSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_hasState)
            {
                snapshot = default;
                return false;
            }

            var latest = _latest;
            if (latest.Frame is { } frame)
            {
                // A frame the field already reused (only possible for a connection that left its field long ago) carries
                // no entity state any more.
                latest = frame.TryPin(latest.FrameGeneration, _pinKey)
                    ? latest.WithPin(_pinKey)
                    : latest.WithoutFrame();
            }

            snapshot = WithBatches(latest) with
            {
                Cooldowns = _cooldowns,
                Quest = _quest.Take(),
                EventsLost = _eventsLost
            };
            _batches.Clear();
            _cooldowns = null;
            _latest = default;
            _eventCount = 0;
            _eventsLost = false;
            _hasState = false;
            _ready.Reader.TryRead(out _);
            return true;
        }
    }

    // A reader that kept up gets the field's own immutable event arrays, shared by every receiver; one that fell behind
    // gets the batches joined in order, with the tick each part belongs to.
    private WorldSnapshot WithBatches(in WorldSnapshot latest)
    {
        if (_batches.Count == 0)
        {
            return latest.WithEvents([]) with { SkillEvents = [], StatusEvents = [], ItemEffects = [] };
        }

        if (_batches.Count == 1)
        {
            var only = _batches[0];
            return latest.WithEvents(only.Combat) with
            {
                SkillEvents = only.Skills,
                StatusEvents = only.Statuses,
                ItemEffects = only.Items,
                EventSegments = only.Tick == latest.ServerTick
                    ? null
                    : [new EventSegment(only.Tick, only.Combat.Length, only.Skills.Length, only.Statuses.Length, only.Items.Length)]
            };
        }

        var segments = new EventSegment[_batches.Count];
        var combat = new List<CombatEventView>();
        var skills = new List<SkillEventView>();
        var statuses = new List<StatusEventView>();
        var items = new List<ItemEffectView>();
        for (var index = 0; index < _batches.Count; index++)
        {
            var batch = _batches[index];
            combat.AddRange(batch.Combat);
            skills.AddRange(batch.Skills);
            statuses.AddRange(batch.Statuses);
            items.AddRange(batch.Items);
            segments[index] = new EventSegment(batch.Tick, batch.Combat.Length, batch.Skills.Length, batch.Statuses.Length, batch.Items.Length);
        }

        return latest.WithEvents(combat.ToArray()) with
        {
            SkillEvents = skills.ToArray(),
            StatusEvents = statuses.ToArray(),
            ItemEffects = items.ToArray(),
            EventSegments = segments
        };
    }

    private readonly record struct EventBatch(uint Tick, CombatEventView[] Combat, SkillEventView[] Skills, StatusEventView[] Statuses, ItemEffectView[] Items);

    private bool Complete(Exception? exception)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            _completed = true;
            return _ready.Writer.TryComplete(exception);
        }
    }

    internal sealed class OutboxWriter(WorldOutbox owner) : ChannelWriter<WorldSnapshot>
    {
        public override bool TryWrite(WorldSnapshot item) => owner.Publish(item);
        public override bool TryComplete(Exception? error = null) => owner.Complete(error);

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(!Volatile.Read(ref owner._completed));
        }
    }

    private sealed class OutboxReader(WorldOutbox owner) : ChannelReader<WorldSnapshot>
    {
        public override Task Completion => owner._ready.Reader.Completion;
        public override bool TryRead(out WorldSnapshot item) => owner.Read(out item);
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            return owner._ready.Reader.WaitToReadAsync(cancellationToken);
        }
    }
}
