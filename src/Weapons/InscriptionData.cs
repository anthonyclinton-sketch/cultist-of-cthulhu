using Godot;

namespace CultistOfCthulhu.Weapons;

public enum InscriptionTier { Lesser, Greater, Forbidden }

/// <summary>
/// docs/03 §3 — a permanent modification etched onto ONE weapon for the rest of the run.
///
/// This is the system the brief specifically asked for: the player buys upgrades for
/// weapons in shops. It is also the game's main gold sink, which is why the prices here
/// are the numbers docs/08 §1.2's whole economy is balanced against — a run generates
/// 620–900 gold and a committed spender affords 5–7 of these.
///
/// Every field is a modifier on <see cref="WeaponData"/> rather than a behaviour hook. The
/// weapon resolves Data + its inscriptions into effective stats, so an inscription can
/// never be "applied" twice or forgotten on a reload path — there is no applied state, only
/// a list and a projection.
/// </summary>
[GlobalClass]
public partial class InscriptionData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "unnamed";
    [Export] public string DisplayName { get; set; } = "Unnamed Etching";
    [Export] public InscriptionTier Tier { get; set; } = InscriptionTier.Lesser;
    [Export(PropertyHint.MultilineText)] public string RulesText { get; set; } = "";

    /// <summary>
    /// docs/03 §3.4 — a weapon may not carry two inscriptions from the same conflict group,
    /// and the UI greys the second out with the reason. Empty means no conflict.
    ///
    /// A string rather than an enum so that adding a conflict is a content change rather
    /// than a code change; the pool is authored data and the groups are a property of the
    /// content, not of the engine.
    /// </summary>
    [Export] public string ConflictGroup { get; set; } = "";

    /// <summary>True for inscriptions that cannot go on a Grimoire or a melee weapon.</summary>
    [Export] public bool RequiresAmmo { get; set; }

    [ExportGroup("Damage")]
    [Export] public float DamageBonus { get; set; }
    [Export] public float FireRateBonus { get; set; }
    /// <summary>Extra damage while the player is at or below half hearts.</summary>
    [Export] public float LowHealthDamageBonus { get; set; }
    /// <summary>Damage per point of Corruption — Sovereign's Mark.</summary>
    [Export] public float DamagePerCorruption { get; set; }

    [ExportGroup("Ammunition")]
    [Export] public float MagazineMultiplier { get; set; } = 1f;
    [Export] public float ReserveMultiplier { get; set; } = 1f;
    /// <summary>Added to reload weight. Negative makes Recitation cheaper (docs/02 §3.2).</summary>
    [Export] public float ReloadWeightDelta { get; set; }

    [ExportGroup("Projectile")]
    [Export] public float SpreadMultiplier { get; set; } = 1f;
    [Export] public float ProjectileSpeedMultiplier { get; set; } = 1f;
    [Export] public float ProjectileLifetimeMultiplier { get; set; } = 1f;
    [Export] public int PierceBonus { get; set; }
    [Export] public bool BouncesOffWalls { get; set; }
    /// <summary>Whispering Rounds: degrees per second of weak homing. docs/03 specifies 12,
    /// which curves a shot without making it undodgeable.</summary>
    [Export] public float HomingDegreesPerSecond { get; set; }

    [ExportGroup("Triggers")]
    /// <summary>Yellow Ink — Sanity restored per kill made with this weapon.</summary>
    [Export] public float KillSanityBonus { get; set; }

    [ExportGroup("Cost")]
    [Export] public int BaseGoldCost { get; set; } = 45;
    [Export] public int CorruptionOnApply { get; set; }
    /// <summary>The Unblinking Eye — you take this much more damage. Fractional damage is
    /// accumulated by the player rather than rounded, so half-heart granularity survives
    /// (see PlayerController.TakeHit).</summary>
    [Export] public float IncomingDamageBonus { get; set; }

    /// <summary>docs/08 §2.2 — prices scale by floor: x1.0 / 1.15 / 1.3 / 1.5 / 1.7 / 2.0.</summary>
    public static float FloorScale(int floor) => floor switch
    {
        <= 1 => 1.0f,
        2 => 1.15f,
        3 => 1.3f,
        4 => 1.5f,
        5 => 1.7f,
        _ => 2.0f,
    };

    public int CostAt(int floor, float shopPriceMultiplier = 1f)
        => Mathf.RoundToInt(BaseGoldCost * FloorScale(floor) * shopPriceMultiplier);

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id == "unnamed") return "inscription has no Id.";
        if (string.IsNullOrWhiteSpace(RulesText)) return $"{Id}: no RulesText — docs/03 §3.1 forbids blind buys.";

        // docs/03 §3.2 — the three tiers ARE the price bands. An inscription priced outside
        // its band silently breaks the gold budget docs/08 §1.2 is balanced against.
        int expected = Tier switch
        {
            InscriptionTier.Lesser => 45,
            InscriptionTier.Greater => 90,
            _ => 130,
        };
        if (BaseGoldCost != expected)
            return $"{Id}: {Tier} costs {BaseGoldCost}, but §3.2 prices that tier at {expected}.";

        // Forbidden is DEFINED as costing a point of Corruption. One that does not is just
        // a cheap Greater, and the tier stops meaning anything.
        if (Tier == InscriptionTier.Forbidden && CorruptionOnApply < 1)
            return $"{Id}: Forbidden inscriptions cost +1 Corruption (§3.2).";
        if (Tier != InscriptionTier.Forbidden && CorruptionOnApply > 0)
            return $"{Id}: only Forbidden inscriptions may cost Corruption (§3.2).";

        if (MagazineMultiplier <= 0f) return $"{Id}: MagazineMultiplier {MagazineMultiplier} would leave no magazine.";
        if (ReserveMultiplier <= 0f) return $"{Id}: ReserveMultiplier {ReserveMultiplier} would leave no reserve.";

        return null;
    }
}
