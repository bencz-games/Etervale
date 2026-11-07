namespace PristonTale.Effects.Vfx;

// How a part draws.
public enum VfxPartShape
{
    // A camera-facing quad.
    Billboard,
    // A quad flat on the ground plane.
    Ground,
    // A camera-facing strip from where the part started to where it is (a stretched streak).
    Streak,
    // A camera-facing strip behind the part along its velocity, LineSeconds of motion long.
    Line,
    // A camera-facing ribbon through the part's last TrailPoints positions.
    Ribbon,
    // A band swept by a bone: from the bone to BladeLength along its BladeAxis (Y), kept for TrailPoints samples.
    Blade,
    // A camera-facing strip from the part to its goal, or to the part plus BeamTo when it has none.
    Beam,
    // An effect model.
    Model
}

// How a part's opacity changes over its life, on top of its animation.
public enum VfxPartFade
{
    None,
    // From 0 to full over the first 25.5% of the life, then down at the same rate (gone by 51%).
    Rise,
    // Full, then down to 0 over the last 25.5% of the life.
    Fall,
    // FadeIn seconds up from 0 and FadeOut seconds down to 0 at the end of the life.
    InOut
}

// Where a moving part heads.
public enum VfxPartGoal
{
    None,
    Caster,
    Target,
    Point
}

// What a part does when it reaches its goal or its life ends.
public enum VfxPartEnd
{
    // Vanish.
    Vanish,
    // Play the last animation frame (fading from the frame before) while still moving at the last velocity.
    LastFrame,
    // Stop where it arrived and fade out there (over FadeOut seconds, or through its animation's last frame).
    Hold
}

// A group of original-style effect objects: billboards, streaks, ribbons or models, each with its own start delay,
// life, animation, motion and fade. Units: world units (sizes are full extents), seconds, degrees, colours 0..255
// (display encoded); effect space has -Z forward and offsets, directions and orbits turn with the effect's facing when
// Turn is set. Animations and motion advance in the original's 70 Hz ticks.
public sealed record VfxPartDefinition
{
    public int Count { get; init; } = 1;
    public float Delay { get; init; }
    // Added to the delay per part index, and a random extra up to DelayRandom.
    public float DelayStep { get; init; }
    public float DelayRandom { get; init; }
    // Seconds a part lives; 0 lives as long as its animation (a looped animation until the effect stops).
    public float[] Lifetime { get; init; } = [0f, 0f];

    public VfxPartShape Shape { get; init; } = VfxPartShape.Billboard;
    // An original animated billboard (Effect/AnimationData/<name>): images, hold times, opacity, size and angle.
    public string Animation { get; init; } = "";
    public bool Loop { get; init; }
    // Numbered frames "<stem>1.<ext>".. when Frames > 1, FrameTime seconds each (PingPong runs them back and forth).
    public string Texture { get; init; } = "";
    public int Frames { get; init; }
    public float FrameTime { get; init; } = 0.1f;
    public bool PingPong { get; init; }
    public string Model { get; init; } = "";
    public string Skeleton { get; init; } = "";
    // Model clip: plays from ModelStart to ModelFrames (clip frames, 30 per second) at ModelSpeed clip frames per second;
    // a looped clip wraps back to LoopFrom.
    public float ModelFrames { get; init; }
    public float ModelSpeed { get; init; } = 30f;
    public float ModelStart { get; init; }
    public float LoopFrom { get; init; }
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public float[] Color { get; init; } = [255f, 255f, 255f, 255f];
    // Full width and height; an animation with its own sizes starts from the drawn width unless Sized is false.
    public float[] Size { get; init; } = [10f, 10f];
    public float[] Height { get; init; } = [0f, 0f];
    public bool Sized { get; init; } = true;
    public float Growth { get; init; }
    public float[] Rotation { get; init; } = [0f, 0f];
    public float Spin { get; init; }
    // Model orientation in degrees (X pitch, Y yaw, Z roll) and its change per second.
    public float[] Angles { get; init; } = [0f, 0f, 0f];
    public float[] Tumble { get; init; } = [0f, 0f, 0f];
    // Models face their flight when they head somewhere.
    public bool FaceGoal { get; init; }
    public VfxPartFade Fade { get; init; } = VfxPartFade.None;
    public float FadeIn { get; init; }
    public float FadeOut { get; init; }
    // Model scale range, and a scale pulse between Pulse[0] and Pulse[1] over PulsePeriod seconds (triangle wave).
    public float[] Scale { get; init; } = [1f, 1f];
    public float[] Pulse { get; init; } = [1f, 1f];
    public float PulsePeriod { get; init; }
    public float BladeLength { get; init; } = 40f;
    // The bone axis a blade runs along, a unit vector in bone space: the far end is BladeLength along it.
    public float[] BladeAxis { get; init; } = [0f, 1f, 0f];
    // Strip and ribbon half width in world units.
    public float[] Width { get; init; } = [1f, 1f];
    public float LineSeconds { get; init; } = 0.1f;
    public int TrailPoints { get; init; } = 15;
    // Alpha taken off per ribbon point from the head.
    public float TrailFade { get; init; }

    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    // The parts move with the anchor instead of staying where they started.
    public bool Follow { get; init; }
    // Models and blades take the bone's full transform (a shield on the forearm, a weapon's blade); the offset is in
    // bone space.
    public bool BoneWorld { get; init; }
    // Offsets, spreads, directions and orbits turn with the effect's facing.
    public bool Turn { get; init; } = true;
    // Random start offset in a box.
    public float[] JitterMin { get; init; } = [0f, 0f, 0f];
    public float[] JitterMax { get; init; } = [0f, 0f, 0f];
    // Start on a ring: radius range, angle of part i = RingStart + i x RingStep (random when RingStep is 0 and Ring set).
    public float[] Ring { get; init; } = [0f, 0f];
    public float RingStart { get; init; }
    public float RingStep { get; init; }
    public bool RingRandom { get; init; }
    // Start on an ellipsoid of these radii in a random direction (streak bursts).
    public float[] Shell { get; init; } = [0f, 0f, 0f];

    // Outward speed in the ground plane along the ring or a random angle, and upward speed.
    public float[] Radial { get; init; } = [0f, 0f];
    public float[] Rise { get; init; } = [0f, 0f];
    // A velocity drawn in a box (effect space).
    public float[] VelocityMin { get; init; } = [0f, 0f, 0f];
    public float[] VelocityMax { get; init; } = [0f, 0f, 0f];
    // Outward along the shell direction.
    public float[] Burst { get; init; } = [0f, 0f];
    // Vertical acceleration (negative falls).
    public float[] Gravity { get; init; } = [0f, 0f];
    // Degrees per second the part's offset from its origin turns about the vertical, and the change of that rate per
    // second. Spiral turns the velocity with the offset, so the path is the straight one turned about the origin.
    public float[] Swirl { get; init; } = [0f, 0f];
    public float[] SwirlGrowth { get; init; } = [0f, 0f];
    public bool Spiral { get; init; }

    public VfxPartGoal Goal { get; init; } = VfxPartGoal.None;
    public float[] GoalOffset { get; init; } = [0f, 0f, 0f];
    public float Speed { get; init; }
    // Distance at which the part has arrived.
    public float Reach { get; init; } = 4f;
    // Seconds a freely moving part moves before it stops where it is; 0 keeps it moving. A Hold part then exits there,
    // keeping its velocity for a line's length (a potion drop that stops and fades through its animation's last frame).
    public float[] MoveTime { get; init; } = [0f, 0f];
    public float[] BeamTo { get; init; } = [0f, 0f, 0f];
    // Lob: an initial upward speed under Gravity while Speed carries the part across the ground towards the goal; it
    // arrives on coming down to the goal's height.
    public float Lift { get; init; }
    public VfxPartEnd End { get; init; } = VfxPartEnd.Vanish;
    // Effect started where a part ends (an impact).
    public string EndEffect { get; init; } = "";

    // Orbit around the anchor: radius, degrees per second, tilt of the orbit plane about X, rise per second, radius
    // growth per second and a phase per part index. With a goal, the orbit starts at the goal's bearing and at least at
    // its distance (a sweep around the caster through the target).
    public float OrbitRadius { get; init; }
    public float OrbitSpeed { get; init; }
    public float OrbitTilt { get; init; }
    public float OrbitRise { get; init; }
    public float OrbitGrowth { get; init; }
    public float OrbitPhase { get; init; }
    public float OrbitPhaseStep { get; init; }
    // Orbit speed alternates direction by index.
    public bool OrbitAlternate { get; init; }

    // A billboard sliding on the screen (a title closing in from the side): it starts SlideFrom pixels of a 600-pixel-high
    // view to the side (negative left) and closes like VfxScreenSlide.
    public float SlideFrom { get; init; }

    // Effect carried by each part: started at its birth, moved with it and stopped when it ends (a fire on a shot).
    public string CarryEffect { get; init; } = "";
    // Effect started every Every seconds at each live part (dust behind a bomb).
    public string EveryEffect { get; init; } = "";
    public float Every { get; init; }
}
