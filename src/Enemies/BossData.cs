using CultistOfCthulhu.Bullets;
using Godot;

namespace CultistOfCthulhu.Enemies;

/// <summary>
/// A boss, as data (docs/05 §7). All tuning lives here as a .tres, per docs/09 §5.
///
/// Phases are three flat blocks rather than an array of phase resources. Godot's inspector
/// makes nested resource arrays painful to author and unreadable in a diff — the same
/// argument <see cref="Generation.RoomTemplate"/> makes for storing exits as four int
/// arrays — and three is the number the design actually uses. If a boss ever needs a
/// fourth, that is the moment to pay for the array, not before.
///
/// Each phase names up to three patterns. The boss cycles them, which is what makes a
/// phase readable: a player learns three shapes and their order, and the phase change is
/// legible because the vocabulary changes rather than the numbers.
/// </summary>
[GlobalClass]
public partial class BossData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "unnamed_boss";
    [Export] public string DisplayName { get; set; } = "Unnamed";
    [Export(PropertyHint.MultilineText)] public string CodexText { get; set; } = "";

    [ExportGroup("Body")]
    [Export] public float MaxHealth { get; set; } = 900f;
    [Export] public float BodyRadius { get; set; } = 22f;
    [Export] public float MoveSpeed { get; set; } = 90f;
    [Export] public float ContactDamage { get; set; } = 0.5f;
    [Export] public Color Tint { get; set; } = new("A65A6E");

    [ExportGroup("Phase 1 — the host")]
    /// <summary>Health fraction at which phase 2 begins.</summary>
    [Export(PropertyHint.Range, "0.05,0.95,0.01")] public float Phase2At { get; set; } = 0.62f;
    [Export] public PatternData? Phase1A { get; set; }
    [Export] public PatternData? Phase1B { get; set; }
    [Export] public PatternData? Phase1C { get; set; }
    [Export] public float Phase1Cooldown { get; set; } = 1.5f;
    [Export] public float Phase1Range { get; set; } = 230f;
    [Export] public float Phase1SpeedMultiplier { get; set; } = 1f;

    [ExportGroup("Phase 2 — the inversion")]
    [Export(PropertyHint.Range, "0.05,0.95,0.01")] public float Phase3At { get; set; } = 0.28f;
    [Export] public PatternData? Phase2A { get; set; }
    [Export] public PatternData? Phase2B { get; set; }
    [Export] public PatternData? Phase2C { get; set; }
    [Export] public float Phase2Cooldown { get; set; } = 1.2f;
    [Export] public float Phase2Range { get; set; } = 150f;
    [Export] public float Phase2SpeedMultiplier { get; set; } = 0.8f;
    /// <summary>Adds summoned on entering phase 2, and again every <see cref="AddInterval"/>.</summary>
    [Export] public int AddCount { get; set; } = 2;
    [Export] public float AddInterval { get; set; } = 14f;

    [ExportGroup("Phase 3 — the passenger")]
    [Export] public PatternData? Phase3A { get; set; }
    [Export] public PatternData? Phase3B { get; set; }
    [Export] public PatternData? Phase3C { get; set; }
    [Export] public float Phase3Cooldown { get; set; } = 1.0f;
    [Export] public float Phase3Range { get; set; } = 90f;
    [Export] public float Phase3SpeedMultiplier { get; set; } = 1.55f;

    /// <summary>
    /// The grab (docs/05 §7): it tries to enter YOU, and a connection costs Sanity rather
    /// than health.
    ///
    /// This is the whole reason the fight is the floor's boss. Every other enemy in the
    /// game threatens hearts; the passenger threatens the resource that pays for reloading
    /// and Banish, so being grabbed at 40 Sanity does not hurt — it disarms. Costing health
    /// instead would make phase 3 a reskin of phase 1.
    /// </summary>
    [Export] public float GrabSanityCost { get; set; } = 30f;
    [Export] public float GrabTelegraph { get; set; } = 0.75f;
    [Export] public float GrabLungeSpeed { get; set; } = 420f;
    [Export] public float GrabLungeSeconds { get; set; } = 0.5f;
    [Export] public float GrabCooldown { get; set; } = 4.5f;
    [Export] public float GrabRange { get; set; } = 260f;

    [ExportGroup("Rewards")]
    [Export] public int GoldReward { get; set; } = 80;
    [Export] public int KeyReward { get; set; } = 1;

    /// <summary>Seconds of invulnerable, non-firing transition between phases.</summary>
    [Export] public float PhaseTransitionSeconds { get; set; } = 1.4f;

    public PatternData?[] PatternsFor(int phase) => phase switch
    {
        1 => new[] { Phase1A, Phase1B, Phase1C },
        2 => new[] { Phase2A, Phase2B, Phase2C },
        _ => new[] { Phase3A, Phase3B, Phase3C },
    };

    public float CooldownFor(int phase) => phase switch
    {
        1 => Phase1Cooldown,
        2 => Phase2Cooldown,
        _ => Phase3Cooldown,
    };

    public float RangeFor(int phase) => phase switch
    {
        1 => Phase1Range,
        2 => Phase2Range,
        _ => Phase3Range,
    };

    public float SpeedMultiplierFor(int phase) => phase switch
    {
        1 => Phase1SpeedMultiplier,
        2 => Phase2SpeedMultiplier,
        _ => Phase3SpeedMultiplier,
    };

    public string? Validate()
    {
        if (MaxHealth <= 0f) return $"{Id}: MaxHealth must be positive.";

        // Phases must actually be reachable and in order. A boss whose phase 3 threshold
        // sits above its phase 2 threshold skips a phase silently, and the symptom is
        // "the fight felt short" three playtests later.
        if (Phase3At >= Phase2At)
            return $"{Id}: phase 3 begins at {Phase3At:P0} but phase 2 begins at {Phase2At:P0} — " +
                   "phase 2 would never run.";

        // Every phase needs at least one pattern, or it is a phase in which the boss stands
        // still. That is a softlock in all but name: doors are sealed and it cannot fight.
        for (int phase = 1; phase <= 3; phase++)
        {
            bool any = false;
            foreach (PatternData? p in PatternsFor(phase)) if (p is not null) any = true;
            if (!any) return $"{Id}: phase {phase} has no attack patterns.";
        }

        if (GrabSanityCost <= 0f)
            return $"{Id}: the grab must cost Sanity — that is what distinguishes phase 3 (docs/05 §7).";

        return null;
    }
}
