namespace CultistOfCthulhu.Core;

/// <summary>
/// docs/05 §8, docs/02 §2 and docs/07 §2 — what the floor index does to a run.
///
/// THE PROBLEM THIS FIXES. Three things were specified, believed present, and absent, and
/// all three were invisible because only one floor of content exists:
///
///   - Attack tokens never scaled. <see cref="Enemies.EnemyManager.AttackTokens"/> defaulted
///     to 4, CombatArena set it to 4 again, and FloorRunner never set it at all — so the knob
///     docs/05 §8 calls "the single most important knob for making a room fair" was frozen at
///     its floor-1 value for the whole descent.
///   - Damage never scaled. The bullet-hit site was a hardcoded 0.5f, so floors 3–6 were
///     half as lethal as docs/02 §2 specifies, and a boss hit the same as a rat.
///   - Room counts never varied. Chain expansion rolled per node with no reference to the
///     floor, which happened to produce a mean of 14.8 rooms on a floor docs/07 §2 wants to
///     be 11–14 — floor 1 was oversized AND floor 4 undersized by the same code.
///
/// A frozen difficulty knob does not fail a gate. It reads as "floor 6 is a bit easy", which
/// is a tuning complaint, and the tuning it invites is of the wrong lever entirely — that is
/// the specific cost of leaving these until floors 2–6 exist.
///
/// Pure functions of a single int. No state, so nothing can go stale.
/// </summary>
public static class FloorScaling
{
    /// <summary>The deepest authored floor. Not RunState.FinalFloor, which tracks how much
    /// content is BUILT (1 today) — this is what the scaling curve interpolates against, and
    /// it must not move when a floor is added.</summary>
    public const int DeepestFloor = 6;

    /// <summary>
    /// Concurrent attackers allowed in a room: 4 on floor 1 rising to 9 on floor 6
    /// (docs/05 §8). Linear, because the doc gives the two endpoints and nothing in between.
    ///
    /// Clamped at both ends. Floor 0 exists in tests, and a floor index past the deepest
    /// authored floor must not keep growing — 20 tokens is not a hard floor, it is a wall.
    /// </summary>
    public static int AttackTokens(int floor)
    {
        int f = Godot.Mathf.Clamp(floor, 1, DeepestFloor);
        int span = Tune.AttackTokensFinalFloor - Tune.AttackTokensFirstFloor;
        return Tune.AttackTokensFirstFloor + span * (f - 1) / (DeepestFloor - 1);
    }

    /// <summary>
    /// What one incoming hit is multiplied by: ×1 on floors 1–2, ×2 on floors 3+ (docs/02 §2).
    ///
    /// A MULTIPLIER, not a damage value, and that is deliberate. Contact damage is authored
    /// per enemy (the Chanter's is 0.0 — it has no body attack at all), so replacing the
    /// number would either resurrect the Chanter as a melee threat or require every enemy to
    /// be re-authored in units of "standard hits". Multiplying preserves the authored zero
    /// and, because the factor is exactly 2, preserves half-heart granularity as well.
    /// </summary>
    public static float DamageMultiplier(int floor) =>
        floor >= Tune.FloorFullHeartDamage ? 2f : 1f;

    /// <summary>
    /// The same multiplier for a boss in a given phase. docs/02 §2 gives the boss a full
    /// heart from phase 2 on EVERY floor, so on floors 1–2 the phase rule is the binding one
    /// and deeper down the floor rule has already got there.
    /// </summary>
    public static float BossDamageMultiplier(int floor, int phase) =>
        Godot.Mathf.Max(DamageMultiplier(floor),
                        phase >= Tune.BossFullHeartPhase ? 2f : 1f);

    /// <summary>
    /// Rooms per floor from docs/07 §2's table, inclusive. False for floor 5, the Plateau of
    /// Leng, which docs/07 §2 lists as "open" — it is a different generator, not a wider
    /// band, and returning an invented range here would let it pass a gate it never met.
    /// </summary>
    public static bool TryRoomCount(int floor, out int min, out int max)
    {
        (min, max) = floor switch
        {
            1 => (11, 14),   // Arkham Undercroft
            2 => (13, 16),   // The Drowned Wharfs
            3 => (14, 17),   // Restricted Archives
            4 => (14, 18),   // Mountains of Madness
            6 => (12, 15),   // R'lyeh — shorter than 4, and deliberately: docs/07 §3 spends
                             // the time budget on the Cthulhu fight, not on the approach.
            _ => (0, 0),
        };
        return max > 0;
    }

    /// <summary>One line for the run summary and the F3 overlay.</summary>
    public static string Describe(int floor) =>
        $"floor {floor}: {AttackTokens(floor)} tokens, " +
        $"hits ×{DamageMultiplier(floor):0.#}" +
        (TryRoomCount(floor, out int lo, out int hi) ? $", {lo}–{hi} rooms" : ", open");
}
