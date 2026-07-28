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

    public void Add(WeaponData data)
    {
        if (_weapons.Count >= MaxSlots) return;
        _weapons.Add(new Weapon(data));
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

    public void Tick(float dt)
    {
        for (int i = 0; i < _weapons.Count; i++) _weapons[i].Tick(dt);

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
                w.Data.Damage, w.Data.MeleeKnockback, out float killSanity);

            if (struck > 0)
            {
                // Rate-capped Sanity per hit. The cap is the difference between melee
                // being a sustain option and melee being the whole economy.
                float granted = 0f;
                for (int i = 0; i < struck; i++)
                {
                    if (GrantMeleeSanity(w.Data.MeleeSanityPerHit)) granted += w.Data.MeleeSanityPerHit;
                }
                if (granted > 0f) sanity.GainFromKill(granted);
            }
            if (killSanity > 0f) sanity.GainFromKill(killSanity);
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
