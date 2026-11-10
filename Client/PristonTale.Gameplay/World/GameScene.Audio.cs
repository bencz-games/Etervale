using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Audio;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Gameplay.World;

// Character, weapon and footstep sounds: monsters on their animation's cues, players' swings from the server's combat
// events (the hit, critical or miss of the attacker's weapon), players' damage and death by class, and the local
// player's steps on the floor of its field.
public sealed partial class GameScene
{
    private CombatSounds _combatSounds;
    private MonsterSoundCues _monsterSounds;
    private int _footsteps;
    private ClipEventCursor _stepClip;

    private void CreateCombatSounds()
    {
        if (_combatSounds is not null || _assetRoot is null)
        {
            return;
        }

        _combatSounds = new CombatSounds(_assetRoot, _log);
        _monsterSounds = new MonsterSoundCues(_combatSounds, this);
        _monsterSounds.WarpStarted += OnMonsterWarp;
    }

    private void UpdateCombatSounds()
    {
        if (_combatSounds is null)
        {
            return;
        }

        _combatSounds.Enabled = _audio?.SoundEnabled ?? true;
        _combatSounds.Update();
        if (_remote is not null && _playerBody is not null)
        {
            _monsterSounds.Update(_monsterViews, _playerBody.Position);
        }

        StepFootsteps();
    }

    // The local player's walk and run clips step on their first two event frames.
    private void StepFootsteps()
    {
        if (!_playerBodyActor.IsValid || !((Effects.IMonsterScene)this).TryGetActorMotion(_playerBodyActor, out var motion, out var ticks)
            || motion is null)
        {
            return;
        }

        _stepClip.Advance(motion, ticks);
        if ((MotionCode)motion.State is not (MotionCode.Walk or MotionCode.Run))
        {
            return;
        }

        for (var index = 0; index < 2; index++)
        {
            if (_stepClip.Passed(index))
            {
                PlayFootstep();
                LeaveStep(index);
            }
        }
    }

    private void ObserveCombatSounds(PresenceUpdate update)
    {
        if (_combatSounds is null || _playerBody is null || _remote is null)
        {
            return;
        }

        var listener = _playerBody.Position;
        foreach (var combatEvent in update.Events)
        {
            switch (combatEvent.Kind)
            {
                case CombatEventKind.Hit or CombatEventKind.Miss:
                    if (SwingOf(combatEvent.AttackerId) is { } swing)
                    {
                        var outcome = combatEvent.Kind == CombatEventKind.Miss ? WeaponSoundOutcome.Miss
                            : combatEvent.Critical ? WeaponSoundOutcome.Critical : WeaponSoundOutcome.Hit;
                        _combatSounds.PlayWeapon(swing.WeaponKey, swing.Class, outcome, swing.Position, listener);
                    }

                    break;
                case CombatEventKind.Revive:
                    var risen = combatEvent.TargetId == _remote.SelfEntityId ? listener
                        : _remote.TryGetDrawnPosition(combatEvent.TargetId, out var drawn) ? drawn : (Vector3?)null;
                    if (risen is { } at)
                    {
                        _combatSounds.PlayRestart(at, listener);
                    }

                    break;
            }
        }
    }

    // The weapon, class and position of a player that swung: the local player from its own equipment, others from their
    // appearance; null for monsters.
    private (string WeaponKey, PlayerClass Class, Vector3 Position)? SwingOf(ulong attackerId)
    {
        if (attackerId == _remote.SelfEntityId)
        {
            var weapon = "";
            foreach (var item in Owner?.Items ?? [])
            {
                if ((EquipmentSlot)item.Slot == EquipmentSlot.Weapon)
                {
                    weapon = item.CatalogKey;
                }
            }

            return (weapon, _player.Class.Class, _playerBody.Position);
        }

        return _remote.TryGetPlayerAudio(attackerId, out var playerClass, out var weaponKey, out var position)
            ? (weaponKey, playerClass, position)
            : null;
    }

    // A player entering damage or death plays its class's sound where it stands.
    private void PlayPlayerMotionSound(PlayerClass playerClass, MotionCode motion, Vector3 position)
    {
        if (_combatSounds is null || _playerBody is null)
        {
            return;
        }

        var cue = motion switch
        {
            MotionCode.Damage => CharacterSoundCue.Damage,
            MotionCode.Dead => CharacterSoundCue.Dead,
            _ => (CharacterSoundCue?)null
        };
        if (cue is { } sound)
        {
            _combatSounds.PlayCharacter((int)playerClass, sound, position, _playerBody.Position);
        }
    }

    private void OnRemotePlayerMotionStarted(PlayerClass playerClass, MotionCode motion, Vector3 position) =>
        PlayPlayerMotionSound(playerClass, motion, position);

    // The local player's step on the floor of its field: stone in towns and ruins, dungeon stone, soft ground in forests
    // and deserts; other fields are silent, as in the original.
    private void PlayFootstep()
    {
        if (_combatSounds is null)
        {
            return;
        }

        _footsteps++;
        var floor = _activeField?.Definition.State switch
        {
            FieldState.Village or FieldState.Ruin => FootstepFloor.Stone,
            FieldState.Forest or FieldState.Desert => FootstepFloor.Soft,
            FieldState.Dungeon => FootstepFloor.Dungeon,
            _ => (FootstepFloor?)null
        };
        if (floor is { } ground)
        {
            _combatSounds.PlayFootstep(ground, _footsteps);
        }
    }
}
