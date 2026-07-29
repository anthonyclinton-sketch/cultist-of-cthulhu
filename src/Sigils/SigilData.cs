using Godot;

namespace CultistOfCthulhu.Sigils;

public enum SigilTier { D, C, B, A, S }

/// <summary>
/// docs/04 §4.1 — the tag vocabulary. Eight, and it stays eight.
///
/// A flags bitmask rather than an array, because a sigil offers 0–2 and wants 0–2, and
/// Godot's inspector renders a flags int as a tidy checkbox list where an enum array is
/// an unreviewable nested structure — the same argument RoomTemplate makes for storing
/// exits as four int arrays.
/// </summary>
[System.Flags]
public enum SigilTag
{
    None = 0,
    Flesh = 1 << 0,
    Tide = 1 << 1,
    Star = 1 << 2,
    Void = 1 << 3,
    Madness = 1 << 4,
    Iron = 1 << 5,
    Dream = 1 << 6,
    Blood = 1 << 7,
}

/// <summary>docs/04 §2.2 — three ley lines, whose TYPE rotates per run.</summary>
public enum LeyType { None, Blood, Salt, Ash, Gate }

/// <summary>
/// One sigil (docs/04 §3.3). All tuning lives here as a .tres, per docs/09 §5.
///
/// The effect fields below are a fixed modifier vocabulary rather than a scripting hook,
/// and that is a deliberate limit on M2's scope. docs/04 §8.2 requires that every A and S
/// sigil "change how you play, not just how hard you hit" — a vocabulary of percentages
/// cannot express all of those, so the ones that need real behaviour carry an explicit
/// flag (see <see cref="LullRegenPerSecond"/>, <see cref="NegateLethalOncePerRoom"/>) and
/// are implemented in the systems they belong to. Anything that cannot be expressed either
/// way is not in the M2 pool; docs/AUDIT-spec-vs-code.md records which.
///
/// Multipliers default to 1 and aggregate by multiplication; bonuses default to 0 and
/// aggregate by addition. Mixing those two up is the easiest way to make a sigil that
/// silently deletes a stat, so the naming is load-bearing: `...Multiplier` or `...Bonus`.
/// </summary>
[GlobalClass]
public partial class SigilData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "unnamed";
    [Export] public string DisplayName { get; set; } = "Unnamed Sigil";
    [Export] public SigilTier Tier { get; set; } = SigilTier.D;
    [Export] public SigilShapeKind Shape { get; set; } = SigilShapeKind.Mote;
    /// <summary>docs/04 §3.2 — the tile carries an arrow and its facing is part of the build.</summary>
    [Export] public bool Directional { get; set; }
    [Export(PropertyHint.MultilineText)] public string CodexText { get; set; } = "";
    /// <summary>Explicit mechanical text. docs/04 §3.3 requires this alongside the flavour —
    /// a player must never have to infer what a tile does.</summary>
    [Export(PropertyHint.MultilineText)] public string RulesText { get; set; } = "";

    [ExportGroup("Adjacency")]
    [Export(PropertyHint.Flags, "Flesh,Tide,Star,Void,Madness,Iron,Dream,Blood")]
    public int Offers { get; set; }
    [Export(PropertyHint.Flags, "Flesh,Tide,Star,Void,Madness,Iron,Dream,Blood")]
    public int Wants { get; set; }
    [Export] public LeyType LeyAffinity { get; set; } = LeyType.None;

    [ExportGroup("Effects — offensive")]
    /// <summary>Additive fraction: 0.08 is +8% damage. Capped at +25% by docs/04 §8.1.</summary>
    [Export] public float DamageBonus { get; set; }
    [Export] public float FireRateBonus { get; set; }
    /// <summary>Applies only against enemies below 30% health (docs/04 §5.1 Open Wound).</summary>
    [Export] public float ExecuteDamageBonus { get; set; }
    /// <summary>Applies only at or below half hearts.</summary>
    [Export] public float LowHealthDamageBonus { get; set; }
    /// <summary>Damage per point of Corruption. The dedicated archetype (docs/04 §5.4).</summary>
    [Export] public float DamagePerCorruption { get; set; }

    [ExportGroup("Effects — defence & mobility")]
    [Export] public float MoveSpeedBonus { get; set; }
    /// <summary>Extra Blink Step distance, in design units (docs/04 §5.3 Tekeli-li).</summary>
    [Export] public float BlinkDistanceUnits { get; set; }
    [Export] public int ArmourPerFloor { get; set; }
    /// <summary>Elder Sign — once per room, a lethal hit is negated.</summary>
    [Export] public bool NegateLethalOncePerRoom { get; set; }

    [ExportGroup("Effects — Sanity")]
    [Export] public float MaxSanityBonus { get; set; }
    /// <summary>Perfect Recitation refunds this much MORE: 0.5 is +50% on top of the base half.</summary>
    [Export] public float PerfectRefundBonus { get; set; }
    /// <summary>Multiplier on the Blink Step cost. docs/04 §8.6: may be discounted, never
    /// zeroed. Only bites in Build B, where the dodge costs anything at all.</summary>
    [Export] public float BlinkCostMultiplier { get; set; } = 1f;
    /// <summary>Multiplier on the Sanity lost when hit. Same rule — Salt Ward halves it,
    /// nothing removes it.</summary>
    [Export] public float HitSanityCostMultiplier { get; set; } = 1f;
    [Export] public float KillSanityBonus { get; set; }

    /// <summary>
    /// Deep One's Gill (docs/04 §5.2, as rewritten by Fable's review). Sanity trickles only
    /// after <see cref="LullDelaySeconds"/> without dodging, reloading or Banishing.
    ///
    /// The delay is not flavour. Pillar I (docs/01 §2) explicitly lists passive in-combat
    /// regeneration as forbidden, and the original flat-regen version of this sigil roughly
    /// doubled a room's Sanity budget on its own. Paying only during a lull rewards clean
    /// positioning instead of repealing the Pillar — which is why the validator below
    /// refuses a trickle with no delay.
    /// </summary>
    [Export] public float LullRegenPerSecond { get; set; }
    [Export] public float LullDelaySeconds { get; set; } = 4f;

    /// <summary>The Unblinking's price: Corruption per Blink Step, so the discount is paid
    /// for at the rate a Corruption build actually accrues (docs/04 §8.7).</summary>
    [Export] public float CorruptionPerBlink { get; set; }

    [ExportGroup("Effects — Ascension")]
    /// <summary>Dreamer's Ballast (docs/04 §5.2). Extra seconds on each Ascension.</summary>
    [Export] public float AscensionDurationBonus { get; set; }
    /// <summary>
    /// Multiplier on the exit heart cost. DISCOUNT ONLY — the original version of this
    /// sigil removed the cost outright, which combined with docs/02 §6's "cannot kill you"
    /// clause to make deliberate Ascension free and farmable forever. That is the single
    /// worst bug Fable's review found, and the validator below is what stops it recurring.
    /// </summary>
    [Export] public float AscensionHeartCostMultiplier { get; set; } = 1f;

    [ExportGroup("Effects — economy")]
    [Export] public float ShopPriceMultiplier { get; set; } = 1f;
    [Export] public float GoldBonus { get; set; }
    [Export] public int KeysPerFloor { get; set; }
    /// <summary>The Ledger of Names — gold for a room cleared without taking damage.</summary>
    [Export] public int GoldPerCleanRoom { get; set; }

    [ExportGroup("Cost")]
    [Export] public int CorruptionOnEquip { get; set; }

    public int Cells => SigilShape.CellCount(Shape);

    /// <summary>docs/04 §6 — dissolution value at Gaunt's stall.</summary>
    public int DissolveValue => Mathf.RoundToInt(20f * Cells * TierMultiplier);

    public float TierMultiplier => Tier switch
    {
        SigilTier.D => 1.0f,
        SigilTier.C => 1.4f,
        SigilTier.B => 2.0f,
        SigilTier.A => 3.0f,
        _ => 4.5f,
    };

    private static int TagCount(int mask)
    {
        int n = 0;
        for (int b = 0; b < 8; b++) if ((mask & (1 << b)) != 0) n++;
        return n;
    }

    /// <summary>
    /// The balance rules from docs/04 §8, enforced at author time.
    ///
    /// These are here rather than in a design review because every one of them was
    /// discovered by a review finding a shipped-in-the-doc sigil that cancelled a Pillar.
    /// A rule that only exists in prose gets violated by the next person who writes a
    /// tile; a rule that fails the content gate does not.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id == "unnamed") return "sigil has no Id.";

        // §8.1 — no single tile may exceed +25% to a core stat. Stacking is the source of
        // power, not any one sigil.
        if (DamageBonus > 0.25f) return $"{Id}: +{DamageBonus:P0} damage exceeds the +25% single-stat cap (§8.1).";
        if (FireRateBonus > 0.25f) return $"{Id}: +{FireRateBonus:P0} fire rate exceeds the +25% cap (§8.1).";
        if (MoveSpeedBonus > 0.25f) return $"{Id}: +{MoveSpeedBonus:P0} move speed exceeds the +25% cap (§8.1).";

        // §4.1 — 0-2 tags each way. More than two and the tooltip stops being readable and
        // the adjacency puzzle stops having a wrong answer.
        if (TagCount(Offers) > 2) return $"{Id}: offers {TagCount(Offers)} tags, maximum 2 (§4.1).";
        if (TagCount(Wants) > 2) return $"{Id}: wants {TagCount(Wants)} tags, maximum 2 (§4.1).";

        // §8.6 — no sigil may reduce a Pillar-I cost to zero. Discounts only.
        if (BlinkCostMultiplier <= 0f)
            return $"{Id}: BlinkCostMultiplier {BlinkCostMultiplier} zeroes a Pillar-I cost (§8.6). Discount, never remove.";
        if (HitSanityCostMultiplier <= 0f)
            return $"{Id}: HitSanityCostMultiplier {HitSanityCostMultiplier} deletes the hit->Sanity drain (§8.6).";
        if (AscensionHeartCostMultiplier <= 0f)
            return $"{Id}: AscensionHeartCostMultiplier {AscensionHeartCostMultiplier} makes Ascension free (§8.6). " +
                   "A cost-removal effect must never exist for Ascension; discounts only.";

        // §8.6, the other half — no unconditional in-combat regeneration. A trickle with no
        // lull requirement IS unconditional regen wearing a different name.
        if (LullRegenPerSecond > 0f && LullDelaySeconds < 2f)
            return $"{Id}: a {LullRegenPerSecond}/s trickle after only {LullDelaySeconds}s is unconditional " +
                   "in-combat regeneration (§8.6). Pillar I forbids it.";

        // §8.5 / §8.7 — a Corruption sigil must be an INVESTMENT. If it pays out
        // unconditionally, its only drawback is Corruption, which is exactly what the
        // archetype most likely to take it already wants.
        if (CorruptionOnEquip > 0 && DamagePerCorruption <= 0f
            && (DamageBonus > 0f || FireRateBonus > 0f || MoveSpeedBonus > 0f || MaxSanityBonus > 0f))
        {
            return $"{Id}: costs Corruption but pays flat, unconditional power (§8.5). " +
                   "Corruption sigils must be net-negative at Corruption 0.";
        }

        // §8.2 — if an A or S tile can be summarised as a percentage, it is not A tier.
        if (Tier is SigilTier.A or SigilTier.S && !ChangesHowYouPlay())
            return $"{Id}: {Tier}-tier but every effect is a flat percentage (§8.2).";

        if (string.IsNullOrWhiteSpace(RulesText))
            return $"{Id}: no RulesText. docs/04 §3.3 requires explicit mechanical text.";

        return null;
    }

    /// <summary>
    /// docs/04 §8.2's test, mechanised: does this tile do anything that is not a number on
    /// an existing stat? Conditional damage counts — "kill wounded things faster" changes
    /// target priority, which is a change in play.
    /// </summary>
    private bool ChangesHowYouPlay() =>
        ExecuteDamageBonus > 0f || LowHealthDamageBonus > 0f || DamagePerCorruption > 0f
        || NegateLethalOncePerRoom || LullRegenPerSecond > 0f || BlinkDistanceUnits > 0f
        || BlinkCostMultiplier < 1f || HitSanityCostMultiplier < 1f
        || GoldPerCleanRoom > 0 || KeysPerFloor > 0 || ArmourPerFloor > 0
        || AscensionDurationBonus > 0f || AscensionHeartCostMultiplier < 1f
        || Directional;
}
