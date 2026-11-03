using System.Collections.ObjectModel;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

public sealed class LearnedSkills
{
    // The use counter adds proficiency to a skill; it stops at 10000, a fully trained skill.
    public const int MaxUses = 10000;

    private readonly Dictionary<uint, int> _ranks = new();
    private readonly Dictionary<uint, int> _uses = new();
    private readonly IReadOnlyList<Skill> _available;

    public LearnedSkills(IReadOnlyList<Skill> classSkills)
    {
        _available = classSkills;
        Ranks = new ReadOnlyDictionary<uint, int>(_ranks);
    }

    public IReadOnlyDictionary<uint, int> Ranks { get; }
    public int RankOf(uint code) => _ranks.GetValueOrDefault(code);
    public int UsesOf(uint code) => _uses.GetValueOrDefault(code);
    public long Revision { get; private set; }
    public event Action Changed;

    private bool IsAvailable(uint code)
    {
        foreach (var skill in _available)
        {
            if (skill.Code == code && code != 0)
            {
                return true;
            }
        }

        return false;
    }

    internal void Increase(uint code)
    {
        var rank = RankOf(code);
        if (!IsAvailable(code) || rank >= Progression.SkillLearning.MaxRank)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        _ranks[code] = rank + 1;
        Revision++;
        Changed?.Invoke();
    }

    // Proficiency changes no derived stat, so it does not announce a change; it is saved with the ranks.
    public void RecordUses(uint code, int uses)
    {
        if (RankOf(code) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        _uses[code] = Math.Clamp(uses, 0, MaxUses);
    }

    // Forgets every learned skill and its proficiency (a skill reset).
    public void Clear()
    {
        _ranks.Clear();
        _uses.Clear();
        Revision++;
        Changed?.Invoke();
    }

    public void Restore(uint code, int rank, int uses = 0)
    {
        if (!IsAvailable(code) || rank < 1 || rank > SkillLearning.MaxRank || uses < 0 || uses > MaxUses || !_ranks.TryAdd(code, rank))
        {
            throw new ArgumentException("Invalid or duplicate learned skill.");
        }

        if (uses > 0)
        {
            _uses[code] = uses;
        }

        Revision++;
        Changed?.Invoke();
    }
}
