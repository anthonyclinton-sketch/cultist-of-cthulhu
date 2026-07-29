using System.Collections.Generic;
using Godot;

namespace CultistOfCthulhu.Sigils;

/// <summary>
/// Everything the Circle currently grants, flattened into one block the rest of the game
/// reads.
///
/// The whole point is that no gameplay system ever walks the grid. <c>PlayerController</c>
/// asks for a move-speed multiplier; it does not know what a ley line is, and it must
/// never learn — otherwise adding a sigil means editing the player, the weapon and the
/// Sanity system, and the Reverie screen's live diff panel (docs/04 §7) becomes impossible
/// to keep honest. Resolve once when the layout changes, read cheaply forever after.
///
/// Recomputed on every placement, never per tick.
/// </summary>
public sealed class SigilEffects
{
    // --- Offence ------------------------------------------------------------
    public float DamageMultiplier = 1f;
    public float FireRateMultiplier = 1f;
    /// <summary>Extra damage against enemies below 30% health.</summary>
    public float ExecuteDamageBonus;
    /// <summary>Extra damage while the player is at or below half hearts.</summary>
    public float LowHealthDamageBonus;
    public float DamagePerCorruption;

    // --- Defence & mobility -------------------------------------------------
    public float MoveSpeedMultiplier = 1f;
    public float BlinkDistanceUnits;
    public int ArmourPerFloor;
    public bool NegateLethalOncePerRoom;

    // --- Sanity -------------------------------------------------------------
    public float MaxSanityBonus;
    public float PerfectRefundBonus;
    public float BlinkCostMultiplier = 1f;
    public float HitSanityCostMultiplier = 1f;
    public float KillSanityBonus;
    public float LullRegenPerSecond;
    public float LullDelaySeconds = 4f;
    public float CorruptionPerBlink;

    // --- Ascension ----------------------------------------------------------
    public float AscensionDurationBonus;
    public float AscensionHeartCostMultiplier = 1f;

    // --- Economy ------------------------------------------------------------
    public float ShopPriceMultiplier = 1f;
    public float GoldMultiplier = 1f;
    public int KeysPerFloor;
    public int GoldPerCleanRoom;

    // --- Diagnostics --------------------------------------------------------
    public int ActiveSynergies;
    public int CorruptionFromSigils;
    public int CellsUsed;

    public void Reset()
    {
        DamageMultiplier = 1f;
        FireRateMultiplier = 1f;
        ExecuteDamageBonus = 0f;
        LowHealthDamageBonus = 0f;
        DamagePerCorruption = 0f;

        MoveSpeedMultiplier = 1f;
        BlinkDistanceUnits = 0f;
        ArmourPerFloor = 0;
        NegateLethalOncePerRoom = false;

        MaxSanityBonus = 0f;
        PerfectRefundBonus = 0f;
        BlinkCostMultiplier = 1f;
        HitSanityCostMultiplier = 1f;
        KillSanityBonus = 0f;
        LullRegenPerSecond = 0f;
        LullDelaySeconds = 4f;
        CorruptionPerBlink = 0f;

        AscensionDurationBonus = 0f;
        AscensionHeartCostMultiplier = 1f;

        ShopPriceMultiplier = 1f;
        GoldMultiplier = 1f;
        KeysPerFloor = 0;
        GoldPerCleanRoom = 0;

        ActiveSynergies = 0;
        CorruptionFromSigils = 0;
        CellsUsed = 0;
    }

    /// <summary>
    /// Fold one placed sigil in, with its ley multipliers already decided by the caller.
    ///
    /// Ley bonuses are passed as separate offensive/defensive/trigger multipliers rather
    /// than as a single "×1.5" because docs/04 §2.2 splits them that way: Blood amplifies
    /// offence, Salt amplifies defence and utility, Ash doubles triggers. A sigil sitting
    /// on two crossing leys gets both, which is what makes the ley cross prime real estate
    /// and is the central tension of the puzzle (§2.2).
    /// </summary>
    public void Add(SigilData s, float offensive, float defensive, float trigger)
    {
        DamageMultiplier += s.DamageBonus * offensive;
        FireRateMultiplier += s.FireRateBonus * offensive;
        ExecuteDamageBonus += s.ExecuteDamageBonus * offensive;
        LowHealthDamageBonus += s.LowHealthDamageBonus * offensive;
        DamagePerCorruption += s.DamagePerCorruption * offensive;

        MoveSpeedMultiplier += s.MoveSpeedBonus * defensive;
        BlinkDistanceUnits += s.BlinkDistanceUnits * defensive;
        ArmourPerFloor += s.ArmourPerFloor;
        NegateLethalOncePerRoom |= s.NegateLethalOncePerRoom;

        MaxSanityBonus += s.MaxSanityBonus * defensive;
        PerfectRefundBonus += s.PerfectRefundBonus * defensive;
        KillSanityBonus += s.KillSanityBonus * trigger;
        CorruptionPerBlink += s.CorruptionPerBlink;

        if (s.LullRegenPerSecond > LullRegenPerSecond)
        {
            // Take the strongest rather than summing. Two lull sigils stacking into a
            // torrent is exactly the unconditional-regen failure §8.6 exists to prevent,
            // and the delay would be ambiguous besides.
            LullRegenPerSecond = s.LullRegenPerSecond;
            LullDelaySeconds = s.LullDelaySeconds;
        }

        // Discounts compound multiplicatively and are floored, so no stack of tiles can
        // reach zero (§8.6). Two half-price sigils give a quarter price, never free.
        BlinkCostMultiplier *= s.BlinkCostMultiplier;
        HitSanityCostMultiplier *= s.HitSanityCostMultiplier;

        AscensionDurationBonus += s.AscensionDurationBonus * defensive;
        AscensionHeartCostMultiplier *= s.AscensionHeartCostMultiplier;

        ShopPriceMultiplier *= s.ShopPriceMultiplier;
        GoldMultiplier += s.GoldBonus * defensive;
        KeysPerFloor += s.KeysPerFloor;
        GoldPerCleanRoom += s.GoldPerCleanRoom * (trigger > 1f ? 2 : 1);

        CorruptionFromSigils += s.CorruptionOnEquip;
        CellsUsed += s.Cells;
    }

    /// <summary>Clamp anything that must never reach an absorbing value.</summary>
    public void Finalise()
    {
        BlinkCostMultiplier = Mathf.Max(0.1f, BlinkCostMultiplier);
        HitSanityCostMultiplier = Mathf.Max(0.1f, HitSanityCostMultiplier);
        ShopPriceMultiplier = Mathf.Max(0.25f, ShopPriceMultiplier);
        MoveSpeedMultiplier = Mathf.Max(0.5f, MoveSpeedMultiplier);

        // The hard floor on the Ascension discount. docs/04 §5.2: the exit heart cost is
        // "reduced by half a heart (never to zero)" — a stack of discounts reaching zero
        // recreates the farmable-forever exploit the whole debt rule exists to close.
        AscensionHeartCostMultiplier = Mathf.Max(0.5f, AscensionHeartCostMultiplier);
    }

    /// <summary>Human-readable diff, for the Reverie panel and the logs.</summary>
    public IEnumerable<string> Describe()
    {
        if (DamageMultiplier != 1f) yield return $"damage {Pct(DamageMultiplier)}";
        if (FireRateMultiplier != 1f) yield return $"fire rate {Pct(FireRateMultiplier)}";
        if (MoveSpeedMultiplier != 1f) yield return $"move {Pct(MoveSpeedMultiplier)}";
        if (MaxSanityBonus != 0f) yield return $"max Sanity +{MaxSanityBonus:F0}";
        if (KillSanityBonus != 0f) yield return $"Sanity/kill +{KillSanityBonus:F0}";
        if (ExecuteDamageBonus != 0f) yield return $"vs wounded +{ExecuteDamageBonus:P0}";
        if (LowHealthDamageBonus != 0f) yield return $"at low health +{LowHealthDamageBonus:P0}";
        if (DamagePerCorruption != 0f) yield return $"+{DamagePerCorruption:P0} dmg per Corruption";
        if (BlinkDistanceUnits != 0f) yield return $"Blink +{BlinkDistanceUnits:F1}u";
        if (BlinkCostMultiplier != 1f) yield return $"Blink cost {BlinkCostMultiplier:P0}";
        if (HitSanityCostMultiplier != 1f) yield return $"hit Sanity {HitSanityCostMultiplier:P0}";
        if (PerfectRefundBonus != 0f) yield return $"Perfect refund +{PerfectRefundBonus:P0}";
        if (LullRegenPerSecond != 0f) yield return $"lull {LullRegenPerSecond:F0}/s after {LullDelaySeconds:F0}s";
        if (ArmourPerFloor != 0) yield return $"armour +{ArmourPerFloor}/floor";
        if (KeysPerFloor != 0) yield return $"keys +{KeysPerFloor}/floor";
        if (GoldPerCleanRoom != 0) yield return $"{GoldPerCleanRoom} gold per clean room";
        if (ShopPriceMultiplier != 1f) yield return $"shop prices {ShopPriceMultiplier:P0}";
        if (GoldMultiplier != 1f) yield return $"gold {Pct(GoldMultiplier)}";
        if (AscensionDurationBonus != 0f) yield return $"Ascension +{AscensionDurationBonus:F0}s";
        if (AscensionHeartCostMultiplier != 1f) yield return $"Ascension exit cost {AscensionHeartCostMultiplier:P0}";
        if (NegateLethalOncePerRoom) yield return "negates one lethal hit per room";
    }

    private static string Pct(float mult) => $"{(mult - 1f) * 100f:+0;-0}%";
}
