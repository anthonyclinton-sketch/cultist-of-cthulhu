using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Enemies;

public enum EnemyState { Idle, Approach, Telegraph, Attack, Recover, Reposition, Dead }

/// <summary>
/// One enemy. Owned and ticked by <see cref="EnemyManager"/> rather than by the scene
/// tree, so the manager can enforce the attack-token budget globally — which is the single
/// most important knob for making a room fair (docs/05 §8).
/// </summary>
public sealed class Enemy
{
    public EnemyData Data { get; }
    public int Id { get; }

    public Vector2 Position;
    public Vector2 Velocity;
    public float Health;
    public EnemyState State { get; private set; } = EnemyState.Idle;
    public bool Alive => State != EnemyState.Dead;

    /// <summary>Drives the hit flash — 2 frames white, then additive tint (docs/02 §8).</summary>
    public float HitFlash { get; private set; }

    /// <summary>0..1 wind-up progress, for the telegraph ring. docs/05 R3.</summary>
    public float TelegraphProgress => _pattern.TelegraphProgress;

    public bool HoldsAttackToken { get; private set; }

    private readonly PatternPlayer _pattern = new();
    private readonly Rng _rng;
    private float _stateTimer;
    private float _attackCooldown;
    private Vector2 _repositionTarget;
    private float _weakPointAngle;

    /// <summary>
    /// docs/02 §7.2 — Awakened, at Corruption 3 and above (and at 10 regardless).
    ///
    /// The health bump is small on purpose; the real change is the SECOND ATTACK PATTERN.
    /// Fodder that stops dying quickly starves the Sanity economy (docs/05 §2), so making
    /// Corruption bite by inflating health would have punished the player twice — once with
    /// tougher enemies and again with less Sanity to fight them with.
    /// </summary>
    public bool Awakened { get; }

    private readonly BulletManager _bullets;

    public Enemy(int id, EnemyData data, Vector2 position, BulletManager enemyBullets, Rng rng,
                 bool awakened = false)
    {
        Id = id;
        Data = data;
        Position = position;
        Awakened = awakened && data.AwakenedAttack is not null;
        Health = data.MaxHealth * (awakened ? Tune.AwakenedHealthMultiplier : 1f);
        _rng = rng;
        _bullets = enemyBullets;
        _attackCooldown = rng.Range(0.2f, data.AttackCooldown);

        if (FirstAttack is not null) _pattern.Configure(FirstAttack, enemyBullets, rng);
    }

    /// <summary>Max health as this instance was actually built, so health bars and the
    /// execute threshold read the Awakened value rather than the authored one.</summary>
    public float MaxHealth => Data.MaxHealth * (Awakened ? Tune.AwakenedHealthMultiplier : 1f);

    /// <summary>
    /// Whichever pattern this enemy opens with.
    ///
    /// Not always the primary: the Cellar Ghoul is a pure melee Rusher with no primary
    /// attack at all, and its Awakened pattern is the first ranged attack it has ever had.
    /// Assuming a primary here would have left the one enemy the upgrade changes most
    /// unable to use it.
    /// </summary>
    private PatternData? FirstAttack => Data.PrimaryAttack ?? (Awakened ? Data.AwakenedAttack : null);

    private bool HasAnyAttack => Data.PrimaryAttack is not null || (Awakened && Data.AwakenedAttack is not null);

    /// <summary>Returns true if it wants an attack token this tick.</summary>
    public bool WantsToAttack =>
        Alive && HasAnyAttack && _attackCooldown <= 0f
        && State is EnemyState.Approach or EnemyState.Idle;

    public void GrantToken() { HoldsAttackToken = true; }

    /// <summary>Set while the player is Ascended (docs/02 §6). Enemies break off and flee.</summary>
    public bool Ascended;

    /// <summary>Pushed down from the manager each tick, sourced from the player's Sanity
    /// band. See PatternPlayer.HallucinationRatio.</summary>
    public float HallucinationRatio
    {
        get => _pattern.HallucinationRatio;
        set => _pattern.HallucinationRatio = value;
    }

    /// <summary>Banish stun (docs/02 §5.2). Cancels a wind-up in progress.</summary>
    public float StunRemaining { get; private set; }
    public bool IsStunned => StunRemaining > 0f;

    /// <summary>
    /// docs/02 §3.4 — weak point. Orbits the body slowly so it cannot be held in a fixed
    /// screen position; the player has to track it.
    ///
    /// It is ALWAYS live, and only *visible* at Fraying and below. That distinction is
    /// deliberate: the low band's payoff is then genuinely informational rather than a
    /// hidden damage buff, and a lucky blind hit still rewards you. Making it inert above
    /// Fraying would turn the ladder back into the flat damage bonus that Fable's review
    /// removed.
    /// </summary>
    public Vector2 WeakPointOffset { get; private set; }
    public const float WeakPointRadiusFraction = 0.45f;
    public const float WeakPointDamageBonus = 1.5f;

    /// <summary>docs/02 §4 — dashing through an enemy Marks it: +25% damage taken, 0.3s.</summary>
    public float MarkedRemaining { get; private set; }
    public bool IsMarked => MarkedRemaining > 0f;
    public const float MarkedDamageMultiplier = 1.25f;
    public const float MarkedDuration = 0.3f;

    public void ApplyMark() => MarkedRemaining = MarkedDuration;

    /// <summary>World position of the weak point.</summary>
    public Vector2 WeakPointPosition => Position + WeakPointOffset;

    /// <summary>Radius within which a hit counts as a weak-point hit.</summary>
    public float WeakPointRadius => Data.BodyRadius * WeakPointRadiusFraction;

    /// <summary>
    /// Stun and shove. Cancelling the pattern matters more than the stun duration: the
    /// whole point of Banish is to interrupt an incoming volley, so an enemy that keeps
    /// its telegraph and fires the moment the stun ends has not really been interrupted.
    /// </summary>
    public void ApplyBanish(Vector2 impulse, float stunSeconds)
    {
        if (!Alive) return;
        StunRemaining = Mathf.Max(StunRemaining, stunSeconds);
        _pattern.Cancel();
        HoldsAttackToken = false;
        _attackCooldown = Mathf.Max(_attackCooldown, stunSeconds);
        Velocity += impulse;
        if (State is EnemyState.Telegraph or EnemyState.Attack) Transition(EnemyState.Recover);
    }

    /// <summary>
    /// Solid geometry, pushed down from the manager. Null in the fixed arena.
    ///
    /// Enemies are ticked by hand rather than being <c>CharacterBody2D</c>s, which is what
    /// makes 60 of them affordable (docs/05 §8) — and is also why they walked through walls
    /// until this existed. The flow field routes AROUND geometry, but a flee, a knockback
    /// or a Rusher's lunge all ignore the field entirely, so steering alone was never going
    /// to be enough.
    /// </summary>
    private Core.TileMask? _walls;

    /// <summary>
    /// Integrate position with wall resolution. Every movement path goes through here —
    /// approach, flee, stun-slide and knockback — because the three that bypassed the flow
    /// field were exactly the three that put enemies inside walls.
    /// </summary>
    private void Move(float dt)
    {
        Vector2 delta = Velocity * dt;
        Position = _walls is null
            ? Position + delta
            : _walls.MoveCircle(Position, delta, Data.BodyRadius);
    }

    public void Tick(float dt, Vector2 playerPos, Vector2 playerVel, FlowField field,
                     Core.TileMask? walls)
    {
        if (!Alive) return;
        _walls = walls;

        if (HitFlash > 0f) HitFlash -= dt;
        if (_attackCooldown > 0f) _attackCooldown -= dt;
        if (MarkedRemaining > 0f) MarkedRemaining -= dt;
        _stateTimer += dt;

        // Weak point orbits. Seeded by Id so enemies of the same type are out of phase and
        // the room does not pulse in unison.
        _weakPointAngle += dt * 1.15f;
        float wpr = Data.BodyRadius * 0.5f;
        WeakPointOffset = new Vector2(Mathf.Cos(_weakPointAngle + Id), Mathf.Sin(_weakPointAngle + Id)) * wpr;

        if (StunRemaining > 0f)
        {
            StunRemaining -= dt;
            // Carry the knockback impulse, decaying. Freezing in place instead would make
            // Banish feel like a pause button rather than a shove.
            Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 2.5f * dt);
            Move(dt);
            return;
        }

        float distToPlayer = Position.DistanceTo(playerPos);
        Vector2 toPlayer = distToPlayer > 0.01f ? (playerPos - Position) / distToPlayer : Vector2.Right;

        // While the player is Ascended, nothing fights back. This is the mechanical half
        // of the power fantasy — an invulnerable player who was still being shot at would
        // feel like a stat buff rather than a transformation.
        if (Ascended)
        {
            _pattern.Cancel();
            HoldsAttackToken = false;
            if (State != EnemyState.Dead) State = EnemyState.Reposition;

            // Flee, with a wobble so the room reads as panicking rather than retreating
            // in formation.
            float wobble = Mathf.Sin(_stateTimer * 7f + Id) * 0.5f;
            Vector2 away = -toPlayer.Rotated(wobble);
            Velocity = Velocity.MoveToward(away * Data.MoveSpeed * 1.3f, Data.MoveSpeed * 6f * dt);
            Move(dt);
            return;
        }

        switch (State)
        {
            case EnemyState.Idle:
                if (distToPlayer <= Data.AggroRange) Transition(EnemyState.Approach);
                break;

            case EnemyState.Approach:
                MoveTowardPreferredRange(dt, distToPlayer, field);
                if (HoldsAttackToken && _attackCooldown <= 0f)
                {
                    SelectNextPattern();
                    _pattern.Fire();
                    Transition(EnemyState.Telegraph);
                }
                break;

            case EnemyState.Telegraph:
                // Turrets and Zoners plant themselves to wind up; Rushers keep closing,
                // which is what makes them feel different without any extra code.
                if (Data.Role is EnemyRole.Rusher)
                    Velocity = toPlayer * Data.MoveSpeed * Data.LungeMultiplier;
                else
                    Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 6f * dt);

                _pattern.Tick(dt, Position, toPlayer, playerPos, playerVel);

                // Both exits must be handled. A single-burst pattern (BurstCount == 1)
                // passes through Firing and reaches Finished inside ONE tick, so waiting
                // only for Firing leaves the enemy telegraphing forever — it fires its
                // opening volley and then stands still for the rest of the room. That is
                // what the headless trace showed as a permanent "tel 3".
                if (_pattern.Current is PatternPlayer.State.Finished) Transition(EnemyState.Recover);
                else if (_pattern.Current is PatternPlayer.State.Firing) Transition(EnemyState.Attack);
                break;

            case EnemyState.Attack:
                _pattern.Tick(dt, Position, toPlayer, playerPos, playerVel);
                if (_pattern.Current is PatternPlayer.State.Finished) Transition(EnemyState.Recover);
                break;

            case EnemyState.Recover:
                // The player's punish window. Deliberately motionless.
                Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 4f * dt);
                if (_stateTimer >= Data.RecoverySeconds)
                {
                    ReleaseToken();
                    _attackCooldown = Data.AttackCooldown
                                      + _rng.Range(-Data.AttackCooldownVariance, Data.AttackCooldownVariance);
                    Transition(Data.Role == EnemyRole.Turret ? EnemyState.Approach : EnemyState.Reposition);
                }
                break;

            case EnemyState.Reposition:
                if (_stateTimer < 0.05f)
                {
                    // Strafe to a new angle around the player rather than backing straight
                    // off — retreating in a line reads as fleeing and looks broken.
                    float a = Mathf.Atan2(-toPlayer.Y, -toPlayer.X) + _rng.Range(-1.1f, 1.1f);
                    _repositionTarget = playerPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Data.PreferredRange;
                }
                Velocity = (_repositionTarget - Position).Normalized() * Data.MoveSpeed;
                if (_stateTimer > 0.9f || Position.DistanceTo(_repositionTarget) < 12f)
                    Transition(EnemyState.Approach);
                break;
        }

        Move(dt);
    }

    /// <summary>
    /// Alternate primary and Awakened, strictly.
    ///
    /// Alternating rather than rolling per volley on purpose. docs/05 §1's readability
    /// contract is the whole reason the pattern vocabulary is small and telegraphed — a
    /// player learns an enemy by learning what it does next, and a coin flip between two
    /// shapes is not learnable, it is just noisier. Alternation means an Awakened enemy has
    /// a RHYTHM the player can read and punish, which is what makes the upgrade a change in
    /// difficulty rather than a change in variance.
    /// </summary>
    private void SelectNextPattern()
    {
        if (!Awakened || Data.AwakenedAttack is null || Data.PrimaryAttack is null) return;

        _useAwakenedNext = !_useAwakenedNext;
        _pattern.Configure(_useAwakenedNext ? Data.AwakenedAttack : Data.PrimaryAttack, _bullets, _rng);
    }

    private bool _useAwakenedNext;

    private void MoveTowardPreferredRange(float dt, float dist, FlowField field)
    {
        // Flow field for the long haul (it routes around geometry), direct vector for the
        // last few pixels (the field is cell-resolution and jitters at close range).
        Vector2 desired;
        if (dist > Data.PreferredRange * 1.15f)
        {
            Vector2 flow = field.Sample(Position);
            desired = flow == Vector2.Zero ? Vector2.Zero : flow * Data.MoveSpeed;
        }
        else if (dist < Data.PreferredRange * 0.85f && Data.Role != EnemyRole.Rusher)
        {
            desired = -field.Sample(Position) * Data.MoveSpeed * 0.7f;
        }
        else
        {
            desired = Vector2.Zero;
        }

        Velocity = Velocity.MoveToward(desired, Data.MoveSpeed * 5f * dt);
    }

    private void Transition(EnemyState next)
    {
        State = next;
        _stateTimer = 0f;
    }

    private void ReleaseToken() => HoldsAttackToken = false;

    /// <summary>Returns true if this hit killed it.</summary>
    public bool TakeDamage(float amount)
    {
        if (!Alive) return false;
        Health -= amount;
        HitFlash = 0.1f;
        if (Health > 0f) return false;

        State = EnemyState.Dead;
        HoldsAttackToken = false;
        _pattern.Cancel();
        return true;
    }

    public void ApplyKnockback(Vector2 impulse) => Velocity += impulse;
}
