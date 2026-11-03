using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.Audio;

public sealed class NpcAudioController(PristonAudioLibrary library) : IDisposable
{
    private SoundEffectInstance _talk;

    public void Update()
    {
        if (!library.SoundEnabled)
        {
            StopTalk();
        }
    }

    public void PlayInteraction(NpcInteraction interaction, int soundCode, Vector3 sourcePosition, Vector3 listenerPosition)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        StopTalk();
        if (!library.SoundEnabled || !interaction.Ok)
        {
            return;
        }

        foreach (var line in interaction.Dialog)
        {
            if (!library.TryGetTalkSound(line, out var talk))
            {
                continue;
            }

            _talk = talk.CreateInstance();
            _talk.Volume = 1f;
            _talk.Play();
            return;
        }

        PlayMotion(soundCode, MotionCode.Stand, sourcePosition, listenerPosition, allowStand: true);
    }

    public void PlayMotion(int soundCode, MotionCode motion, Vector3 sourcePosition, Vector3 listenerPosition)
    {
        PlayMotion(soundCode, motion, sourcePosition, listenerPosition, allowStand: false);
    }

    public void StopTalk()
    {
        _talk?.Stop();
        _talk?.Dispose();
        _talk = null;
    }

    internal static float ProximityVolume(Vector3 sourcePosition, Vector3 listenerPosition) => SoundFalloff.Volume(sourcePosition, listenerPosition);

    private void PlayMotion(
        int soundCode,
        MotionCode motion,
        Vector3 sourcePosition,
        Vector3 listenerPosition,
        bool allowStand)
    {
        if (!library.SoundEnabled || soundCode == 0 || motion == MotionCode.Stand && !allowStand)
        {
            return;
        }

        var lookupMotion = motion == MotionCode.Run ? MotionCode.Walk : motion;
        if (!library.TryGetNpcSound(soundCode, lookupMotion, out var variants))
        {
            return;
        }

        var volume = ProximityVolume(sourcePosition, listenerPosition);
        if (volume <= 0f || lookupMotion == MotionCode.Walk && volume < 0.75f)
        {
            return;
        }

        variants[Random.Shared.Next(variants.Length)].Play(volume, 0f, 0f);
    }

    public void Dispose()
    {
        StopTalk();
    }
}
