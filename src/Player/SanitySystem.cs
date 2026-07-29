using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Player;

public enum SanityBand
{
    Lucid = 0,
    Unsettled = 1,
    Fraying = 2,
    Unravelled = 3,
    Ascension = 4,
}

/// <summary>
/// Bet 1 (docs/02 §3). Sanity is the stamina bar: it pays for dodging, reloading and
/// Banish, and it is refunded by kills.
///
/// Two things here are the outcome of Fable's review and must not be quietly reverted:
///
///   1. THE LADDER GRANTS NO DAMAGE (docs/02 §3.4). Bands pay out in information,
///      mobility and perception only. A flat damage bonus at low Sanity paid the player
///      for getting hit, which inverted the skill curve.
///
///   2. THE LUCID CEILING (docs/02 §3.3.1). Out-of-combat regen refills only to a ceiling
///      that falls as the floor progresses. Without it the corridor laundered the entire
///      mechanic and Sanity was a per-room allowance, not a descent.
///
/// This is a plain C# class, not a Node — it holds no scene state and needs to be
/// simulable headlessly for the economy tests in docs/09 §9.
/// </summary>
public sealed class SanitySystem
{
    public float Max { get; private set; } = Tune.SanityMax;
    public float Current { get; private set; } = Tune.SanityMax;

    /// <summary>Regen refills only to here. Falls 5 per room cleared, floored at 50.</summary>
    public float LucidCeiling { get; private set; } = Tune.LucidCeilingStart;

    public SanityBand Band { get; private set; } = SanityBand.Lucid;

    /// <summary>Set true while any enemy is alive. Gates regeneration entirely.</summary>
    public bool InCombat;

    private float _timeSinceCombat;
    private float _openEyeCooldown;

    public float Fraction => Max <= 0f ? 0f : Current / Max;
    public bool CanOpenEye => _openEyeCooldown <= 0f && Current >= Tune.OpenEyeCost + Tune.OpenEyeMinSanity;

    // ---------------------------------------------------------------- Mutation

    /// <summary>
    /// Set true while Ascended. Suspends all drain, spend and regen — the bar is not a
    /// resource during the Ascended window, it is the thing that ran out.
    /// </summary>
    public bool Suspended;

    /// <summary>
    /// Latched when Sanity reaches zero from any direction. Consumed exactly once by the
    /// AscensionController.
    /// </summary>
    public bool AscensionPending { get; private set; }

    public bool ConsumeAscensionTrigger()
    {
        if (!AscensionPending) return false;
        AscensionPending = false;
        return true;
    }

    /// <summary>
    /// THE ONLY PLACE Current is written. Everything else routes through here.
    ///
    /// This exists because it did not, and that produced a real bug: Drain() checked for
    /// zero and TrySpend() did not, so being HIT to zero triggered Ascension while
    /// SPENDING to zero silently dropped the player into the Ascension band with nothing
    /// happening — and the next kill quietly refunded them back out. Banish at exactly 45
    /// Sanity hit that path every time.
    ///
    /// Two code paths to the same state, one of them wired: that is a bug class, not a
    /// bug. Funnelling every write through one method removes the class.
    /// </summary>
    private void SetCurrent(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, Max);
        bool wasAlive = Current > 0f;
        Current = clamped;

        if (wasAlive && Current <= 0f) AscensionPending = true;

        RecomputeBand();
    }

    public bool CanAfford(float cost) => Current >= cost;

    /// <summary>Spend if affordable. Returns false and spends nothing otherwise.</summary>
    public bool TrySpend(float cost)
    {
        if (Suspended) return true;      // Ascended: actions are free, nothing is deducted
        if (Current < cost) return false;
        SetCurrent(Current - cost);
        return true;
    }

    /// <summary>
    /// Unconditional drain (taking a hit, Revelations). Returns true if this drove Sanity
    /// to zero. Callers may also poll <see cref="ConsumeAscensionTrigger"/>, which is the
    /// path-independent version and the one the controller actually uses.
    /// </summary>
    public bool Drain(float amount)
    {
        if (Suspended) return false;
        SetCurrent(Current - amount);
        return Current <= 0f;
    }

    // ---------------------------------------------------------------- Gaining

    /// <summary>
    /// Kill refund. Halved below the Fraying boundary (docs/02 §3.5.2) so that a player
    /// who chose the low band is not immediately ejected from it by playing well.
    /// </summary>
    public void GainFromKill(float baseAmount)
    {
        float amount = Current < Tune.BandFraying ? baseAmount * Tune.LowBandKillRefundMult : baseAmount;
        Add(amount, respectCeiling: true);
    }

    /// <summary>Candles, shop purchases, Unbroken Seals. These PIERCE the Lucid Ceiling —
    /// that is what makes them a strategic purchase rather than a top-up (docs/02 §3.3.1).</summary>
    public void GainPiercing(float amount) => Add(amount, respectCeiling: false);

    /// <summary>
    /// The lull trickle (docs/04 §5.2, Deep One's Gill). Respects the ceiling, and is
    /// gated by the caller on several seconds of not spending anything.
    ///
    /// Kept separate from <see cref="GainFromKill"/> because it must NOT carry the low-band
    /// refund treatment, and separate from <see cref="GainPiercing"/> because it must not
    /// pierce the Lucid Ceiling — the candle is the only thing in the game that does
    /// (docs/02 §3.3.1), and a passive trickle that also pierced would quietly become the
    /// strongest effect in the run.
    /// </summary>
    public void GainTrickle(float amount) => Add(amount, respectCeiling: true);

    private void Add(float amount, bool respectCeiling)
    {
        if (Suspended) return;
        float cap = respectCeiling ? Mathf.Min(Max, LucidCeiling) : Max;
        // Never *reduce* Sanity via a gain — a player already above the ceiling (from a
        // candle) does not lose it by killing something.
        if (Current >= cap && respectCeiling) return;
        SetCurrent(Mathf.Min(cap, Current + amount));
    }

    // ---------------------------------------------------------------- Tick

    public void Tick(float dt)
    {
        if (Suspended) return;
        if (_openEyeCooldown > 0f) _openEyeCooldown -= dt;

        if (InCombat)
        {
            _timeSinceCombat = 0f;
            return;   // docs/02 §3.3 — no in-combat regen. There is no waiting it out.
        }

        _timeSinceCombat += dt;
        if (_timeSinceCombat >= Tune.SanityOutOfCombatDelay)
        {
            Add(Tune.SanityOutOfCombatRegen * dt, respectCeiling: true);
        }
    }

    // ---------------------------------------------------------------- Room / floor events

    public void OnRoomCleared()
    {
        LucidCeiling = Mathf.Max(Tune.LucidCeilingFloor, LucidCeiling - Tune.LucidCeilingDecayPerRoom);
        Add(Tune.SanityRoomClear, respectCeiling: true);
    }

    /// <summary>New floor, or the boss foyer. Resets the descent.</summary>
    public void ResetCeiling()
    {
        LucidCeiling = Mathf.Min(Max, Tune.LucidCeilingStart);
    }

    /// <summary>Ascension penalty: −10 max Sanity per Ascension, floored at 40 (docs/02 §6).</summary>
    public void ReduceMax(float amount, float floor = 40f)
    {
        Max = Mathf.Max(floor, Max - amount);
        LucidCeiling = Mathf.Min(LucidCeiling, Max);
        SetCurrent(Mathf.Min(Current, Max));
    }

    public void SetMax(float value)
    {
        Max = value;
        SetCurrent(Mathf.Min(Current, Max));
    }

    /// <summary>Restore to a specific value without triggering zero detection — used by
    /// the Ascension exit, which sets Sanity to 50 by fiat.</summary>
    public void RestoreTo(float value)
    {
        Current = Mathf.Clamp(value, 0f, Max);
        AscensionPending = false;
        RecomputeBand();
    }

    // ---------------------------------------------------------------- Open the Eye

    /// <summary>
    /// The deliberate descent verb (docs/02 §3.5.1). Costs 25 Sanity or enough to cross
    /// the next band boundary, whichever is greater — so it always actually descends.
    /// Unavailable below 20, which closes deliberate-Ascension from a third direction.
    /// </summary>
    public bool TryOpenEye()
    {
        if (!CanOpenEye) return false;

        float boundary = NextBandBoundaryBelow(Current);
        float cost = Mathf.Max(Tune.OpenEyeCost, Current - (boundary - 1f));
        if (Current - cost < Tune.OpenEyeMinSanity) return false;

        Current -= cost;
        _openEyeCooldown = Tune.OpenEyeCooldown;
        RecomputeBand();
        return true;
    }

    private static float NextBandBoundaryBelow(float value)
    {
        if (value > Tune.BandUnsettled) return Tune.BandUnsettled;
        if (value > Tune.BandFraying) return Tune.BandFraying;
        return Tune.BandUnravelled;
    }

    // ---------------------------------------------------------------- Bands

    /// <summary>
    /// Hysteresis (docs/02 §3.5.2): a band is entered at its boundary but only exited
    /// once Sanity climbs 8 points past it. Without this, Open the Eye would be pointless —
    /// three kills would undo a deliberate 25-point purchase.
    /// </summary>
    private void RecomputeBand()
    {
        SanityBand target;
        if (Current <= 0f) target = SanityBand.Ascension;
        else if (Current <= Tune.BandUnravelled) target = SanityBand.Unravelled;
        else if (Current <= Tune.BandFraying) target = SanityBand.Fraying;
        else if (Current <= Tune.BandUnsettled) target = SanityBand.Unsettled;
        else target = SanityBand.Lucid;

        if (target < Band)
        {
            // Climbing out: require clearing the boundary by the hysteresis margin.
            float exitThreshold = Band switch
            {
                SanityBand.Unravelled => Tune.BandUnravelled + Tune.BandHysteresis,
                SanityBand.Fraying => Tune.BandFraying + Tune.BandHysteresis,
                SanityBand.Unsettled => Tune.BandUnsettled + Tune.BandHysteresis,
                _ => float.MinValue,
            };
            if (Current < exitThreshold) return;
        }

        Band = target;
    }

    /// <summary>
    /// Fraction of enemy bullets rendered as hallucinations at the current band
    /// (docs/02 §3.4). Their only tell is the missing drop-shadow (docs/05 R9).
    /// </summary>
    public float HallucinationRatio => Band switch
    {
        SanityBand.Fraying => Tune.HallucinationRatioFraying,
        SanityBand.Unravelled => Tune.HallucinationRatioUnravelled,
        _ => 0f,
    };

    /// <summary>docs/02 §3.4 — Unravelled grants +10% move. This is a MOBILITY payout;
    /// there is deliberately no damage equivalent anywhere on the ladder.</summary>
    public float MoveSpeedMultiplier => Band == SanityBand.Unravelled ? 1.10f : 1f;

    public bool WeakPointsVisible => Band >= SanityBand.Fraying;
    public bool SecretsOnMinimap => Band >= SanityBand.Unravelled;

    public void DebugSetCurrent(float value) => RestoreTo(value);
}
