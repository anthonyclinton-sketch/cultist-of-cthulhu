using CultistOfCthulhu.Bullets;
using Godot;

namespace CultistOfCthulhu.Weapons;

public enum WeaponTier { D, C, B, A, S }

/// <summary>docs/03 §2 — six families, each with a distinct relationship to Sanity.</summary>
public enum WeaponFamily
{
    RelicArm,       // period firearms; cheap on Sanity
    DefiledArm,     // guns that have been done something to
    Artefact,       // dug up, mechanically unique
    Grimoire,       // fires FROM Sanity — no ammo, every shot is a purchase
    Devotion,       // melee; no ammo, restores Sanity on hit
    Aberrant,       // joke tier
}

/// <summary>
/// docs/03 §1.3. All tuning lives here as a .tres, per docs/09 §5.
///
/// RELOAD WEIGHT is the most important field in this file. Post-F4 (docs/01 Pillar I)
/// reload is the PRIMARY Sanity sink, so this single number decides how much of the
/// low-Sanity ladder a player carrying this weapon will ever see. It is the main lever
/// for weapon identity on the resource axis, not the damage axis.
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string DisplayName { get; set; } = "Unnamed";
    [Export] public WeaponTier Tier { get; set; } = WeaponTier.C;
    [Export] public WeaponFamily Family { get; set; } = WeaponFamily.RelicArm;
    [Export(PropertyHint.MultilineText)] public string CodexText { get; set; } = "";

    [ExportGroup("Damage")]
    [Export] public float Damage { get; set; } = 10f;
    [Export(PropertyHint.Range, "1,24,1")] public int ProjectilesPerShot { get; set; } = 1;
    [Export] public float SpreadDegrees { get; set; }
    /// <summary>Shots per second.</summary>
    [Export] public float FireRate { get; set; } = 4f;

    [ExportGroup("Ammunition")]
    [Export] public int MagazineSize { get; set; } = 6;
    [Export] public int ReserveMagazines { get; set; } = 6;
    [Export] public float ReloadDuration { get; set; } = 0.8f;

    /// <summary>
    /// Multiplier on the Sanity cost of Recitation (docs/02 §3.2: 12 x weight).
    /// 0.5 = a pistol at 6 Sanity. 2.0 = an elephant gun at 24.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,2.0,0.1")] public float ReloadWeight { get; set; } = 1.0f;

    /// <summary>Grimoires only: Sanity spent per shot instead of consuming ammo.
    /// This breaks the "shooting is free" model the whole economy is taught through —
    /// see the warning in docs/03 §2 Family IV.</summary>
    [Export] public float SanityPerShot { get; set; }

    [ExportGroup("Projectile")]
    [Export] public float ProjectileSpeed { get; set; } = 420f;
    [Export] public float ProjectileRadius { get; set; } = 3f;
    [Export] public float ProjectileRenderSize { get; set; } = 7f;
    [Export] public float ProjectileLifetime { get; set; } = 1.4f;
    [Export] public int Pierce { get; set; }
    /// <summary>docs/10 §1.3 R1 — player projectiles are ALWAYS warm. Never negotiable.</summary>
    [Export] public Color Colour { get; set; } = new("FFB347");
    [Export] public BulletBehaviour Behaviour { get; set; } = BulletBehaviour.Straight;
    [Export] public float BehaviourP0 { get; set; }
    [Export] public float BehaviourP1 { get; set; }

    [ExportGroup("Melee")]
    [Export] public bool IsMelee { get; set; }
    /// <summary>
    /// docs/03 §2 Family V: reach MUST exceed the enemy contact radius + 0.5 units, or
    /// the player pays health to use their own weapon. Validated below.
    /// </summary>
    [Export] public float MeleeReach { get; set; } = 34f;
    [Export] public float MeleeArcDegrees { get; set; } = 90f;
    [Export] public float MeleeKnockback { get; set; } = 200f;
    /// <summary>Sanity restored per hit, before the per-enemy rate cap.</summary>
    [Export] public float MeleeSanityPerHit { get; set; } = 3f;

    [ExportGroup("Meta")]
    [Export(PropertyHint.Range, "1,3,1")] public int InscriptionSlots { get; set; } = 1;
    [Export] public int CorruptionOnPickup { get; set; }
    /// <summary>Character-bound starter: infinite ammo, cannot be dropped (docs/03 §1.1).</summary>
    [Export] public bool IsBoundArm { get; set; }

    public float SanityCostToReload => Core.Tune.SanityReciteCostPerWeight * ReloadWeight;
    public int TotalReserveRounds => MagazineSize * ReserveMagazines;

    /// <summary>
    /// Author-time enforcement of the rules that are easy to violate silently.
    /// Returns null when valid.
    /// </summary>
    public string? Validate()
    {
        // docs/03 §2 Family V, the fix for the contact-damage finding. Enemy contact
        // radius is ~10px; reach must clear it by half a unit (8px).
        if (IsMelee && MeleeReach < 18f)
            return $"Melee reach {MeleeReach} is inside the enemy contact radius — this weapon damages its user.";

        // docs/10 §1.3 R1. A cool-hued player projectile reads as incoming fire.
        if (!IsMelee && Colour.B > Colour.R)
            return "R1 violation: player projectiles must be warm-hued.";

        if (Family == WeaponFamily.Grimoire && SanityPerShot <= 0f)
            return "A Grimoire must have SanityPerShot > 0 — that is what makes it a Grimoire.";

        if (Family != WeaponFamily.Grimoire && SanityPerShot > 0f)
            return "Only Grimoires (or a weapon carrying Vessel Rune) may fire from Sanity.";

        return null;
    }
}
