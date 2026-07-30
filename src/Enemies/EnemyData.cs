using CultistOfCthulhu.Bullets;
using Godot;

namespace CultistOfCthulhu.Enemies;

/// <summary>
/// docs/05 §2. Encounters are composed by ROLE, not by count — a room of pure Turrets is
/// a Sanity death spiral, which is why the populator enforces a fodder floor.
/// </summary>
public enum EnemyRole
{
    Fodder,     // dies fast, refills Sanity, creates positioning pressure
    Turret,     // static or slow, dense patterns, must be prioritised
    Rusher,     // closes distance, punishes camping
    Zoner,      // denies areas
    Support,    // buffs or shields others — the priority target
    Elite,      // a mini-encounter within a room
}

[GlobalClass]
public partial class EnemyData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string DisplayName { get; set; } = "Unnamed";
    [Export] public EnemyRole Role { get; set; } = EnemyRole.Fodder;
    [Export(PropertyHint.MultilineText)] public string CodexText { get; set; } = "";
    [Export] public Color Tint { get; set; } = new("B0B8C4");
    [Export] public float BodyRadius { get; set; } = 10f;

    [ExportGroup("Stats")]
    [Export] public float MaxHealth { get; set; } = 30f;
    [Export] public float MoveSpeed { get; set; } = 42f;
    [Export] public float ContactDamage { get; set; } = 0.5f;

    /// <summary>
    /// docs/05 §3 — "swims through water tiles at 2× speed". True for Deep Ones and the rest
    /// of Mother Hydra's brood; false for everything that has to wade, which is the player and
    /// every Undercroft enemy.
    ///
    /// Authored rather than inferred from the floor an enemy belongs to: the tide is what
    /// makes floor 2 a floor rather than a palette, and which side of it a creature is on is
    /// a design statement about that creature.
    /// </summary>
    [Export] public bool SwimsInWater { get; set; }

    /// <summary>
    /// Cost against the room's Dread Budget (docs/06 §6.1). Not derived from health —
    /// authored, so a cheap-but-annoying enemy can be priced like an expensive one.
    /// </summary>
    [Export] public float DreadCost { get; set; } = 10f;

    /// <summary>
    /// Sanity refunded on death (docs/02 §3.3, threat tiers 4/8/14/25).
    /// Post-F4 this is what funds the player's reloads, so it is the single number that
    /// decides whether a room is survivable with a heavy weapon.
    /// </summary>
    [Export] public float SanityValue { get; set; } = 6f;

    [ExportGroup("Behaviour")]
    /// <summary>Distance the enemy tries to hold from the player, in px.</summary>
    [Export] public float PreferredRange { get; set; } = 120f;
    /// <summary>How far it will engage from at all.</summary>
    [Export] public float AggroRange { get; set; } = 900f;
    [Export] public float AttackCooldown { get; set; } = 2.0f;
    [Export] public float AttackCooldownVariance { get; set; } = 0.4f;
    /// <summary>Seconds of vulnerable recovery after a volley. The player's punish window.</summary>
    [Export] public float RecoverySeconds { get; set; } = 0.5f;
    /// <summary>Rushers only: contact-lunge speed multiplier.</summary>
    [Export] public float LungeMultiplier { get; set; } = 1f;

    [ExportGroup("Attacks")]
    [Export] public PatternData? PrimaryAttack { get; set; }
    /// <summary>Added at Corruption 3+ (docs/02 §7.2, "Awakened"). Authored, never generated.</summary>
    [Export] public PatternData? AwakenedAttack { get; set; }

    public string? Validate()
    {
        if (Role != EnemyRole.Support && PrimaryAttack is null && ContactDamage <= 0f)
            return "Enemy has no attack pattern and no contact damage — it cannot threaten the player.";

        if (MaxHealth <= 0f) return "MaxHealth must be positive.";

        // docs/05 §2: fodder must actually die fast, or it cannot serve its role of
        // funding the player's Sanity.
        if (Role == EnemyRole.Fodder && MaxHealth > 45f)
            return $"Fodder with {MaxHealth} HP will not die fast enough to fund the Sanity economy.";

        return PrimaryAttack?.Validate();
    }
}
