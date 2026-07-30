using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// docs/07 §3 floor 2 — the water level of the Drowned Wharfs, oscillating on a 20s cycle
/// across the whole floor.
///
/// THE DESIGN CONSTRAINT THAT SHAPES THIS: "**The tide is synchronised across the floor**, so
/// it is predictable and can be planned around — it's a rhythm layer over the combat rhythm,
/// not a random hazard." Every word of that is load-bearing:
///
///   - SYNCHRONISED means one cycle for the floor, owned by the floor, not per room and
///     certainly not per water tile. A player who learns the rhythm in the first room must
///     still be right about it in the tenth.
///   - PREDICTABLE means no RNG anywhere in here, and it means the phase must survive a room
///     transition. A tide that resets when you walk through a door is a random hazard wearing
///     a rhythm's clothes.
///   - A RHYTHM LAYER means the player needs to know what is coming, not just what is here,
///     so <see cref="SecondsUntilTurn"/> exists for the HUD before anything asks for it.
///
/// Driven by accumulated delta rather than any wall clock, so it is deterministic and the
/// determinism gate can replay it.
/// </summary>
public sealed class TideCycle
{
    private float _elapsed;

    /// <summary>Seconds for a full low → high → low cycle (docs/07 §3).</summary>
    public float Period { get; set; } = Tune.TidePeriod;

    /// <summary>Where in the cycle we are, 0..1. Exposed for the HUD and for save/load —
    /// restoring this is what makes the tide survive a floor transition.</summary>
    public float Phase
    {
        get => Period <= 0f ? 0f : _elapsed / Period;
        set => _elapsed = Mathf.PosMod(value, 1f) * Period;
    }

    /// <summary>
    /// Water level, 0 (fully out) to 1 (fully in).
    ///
    /// A raised cosine, not a triangle wave. The dwell at the extremes is the point: a linear
    /// ramp spends almost no time at high tide, so "wait for the water to drop and then cross"
    /// becomes a frame-perfect read rather than a decision. The cosine gives roughly a third
    /// of the cycle recognisably high and a third recognisably low, with fast transitions
    /// between — which is the shape a player can actually plan against.
    /// </summary>
    public float Level => 0.5f - 0.5f * Mathf.Cos(Phase * Mathf.Tau);

    /// <summary>True while the water is coming in. The tide line moves outward.</summary>
    public bool Rising => Phase < 0.5f;

    /// <summary>Seconds until the tide turns — reaches full high, or full low. What a HUD
    /// readout needs: not "how wet is it" but "how long have I got".</summary>
    public float SecondsUntilTurn
    {
        get
        {
            float next = Rising ? 0.5f : 1f;
            return (next - Phase) * Period;
        }
    }

    public void Tick(float dt)
    {
        if (Period <= 0f) return;
        _elapsed += dt;
        while (_elapsed >= Period) _elapsed -= Period;
    }

    /// <summary>Start of a cycle: fully out. Called when a floor begins, so the player's
    /// first room always opens on dry ground and the first rise is something they watch
    /// happen rather than something they arrive in the middle of.</summary>
    public void Reset() => _elapsed = 0f;

    /// <summary>One line for the F3 overlay and the run log.</summary>
    public string Describe() =>
        $"tide {Level * 100f:0}% {(Rising ? "rising" : "falling")}, " +
        $"turns in {SecondsUntilTurn:0.0}s";
}
