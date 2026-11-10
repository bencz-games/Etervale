using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.Effects;

// What other players and the user see when an item is used, with the converted particle scripts and item effects:
// firecrackers fire four rockets half a second apart, each bursting 150 units up into a scripted shape chosen from the
// use's roll; hair tints, force orbs, ether cores, crystal summons and head changes play the skill-learn sound (their
// light is ItemVfx.Learn or the return sparkle), skill masters, boosters and puzzle reveals the drink sound, an opened
// gift box the eat sound under an orange light, and a potion taking effect its drops with the eat sound, as the original
// plays at the eat motion's event for every drinker in view.
public sealed class ItemUseEffects
{
    public const string LearnSound = "wav/Effects/Skill/Learn_Skill.wav";
    public const string DrinkSound = "Image/SinImage/Sound/drink2.wav";
    public const string EatSound = "Image/SinImage/Sound/drink1.wav";
    // An aged copy reaching its level plays the level-up sound at 16000 of its 22050 Hz.
    public const string AgingUpSound = "wav/Effects/event/level UP.wav";
    private static readonly float AgingUpPitch = MathF.Log2(1600f / 2205f);
    public static readonly string[] FirecrackerSounds = ["wav/Effects/event/firecracker01.wav", "wav/Effects/event/firecracker02.wav",
        "wav/Effects/event/firecracker03.wav"];
    private static readonly string[] Crackers = ["FireCrackerAll", "FireCrackerHeart", "FireCrackerMoon", "FireCrackerStar", "FireCrackerNormal"];
    private static readonly string[] Bombs = ["FireCrackerBombHeart", "FireCrackerBombMoon", "FireCrackerBombStar"];
    private const float BurstHeight = 100f;

    private readonly SkillPresentation _skills;
    private readonly PristonTransientLights _lights;
    private readonly SkillSoundBank _sounds;
    private readonly List<Burst> _bursts = new();
    private readonly Random _random = new();

    public ItemUseEffects(SkillPresentation skills, PristonTransientLights lights, SkillSoundBank sounds)
    {
        _skills = skills;
        _lights = lights;
        _sounds = sounds;
        sounds?.Preload(FirecrackerSounds.Append(LearnSound).Append(DrinkSound).Append(EatSound).Append(AgingUpSound));
    }

    public int PendingBursts => _bursts.Count;

    // The original firecracker table: a roll of 0..16382 names four bursts, one per byte, read high byte first.
    public static int[] FirecrackerBursts(int roll) => [roll / 8000, roll / 400 % 20, roll / 20 % 20, roll % 20];

    // A burst of 0..19 picks the shape (burst / 4) and the bomb (burst % 4, 3 for none).
    public static (string Cracker, string Bomb) Shapes(int burst) =>
        burst is >= 0 and < 20 ? (Crackers[burst / 4], burst % 4 < 3 ? Bombs[burst % 4] : null) : (null, null);

    public void Play(ItemEffectKind effect, ulong entity, int variant, Vector3 position, Vector3 listener, double nowMs)
    {
        switch (effect)
        {
            case ItemEffectKind.Firecracker:
                var bursts = FirecrackerBursts(variant);
                for (var index = 0; index < bursts.Length; index++)
                {
                    _bursts.Add(new Burst(entity, nowMs + 500d * index, bursts[index]));
                }

                break;
            case ItemEffectKind.Learn:
            case ItemEffectKind.ForceOrb:
                _sounds?.Play(LearnSound, position, listener);
                break;
            case ItemEffectKind.Mastery:
            case ItemEffectKind.Drink:
                _sounds?.Play(DrinkSound, position, listener);
                break;
            case ItemEffectKind.Return:
            case ItemEffectKind.HeadChange:
                _sounds?.Play(LearnSound, position, listener);
                break;
            case ItemEffectKind.AgingUp:
                _sounds?.Play(AgingUpSound, position, listener, 1f, AgingUpPitch);
                break;
            case ItemEffectKind.GiftBox:
                // The eat event's orange light at chest height.
                _lights?.Add(position + Vector3.Up * 48f, new Color(100, 70, 0), 250, 1);
                _sounds?.Play(EatSound, position, listener);
                break;
        }
    }

    // A potion taking effect at chest height: the drops of each resource it restores (bit 0 life, 1 mana, 2 stamina) and the
    // potion sound once.
    public void Potion(int resources, Vector3 chest, Vector3 listener)
    {
        for (var resource = 0; resource < 3; resource++)
        {
            if ((resources & (1 << resource)) != 0)
            {
                _skills?.SpawnAt(ItemVfx.Potion(resource), chest, Matrix.Identity);
            }
        }

        _sounds?.Play(EatSound, chest, listener);
    }

    // Fires the bursts that came due; position resolves the entity's drawn position (null when it left the view).
    public void Update(double nowMs, Func<ulong, Vector3?> position, Vector3 listener)
    {
        for (var index = _bursts.Count - 1; index >= 0; index--)
        {
            var burst = _bursts[index];
            if (burst.DueMs > nowMs)
            {
                continue;
            }

            _bursts.RemoveAt(index);
            if (position(burst.Entity) is not { } feet)
            {
                continue;
            }

            var start = feet + new Vector3(_random.Next(-20, 21), _random.Next(-5, 6) + 50, _random.Next(-20, 21));
            var top = start + Vector3.Up * BurstHeight;
            Spawn(ItemVfx.FirecrackerRocket, start);
            Spawn(ItemVfx.FirecrackerFlash, top);
            var (cracker, bomb) = Shapes(burst.Shape);
            if (cracker is not null)
            {
                Spawn("particle." + cracker, top);
            }

            if (bomb is not null)
            {
                Spawn("particle." + bomb, top);
            }

            _sounds?.Play(FirecrackerSounds[_random.Next(FirecrackerSounds.Length)], top, listener);
        }
    }

    private void Spawn(string effect, Vector3 position) => _skills?.SpawnAt(effect, position, Matrix.Identity);

    private readonly record struct Burst(ulong Entity, double DueMs, int Shape);
}
