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
    /// <summary>
    /// **9.0 units/s = 144 px/s**, raised from 5.6 (89.6 px/s) when rooms were scaled to
    /// be screen-relative.
    ///
    /// At the old speed a screen took 7.1 seconds to cross and the new 1088px-wide rooms
    /// took twelve. Traversal is a room-scale concern, so when rooms grew ~2.5x linearly
    /// movement had to follow.
    ///
    /// NOT scaled by the full 2.5x, deliberately. Reaction time is a SCREEN-scale concern
    /// and the screen did not change size — a player crossing the viewport in under three
    /// seconds reads bullet patterns very differently. 1.6x restores traversal without
    /// rewriting how every pattern plays.
    ///
    /// Everything derived from this scales for free: the dash is a multiple of move speed
    /// (docs/02 §4), so its reach went 57px -> 92px, which is what makes it useful for
    /// crossing a big room rather than just for i-frames.
    /// </summary>
    public const float PlayerMoveSpeed = 9.0f * PixelsPerUnit;
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
    /// Build A (shipping): ZERO — Blink Step is free (fallback F4, docs/11 M1 test design).
    /// Build B (control arm): 18, via <c>--metered-dodge</c>.
    ///
    /// NOT a const, because the M1 test design needs both arms runnable from one binary.
    /// Forcing a rebuild between arms pushes testers into separate blocks and loses the
    /// counterbalanced ordering — same tester, both builds, order alternated — which is
    /// the whole reason the control arm has any statistical value.
    /// </summary>
    public static float SanityBlinkCost { get; private set; }

    /// <summary>The metered-dodge cost, restored by Build B.</summary>
    public const float SanityBlinkCostMetered = 18f;

    /// <summary>True when running the control arm. Recorded in telemetry so a CSV can
    /// never be attributed to the wrong build.</summary>
    public static bool MeteredDodge { get; private set; }

    public static void SetMeteredDodge(bool enabled)
    {
        MeteredDodge = enabled;
        SanityBlinkCost = enabled ? SanityBlinkCostMetered : 0f;
    }

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

    /// <summary>
    /// **60, raised from 45.** The floor must sit inside Unsettled, not on the lip of
    /// Fraying.
    ///
    /// docs/02 §3.3.1 states the intent precisely: late-floor rooms "begin in Unsettled
    /// and end in Fraying". At 45 that failed — 45 is only 5 points above the Fraying
    /// boundary, and kill income is capped at the ceiling, so once the ceiling bottomed
    /// out a player entered every remaining room 5 points from Fraying, dropped through on
    /// the first reload, and could never climb back. The economy simulation measured 66%
    /// of all combat time below 40 against a 25–45% target.
    ///
    /// The failure mode Fable named for this is exactly right: the bar became a leash. The
    /// ceiling is supposed to walk the player to the edge of the ladder; at 45 it parked
    /// them past it.
    /// </summary>
    public const float LucidCeilingFloor = 60f;

    // --- Band hysteresis (docs/02 §3.5.2) — lets a chosen band be HELD.
    public const float BandHysteresis = 8f;

    /// <summary>
    /// Kill-refund multiplier below the Fraying boundary. **1.0 — the halving is removed.**
    ///
    /// It was 0.5, and the economy simulation showed why that could not stand post-F4:
    /// the low band became an ABSORBING STATE. Below 40 you earn half, so you cannot climb
    /// out, so you keep earning half. Measured result was 66% of all combat time spent
    /// below 40 against a 25–45% target, and — the diagnostic tell — an expert at 67%
    /// versus a novice at 73%, meaning skill barely mattered because everyone lived at the
    /// bottom.
    ///
    /// The halving and the 8-point hysteresis were BOTH introduced to do one job: let a
    /// player hold a band they entered deliberately. Hysteresis does that job without
    /// touching income. The halving was designed when dodging cost 18 Sanity and the
    /// economy was steeply negative anyway; F4 removed that premise and left a brake on a
    /// system that no longer accelerates.
    ///
    /// Kept as a named constant rather than deleted so the sim can sweep it.
    /// </summary>
    public const float LowBandKillRefundMult = 1.0f;

    /// <summary>docs/02 §3.3 — Sanity candle. PIERCES the Lucid Ceiling, which is the
    /// entire reason it exists: everything else in the economy pushes Sanity down across
    /// a floor, and this is the only thing that pushes back above the cap.</summary>
    public const float SanityCandleValue = 25f;

    /// <summary>docs/02 §2 — armour absorbs one hit of any size, consumed entirely.</summary>
    public const int MaxArmour = 4;

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

    // ------------------------------------------------------------ Corruption (docs/02 §7)
    //
    // The thresholds. Read only through Core.CorruptionTiers, which is the one place that
    // knows what each of them does — these are the boundaries, not the effects.
    //
    // Corruption is the run-long risk stat and docs/02 §7.3 calls it "the game's real
    // difficulty selector". It only goes up, and every source of it is voluntary or clearly
    // telegraphed, so the numbers below are the price list for choices the player makes
    // deliberately. They should be tuned only alongside the payouts in docs/08 §3.

    public const float CorruptionFirst = 1f;        // loot bump, Corrupted Doors (M3)
    public const float CorruptionAwakened = 3f;     // Awakened enemies, extra shop stock
    public const float CorruptionHound = 5f;        // the Hound of Tindalos (M3)
    public const float CorruptionSwarm = 7f;        // +1 enemy per room
    public const float CorruptionYellowSign = 10f;  // the palette turns; everything Awakened

    /// <summary>docs/02 §7.2 — Awakened enemies carry +15% health. Deliberately small: the
    /// threat comes from the second attack pattern, not from bloated health bars, because
    /// fodder that stops dying fast starves the Sanity economy (docs/05 §2).</summary>
    public const float AwakenedHealthMultiplier = 1.15f;

    // ----------------------------------------------------- Floor scaling (docs/05 §8, 02 §2)
    //
    // The anchors, not the effects. Read only through Core.FloorScaling, which owns the
    // interpolation and the per-floor room-count table for the same reason CorruptionTiers
    // owns the Corruption thresholds: a scaling table split across files is a table that
    // disagrees with itself the first time one half is tuned.

    /// <summary>Concurrent attackers on floor 1 and floor 6 — docs/05 §8 calls this "the
    /// single most important knob for making a room fair", and it is how docs/05 R7's
    /// 600-bullet ceiling is honoured by design rather than clamped at runtime.</summary>
    public const int AttackTokensFirstFloor = 4;
    public const int AttackTokensFinalFloor = 9;

    /// <summary>One bullet costs this many hearts before floor scaling (docs/02 §2). Contact
    /// damage is authored per enemy instead; a bullet has no author, so it needs a base.</summary>
    public const float BulletHitDamage = 0.5f;

    /// <summary>One hit is half a heart until this floor, then a full one (docs/02 §2). The
    /// multiplier is exactly 2 so that half-heart granularity survives — see the note on
    /// PlayerController.ApplyIncomingDamageBonus for why fractional damage cannot.</summary>
    public const int FloorFullHeartDamage = 3;

    /// <summary>docs/02 §2 — a boss hits for a full heart from phase 2, on every floor.</summary>
    public const int BossFullHeartPhase = 2;

    // ------------------------------------------------------ The Tide (docs/07 §3, floor 2)
    //
    // Read through Core.TideCycle and Core.TideField. The period is the one number the player
    // actually learns, so it is a rhythm to be tuned by feel, not a difficulty knob.

    /// <summary>Seconds for a full low → high → low cycle.</summary>
    public const float TidePeriod = 20f;

    /// <summary>docs/07 §3 — wading slows you to 0.7. A movement tax the player can see
    /// coming and route around, which is the whole point of synchronising the cycle.</summary>
    public const float TideWadeSpeedMultiplier = 0.7f;

    /// <summary>docs/07 §3 — Deep Ones swim. docs/05 §3 gives the same 2× for water tiles, so
    /// this is the one number both statements mean.</summary>
    public const float TideSwimSpeedMultiplier = 2f;

    /// <summary>docs/03 §Elements — Drenched targets take +40% from lightning.</summary>
    public const float DrenchedLightningMultiplier = 1.4f;

    /// <summary>docs/03 §Elements — Drenched costs 20% move. Distinct from wading: wading is
    /// where you ARE, Drenched is what you CARRY, and a player who leaves the water wet keeps
    /// this until it dries. They stack, and that is intended — being caught by the tide
    /// should cost something past the moment it catches you.</summary>
    public const float DrenchedMoveMultiplier = 0.8f;

    /// <summary>Seconds a target stays Drenched after leaving the water.</summary>
    public const float DrenchedDuration = 4f;

    // ---------------------------------------------------------------- Bullets (docs/09 §3)

    public const int MaxBullets = 4096;                  // hard array capacity
    public const int EnemyBulletDesignCap = 600;         // docs/05 R7 — DESIGN ceiling, not a runtime clamp

    /// <summary>docs/02 §3.4 / docs/05 R9 — real bullets cast this; hallucinations do not.</summary>
    public static readonly Godot.Vector2 BulletShadowOffset = new(2f, 3f);
    public const float BulletShadowAlpha = 0.35f;
    public const float BulletShadowScale = 0.9f;
}
