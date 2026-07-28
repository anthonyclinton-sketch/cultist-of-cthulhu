using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Player;

public enum BlinkPhase { None, Startup, Invulnerable, Recovery }

/// <summary>
/// docs/02 §1, §4. The single most-pressed button in the game is Blink Step, so its frame
/// data is the spec of the game and is implemented in FRAMES, not seconds.
///
/// Deliberately NOT physics-driven — no RigidBody2D, velocity assigned directly. Any
/// "weight" the character has is animation, never simulation (docs/02 §1.1).
/// </summary>
public sealed partial class PlayerController : CharacterBody2D
{
    [Export] public NodePath BulletManagerPath { get; set; } = default!;

    public SanitySystem Sanity { get; } = new();

    public BlinkPhase Phase { get; private set; } = BlinkPhase.None;
    public bool IsInvulnerable => Phase == BlinkPhase.Invulnerable || _damageIFrames > 0f;

    /// <summary>Aim direction in world space. Mouse cursor on KBM, right stick on pad.</summary>
    public Vector2 AimDirection { get; private set; } = Vector2.Right;

    public int HitsTaken { get; private set; }

    /// <summary>Dodges the player attempted but could not pay for. Metric 3 in the M1
    /// test design (docs/11) — "denied-action events". Instrumented from day one because
    /// it is the number that tells us whether players can model the Sanity bar.</summary>
    public int DeniedBlinkCount { get; private set; }

    private BulletManager? _bullets;

    private int _blinkFrame;
    private Vector2 _blinkVelocity;
    private float _blinkCooldown;
    private float _damageIFrames;
    private float _banishHoldTime;
    private bool _banishConsumed;

    private Vector2 _smoothedVelocity;

    public override void _Ready()
    {
        if (BulletManagerPath is not null && !BulletManagerPath.IsEmpty)
        {
            _bullets = GetNodeOrNull<BulletManager>(BulletManagerPath);
        }
        _bullets ??= GetTree().Root.FindChild(nameof(BulletManager), recursive: true, owned: false) as BulletManager;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_blinkCooldown > 0f) _blinkCooldown -= dt;
        if (_damageIFrames > 0f) _damageIFrames -= dt;

        UpdateAim();
        HandleBanishAndOpenEye(dt);
        HandleBlinkInput();

        if (Phase != BlinkPhase.None) TickBlink();
        else ApplyWalkMovement(dt);

        MoveAndSlide();

        Sanity.Tick(dt);
        PublishToBulletManager();
        ConsumeIncomingHits();
    }

    // ---------------------------------------------------------------- Aiming (docs/02 §1.2)

    private void UpdateAim()
    {
        Vector2 stick = new(
            Input.GetActionStrength("aim_right") - Input.GetActionStrength("aim_left"),
            Input.GetActionStrength("aim_down") - Input.GetActionStrength("aim_up"));

        if (stick.LengthSquared() > 0.05f)
        {
            AimDirection = stick.Normalized();   // pad: 1:1, no lerp
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

        // Near-instant accel/decel. Bullet hell demands 1:1 input; there is no ice.
        float rate = target.LengthSquared() > _smoothedVelocity.LengthSquared()
            ? Tune.PlayerAccelTime
            : Tune.PlayerDecelTime;

        _smoothedVelocity = rate <= 0f
            ? target
            : _smoothedVelocity.MoveToward(target, speed * dt / rate);

        Velocity = _smoothedVelocity;
    }

    // ---------------------------------------------------------------- Blink Step (docs/02 §4)

    private void HandleBlinkInput()
    {
        if (!Input.IsActionJustPressed("blink_step")) return;
        if (Phase != BlinkPhase.None && Phase != BlinkPhase.Recovery) return;
        if (_blinkCooldown > 0f) return;

        if (!Sanity.TrySpend(Tune.SanityBlinkCost))
        {
            // The intended failure state (docs/02 §3.3): a dodge you cannot pay for.
            DeniedBlinkCount++;
            return;
        }

        Vector2 dir = ReadMoveInput();
        if (dir.LengthSquared() < 0.01f) dir = AimDirection;
        dir = dir.Normalized();

        Phase = BlinkPhase.Startup;
        _blinkFrame = 0;

        // Constant velocity across the whole 24-frame window so total travel matches the
        // authored 3.2 units exactly, independent of framerate.
        float duration = Tune.BlinkTotalFrames / 60f;
        _blinkVelocity = dir * (Tune.BlinkDistance / duration);
        _smoothedVelocity = _blinkVelocity;
    }

    private void TickBlink()
    {
        _blinkFrame++;

        // Frames 1-2 startup | 3-16 INVULNERABLE | 17-24 recovery (vulnerable, 40% move)
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

    // ------------------------------------------------- Banish / Open the Eye (docs/02 §5.2, §3.5.1)

    /// <summary>
    /// Tap Banish, hold for Open the Eye. Hold-vs-tap rather than a new binding — the
    /// input scheme is already full, and both actions are "spend Sanity dramatically".
    /// </summary>
    private void HandleBanishAndOpenEye(float dt)
    {
        if (Input.IsActionPressed("banish"))
        {
            _banishHoldTime += dt;
            if (!_banishConsumed && _banishHoldTime >= Tune.OpenEyeHoldTime)
            {
                if (Sanity.TryOpenEye()) EmitSignal(SignalName.EyeOpened);
                _banishConsumed = true;
            }
            return;
        }

        if (_banishHoldTime > 0f)
        {
            if (!_banishConsumed && _banishHoldTime < Tune.OpenEyeHoldTime)
            {
                if (Sanity.TrySpend(Tune.SanityBanishCost)) EmitSignal(SignalName.Banished);
            }
            _banishHoldTime = 0f;
            _banishConsumed = false;
        }
    }

    [Signal] public delegate void BanishedEventHandler();
    [Signal] public delegate void EyeOpenedEventHandler();
    [Signal] public delegate void AscendedEventHandler();

    // ---------------------------------------------------------------- Damage

    private void PublishToBulletManager()
    {
        if (_bullets is null) return;
        _bullets.TargetPosition = GlobalPosition;
        _bullets.TargetRadius = Tune.PlayerHitboxRadius;
        _bullets.TargetInvulnerable = IsInvulnerable;
    }

    private void ConsumeIncomingHits()
    {
        if (_bullets is null || _bullets.HitsThisTick <= 0) return;
        if (IsInvulnerable) return;

        HitsTaken++;
        _damageIFrames = 1.0f;   // docs/02 §2 — 1s post-damage invulnerability

        // Damage compounds: being hit also costs Sanity, so you get hit, then you cannot
        // dodge. This is one of the two mechanisms that punish both extremes (docs/02 §3.3).
        if (Sanity.Drain(Tune.SanityHitCost)) EmitSignal(SignalName.Ascended);
    }

    public void ResetForTest(Vector2 position)
    {
        GlobalPosition = position;
        Velocity = Vector2.Zero;
        _smoothedVelocity = Vector2.Zero;
        Phase = BlinkPhase.None;
        _blinkFrame = 0;
        _blinkCooldown = 0f;
        _damageIFrames = 0f;
        HitsTaken = 0;
        DeniedBlinkCount = 0;
    }
}
