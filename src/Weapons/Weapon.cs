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

    /// <summary>docs/02 §5.1 — 0.16s window, deliberately generous to learn, hard under pressure.</summary>
    private const float PerfectWindowDuration = 0.16f;
    private const float PerfectWindowStartFraction = 0.55f;

    public Weapon(WeaponData data)
    {
        Data = data;
        Magazine = data.MagazineSize;
        Reserve = data.IsBoundArm ? int.MaxValue : data.TotalReserveRounds;
    }

    public bool IsEmpty => Magazine <= 0;
    public bool HasReserve => Reserve > 0;
    public float ReserveFraction => Data.IsBoundArm ? 1f
        : Data.TotalReserveRounds <= 0 ? 0f
        : Mathf.Clamp(Reserve / (float)Data.TotalReserveRounds, 0f, 1f);

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

        _fireCooldown = 1f / Mathf.Max(0.01f, Data.FireRate);

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

        float damage = Data.Damage * (PerfectBonusActive ? 1.15f : 1f);
        float baseAngle = Mathf.Atan2(direction.Y, direction.X);
        float spread = Mathf.DegToRad(Data.SpreadDegrees);

        var flags = BulletFlags.PlayerOwned;
        if (Data.Pierce > 0) flags |= BulletFlags.Piercing;

        for (int i = 0; i < Data.ProjectilesPerShot; i++)
        {
            float angle = Data.ProjectilesPerShot > 1
                ? baseAngle + rng.Range(-spread * 0.5f, spread * 0.5f)
                : baseAngle + (spread > 0f ? rng.Range(-spread * 0.5f, spread * 0.5f) : 0f);

            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            bullets.Spawn(
                position: origin,
                velocity: dir * Data.ProjectileSpeed,
                radius: Data.ProjectileRadius,
                lifetime: Data.ProjectileLifetime,
                color: Data.Colour,
                renderSize: Data.ProjectileRenderSize,
                flags: flags,
                behaviour: Data.Behaviour,
                bhParam0: Data.BehaviourP0,
                bhParam1: Data.BehaviourP1,
                damage: damage);
        }

        return Data.ProjectilesPerShot;
    }

    // ---------------------------------------------------------------- Recitation

    public bool NeedsReload => !Data.IsMelee && Data.SanityPerShot <= 0f && Magazine < Data.MagazineSize;

    /// <summary>
    /// Begin a reload. Costs Sanity up front (docs/02 §3.2) — if the player cannot pay,
    /// this returns false and the weapon stays empty. That is the F4 failure state.
    /// </summary>
    public bool TryBeginReload(SanitySystem sanity)
    {
        if (IsReloading || !NeedsReload) return false;
        if (!Data.IsBoundArm && Reserve <= 0) return false;
        if (!sanity.TrySpend(Data.SanityCostToReload)) return false;

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

        sanity.GainPiercing(Data.SanityCostToReload * 0.5f);
        PerfectBonusActive = true;
        PerfectRecitations++;
        return true;
    }

    private void CompleteReload()
    {
        int needed = Data.MagazineSize - Magazine;
        int taken = Data.IsBoundArm ? needed : Mathf.Min(needed, Reserve);

        Magazine += taken;
        if (!Data.IsBoundArm) Reserve -= taken;

        IsReloading = false;
        PerfectWindowOpen = false;
        ReloadProgress = 0f;
        _reloadTimer = 0f;
    }

    public void AddReserve(int rounds)
    {
        if (Data.IsBoundArm) return;
        Reserve = Mathf.Min(Data.TotalReserveRounds, Reserve + rounds);
    }

    /// <summary>Consumed when a fresh magazine is spent, so the bonus lasts exactly one.</summary>
    public void ClearPerfectBonusIfMagazineSpent()
    {
        if (PerfectBonusActive && Magazine <= 0) PerfectBonusActive = false;
    }
}
