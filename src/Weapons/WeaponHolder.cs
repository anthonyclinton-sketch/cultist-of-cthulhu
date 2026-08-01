using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Weapons;

/// <summary>
/// The player's carried weapons (docs/03 §1.1): three slots, one of which is the Bound Arm
/// — infinite ammo, cannot be dropped. The Bound Arm is the safety net that makes running
/// dry survivable rather than fatal.
/// </summary>
public sealed class WeaponHolder
{
    public const int MaxSlots = 3;

    private readonly List<Weapon> _weapons = new(MaxSlots);
    private int _active;

    /// <summary>Per-enemy melee Sanity rate cap (docs/03 §2 Family V): 12 per enemy per 3s.
    /// Without this, a fast melee weapon is an uncapped Sanity printer.</summary>
    private readonly Dictionary<int, float> _meleeSanityBudget = new();
    private float _meleeBudgetResetTimer;

    public Weapon Active => _weapons[_active];
    public int Count => _weapons.Count;
    public IReadOnlyList<Weapon> Weapons => _weapons;

    public float TotalSanitySpentOnReloads { get; private set; }
    public int ReloadsAttempted { get; private set; }
    public int ReloadsDenied { get; private set; }

    /// <summary>Add a weapon. Returns it, so a caller restoring a run can etch its
    /// inscriptions straight back on without looking it up again.</summary>
    public Weapon? Add(WeaponData data)
    {
        if (_weapons.Count >= MaxSlots) return null;
        var w = new Weapon(data);
        _weapons.Add(w);
        return w;
    }

    /// <summary>
    /// Swap the active weapon for a new one, in place.
    ///
    /// This is what makes a fourth weapon possible at all (docs/03 §1.1: "a fourth pickup
    /// forces a swap prompt"). It replaces IN PLACE rather than removing and appending so
    /// the new weapon lands in the slot the player was already looking at — an acquisition
    /// that silently reorders the loadout makes the next Q press do something different from
    /// what the player learned.
    ///
    /// Refuses to drop a Bound Arm (docs/03 §1.1 — it cannot be dropped). The caller checks
    /// this too, so it can say *why*; the check is here as well because it is an invariant of
    /// the loadout, not of the shop.
    ///
    /// The replaced weapon's Inscriptions go with it. docs/03 §3.4 makes that the rule and
    /// the review's transfer affordance (§3.1, 60 gold per Inscription) is the escape hatch —
    /// which is not built, so for now a swap is a genuine loss and the prompt must say so.
    /// </summary>
    public bool ReplaceActive(WeaponData data)
    {
        if (_weapons.Count == 0) return Add(data) is not null;
        if (_weapons[_active].Data.IsBoundArm) return false;

        _weapons[_active] = new Weapon(data);
        return true;
    }

    /// <summary>Drop everything. Used when a floor restores a run's loadout.</summary>
    public void Clear()
    {
        _weapons.Clear();
        _active = 0;
        _meleeSanityBudget.Clear();
    }

    public void SetActive(int index)
    {
        if (index < 0 || index >= _weapons.Count) return;
        _active = index;
    }

    public void CycleActive()
    {
        if (_weapons.Count == 0) return;
        _active = (_active + 1) % _weapons.Count;
    }

    /// <summary>
    /// Loadout modifiers, pushed down to every carried weapon each tick.
    ///
    /// Set by the player rather than read from the Sigil Circle here, because two of the
    /// three are CONDITIONAL on player state the holder cannot see — the low-health bonus
    /// and Corruption scaling both depend on the player, and a weapon asking the circle
    /// directly would get the unconditional number and be quietly wrong exactly when the
    /// sigil is supposed to matter.
    /// </summary>
    public float DamageMultiplier { get; set; } = 1f;
    public float FireRateMultiplier { get; set; } = 1f;
    public float PerfectRefundBonus { get; set; }

    public void Tick(float dt)
    {
        for (int i = 0; i < _weapons.Count; i++)
        {
            Weapon w = _weapons[i];
            w.DamageMultiplier = DamageMultiplier;
            w.FireRateMultiplier = FireRateMultiplier;
            w.PerfectRefundBonus = PerfectRefundBonus;
            w.Tick(dt);
        }

        _meleeBudgetResetTimer -= dt;
        if (_meleeBudgetResetTimer <= 0f)
        {
            _meleeBudgetResetTimer = 3f;
            _meleeSanityBudget.Clear();
        }
    }

    /// <summary>
    /// Fire the active weapon. Handles melee arc resolution and the Sanity rate cap.
    /// Returns true if anything happened.
    /// </summary>
    public bool TryFire(Vector2 origin, Vector2 aim, BulletManager playerBullets,
                        SanitySystem sanity, EnemyManager enemies, Rng rng)
    {
        Weapon w = Active;
        if (!w.CanFire(sanity)) return false;

        if (w.Data.IsMelee)
        {
            w.Fire(origin, aim, playerBullets, sanity, rng);   // consumes the cooldown only

            int struck = enemies.ResolveMeleeArc(
                origin, aim, w.Data.MeleeReach, w.Data.MeleeArcDegrees,
                w.EffectiveDamage, w.Data.MeleeKnockback);

            if (struck > 0)
            {
                // Rate-capped Sanity per HIT. The cap is the difference between melee
                // being a sustain option and melee being the whole economy.
                // Kill Sanity is handled separately via PendingSanityReward, so that
                // melee kills get the same chain and i-frame multipliers as gun kills.
                float granted = 0f;
                for (int i = 0; i < struck; i++)
                {
                    if (GrantMeleeSanity(w.Data.MeleeSanityPerHit)) granted += w.Data.MeleeSanityPerHit;
                }
                if (granted > 0f) sanity.GainFromKill(granted);
            }
            return true;
        }

        return w.Fire(origin, aim, playerBullets, sanity, rng) > 0;
    }

    /// <summary>
    /// Melee Sanity is capped at 12 per enemy per 3 seconds. Tracked as a single shared
    /// budget for M1 — per-target tracking needs a target id threaded through the melee
    /// resolution, which is an M2 refinement once enemies carry status effects anyway.
    /// </summary>
    private bool GrantMeleeSanity(float amount)
    {
        const int SharedKey = 0;
        const float CapPerWindow = 12f;

        _meleeSanityBudget.TryGetValue(SharedKey, out float used);
        if (used + amount > CapPerWindow) return false;
        _meleeSanityBudget[SharedKey] = used + amount;
        return true;
    }

    /// <summary>
    /// Recite. Post-F4 this is the primary Sanity sink, so a denial here is the game's
    /// intended failure state and is counted for M1 metric 3.
    /// </summary>
    public bool TryRecite(SanitySystem sanity)
    {
        Weapon w = Active;

        if (w.IsReloading) return w.TryPerfectRecitation(sanity);
        if (!w.NeedsReload) return false;

        ReloadsAttempted++;
        float costBefore = w.Data.SanityCostToReload;

        if (w.TryBeginReload(sanity))
        {
            TotalSanitySpentOnReloads += costBefore;
            return true;
        }

        ReloadsDenied++;
        return false;
    }

    /// <summary>Auto-reload when a magazine empties, after a delay that lets the player
    /// pre-empt manually and go for a Perfect Recitation (docs/02 §5.1).</summary>
    public void TickAutoReload(float dt, SanitySystem sanity, ref float autoReloadDelay)
    {
        Weapon w = Active;
        if (!w.IsEmpty || w.IsReloading || !w.NeedsReload)
        {
            autoReloadDelay = 0f;
            return;
        }

        autoReloadDelay += dt;
        if (autoReloadDelay < 0.25f) return;

        autoReloadDelay = 0f;
        ReloadsAttempted++;
        float cost = w.Data.SanityCostToReload;
        if (w.TryBeginReload(sanity)) TotalSanitySpentOnReloads += cost;
        else ReloadsDenied++;
    }
}
