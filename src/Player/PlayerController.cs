using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Meta;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Player;

public enum BlinkPhase { None, Startup, Invulnerable, Recovery }

/// <summary>
/// docs/02 §1, §4.
///
/// Deliberately NOT physics-driven — no RigidBody2D, velocity assigned directly. Any
/// "weight" the character has is animation, never simulation (docs/02 §1.1).
///
/// Post-F4 (docs/01 Pillar I) Blink Step is FREE; the Sanity economy lives in Recitation,
/// Banish and Open the Eye. The frame data below is unchanged by that decision, because
/// the recovery tail and cooldown are now the ONLY brake on dodge spam.
/// </summary>
public sealed partial class PlayerController : CharacterBody2D
{
    public SanitySystem Sanity { get; } = new();
    public WeaponHolder Weapons { get; } = new();
    public Telemetry? Telemetry { get; set; }

    public BlinkPhase Phase { get; private set; } = BlinkPhase.None;
    public bool IsInvulnerable => Phase == BlinkPhase.Invulnerable || _damageIFrames > 0f;

    public Vector2 AimDirection { get; private set; } = Vector2.Right;

    /// <summary>docs/02 §2 — hearts, half-heart granularity. Hits are events, not chip damage.</summary>
    public float Hearts { get; private set; } = 3f;
    public float MaxHearts { get; private set; } = 3f;
    public bool IsDead => Hearts <= 0f;

    public int HitsTaken { get; private set; }

    /// <summary>M1 metric 3. Post-F4 this counts denied RELOADS and BANISHES — the dodge
    /// is free, so "could not afford to keep shooting" is the failure state now.</summary>
    public int DeniedSustainCount { get; private set; }

    /// <summary>Retained for Build B (the metered-dodge control arm). Zero unless
    /// Tune.SanityBlinkCost is flipped back to 18.</summary>
    public int DeniedBlinkCount { get; private set; }

    public BulletManager? EnemyBullets { get; set; }
    public BulletManager? PlayerBullets { get; set; }
    public EnemyManager? Enemies { get; set; }

    private Rng _rng = null!;
    private int _blinkFrame;
    private Vector2 _blinkVelocity;
    private float _blinkCooldown;
    private float _damageIFrames;
    private float _banishHoldTime;
    private bool _banishConsumed;
    private float _autoReloadDelay;
    private Vector2 _smoothedVelocity;
    private float _contactDamageCooldown;

    // Feel (docs/02 §8) — hit stop is applied by the arena, which owns the time scale.
    public float PendingHitStop { get; private set; }

    [Signal] public delegate void BanishedEventHandler();
    [Signal] public delegate void EyeOpenedEventHandler();
    [Signal] public delegate void AscendedEventHandler();
    [Signal] public delegate void DiedEventHandler();

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "player");
    }

    public void GiveWeapon(WeaponData data) => Weapons.Add(data);

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead) return;

        float dt = (float)delta;
        PendingHitStop = 0f;

        if (_blinkCooldown > 0f) _blinkCooldown -= dt;
        if (_damageIFrames > 0f) _damageIFrames -= dt;
        if (_contactDamageCooldown > 0f) _contactDamageCooldown -= dt;

        UpdateAim();
        HandleBanishAndOpenEye(dt);
        HandleBlinkInput();
        HandleWeaponInput(dt);

        if (Phase != BlinkPhase.None) TickBlink();
        else ApplyWalkMovement(dt);

        MoveAndSlide();

        Weapons.Tick(dt);
        Sanity.Tick(dt);

        CollectKillRewards();
        PublishToBulletManagers();
        ConsumeIncomingHits();
        ConsumeContactDamage();
    }

    // ---------------------------------------------------------------- Aiming

    private void UpdateAim()
    {
        Vector2 stick = new(
            Input.GetActionStrength("aim_right") - Input.GetActionStrength("aim_left"),
            Input.GetActionStrength("aim_down") - Input.GetActionStrength("aim_up"));

        if (stick.LengthSquared() > 0.05f)
        {
            AimDirection = stick.Normalized();
        }
        else if (!GameRoot.Instance.HeadlessTestMode)
        {
            Vector2 toMouse = GetGlobalMousePosition() - GlobalPosition;
            if (toMouse.LengthSquared() > 1f) AimDirection = toMouse.Normalized();
        }
    }

    private static Vector2 ReadMoveInput() => new Vector2(
        Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
        Input.GetActionStrength("move_down") - Input.GetActionStrength("move_up")).LimitLength(1f);

    // ---------------------------------------------------------------- Movement

    private void ApplyWalkMovement(float dt)
    {
        Vector2 input = ReadMoveInput();

        float speed = Tune.PlayerMoveSpeed * Sanity.MoveSpeedMultiplier;
        if (Input.IsActionPressed("fire")) speed *= Tune.PlayerFiringSpeedMult;

        Vector2 target = input * speed;
        float rate = target.LengthSquared() > _smoothedVelocity.LengthSquared()
            ? Tune.PlayerAccelTime
            : Tune.PlayerDecelTime;

        _smoothedVelocity = rate <= 0f ? target
            : _smoothedVelocity.MoveToward(target, speed * dt / rate);

        Velocity = _smoothedVelocity;
    }

    // ---------------------------------------------------------------- Blink Step

    private void HandleBlinkInput()
    {
        if (!Input.IsActionJustPressed("blink_step")) return;
        if (Phase != BlinkPhase.None && Phase != BlinkPhase.Recovery) return;

        // FREE (fallback F4). The limiter is the cooldown plus the 8-frame vulnerable
        // recovery tail — spamming is punished by the tail, not by a price, which is what
        // makes timing rather than budgeting the skill.
        if (_blinkCooldown > 0f) return;

        // Cost is 0 by default; the call is kept so flipping Tune.SanityBlinkCost to 18
        // restores the metered variant (Build B) with no code change.
        if (Tune.SanityBlinkCost > 0f && !Sanity.TrySpend(Tune.SanityBlinkCost))
        {
            DeniedBlinkCount++;
            return;
        }

        Vector2 dir = ReadMoveInput();
        if (dir.LengthSquared() < 0.01f) dir = AimDirection;
        dir = dir.Normalized();

        Phase = BlinkPhase.Startup;
        _blinkFrame = 0;

        float duration = Tune.BlinkTotalFrames / 60f;
        _blinkVelocity = dir * (Tune.BlinkDistance / duration);
        _smoothedVelocity = _blinkVelocity;
    }

    private void TickBlink()
    {
        _blinkFrame++;

        if (_blinkFrame <= Tune.BlinkStartupFrames)
        {
            Phase = BlinkPhase.Startup;
            Velocity = _blinkVelocity;
        }
        else if (_blinkFrame <= Tune.BlinkStartupFrames + Tune.BlinkInvulnFrames)
        {
            Phase = BlinkPhase.Invulnerable;
            Velocity = _blinkVelocity;
        }
        else if (_blinkFrame <= Tune.BlinkTotalFrames)
        {
            Phase = BlinkPhase.Recovery;
            Velocity = _blinkVelocity * Tune.BlinkRecoveryMoveMult;
        }
        else
        {
            Phase = BlinkPhase.None;
            _blinkFrame = 0;
            _blinkCooldown = Tune.BlinkCooldown;
            _smoothedVelocity = Velocity;
        }
    }

    // ---------------------------------------------------------------- Weapons

    private void HandleWeaponInput(float dt)
    {
        if (Weapons.Count == 0 || PlayerBullets is null || Enemies is null) return;

        if (Input.IsActionJustPressed("swap_weapon")) Weapons.CycleActive();

        if (Input.IsActionPressed("fire"))
        {
            Weapons.TryFire(GlobalPosition, AimDirection, PlayerBullets, Sanity, Enemies, _rng);
        }

        if (Input.IsActionJustPressed("recite"))
        {
            int deniedBefore = Weapons.ReloadsDenied;
            Weapons.TryRecite(Sanity);
            if (Weapons.ReloadsDenied > deniedBefore)
            {
                DeniedSustainCount++;
                Telemetry?.NoteDeniedSustain();
            }
        }

        int autoDeniedBefore = Weapons.ReloadsDenied;
        Weapons.TickAutoReload(dt, Sanity, ref _autoReloadDelay);
        if (Weapons.ReloadsDenied > autoDeniedBefore)
        {
            DeniedSustainCount++;
            Telemetry?.NoteDeniedSustain();
        }

        Weapons.Active.ClearPerfectBonusIfMagazineSpent();
    }

    // ------------------------------------------------- Banish / Open the Eye

    private void HandleBanishAndOpenEye(float dt)
    {
        if (Input.IsActionPressed("banish"))
        {
            _banishHoldTime += dt;
            if (!_banishConsumed && _banishHoldTime >= Tune.OpenEyeHoldTime)
            {
                if (Sanity.TryOpenEye())
                {
                    Telemetry?.NoteOpenEye();
                    EmitSignal(SignalName.EyeOpened);
                }
                _banishConsumed = true;
            }
            return;
        }

        if (_banishHoldTime > 0f)
        {
            if (!_banishConsumed && _banishHoldTime < Tune.OpenEyeHoldTime)
            {
                if (Sanity.TrySpend(Tune.SanityBanishCost))
                {
                    Telemetry?.NoteSanitySpend(Tune.SanityBanishCost);
                    EmitSignal(SignalName.Banished);
                }
                else
                {
                    DeniedSustainCount++;
                    Telemetry?.NoteDeniedSustain();
                }
            }
            _banishHoldTime = 0f;
            _banishConsumed = false;
        }
    }

    // ---------------------------------------------------------------- Damage & rewards

    private void CollectKillRewards()
    {
        if (Enemies is null || Enemies.PendingSanityReward <= 0f) return;

        float reward = Enemies.PendingSanityReward;
        Sanity.GainFromKill(reward);
        Telemetry?.NoteSanityIncome(reward);

        for (int i = 0; i < Enemies.KillsThisTick; i++) Telemetry?.NoteKill();

        // docs/02 §8 — a 0.15s freeze at the death frame, scaled by how many died.
        if (Enemies.KillsThisTick > 0) PendingHitStop = 0.04f * Enemies.KillsThisTick;
    }

    private void PublishToBulletManagers()
    {
        if (EnemyBullets is not null)
        {
            EnemyBullets.TargetPosition = GlobalPosition;
            EnemyBullets.TargetRadius = Tune.PlayerHitboxRadius;
            EnemyBullets.TargetInvulnerable = IsInvulnerable;
        }
        if (Enemies is not null)
        {
            Enemies.PlayerPosition = GlobalPosition;
            Enemies.PlayerVelocity = Velocity;
        }
    }

    private void ConsumeIncomingHits()
    {
        if (EnemyBullets is null || EnemyBullets.HitsThisTick <= 0) return;
        if (IsInvulnerable) return;
        TakeHit(0.5f);
    }

    private void ConsumeContactDamage()
    {
        if (Enemies is null || IsInvulnerable || _contactDamageCooldown > 0f) return;
        float dmg = Enemies.QueryContactDamage(GlobalPosition, Tune.PlayerHitboxRadius);
        if (dmg <= 0f) return;
        _contactDamageCooldown = 0.6f;
        TakeHit(dmg);
    }

    private void TakeHit(float hearts)
    {
        HitsTaken++;
        Hearts = Mathf.Max(0f, Hearts - hearts);
        _damageIFrames = 1.0f;
        PendingHitStop = 0.09f;

        Telemetry?.NoteHitTaken();
        Telemetry?.NoteSanitySpend(Tune.SanityHitCost);

        // Damage compounds: being hit also costs Sanity, so you get hit and then cannot
        // afford to reload. One of the two mechanisms that punish both extremes.
        if (Sanity.Drain(Tune.SanityHitCost))
        {
            Telemetry?.NoteAscension();
            EmitSignal(SignalName.Ascended);
        }

        if (IsDead) EmitSignal(SignalName.Died);
    }

    public void Heal(float hearts) => Hearts = Mathf.Min(MaxHearts, Hearts + hearts);

    public void ResetForTest(Vector2 position)
    {
        GlobalPosition = position;
        Velocity = Vector2.Zero;
        _smoothedVelocity = Vector2.Zero;
        Phase = BlinkPhase.None;
        _blinkFrame = 0;
        _blinkCooldown = 0f;
        _damageIFrames = 0f;
        Hearts = MaxHearts;
        HitsTaken = 0;
        DeniedBlinkCount = 0;
        DeniedSustainCount = 0;

        // Sanity and the Lucid Ceiling must reset too, or a new run inherits the previous
        // run's descent — the next attempt would start mid-ladder and every metric keyed
        // to time-in-band would be quietly wrong.
        Sanity.SetMax(Tune.SanityMax);
        Sanity.DebugSetCurrent(Tune.SanityMax);
        Sanity.ResetCeiling();
    }
}
