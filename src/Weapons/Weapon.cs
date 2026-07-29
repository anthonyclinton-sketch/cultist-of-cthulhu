using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Weapons;

/// <summary>
/// Runtime state for one carried weapon (docs/03). Plain class, not a Node — the player
/// carries three and they need no scene presence.
///
/// Owns Recitation, which post-F4 is the game's primary Sanity sink (docs/01 Pillar I).
/// The consequence worth stating: a player who cannot afford to reload cannot keep
/// shooting, and that — not "cannot dodge" — is now the intended failure state.
/// </summary>
public sealed class Weapon
{
    public WeaponData Data { get; }

    public int Magazine { get; private set; }
    public int Reserve { get; private set; }
    public bool IsReloading { get; private set; }

    /// <summary>0..1 through the reload. Drives the shrinking Perfect Recitation ring.</summary>
    public float ReloadProgress { get; private set; }

    /// <summary>True while the Perfect Recitation window is open (docs/02 §5.1).</summary>
    public bool PerfectWindowOpen { get; private set; }

    /// <summary>Set for one magazine after a successful Perfect Recitation: +15% damage.</summary>
    public bool PerfectBonusActive { get; private set; }

    public int PerfectRecitations { get; private set; }
    public int FailedRecitations { get; private set; }

    private float _fireCooldown;
    private float _reloadTimer;
    private bool _perfectConsumed;

    // ---------------------------------------------------------------- Loadout modifiers
    //
    // Pushed down by the holder each tick, sourced from the Sigil Circle. Plain floats
    // rather than a reference to SigilEffects on purpose: a weapon has no business knowing
    // what a ley line is, and the day Inscriptions also modify these numbers the weapon
    // should not have to learn about them either. It reads two multipliers and one bonus.

    /// <summary>Global damage multiplier — sigils, and the player's own conditional
    /// bonuses (low health, Corruption scaling) already folded in by the caller.</summary>
    public float DamageMultiplier { get; set; } = 1f;
    public float FireRateMultiplier { get; set; } = 1f;
    /// <summary>Extra fraction refunded by a Perfect Recitation, on top of the base half.</summary>
    public float PerfectRefundBonus { get; set; }

    // ---------------------------------------------------------------- Inscriptions
    //
    // docs/03 §3. Held as a LIST and projected into effective stats on read, never applied
    // destructively to the weapon. There is therefore no "already applied" state to get
    // wrong, no double-application on a reload path, and removing one is exact rather than
    // an attempt to undo arithmetic.

    private readonly List<InscriptionData> _inscriptions = new(3);
    public IReadOnlyList<InscriptionData> Inscriptions => _inscriptions;
    public int InscriptionSlots => Data.InscriptionSlots;
    public bool HasFreeSlot => _inscriptions.Count < InscriptionSlots;

    /// <summary>
    /// Why this inscription cannot go on this weapon, or null if it can.
    ///
    /// Returns the REASON rather than a bool because docs/03 §3.4 requires the bench to
    /// grey a conflicting offer out with an explicit explanation — and a reason
    /// reconstructed in the UI drifts from the rule that produced it.
    /// </summary>
    public string? RejectReason(InscriptionData ins)
    {
        foreach (InscriptionData held in _inscriptions)
        {
            if (held.Id == ins.Id) return $"{Data.DisplayName} already carries {ins.DisplayName}.";
            if (ins.ConflictGroup.Length > 0 && held.ConflictGroup == ins.ConflictGroup)
                return $"conflicts with {held.DisplayName}.";
        }

        if (ins.RequiresAmmo && (Data.IsMelee || Data.SanityPerShot > 0f))
            return $"{Data.DisplayName} does not use ammunition.";

        return null;
    }

    /// <summary>Etch it on. The caller has already taken the gold and checked the slot.</summary>
    public void AddInscription(InscriptionData ins)
    {
        _inscriptions.Add(ins);

        // A magazine modifier changes the size of the thing currently in the gun. Topping
        // up to the new maximum is the generous reading and the right one: the player just
        // paid for a bigger magazine and should not have to reload to see it.
        Magazine = Mathf.Min(Magazine, EffectiveMagazineSize);
        Reserve = Mathf.Min(Reserve, ReserveCap);
    }

    /// <summary>Replace the inscription in a slot. docs/03 §3.1 — overwriting costs 1.5x
    /// and refunds nothing, which is what makes a mistake recoverable rather than free.</summary>
    public InscriptionData? ReplaceInscription(int slot, InscriptionData ins)
    {
        if (slot < 0 || slot >= _inscriptions.Count) return null;
        InscriptionData old = _inscriptions[slot];
        _inscriptions[slot] = ins;
        return old;
    }

    // ---------------------------------------------------------------- Effective stats

    public float EffectiveDamage
    {
        get
        {
            float bonus = 0f;
            foreach (InscriptionData i in _inscriptions) bonus += i.DamageBonus;
            return Data.Damage * (1f + bonus)
                   * (PerfectBonusActive ? 1.15f : 1f)
                   * Mathf.Max(0.05f, DamageMultiplier);
        }
    }

    public float EffectiveFireRate
    {
        get
        {
            float bonus = 0f;
            foreach (InscriptionData i in _inscriptions) bonus += i.FireRateBonus;
            return Mathf.Max(0.05f, Data.FireRate * (1f + bonus) * Mathf.Max(0.05f, FireRateMultiplier));
        }
    }

    public int EffectiveMagazineSize
    {
        get
        {
            float m = 1f;
            foreach (InscriptionData i in _inscriptions) m *= i.MagazineMultiplier;
            return Mathf.Max(1, Mathf.RoundToInt(Data.MagazineSize * m));
        }
    }

    public int EffectiveTotalReserve
    {
        get
        {
            float m = 1f;
            foreach (InscriptionData i in _inscriptions) m *= i.ReserveMultiplier;
            return Mathf.Max(1, Mathf.RoundToInt(Data.TotalReserveRounds * m));
        }
    }

    /// <summary>
    /// Recitation cost after Light Etching and friends. Floored well above zero: docs/04
    /// §8.6's rule is a general one, and reload is the primary Sanity sink post-F4 — an
    /// inscription stack that made it free would repeal Pillar I as surely as any sigil.
    /// </summary>
    public float EffectiveReloadCost
    {
        get
        {
            float weight = Data.ReloadWeight;
            foreach (InscriptionData i in _inscriptions) weight += i.ReloadWeightDelta;
            return Core.Tune.SanityReciteCostPerWeight * Mathf.Max(0.2f, weight);
        }
    }

    public float EffectiveSpread
    {
        get
        {
            float m = 1f;
            foreach (InscriptionData i in _inscriptions) m *= i.SpreadMultiplier;
            return Data.SpreadDegrees * m;
        }
    }

    public float EffectiveProjectileSpeed
    {
        get
        {
            float m = 1f;
            foreach (InscriptionData i in _inscriptions) m *= i.ProjectileSpeedMultiplier;
            return Data.ProjectileSpeed * m;
        }
    }

    public float EffectiveProjectileLifetime
    {
        get
        {
            float m = 1f;
            foreach (InscriptionData i in _inscriptions) m *= i.ProjectileLifetimeMultiplier;
            return Data.ProjectileLifetime * m;
        }
    }

    public int EffectivePierce
    {
        get
        {
            int p = Data.Pierce;
            foreach (InscriptionData i in _inscriptions) p += i.PierceBonus;
            return p;
        }
    }

    public bool Bounces
    {
        get
        {
            foreach (InscriptionData i in _inscriptions) if (i.BouncesOffWalls) return true;
            return false;
        }
    }

    /// <summary>Sanity granted by a kill made with this weapon (Yellow Ink).</summary>
    public float KillSanityBonus
    {
        get
        {
            float s = 0f;
            foreach (InscriptionData i in _inscriptions) s += i.KillSanityBonus;
            return s;
        }
    }

    /// <summary>Extra damage the player takes while carrying this weapon (The Unblinking Eye).</summary>
    public float IncomingDamageBonus
    {
        get
        {
            float s = 0f;
            foreach (InscriptionData i in _inscriptions) s += i.IncomingDamageBonus;
            return s;
        }
    }

    public float LowHealthDamageBonus
    {
        get
        {
            float s = 0f;
            foreach (InscriptionData i in _inscriptions) s += i.LowHealthDamageBonus;
            return s;
        }
    }

    public float DamagePerCorruption
    {
        get
        {
            float s = 0f;
            foreach (InscriptionData i in _inscriptions) s += i.DamagePerCorruption;
            return s;
        }
    }

    /// <summary>Homing overrides the authored behaviour when Whispering Rounds is etched on.</summary>
    private bool TryHoming(out float turnRadiansPerSecond)
    {
        float best = 0f;
        foreach (InscriptionData i in _inscriptions)
            if (i.HomingDegreesPerSecond > best) best = i.HomingDegreesPerSecond;
        turnRadiansPerSecond = Mathf.DegToRad(best);
        return best > 0f;
    }

    /// <summary>docs/02 §5.1 — 0.16s window, deliberately generous to learn, hard under pressure.</summary>
    private const float PerfectWindowDuration = 0.16f;
    private const float PerfectWindowStartFraction = 0.55f;

    public Weapon(WeaponData data)
    {
        Data = data;
        Magazine = data.MagazineSize;
        Reserve = data.IsBoundArm ? int.MaxValue : data.TotalReserveRounds;
    }

    /// <summary>Simplify the awkward case: a bound arm's reserve is conceptually infinite,
    /// and clamping it against an effective maximum would make it finite.</summary>
    private int ReserveCap => Data.IsBoundArm ? int.MaxValue : EffectiveTotalReserve;

    public bool IsEmpty => Magazine <= 0;
    public bool HasReserve => Reserve > 0;
    public float ReserveFraction => Data.IsBoundArm ? 1f
        : EffectiveTotalReserve <= 0 ? 0f
        : Mathf.Clamp(Reserve / (float)EffectiveTotalReserve, 0f, 1f);

    public void Tick(float dt)
    {
        if (_fireCooldown > 0f) _fireCooldown -= dt;
        if (!IsReloading) return;

        _reloadTimer += dt;
        ReloadProgress = Mathf.Clamp(_reloadTimer / Data.ReloadDuration, 0f, 1f);

        float windowStart = Data.ReloadDuration * PerfectWindowStartFraction;
        PerfectWindowOpen = !_perfectConsumed
                            && _reloadTimer >= windowStart
                            && _reloadTimer <= windowStart + PerfectWindowDuration;

        if (_reloadTimer >= Data.ReloadDuration) CompleteReload();
    }

    // ---------------------------------------------------------------- Firing

    public bool CanFire(SanitySystem sanity)
    {
        if (IsReloading || _fireCooldown > 0f) return false;
        if (Data.IsMelee) return true;
        if (Data.SanityPerShot > 0f) return sanity.CanAfford(Data.SanityPerShot);
        return Magazine > 0;
    }

    /// <summary>
    /// Fire. Returns the number of projectiles emitted (0 if the shot did not happen).
    /// Melee returns 0 and is resolved by the caller via <see cref="Data"/>.MeleeReach.
    /// </summary>
    public int Fire(Vector2 origin, Vector2 direction, BulletManager bullets, SanitySystem sanity, Rng rng)
    {
        if (!CanFire(sanity)) return 0;

        _fireCooldown = 1f / EffectiveFireRate;

        // Grimoires spend Sanity instead of ammo. This is the mechanic docs/03 §2 flags
        // as an untested second economy — a 30-shot room on Cantrip: Withering costs
        // ~120 Sanity before any Banish, which is most of a room's budget.
        if (Data.SanityPerShot > 0f)
        {
            if (!sanity.TrySpend(Data.SanityPerShot)) return 0;
        }
        else if (!Data.IsMelee)
        {
            Magazine--;
        }

        if (Data.IsMelee) return 0;

        float damage = EffectiveDamage;
        float baseAngle = Mathf.Atan2(direction.Y, direction.X);
        float spread = Mathf.DegToRad(EffectiveSpread);
        float speed = EffectiveProjectileSpeed;

        var flags = BulletFlags.PlayerOwned;
        if (EffectivePierce > 0) flags |= BulletFlags.Piercing;
        // Rebounding Rune. Worth almost nothing until walls existed to bounce off — before
        // wall collision the flag only reflected at the arena bounds, hundreds of pixels
        // outside any room the player was standing in.
        if (Bounces) flags |= BulletFlags.BouncesOffWalls;

        BulletBehaviour behaviour = Data.Behaviour;
        float p0 = Data.BehaviourP0;
        float p1 = Data.BehaviourP1;
        if (TryHoming(out float turn))
        {
            behaviour = BulletBehaviour.Homing;
            p0 = turn;
            p1 = EffectiveProjectileLifetime;
        }

        for (int i = 0; i < Data.ProjectilesPerShot; i++)
        {
            float angle = spread > 0f
                ? baseAngle + rng.Range(-spread * 0.5f, spread * 0.5f)
                : baseAngle;

            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            bullets.Spawn(
                position: origin,
                velocity: dir * speed,
                radius: Data.ProjectileRadius,
                lifetime: EffectiveProjectileLifetime,
                color: Data.Colour,
                renderSize: Data.ProjectileRenderSize,
                flags: flags,
                behaviour: behaviour,
                bhParam0: p0,
                bhParam1: p1,
                damage: damage);
        }

        return Data.ProjectilesPerShot;
    }

    // ---------------------------------------------------------------- Recitation

    public bool NeedsReload => !Data.IsMelee && Data.SanityPerShot <= 0f && Magazine < EffectiveMagazineSize;

    /// <summary>
    /// Begin a reload. Costs Sanity up front (docs/02 §3.2) — if the player cannot pay,
    /// this returns false and the weapon stays empty. That is the F4 failure state.
    /// </summary>
    public bool TryBeginReload(SanitySystem sanity)
    {
        if (IsReloading || !NeedsReload) return false;
        if (!Data.IsBoundArm && Reserve <= 0) return false;
        if (!sanity.TrySpend(EffectiveReloadCost)) return false;

        IsReloading = true;
        _reloadTimer = 0f;
        ReloadProgress = 0f;
        _perfectConsumed = false;
        PerfectBonusActive = false;
        return true;
    }

    /// <summary>
    /// Player pressed Recite while already reloading. Inside the window this refunds half
    /// the Sanity and grants +15% damage for the magazine; outside it, the attempt is
    /// simply wasted (no penalty beyond losing the refund).
    /// </summary>
    public bool TryPerfectRecitation(SanitySystem sanity)
    {
        if (!IsReloading || _perfectConsumed) return false;
        _perfectConsumed = true;

        if (!PerfectWindowOpen)
        {
            FailedRecitations++;
            return false;
        }

        // Base refund is half the cost; the Yellow Ledger sigil raises the fraction, never
        // to the whole cost — a free reload would repeal Pillar I (docs/04 §8.6).
        sanity.GainPiercing(EffectiveReloadCost * Mathf.Min(0.9f, 0.5f * (1f + PerfectRefundBonus)));
        PerfectBonusActive = true;
        PerfectRecitations++;
        return true;
    }

    private void CompleteReload()
    {
        int needed = EffectiveMagazineSize - Magazine;
        int taken = Data.IsBoundArm ? needed : Mathf.Min(needed, Reserve);

        Magazine += taken;
        if (!Data.IsBoundArm) Reserve -= taken;

        IsReloading = false;
        PerfectWindowOpen = false;
        ReloadProgress = 0f;
        _reloadTimer = 0f;
    }

    /// <summary>Restore a carried reserve across a floor boundary. Ammunition is a run-length
    /// pressure (docs/00 §1.2) — refilling it at every transition would remove the rotation
    /// the whole ammo economy exists to force.</summary>
    public void SetReserve(int rounds)
    {
        if (Data.IsBoundArm) return;
        Reserve = Mathf.Clamp(rounds, 0, ReserveCap);
    }

    public void AddReserve(int rounds)
    {
        if (Data.IsBoundArm) return;
        Reserve = Mathf.Min(ReserveCap, Reserve + rounds);
    }

    /// <summary>Consumed when a fresh magazine is spent, so the bonus lasts exactly one.</summary>
    public void ClearPerfectBonusIfMagazineSpent()
    {
        if (PerfectBonusActive && Magazine <= 0) PerfectBonusActive = false;
    }
}
