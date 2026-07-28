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

    public Enemy(int id, EnemyData data, Vector2 position, BulletManager enemyBullets, Rng rng)
    {
        Id = id;
        Data = data;
        Position = position;
        Health = data.MaxHealth;
        _rng = rng;
        _attackCooldown = rng.Range(0.2f, data.AttackCooldown);

        if (data.PrimaryAttack is not null) _pattern.Configure(data.PrimaryAttack, enemyBullets, rng);
    }

    /// <summary>Returns true if it wants an attack token this tick.</summary>
    public bool WantsToAttack =>
        Alive && Data.PrimaryAttack is not null && _attackCooldown <= 0f
        && State is EnemyState.Approach or EnemyState.Idle;

    public void GrantToken() { HoldsAttackToken = true; }

    /// <summary>Set while the player is Ascended (docs/02 §6). Enemies break off and flee.</summary>
    public bool Ascended;

    /// <summary>Banish stun (docs/02 §5.2). Cancels a wind-up in progress.</summary>
    public float StunRemaining { get; private set; }
    public bool IsStunned => StunRemaining > 0f;

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

    public void Tick(float dt, Vector2 playerPos, Vector2 playerVel, FlowField field)
    {
        if (!Alive) return;

        if (HitFlash > 0f) HitFlash -= dt;
        if (_attackCooldown > 0f) _attackCooldown -= dt;
        _stateTimer += dt;

        if (StunRemaining > 0f)
        {
            StunRemaining -= dt;
            // Carry the knockback impulse, decaying. Freezing in place instead would make
            // Banish feel like a pause button rather than a shove.
            Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 2.5f * dt);
            Position += Velocity * dt;
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
            Position += Velocity * dt;
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

        Position += Velocity * dt;
    }

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
