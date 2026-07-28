namespace CultistOfCthulhu.Core;

/// <summary>
/// M0 tuning constants, transcribed directly from the design docs.
///
/// This class is TEMPORARY SCAFFOLDING. Per docs/09 §5 the binding rule is "no gameplay
/// number appears in a .cs file" — all of this moves into .tres Resources at M1 when the
/// data pipeline lands. It exists now only so M0 can be built and stress-tested without
/// first building the resource pipeline. Every constant carries its doc reference so the
/// migration is mechanical.
/// </summary>
public static class Tune
{
    /// <summary>docs/02 §1.1 — 16px = 1 design unit.</summary>
    public const float PixelsPerUnit = 16f;

    public static float Units(float u) => u * PixelsPerUnit;

    // ---------------------------------------------------------------- Player (docs/02 §1.1)

    public const float PlayerHitboxRadius = 6f;          // px — deliberately far smaller than the sprite
    public const float PlayerMoveSpeed = 5.6f * PixelsPerUnit;
    public const float PlayerFiringSpeedMult = 0.82f;
    public const float PlayerAccelTime = 0.06f;          // seconds 0 -> max
    public const float PlayerDecelTime = 0.05f;          // no ice, ever

    // ------------------------------------------------------------ Blink Step (docs/02 §4)
    // Frame-exact at 60Hz. These are FRAME COUNTS, not seconds, because the design is
    // specified in frames and rounding them through seconds is how dodge feel gets lost.

    public const int BlinkStartupFrames = 2;             // frames 1-2   vulnerable
    public const int BlinkInvulnFrames = 14;             // frames 3-16  INVULNERABLE
    public const int BlinkRecoveryFrames = 8;            // frames 17-24 vulnerable, 40% move
    public const int BlinkTotalFrames = BlinkStartupFrames + BlinkInvulnFrames + BlinkRecoveryFrames; // 24 = 0.40s
    public const float BlinkRecoveryMoveMult = 0.40f;
    public const float BlinkCooldown = 0.12f;            // prevents input-buffer chaining

    /// <summary>
    /// Blink Step travels at this multiple of the player's CURRENT move speed — so it is
    /// a dash, and so it inherits move-speed modifiers (the Unravelled band's +10%, and
    /// later any mobility sigil) instead of being a fixed teleport hop.
    ///
    /// This replaces the old authored BlinkDistance, which was a lie: distance was
    /// computed as `BlinkDistance / totalDuration` but the recovery frames then scaled
    /// velocity by 0.4, so the dash never actually covered the 3.2 units the docs claimed
    /// — it covered ~2.56. Authoring the SPEED and deriving the distance makes the number
    /// in the design doc match the number in the game.
    /// </summary>
    public const float BlinkSpeedMultiplier = 2.0f;

    /// <summary>
    /// Distance a dash actually covers, derived from the frame data. Full speed through
    /// startup and i-frames, then BlinkRecoveryMoveMult through the recovery tail.
    /// ~57px / 3.6 units at the default multiplier.
    /// </summary>
    public static float BlinkEffectiveDistance =>
        PlayerMoveSpeed * BlinkSpeedMultiplier *
        ((BlinkStartupFrames + BlinkInvulnFrames) / 60f
         + BlinkRecoveryFrames / 60f * BlinkRecoveryMoveMult);

    // ---------------------------------------------------------------- Sanity (docs/02 §3)

    public const float SanityMax = 100f;

    /// <summary>
    /// ZERO — Blink Step is free (fallback F4, docs/11 M1 test design).
    ///
    /// Kept as a named constant rather than deleted because the metered-dodge variant is
    /// still the thing M1 measures against; Build B flips this to 18 and changes nothing
    /// else. Deleting it would make the A/B a code fork instead of a config change.
    /// </summary>
    public const float SanityBlinkCost = 0f;

    public const float SanityReciteCostPerWeight = 12f;
    public const float SanityBanishCost = 45f;
    public const float SanityHitCost = 10f;

    public const float SanityKillBase = 6f;
    public const float SanityRoomClear = 20f;
    public const float SanityOutOfCombatRegen = 8f;      // per second
    public const float SanityOutOfCombatDelay = 2.5f;    // seconds after last enemy dies
    public const float SanityInCombatRegen = 0f;         // deliberate — docs/02 §3.3

    // --- Lucid Ceiling (docs/02 §3.3.1) — the descent arc. Regen refills only to here.
    //
    // With a free Blink Step the ceiling is now the PRIMARY driver of the descent rather
    // than a secondary one: dodging was the dominant Sanity sink, and without it spending
    // alone can no longer carry a player down the ladder. Decay steepened from 5 to 7 and
    // the floor lowered from 50 to 45 so that late-floor rooms reliably START in Unsettled
    // and end in Fraying. Without this change Pillar III would effectively never fire.
    //
    // These two numbers are the primary M1 tuning lever. The governing metric is
    // time-in-band (docs/11 metric 1, target 25-45% of combat below 40 Sanity).
    public const float LucidCeilingStart = 100f;
    public const float LucidCeilingDecayPerRoom = 7f;
    public const float LucidCeilingFloor = 45f;

    // --- Band hysteresis (docs/02 §3.5.2) — lets a chosen band be HELD.
    public const float BandHysteresis = 8f;
    public const float LowBandKillRefundMult = 0.5f;     // below Fraying, kills refund half

    // --- Banish (docs/02 §5.2) — the panic button. Not an item; gated purely on Sanity,
    // which is why entering Fraying (40) takes it away from you: it costs 45.
    public const float BanishRadius = 9f * PixelsPerUnit;        // 144px
    public const float BanishKnockback = 2f * PixelsPerUnit;     // 32px of push
    public const float BanishStunSeconds = 0.6f;
    public const float BanishCooldown = 1.2f;
    /// <summary>You are unmaking part of reality, and it notices (docs/02 §7.1).</summary>
    public const float BanishCorruption = 0.25f;
    /// <summary>Out of combat, breaking a cracked wall is cheaper and costs no Corruption —
    /// otherwise secret-hunting is an involuntary Corruption tax (docs/02 §5.2 review).</summary>
    public const float BanishWallBreakCost = 15f;

    // --- Open the Eye (docs/02 §3.5.1) — the deliberate descent verb.
    public const float OpenEyeCost = 25f;
    public const float OpenEyeCooldown = 8f;
    public const float OpenEyeHoldTime = 0.4f;           // hold Banish to disambiguate
    public const float OpenEyeMinSanity = 20f;           // cannot Open the Eye into Ascension

    // --- Band boundaries (docs/02 §3.4). The ladder grants NO damage — information only.
    public const float BandUnsettled = 60f;
    public const float BandFraying = 40f;
    public const float BandUnravelled = 20f;

    public const float HallucinationRatioFraying = 0.125f;   // 1 in 8
    public const float HallucinationRatioUnravelled = 0.25f; // 1 in 4

    // ------------------------------------------------------------- Ascension (docs/02 §6)
    //
    // Sanity zero does not kill you — it changes you. The design constraint that governs
    // every number here: ASCENSION MUST NEVER BE OPTIMAL TO FARM. Fable's review found it
    // was farmable to infinity because the heart cost vanished at low health and the
    // max-Sanity penalty stopped escalating at the floor. The debt rule and the
    // diminishing duration below are what close that.

    /// <summary>Duration by ascension index within a run. Repetition is allowed; farming
    /// is not — the fifth is a quarter of the first.</summary>
    public static readonly float[] AscensionDurations = { 20f, 14f, 10f, 7f, 5f };
    public const float AscensionMinDuration = 5f;

    public const float AscensionSpeedMultiplier = 1.35f;
    public const float AscensionExitSanity = 50f;

    /// <summary>Heart cost on exit, rising half a heart per Ascension.</summary>
    public const float AscensionExitHeartCost = 1.0f;
    public const float AscensionHeartCostEscalation = 0.5f;

    /// <summary>Max-Sanity penalty per Ascension, floored so it cannot reach zero.</summary>
    public const float AscensionMaxSanityPenalty = 10f;
    public const float AscensionMaxSanityFloor = 40f;

    /// <summary>The heart cost cannot reduce you below this — but see the debt rule.</summary>
    public const float AscensionHeartFloor = 0.5f;
    /// <summary>Heart containers can never be reduced below this by Ascension debt.</summary>
    public const float AscensionMinContainers = 1f;

    public const float AscensionCorruption = 1f;

    /// <summary>Fire rate and damage of the form attack that replaces your weapons.</summary>
    public const float AscensionAttackRate = 9f;
    public const float AscensionAttackDamage = 14f;
    public const int AscensionAttackProjectiles = 5;

    // ---------------------------------------------------------------- Bullets (docs/09 §3)

    public const int MaxBullets = 4096;                  // hard array capacity
    public const int EnemyBulletDesignCap = 600;         // docs/05 R7 — DESIGN ceiling, not a runtime clamp

    /// <summary>docs/02 §3.4 / docs/05 R9 — real bullets cast this; hallucinations do not.</summary>
    public static readonly Godot.Vector2 BulletShadowOffset = new(2f, 3f);
    public const float BulletShadowAlpha = 0.35f;
    public const float BulletShadowScale = 0.9f;
}
