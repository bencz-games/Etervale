using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.UI.Bellatra;

namespace PristonTale.Gameplay.World;

// Bellatra's show on the scene while the player is in the arena: its sounds, the arena's music and the room seals' quake,
// which shakes the camera (the original's 50, every 3 + 1 frames) with the Diastrophism skill's impact sound. A step of
// the show due as the player leaves plays nothing elsewhere.
public sealed partial class GameScene : IBellatraStage
{
    private const int SealShake = 50;
    private const int SealShakeDelay = 3;
    private const uint DiastrophismSkill = 0x08040002;

    private bool InBellatraArena => _activeField?.Definition.FieldCode == Simulation.World.Bellatra.BellatraArena.Field;

    void IBellatraStage.Play(EventCue cue)
    {
        if (InBellatraArena)
        {
            _fieldAudio?.PlayEvent(cue);
        }
    }

    void IBellatraStage.Music(string code)
    {
        if (InBellatraArena)
        {
            _fieldAudio?.SetEventMusic(code);
        }
    }

    void IBellatraStage.Quake()
    {
        if (!InBellatraArena)
        {
            return;
        }

        _camera?.Wave(SealShake, SealShakeDelay);
        if (Simulation.Skills.SkillRuleCatalog.TryGet(DiastrophismSkill, out var rule) && rule.ImpactSounds is [var sound, ..])
        {
            _skillSounds?.Play(sound, PlayerPosition, PlayerPosition);
        }
    }
}
