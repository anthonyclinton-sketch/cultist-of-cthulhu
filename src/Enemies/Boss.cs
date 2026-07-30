using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Enemies;

public enum BossState { Idle, Stalk, Telegraph, Attack, Recover, Transition, GrabWindup, GrabLunge, Dead }

/// <summary>
/// The Thing on the Doorstep, and the shape every later boss will be built from
/// (docs/05 §7).
///
/// NOT an <see cref="Enemy"/> with more health. The three phases are three different
/// fights — a human who strafes and shoots, a body that has inverted and holds ground, and
/// a formless thing that abandons the corpse and comes for you — and expressing that
/// through the shared enemy FSM would mean bolting phase state, an add spawner, a grab and
/// a health bar onto the class that has to stay cheap enough to run sixty of.
///
/// What it DOES share is everything that made the enemy work: the same
/// <see cref="PatternPlayer"/>, so a boss volley obeys docs/05 R3's telegraph minimum
/// exactly as an acolyte's does; the same <see cref="TileMask"/> movement, so it cannot
/// walk through the arena wall; and the same hit resolution through
/// <see cref="EnemyManager"/>, so weak points, Marked and the execute bonus all apply
/// without a second code path.
/// </summary>
public sealed class Boss
{
    /// <summary>
    /// Base of the reserved target id range. Negative so it can never collide with the enemy
    /// manager's counter, which starts at 1 and only rises.
    /// </summary>
    public const int TargetIdBase = -777;

    /// <summary>
    /// This boss's target id, assigned by <see cref="EnemyManager.RegisterBoss"/>.
    ///
    /// PER INSTANCE, not a constant, because floor 2 fights two bosses at once and a shared
    /// id routes every hit on the consort into the matriarch's health bar. Assigned from
    /// registration order rather than a static counter, so it is identical on every replay of
    /// a seed — a static would make the ids depend on how many bosses the process had ever
    /// built, which is exactly the kind of thing the determinism gate exists to catch.
    /// </summary>
    public int TargetId { get; internal set; } = TargetIdBase;

    public BossData Data { get; }
    public Vector2 Position;
    public Vector2 Velocity;
    public float Health { get; private set; }
    public BossState State { get; private set; } = BossState.Idle;
    public int Phase { get; private set; } = 1;

    public bool Alive => State != BossState.Dead;

    public float HealthFraction => Data.MaxHealth <= 0f ? 0f : Mathf.Clamp(Health / Data.MaxHealth, 0f, 1f);

    /// <summary>
    /// Under the water and out of reach — docs/05 §7, where the tide submerges Mother Hydra
    /// and her consort in turn and "the player must fight the right one at the right time".
    ///
    /// Pushed in by whoever owns the fight rather than read from a TideCycle here, for the
    /// same reason the player does not know what water is: this class knows it cannot be hit,
    /// not why. It also means the Doorstep, which has no such rule, carries no knowledge of it.
    /// </summary>
    public bool Submerged { get; set; }

    /// <summary>
    /// Invulnerable during a phase transition — the fight is not happening, it is changing.
    /// Damage landed here would be dealt to a boss that cannot answer.
    ///
    /// Two sources, one question. Everything that asks "can I hurt this" — TakeDamage, the
    /// target registration, the contact-damage scan, the HUD's UNTOUCHABLE label — asks this,
    /// so the tide rule reached all four for free. A second parallel flag would have needed
    /// each of them updated, and the one that got missed would be the interesting bug.
    /// </summary>
    public bool Invulnerable => State == BossState.Transition || Submerged;

    public float HitFlash { get; private set; }
    public float TelegraphProgress => _pattern.TelegraphProgress;

    /// <summary>0..1 through a phase transition, for the visual.</summary>
    public float TransitionProgress { get; private set; }

    /// <summary>Set on the tick a grab connects. Polled and cleared by the room owner —
    /// a counter rather than an event, matching the rest of the combat layer.</summary>
    public bool GrabConnectedThisTick { get; private set; }

    /// <summary>Adds owed to the room owner, which is the only thing that knows the enemy
    /// roster and where the floor is.</summary>
    public int PendingAdds { get; private set; }

    /// <summary>
    /// A phase change waiting to be announced, or 0. LATCHED, and consumed by the owner
    /// rather than cleared by the tick.
    ///
    /// This was a per-tick flag and it never fired once. Phase changes happen inside
    /// <see cref="TakeDamage"/>, which is called from the enemy manager's hit resolution —
    /// and Godot ticks parents before children, so the room owner cleared the flag at the
    /// top of its own frame, the manager set it later in that same frame, and the owner
    /// cleared it again before ever reading it. The boss advanced through all three phases
    /// correctly and silently: no bullet clear, no screen shake, no line of dialogue.
    ///
    /// Exactly the bug class <see cref="SanitySystem"/> documents for Ascension's trigger,
    /// and it gets the same answer — a latch consumed once, so it cannot matter who ticks
    /// first.
    /// </summary>
    private int _pendingPhaseChange;

    public int ConsumePhaseChange()
    {
        int p = _pendingPhaseChange;
        _pendingPhaseChange = 0;
        return p;
    }

    public float BodyRadius => Phase switch
    {
        // The body inverts in phase 2 and it is bigger; the passenger has no body at all
        // and is a harder target. Radius is the readable half of a phase change — the
        // silhouette changes before the player reads any pattern.
        2 => Data.BodyRadius * 1.35f,
        3 => Data.BodyRadius * 0.7f,
        _ => Data.BodyRadius,
    };

    private readonly PatternPlayer _pattern = new();
    private readonly BulletManager _bullets;
    private readonly Rng _rng;
    private TileMask? _walls;

    private float _stateTimer;
    private float _attackCooldown;
    private float _grabCooldown;
    private float _addTimer;
    private int _patternIndex;
    private Vector2 _strafeTarget;
    private Vector2 _lungeDirection;
    private bool _lungeConsumed;

    public Boss(BossData data, Vector2 position, BulletManager bullets, Rng rng)
    {
        Data = data;
        Position = position;
        Health = data.MaxHealth;
        _bullets = bullets;
        _rng = rng;
        _addTimer = data.AddInterval;
    }

    public float HallucinationRatio
    {
        get => _pattern.HallucinationRatio;
        set => _pattern.HallucinationRatio = value;
    }

    public void SetWalls(TileMask? walls) => _walls = walls;

    private void Move(float dt)
    {
        Vector2 delta = Velocity * dt;
        Position = _walls is null ? Position + delta : _walls.MoveCircle(Position, delta, BodyRadius);
    }

    public void Tick(float dt, Vector2 playerPos, Vector2 playerVel)
    {
        GrabConnectedThisTick = false;
        PendingAdds = 0;

        if (!Alive) return;

        if (HitFlash > 0f) HitFlash -= dt;
        if (_attackCooldown > 0f) _attackCooldown -= dt;
        if (_grabCooldown > 0f) _grabCooldown -= dt;
        _stateTimer += dt;

        // Phase 2's adds. Timed rather than triggered on damage, so a player who burns the
        // phase down fast is rewarded with fewer of them rather than the same number
        // arriving in a shorter window.
        if (Phase == 2 && State != BossState.Transition)
        {
            _addTimer -= dt;
            if (_addTimer <= 0f)
            {
                _addTimer = Data.AddInterval;
                PendingAdds = Data.AddCount;
            }
        }

        float dist = Position.DistanceTo(playerPos);
        Vector2 toPlayer = dist > 0.01f ? (playerPos - Position) / dist : Vector2.Right;

        switch (State)
        {
            case BossState.Idle:
                Transition(BossState.Stalk);
                break;

            case BossState.Transition:
                TransitionProgress = Mathf.Clamp(_stateTimer / Data.PhaseTransitionSeconds, 0f, 1f);
                Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 4f * dt);
                if (_stateTimer >= Data.PhaseTransitionSeconds) Transition(BossState.Stalk);
                break;

            case BossState.Stalk:
                Stalk(dt, dist, toPlayer, playerPos);

                // The grab takes priority over the volley: in phase 3 it IS the fight, and
                // an ordering that let a pattern pre-empt it would leave the signature
                // attack firing only when the boss happened to be off cooldown for both.
                if (Phase == 3 && _grabCooldown <= 0f && dist <= Data.GrabRange)
                {
                    _lungeConsumed = false;
                    Transition(BossState.GrabWindup);
                }
                else if (_attackCooldown <= 0f && ConfigureNextPattern())
                {
                    _pattern.Fire();
                    Transition(BossState.Telegraph);
                }
                break;

            case BossState.Telegraph:
                // Hold ground through the wind-up in phases 1 and 2; the passenger keeps
                // closing, which is what makes phase 3 feel like being hunted.
                Velocity = Phase == 3
                    ? toPlayer * Data.MoveSpeed * Data.SpeedMultiplierFor(Phase) * 0.6f
                    : Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 5f * dt);

                _pattern.Tick(dt, Position, toPlayer, playerPos, playerVel);

                // Both exits handled. A single-burst pattern passes through Firing and
                // reaches Finished inside one tick, and waiting only for Firing leaves the
                // emitter telegraphing forever — the bug that once froze every Chanter in
                // the game in a permanent wind-up.
                if (_pattern.Current is PatternPlayer.State.Finished) Transition(BossState.Recover);
                else if (_pattern.Current is PatternPlayer.State.Firing) Transition(BossState.Attack);
                break;

            case BossState.Attack:
                _pattern.Tick(dt, Position, toPlayer, playerPos, playerVel);
                if (_pattern.Current is PatternPlayer.State.Finished) Transition(BossState.Recover);
                break;

            case BossState.Recover:
                // The punish window. Every boss in this game has one, and its length is the
                // difficulty dial that does not make the patterns less readable.
                Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 3f * dt);
                if (_stateTimer >= 0.55f)
                {
                    _attackCooldown = Data.CooldownFor(Phase);
                    Transition(BossState.Stalk);
                }
                break;

            case BossState.GrabWindup:
                // Plant, and aim. Locking the direction at the END of the wind-up rather
                // than the start is what makes the grab dodgeable by TIMING rather than by
                // simply walking sideways for a second.
                Velocity = Velocity.MoveToward(Vector2.Zero, Data.MoveSpeed * 6f * dt);
                if (_stateTimer >= Data.GrabTelegraph)
                {
                    _lungeDirection = toPlayer;
                    Transition(BossState.GrabLunge);
                }
                break;

            case BossState.GrabLunge:
                Velocity = _lungeDirection * Data.GrabLungeSpeed;

                // One connection per lunge. Without the latch the boss drains 30 Sanity per
                // TICK of contact, which at 60Hz is the whole bar in half a second and reads
                // as an instant unavoidable kill.
                if (!_lungeConsumed && dist <= BodyRadius + Tune.PlayerHitboxRadius + 4f)
                {
                    _lungeConsumed = true;
                    GrabConnectedThisTick = true;
                }

                if (_stateTimer >= Data.GrabLungeSeconds)
                {
                    _grabCooldown = Data.GrabCooldown;
                    Transition(BossState.Recover);
                }
                break;
        }

        Move(dt);
    }

    /// <summary>
    /// Hold the phase's preferred range and strafe around it, rather than walking straight
    /// at the player. A boss that closes in a line is a boss the player kites backwards
    /// forever, which turns every phase into the same fight at a different range.
    /// </summary>
    private void Stalk(float dt, float dist, Vector2 toPlayer, Vector2 playerPos)
    {
        float range = Data.RangeFor(Phase);
        float speed = Data.MoveSpeed * Data.SpeedMultiplierFor(Phase);

        if (_stateTimer < 0.05f || Position.DistanceTo(_strafeTarget) < 24f)
        {
            float around = Mathf.Atan2(-toPlayer.Y, -toPlayer.X) + _rng.Range(-1.2f, 1.2f);
            _strafeTarget = playerPos + new Vector2(Mathf.Cos(around), Mathf.Sin(around)) * range;
        }

        Vector2 desired;
        if (dist > range * 1.2f) desired = toPlayer * speed;
        else if (dist < range * 0.8f) desired = -toPlayer * speed * 0.8f;
        else desired = (_strafeTarget - Position).Normalized() * speed * 0.7f;

        Velocity = Velocity.MoveToward(desired, speed * 4f * dt);
    }

    /// <summary>Cycle this phase's patterns in order. Returns false if the phase has none.</summary>
    private bool ConfigureNextPattern()
    {
        PatternData?[] patterns = Data.PatternsFor(Phase);

        for (int attempt = 0; attempt < patterns.Length; attempt++)
        {
            _patternIndex = (_patternIndex + 1) % patterns.Length;
            PatternData? p = patterns[_patternIndex];
            if (p is null) continue;
            _pattern.Configure(p, _bullets, _rng);
            return true;
        }
        return false;
    }

    /// <summary>Returns true if this hit killed it.</summary>
    public bool TakeDamage(float amount)
    {
        if (!Alive || Invulnerable) return false;

        Health -= amount;
        HitFlash = 0.09f;

        if (Health <= 0f)
        {
            Health = 0f;
            State = BossState.Dead;
            _pattern.Cancel();
            return true;
        }

        AdvancePhaseIfNeeded();
        return false;
    }

    /// <summary>
    /// Phase changes are driven from damage rather than polled in the tick, so the
    /// transition begins on the exact hit that crosses the threshold. Polling would let the
    /// boss fire one more volley from the phase it has already left.
    /// </summary>
    private void AdvancePhaseIfNeeded()
    {
        float f = HealthFraction;
        int want = f <= Data.Phase3At ? 3 : f <= Data.Phase2At ? 2 : 1;
        if (want <= Phase) return;

        Phase = want;
        _pendingPhaseChange = want;

        _pattern.Cancel();
        _patternIndex = -1;
        _attackCooldown = 0f;
        _grabCooldown = 0f;
        _addTimer = 0f;   // phase 2 summons immediately on arrival
        TransitionProgress = 0f;
        Transition(BossState.Transition);
    }

    private void Transition(BossState next)
    {
        State = next;
        _stateTimer = 0f;
    }

    /// <summary>Colour for the current phase. The palette carries the narrative: a human,
    /// then something wearing one, then the thing itself.</summary>
    public Color PhaseTint => Phase switch
    {
        1 => Data.Tint,
        2 => Data.Tint.Lerp(new Color("6E2B4A"), 0.55f),
        _ => new Color("4A3F6E"),
    };
}
