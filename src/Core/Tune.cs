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
    public const float BlinkDistance = 3.2f * PixelsPerUnit;  // ~51px
    public const float BlinkRecoveryMoveMult = 0.40f;
    public const float BlinkCooldown = 0.12f;            // prevents input-buffer chaining

    // ---------------------------------------------------------------- Sanity (docs/02 §3)

    public const float SanityMax = 100f;
    public const float SanityBlinkCost = 18f;
    public const float SanityReciteCostPerWeight = 12f;
    public const float SanityBanishCost = 45f;
    public const float SanityHitCost = 10f;

    public const float SanityKillBase = 6f;
    public const float SanityRoomClear = 20f;
    public const float SanityOutOfCombatRegen = 8f;      // per second
    public const float SanityOutOfCombatDelay = 2.5f;    // seconds after last enemy dies
    public const float SanityInCombatRegen = 0f;         // deliberate — docs/02 §3.3

    // --- Lucid Ceiling (docs/02 §3.3.1) — the descent arc. Regen refills only to here.
    public const float LucidCeilingStart = 100f;
    public const float LucidCeilingDecayPerRoom = 5f;
    public const float LucidCeilingFloor = 50f;

    // --- Band hysteresis (docs/02 §3.5.2) — lets a chosen band be HELD.
    public const float BandHysteresis = 8f;
    public const float LowBandKillRefundMult = 0.5f;     // below Fraying, kills refund half

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

    // ---------------------------------------------------------------- Bullets (docs/09 §3)

    public const int MaxBullets = 4096;                  // hard array capacity
    public const int EnemyBulletDesignCap = 600;         // docs/05 R7 — DESIGN ceiling, not a runtime clamp

    /// <summary>docs/02 §3.4 / docs/05 R9 — real bullets cast this; hallucinations do not.</summary>
    public static readonly Godot.Vector2 BulletShadowOffset = new(2f, 3f);
    public const float BulletShadowAlpha = 0.35f;
    public const float BulletShadowScale = 0.9f;
}
