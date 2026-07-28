using Godot;

namespace CultistOfCthulhu.Bullets;

/// <summary>docs/05 §4.1 — the primitive vocabulary every enemy attack is built from.</summary>
public enum PatternPrimitive
{
    /// <summary>n bullets evenly across `spread` degrees, centred on the emitter's facing.</summary>
    Radial,
    /// <summary>A rotating emitter. Rotation persists between volleys.</summary>
    Spiral,
    /// <summary>n bullets toward the player, with optional prediction lead.</summary>
    Aimed,
    /// <summary>A line perpendicular to the firing direction, with gaps.</summary>
    Wall,
    /// <summary>A sweeping arc over time.</summary>
    Arc,
    /// <summary>Spawns on a circle around the target and converges inward.</summary>
    RingIn,
}

/// <summary>docs/03 §5 — four statuses, each with a clear visual and a clear counter.</summary>
public enum Element { None, Fire, Brine, Void, Rot }

/// <summary>
/// A single authored attack pattern (docs/05 §4).
///
/// This is the file designers tune. Per docs/09 §5 the binding rule is that no gameplay
/// number lives in a .cs file — patterns are .tres Resources so a designer can rebalance
/// an enemy's volley without a C# recompile, which in Godot is slow enough to break flow.
///
/// The grammar is deliberately a flat parameter block rather than a composable expression
/// tree. A tree is more expressive on paper, but it needs a custom editor to author and a
/// visitor to evaluate; a flat block is editable in Godot's stock inspector on day one and
/// covers every pattern in docs/05 §3's bestiary. Revisit only if a real enemy needs
/// something this cannot express.
/// </summary>
[GlobalClass]
public partial class PatternData : Resource
{
    [ExportGroup("Shape")]
    [Export] public PatternPrimitive Primitive { get; set; } = PatternPrimitive.Radial;

    /// <summary>Bullets per volley.</summary>
    [Export(PropertyHint.Range, "1,64,1")] public int Count { get; set; } = 8;

    /// <summary>Angular width in degrees. 360 = full circle for Radial.</summary>
    [Export(PropertyHint.Range, "0,360,1")] public float SpreadDegrees { get; set; } = 360f;

    /// <summary>Constant angular offset applied to the whole volley.</summary>
    [Export] public float OffsetDegrees { get; set; }

    /// <summary>Spiral only: degrees per second the emitter rotates.</summary>
    [Export] public float SpiralRateDegPerSec { get; set; } = 55f;

    /// <summary>Spiral only: seconds between rotation reversals. 0 = never reverse.</summary>
    [Export] public float SpiralReverseInterval { get; set; }

    /// <summary>Wall only: how many gaps to leave. The gap is the dodge.</summary>
    [Export(PropertyHint.Range, "0,8,1")] public int WallGaps { get; set; } = 1;

    /// <summary>Wall/RingIn only: width or radius in pixels.</summary>
    [Export] public float Extent { get; set; } = 160f;

    /// <summary>Aimed only: seconds of player-velocity prediction. 0 = fire at current position.</summary>
    [Export] public float AimLead { get; set; }

    [ExportGroup("Volley timing")]
    /// <summary>Repetitions of the whole shape.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int BurstCount { get; set; } = 1;

    /// <summary>Seconds between burst repetitions.</summary>
    [Export] public float BurstInterval { get; set; } = 0.1f;

    /// <summary>
    /// Seconds of wind-up before the first bullet. docs/05 R3 mandates a MINIMUM of 0.35s
    /// of readable telegraph; the validator below enforces it, because a pattern that
    /// fires without warning is a bug, not a difficulty choice.
    /// </summary>
    [Export] public float TelegraphSeconds { get; set; } = 0.4f;

    [ExportGroup("Projectile")]
    [Export] public float Speed { get; set; } = 95f;
    [Export] public float SpeedVariance { get; set; }
    /// <summary>Collision radius in px. Kept smaller than the render size so near-misses
    /// read as near-misses.</summary>
    [Export] public float Radius { get; set; } = 4f;
    [Export] public float RenderSize { get; set; } = 9f;
    [Export] public float Lifetime { get; set; } = 8f;
    [Export] public Element Element { get; set; } = Element.None;
    [Export] public Color Colour { get; set; } = new("7FBF3F");

    [ExportGroup("Behaviour")]
    [Export] public BulletBehaviour Behaviour { get; set; } = BulletBehaviour.Straight;
    [Export] public float BehaviourP0 { get; set; }
    [Export] public float BehaviourP1 { get; set; }

    /// <summary>
    /// Enforces the readability contract in docs/05 §1 at author time rather than at
    /// playtest time. Called by the Pattern Lab and by a CI content check.
    /// Returns null when valid, or the reason it is not.
    /// </summary>
    public string? Validate()
    {
        if (TelegraphSeconds < 0.35f)
            return $"R3 violation: telegraph {TelegraphSeconds:F2}s is below the 0.35s minimum.";

        if (Count < 1) return "Count must be at least 1.";

        if (Primitive == PatternPrimitive.Wall && WallGaps < 1)
            return "R6-adjacent: a Wall with no gaps has no positional solution.";

        // R1: enemy bullets live in the cool half of the palette. A warm bullet reads as
        // the player's and will get walked into.
        //
        // The test is the RED-BLUE GAP, not "is red the largest channel". Bone #E8E1D5 is
        // an explicitly sanctioned enemy colour (docs/10 §1.3) and is very slightly
        // red-dominant simply because it is a warm-tinted white; a naive
        // "R > B" check rejects it. What actually matters for readability is whether the
        // hue reads as warm at a glance, which needs a real separation between channels.
        const float WarmGap = 0.15f;
        if (Colour.R - Colour.B > WarmGap && Colour.R > 0.6f)
            return $"R1 violation: enemy projectiles must not be warm-hued " +
                   $"(R-B gap {Colour.R - Colour.B:F2} exceeds {WarmGap}); that reads as player fire.";

        return null;
    }

    /// <summary>Total bullets one full activation will emit — the input to the on-screen
    /// budget check in docs/05 R7.</summary>
    public int TotalBullets => Count * BurstCount;
}
