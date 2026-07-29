namespace CultistOfCthulhu.Core;

/// <summary>
/// docs/02 §7.2 — what Corruption actually does to a run.
///
/// THE PROBLEM THIS FIXES. Corruption has been accruing since M1 from four sources — Banish
/// (+0.25), Ascension (+1), the reward room's third option (+1) and Forbidden inscriptions
/// (+1) — and the only thing that has ever read it is the loot-tier bump, which is a
/// REWARD. Two sigils and one inscription also scale damage off it. So Corruption was
/// strictly upside: accrue it freely, hit harder, get better loot, pay nothing.
///
/// Every price in the game denominated in Corruption was therefore a discount, which
/// inverted the one axis docs/02 §7.3 calls "the game's real difficulty selector" and broke
/// the rule docs/04 §8.5 exists to enforce — a Corruption sigil must be net-negative at
/// Corruption 0, and none of them could be while Corruption cost nothing.
///
/// ONE PLACE OWNS THE THRESHOLDS. That includes the loot bump, which used to live inside
/// <see cref="Sigils.SigilPool"/> — a threshold table split across two files is a table that
/// disagrees with itself the first time one half is tuned.
///
/// Pure functions of a single float. No state, so nothing can go stale.
/// </summary>
public static class CorruptionTiers
{
    /// <summary>The threshold a value has reached: 0, 1, 3, 5, 7 or 10.</summary>
    public static int TierFor(float corruption) =>
        corruption >= Tune.CorruptionYellowSign ? 10
        : corruption >= Tune.CorruptionSwarm ? 7
        : corruption >= Tune.CorruptionHound ? 5
        : corruption >= Tune.CorruptionAwakened ? 3
        : corruption >= Tune.CorruptionFirst ? 1
        : 0;

    /// <summary>
    /// 3+ — enemies become Awakened: tougher, and each gains a second attack pattern.
    /// At 10 the Yellow Sign awakens everything regardless, which is the same condition
    /// stated twice in docs/02 §7.2 and is therefore folded together here.
    /// </summary>
    public static bool EnemiesAwakened(float corruption) => corruption >= Tune.CorruptionAwakened;

    public static float EnemyHealthMultiplier(float corruption) =>
        EnemiesAwakened(corruption) ? Tune.AwakenedHealthMultiplier : 1f;

    /// <summary>7+ — every room spawns one more enemy than its budget bought.</summary>
    public static int ExtraEnemiesPerRoom(float corruption) =>
        corruption >= Tune.CorruptionSwarm ? 1 : 0;

    /// <summary>3+ — Gaunt stocks an extra Inscription. The only threshold effect that is
    /// purely a gift, and it is there because the shop is where a Corruption build cashes
    /// out (docs/08 §2.1).</summary>
    public static int ExtraBenchOffers(float corruption) =>
        corruption >= Tune.CorruptionAwakened ? 1 : 0;

    /// <summary>10 — the Yellow Sign. The palette turns and everything is Awakened.</summary>
    public static bool YellowSign(float corruption) => corruption >= Tune.CorruptionYellowSign;

    /// <summary>
    /// Chance a loot roll is bumped up one tier (docs/08 §3): 20% / 45% / 70% at 1 / 3 / 5.
    ///
    /// This is the payout half of the same table, and it has been live since M2 while every
    /// cost half was missing.
    /// </summary>
    public static float LootTierBumpChance(float corruption) =>
        corruption >= Tune.CorruptionHound ? 0.70f
        : corruption >= Tune.CorruptionAwakened ? 0.45f
        : corruption >= Tune.CorruptionFirst ? 0.20f
        : 0f;

    /// <summary>One line describing the current tier, for the HUD and the run summary.</summary>
    public static string Describe(float corruption) => TierFor(corruption) switch
    {
        0 => "unmarked",
        1 => "marked",
        3 => "awakened",
        5 => "hunted",
        7 => "thronged",
        _ => "THE YELLOW SIGN",
    };

    /// <summary>
    /// The next threshold above the current value, or 0 at the cap.
    ///
    /// Exists because the HUD needs to answer a question the pip row cannot: not "how
    /// corrupt am I" but "what does the next one cost me". Banish grants 0.25, so a player
    /// is almost always between thresholds, and a readout that only shows the tier already
    /// reached says nothing at all for three spends out of four.
    /// </summary>
    public static float NextThreshold(float corruption) =>
        corruption < Tune.CorruptionFirst ? Tune.CorruptionFirst
        : corruption < Tune.CorruptionAwakened ? Tune.CorruptionAwakened
        : corruption < Tune.CorruptionHound ? Tune.CorruptionHound
        : corruption < Tune.CorruptionSwarm ? Tune.CorruptionSwarm
        : corruption < Tune.CorruptionYellowSign ? Tune.CorruptionYellowSign
        : 0f;

    /// <summary>What crossing the next threshold will do. Short enough for one HUD line.</summary>
    public static string NextEffect(float corruption)
    {
        float next = NextThreshold(corruption);
        if (next <= 0f) return "";

        return next switch
        {
            Tune.CorruptionFirst => "better loot",
            Tune.CorruptionAwakened => "they awaken",
            Tune.CorruptionHound => "something hunts you",
            Tune.CorruptionSwarm => "fuller rooms",
            _ => "the Yellow Sign",
        };
    }
}
