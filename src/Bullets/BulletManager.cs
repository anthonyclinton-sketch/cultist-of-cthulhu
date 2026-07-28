using System;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Bullets;

[Flags]
public enum BulletFlags
{
    None = 0,
    /// <summary>docs/02 §3.4 — passes through the player harmlessly, and casts no shadow.</summary>
    Hallucination = 1 << 0,
    IgnoresWalls = 1 << 1,
    /// <summary>Survives its first player hit (enemy bullets rarely use this; boss lasers do).</summary>
    Piercing = 1 << 2,
    /// <summary>Reflects off arena bounds instead of despawning.</summary>
    BouncesOffWalls = 1 << 3,
    /// <summary>Fired by the player. Collides with enemies instead of the player.</summary>
    PlayerOwned = 1 << 4,
}

/// <summary>
/// Per-bullet motion modifier (docs/05 §4.2). Stored as a byte in the SoA arrays and
/// dispatched by switch — never by interface, see ApplyBehaviour.
/// </summary>
public enum BulletBehaviour : byte
{
    Straight = 0,
    /// <summary>p0 = turn rate rad/s, p1 = duration s.</summary>
    Homing = 1,
    /// <summary>p0 = lateral amplitude px/s, p1 = frequency Hz.</summary>
    Wave = 2,
    /// <summary>p0 = acceleration px/s², p1 = max speed multiplier.</summary>
    Accelerate = 3,
    /// <summary>p0 = hold duration s. Freeze, then resume — the "pause and fire" trick.</summary>
    DelayThenGo = 4,
}

/// <summary>
/// THE hot path (docs/09 §3). Structure-of-arrays bullet simulation with MultiMesh rendering.
///
/// The architectural claim being cashed here: enemy bullets only ever need to collide with
/// the player's single 6px circle and with walls. That is not a broad-phase problem — it is
/// a linear scan against one circle. Godot's physics server is ~50x slower for this and an
/// Area2D-per-bullet design collapses at roughly 800 bullets (docs/09 §3.4).
///
/// Invariants this class must never violate:
///   1. ZERO heap allocations in _PhysicsProcess. Every array is preallocated.
///   2. Dense packing. Live bullets occupy [0, _count); removal is swap-with-last.
///   3. Three draw calls total, regardless of bullet count.
///   4. Real bullets cast a shadow; hallucinations do not. This is GAMEPLAY, not decoration
///      (docs/05 R9) — it is the sole tell distinguishing them, so the shadow layer is a
///      hard requirement, not a polish pass.
/// </summary>
public sealed partial class BulletManager : Node2D
{
    private const int Cap = Tune.MaxBullets;

    /// <summary>MultiMesh 2D buffer stride: 8 floats transform + 4 floats colour.</summary>
    private const int Stride = 12;

    // ---------------------------------------------------------------- Simulation state (SoA)
    // Parallel arrays, not an array of objects. Contiguous, cache-friendly, GC-invisible.

    private readonly float[] _posX = new float[Cap];
    private readonly float[] _posY = new float[Cap];
    private readonly float[] _velX = new float[Cap];
    private readonly float[] _velY = new float[Cap];
    private readonly float[] _radius = new float[Cap];
    private readonly float[] _life = new float[Cap];
    private readonly float[] _rot = new float[Cap];
    private readonly float[] _size = new float[Cap];      // render diameter in px
    private readonly int[] _flags = new int[Cap];
    private readonly float[] _colR = new float[Cap];
    private readonly float[] _colG = new float[Cap];
    private readonly float[] _colB = new float[Cap];
    private readonly float[] _damage = new float[Cap];

    // Per-bullet behaviour (docs/05 §4.2 modifiers). Two generic float params rather than
    // a per-behaviour struct: keeps the SoA layout flat and the switch branch-predictable.
    private readonly byte[] _bhType = new byte[Cap];
    private readonly float[] _bhP0 = new float[Cap];
    private readonly float[] _bhP1 = new float[Cap];
    private readonly float[] _age = new float[Cap];
    private readonly float[] _baseSpeed = new float[Cap];

    // Previous-tick position, for render interpolation (docs/09 §4).
    private readonly float[] _prevX = new float[Cap];
    private readonly float[] _prevY = new float[Cap];
    private readonly float[] _prevRot = new float[Cap];

    private int _count;

    /// <summary>Where the player currently is, for homing. Set alongside TargetPosition.</summary>
    private Vector2 HomingTarget => TargetPosition;

    // ---------------------------------------------------------------- Rendering

    private MultiMeshInstance2D _bodyLayer = null!;
    private MultiMeshInstance2D _shadowLayer = null!;
    private MultiMesh _bodyMesh = null!;
    private MultiMesh _shadowMesh = null!;
    private readonly float[] _bodyBuffer = new float[Cap * Stride];
    private readonly float[] _shadowBuffer = new float[Cap * Stride];

    // ---------------------------------------------------------------- Collision inputs

    /// <summary>Set by PlayerController each tick. The ONE circle enemy bullets test against.</summary>
    public Vector2 TargetPosition;
    public float TargetRadius = Tune.PlayerHitboxRadius;

    /// <summary>When true (Blink Step i-frames), bullets pass through without being consumed.</summary>
    public bool TargetInvulnerable;

    /// <summary>Arena bounds. Bullets leaving are despawned (or reflected, per flag).</summary>
    public Rect2 Bounds = new(-2000, -2000, 4000, 4000);

    // ---------------------------------------------------------------- Frame outputs

    /// <summary>Hits landed on the player this tick. Polled and cleared by PlayerController —
    /// a counter rather than an event, so there is no delegate allocation in the tick.</summary>
    public int HitsThisTick { get; private set; }

    // ---------------------------------------------------------------- Enemy collision
    // Used only when CollideWithEnemies is set (the player-bullet manager).
    //
    // docs/09 §3.2 specifies a uniform spatial hash here. At M1 scale that is premature:
    // ~200 player bullets against <=64 enemies is 12,800 distance tests, which is cheaper
    // than building and querying the hash. Revisit when enemy counts exceed ~120 — the
    // arrays below are already the right shape to drop a hash in front of.

    private const int MaxTargets = 128;
    private readonly float[] _tgtX = new float[MaxTargets];
    private readonly float[] _tgtY = new float[MaxTargets];
    private readonly float[] _tgtR = new float[MaxTargets];
    private readonly int[] _tgtId = new int[MaxTargets];
    private int _tgtCount;

    /// <summary>Set true on the player-bullet manager. Swaps the collision target set.</summary>
    public bool CollideWithEnemies;

    private const int MaxHitsPerTick = 256;
    private readonly int[] _hitIds = new int[MaxHitsPerTick];
    private readonly float[] _hitDamage = new float[MaxHitsPerTick];
    private int _hitCount;

    public void BeginTargetRegistration() => _tgtCount = 0;

    public void RegisterTarget(int id, Vector2 position, float radius)
    {
        if (_tgtCount >= MaxTargets) return;
        _tgtX[_tgtCount] = position.X;
        _tgtY[_tgtCount] = position.Y;
        _tgtR[_tgtCount] = radius;
        _tgtId[_tgtCount] = id;
        _tgtCount++;
    }

    /// <summary>Hits landed on enemies this tick, as parallel spans. Consumed by the
    /// enemy manager and cleared at the start of the next tick.</summary>
    public int EnemyHitCount => _hitCount;
    public int GetHitId(int i) => _hitIds[i];
    public float GetHitDamage(int i) => _hitDamage[i];

    public int Count => _count;
    public int Capacity => Cap;

    /// <summary>Bullets rejected because the pool was full. Non-zero means an encounter
    /// exceeded its design budget (docs/05 R7) and should be re-authored, not clamped.</summary>
    public int OverflowCount { get; private set; }

    public override void _Ready()
    {
        // Draw shadows first so they sit beneath every bullet body.
        _shadowMesh = BuildMesh();
        _shadowLayer = new MultiMeshInstance2D { Multimesh = _shadowMesh, Name = "ShadowLayer", ZIndex = -1 };
        AddChild(_shadowLayer);

        _bodyMesh = BuildMesh();
        _bodyLayer = new MultiMeshInstance2D { Multimesh = _bodyMesh, Name = "BodyLayer", ZIndex = 0 };
        AddChild(_bodyLayer);
    }

    private static MultiMesh BuildMesh()
    {
        // A unit quad; per-instance scale carries the bullet's actual diameter.
        var quad = new QuadMesh { Size = Vector2.One };

        return new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = false,
            Mesh = quad,
            // Allocate ONCE at capacity. Resizing InstanceCount per frame reallocates GPU
            // buffers; VisibleInstanceCount is the free way to vary how many draw.
            InstanceCount = Cap,
            VisibleInstanceCount = 0,
        };
    }

    // ================================================================== SPAWNING

    /// <summary>
    /// Spawn a bullet. Returns false if the pool is full — callers must tolerate this
    /// rather than assuming success. Deliberately takes primitives, not a params struct,
    /// so there is no chance of a boxed allocation on a hot path.
    /// </summary>
    public bool Spawn(
        Vector2 position,
        Vector2 velocity,
        float radius,
        float lifetime,
        Color color,
        float renderSize = 0f,
        BulletFlags flags = BulletFlags.None,
        BulletBehaviour behaviour = BulletBehaviour.Straight,
        float bhParam0 = 0f,
        float bhParam1 = 0f,
        float damage = 0f)
    {
        if (_count >= Cap)
        {
            OverflowCount++;
            return false;
        }

        int i = _count++;

        _posX[i] = position.X;
        _posY[i] = position.Y;
        _prevX[i] = position.X;   // no interpolation smear on the spawn frame
        _prevY[i] = position.Y;
        _velX[i] = velocity.X;
        _velY[i] = velocity.Y;
        _radius[i] = radius;
        _life[i] = lifetime;
        _size[i] = renderSize > 0f ? renderSize : radius * 2f;
        _flags[i] = (int)flags;
        _colR[i] = color.R;
        _colG[i] = color.G;
        _colB[i] = color.B;

        _damage[i] = damage;
        _bhType[i] = (byte)behaviour;
        _bhP0[i] = bhParam0;
        _bhP1[i] = bhParam1;
        _age[i] = 0f;
        _baseSpeed[i] = velocity.Length();

        float rot = Mathf.Atan2(velocity.Y, velocity.X);
        _rot[i] = rot;
        _prevRot[i] = rot;

        return true;
    }

    public void Clear()
    {
        _count = 0;
        OverflowCount = 0;
        HitsThisTick = 0;
    }

    // ================================================================== SIMULATION

    /// <summary>
    /// Fixed 60Hz tick. MUST NOT ALLOCATE.
    ///
    /// Work per bullet: integrate, bounds test, one circle-circle test against the player.
    /// At 4096 bullets that is ~16k floating-point ops — comfortably under 0.1ms.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        float dt = (float)delta;
        HitsThisTick = 0;
        _hitCount = 0;

        float tx = TargetPosition.X;
        float ty = TargetPosition.Y;
        float tr = TargetRadius;
        bool canHit = !TargetInvulnerable;

        float minX = Bounds.Position.X;
        float minY = Bounds.Position.Y;
        float maxX = minX + Bounds.Size.X;
        float maxY = minY + Bounds.Size.Y;

        int i = 0;
        while (i < _count)
        {
            _prevX[i] = _posX[i];
            _prevY[i] = _posY[i];
            _prevRot[i] = _rot[i];

            float age = _age[i] + dt;
            _age[i] = age;

            // --- Behaviour (docs/05 §4.2). Straight is the overwhelmingly common case and
            // is hoisted out so the branch predictor sees a stable path for most bullets.
            byte bh = _bhType[i];
            if (bh != (byte)BulletBehaviour.Straight)
            {
                ApplyBehaviour(i, bh, age, dt, tx, ty);
            }

            float x = _posX[i] + _velX[i] * dt;
            float y = _posY[i] + _velY[i] * dt;
            float life = _life[i] - dt;

            bool dead = life <= 0f;
            int flags = _flags[i];

            // --- Walls / bounds -------------------------------------------------------
            if (!dead && (flags & (int)BulletFlags.IgnoresWalls) == 0)
            {
                bool outside = x < minX || x > maxX || y < minY || y > maxY;
                if (outside)
                {
                    if ((flags & (int)BulletFlags.BouncesOffWalls) != 0)
                    {
                        if (x < minX || x > maxX) { _velX[i] = -_velX[i]; x = Math.Clamp(x, minX, maxX); }
                        if (y < minY || y > maxY) { _velY[i] = -_velY[i]; y = Math.Clamp(y, minY, maxY); }
                        _rot[i] = Mathf.Atan2(_velY[i], _velX[i]);
                    }
                    else
                    {
                        dead = true;
                    }
                }
            }

            // --- Targets ------------------------------------------------------------
            if (!dead && CollideWithEnemies)
            {
                // Player bullets: scan the registered enemy circles.
                for (int t = 0; t < _tgtCount; t++)
                {
                    float dx = x - _tgtX[t];
                    float dy = y - _tgtY[t];
                    float rr = _radius[i] + _tgtR[t];
                    if (dx * dx + dy * dy > rr * rr) continue;

                    if (_hitCount < MaxHitsPerTick)
                    {
                        _hitIds[_hitCount] = _tgtId[t];
                        _hitDamage[_hitCount] = _damage[i];
                        _hitCount++;
                    }
                    if ((flags & (int)BulletFlags.Piercing) == 0) { dead = true; break; }
                }
            }
            else if (!dead && canHit && (flags & (int)BulletFlags.Hallucination) == 0)
            {
                // Enemy bullets: one circle-circle test against the player.
                // Hallucinations skip this entirely — visually identical, cannot interact
                // (docs/02 §3.4).
                float dx = x - tx;
                float dy = y - ty;
                float rr = _radius[i] + tr;
                if (dx * dx + dy * dy <= rr * rr)
                {
                    HitsThisTick++;
                    if ((flags & (int)BulletFlags.Piercing) == 0) dead = true;
                }
            }

            if (dead)
            {
                SwapRemove(i);
                continue;   // index i now holds a different bullet — do not advance
            }

            _posX[i] = x;
            _posY[i] = y;
            _life[i] = life;
            i++;
        }

        LastTickMicroseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                               * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>
    /// Cost of THIS system's tick, isolated. Godot's TIME_PHYSICS_PROCESS monitor
    /// aggregates every node's _PhysicsProcess plus engine overhead, which is the wrong
    /// instrument for gating one subsystem against a 0.4ms budget (docs/09 §8).
    /// Two Stopwatch reads per tick; no allocation.
    /// </summary>
    public double LastTickMicroseconds { get; private set; }

    /// <summary>
    /// Per-bullet behaviour modifiers. Mutates velocity/rotation in place; integration
    /// happens in the caller. No allocation, no virtual dispatch — a switch on a byte.
    ///
    /// A note on why this is not an interface with polymorphic Update(): 4096 virtual
    /// calls per tick through a cold vtable costs more than the entire rest of the loop,
    /// and every implementation would need its own object, which is 4096 heap allocations.
    /// </summary>
    private void ApplyBehaviour(int i, byte bh, float age, float dt, float tx, float ty)
    {
        switch ((BulletBehaviour)bh)
        {
            case BulletBehaviour.Homing:
            {
                // p0 = turn rate (rad/s), p1 = duration. Weak homing that expires — the
                // design calls for 12°/s (docs/03 Whispering Rounds), which curves a shot
                // without making it undodgeable.
                if (age > _bhP1[i]) break;

                float curr = _rot[i];
                float desired = Mathf.Atan2(ty - _posY[i], tx - _posX[i]);
                float delta = Mathf.AngleDifference(curr, desired);
                float maxTurn = _bhP0[i] * dt;
                float newRot = curr + Math.Clamp(delta, -maxTurn, maxTurn);

                float spd = _baseSpeed[i];
                _velX[i] = Mathf.Cos(newRot) * spd;
                _velY[i] = Mathf.Sin(newRot) * spd;
                _rot[i] = newRot;
                break;
            }

            case BulletBehaviour.Wave:
            {
                // p0 = amplitude (px/s of lateral push), p1 = frequency (Hz).
                // Perpendicular oscillation about the original heading.
                float baseRot = _rot[i];
                float spd = _baseSpeed[i];
                float lateral = Mathf.Sin(age * _bhP1[i] * Mathf.Tau) * _bhP0[i];
                _velX[i] = Mathf.Cos(baseRot) * spd + Mathf.Cos(baseRot + Mathf.Pi / 2f) * lateral;
                _velY[i] = Mathf.Sin(baseRot) * spd + Mathf.Sin(baseRot + Mathf.Pi / 2f) * lateral;
                break;
            }

            case BulletBehaviour.Accelerate:
            {
                // p0 = acceleration (px/s²), p1 = max speed multiplier.
                float spd = _baseSpeed[i] + _bhP0[i] * age;
                float cap = _baseSpeed[i] * (_bhP1[i] > 0f ? _bhP1[i] : 4f);
                if (spd > cap) spd = cap;
                if (spd < 0f) spd = 0f;
                float r = _rot[i];
                _velX[i] = Mathf.Cos(r) * spd;
                _velY[i] = Mathf.Sin(r) * spd;
                break;
            }

            case BulletBehaviour.DelayThenGo:
            {
                // p0 = hold duration. The classic "freeze in place, then fire" trick
                // (docs/05 §4.2 .Delay). Reads as a pause and then a sudden wall.
                if (age < _bhP0[i])
                {
                    _velX[i] = 0f;
                    _velY[i] = 0f;
                }
                else if (_velX[i] == 0f && _velY[i] == 0f)
                {
                    float r = _rot[i];
                    float spd = _baseSpeed[i];
                    _velX[i] = Mathf.Cos(r) * spd;
                    _velY[i] = Mathf.Sin(r) * spd;
                }
                break;
            }
        }
    }

    private void SwapRemove(int i)
    {
        int last = --_count;
        if (i == last) return;

        _posX[i] = _posX[last];
        _posY[i] = _posY[last];
        _velX[i] = _velX[last];
        _velY[i] = _velY[last];
        _radius[i] = _radius[last];
        _life[i] = _life[last];
        _rot[i] = _rot[last];
        _size[i] = _size[last];
        _flags[i] = _flags[last];
        _colR[i] = _colR[last];
        _colG[i] = _colG[last];
        _colB[i] = _colB[last];
        _damage[i] = _damage[last];
        _bhType[i] = _bhType[last];
        _bhP0[i] = _bhP0[last];
        _bhP1[i] = _bhP1[last];
        _age[i] = _age[last];
        _baseSpeed[i] = _baseSpeed[last];
        _prevX[i] = _prevX[last];
        _prevY[i] = _prevY[last];
        _prevRot[i] = _prevRot[last];
    }

    // ================================================================== RENDERING

    /// <summary>
    /// Runs at display rate, interpolating between the last two sim ticks so the game
    /// looks 144Hz while simulating 60Hz (docs/09 §4).
    ///
    /// Both buffers are uploaded with a single Buffer assignment each. Setting instance
    /// transforms individually would mean ~8000 marshalled interop calls per frame and is
    /// the single easiest way to destroy this system's performance.
    /// </summary>
    public override void _Process(double delta)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        float f = (float)Engine.GetPhysicsInterpolationFraction();
        int shadowCount = 0;

        for (int i = 0; i < _count; i++)
        {
            float x = _prevX[i] + (_posX[i] - _prevX[i]) * f;
            float y = _prevY[i] + (_posY[i] - _prevY[i]) * f;
            float rot = _prevRot[i] + Mathf.AngleDifference(_prevRot[i], _rot[i]) * f;

            float s = _size[i];
            float cos = Mathf.Cos(rot);
            float sin = Mathf.Sin(rot);

            WriteInstance(_bodyBuffer, i, cos * s, -sin * s, x, sin * s, cos * s, y,
                          _colR[i], _colG[i], _colB[i], 1f);

            // Shadow: real bullets only. Its ABSENCE is the hallucination tell, so this
            // branch is load-bearing gameplay logic (docs/05 R9).
            if ((_flags[i] & (int)BulletFlags.Hallucination) == 0)
            {
                float ss = s * Tune.BulletShadowScale;
                WriteInstance(_shadowBuffer, shadowCount,
                              cos * ss, -sin * ss, x + Tune.BulletShadowOffset.X,
                              sin * ss, cos * ss, y + Tune.BulletShadowOffset.Y,
                              0f, 0f, 0f, Tune.BulletShadowAlpha);
                shadowCount++;
            }
        }

        _bodyMesh.Buffer = _bodyBuffer;
        _bodyMesh.VisibleInstanceCount = _count;

        _shadowMesh.Buffer = _shadowBuffer;
        _shadowMesh.VisibleInstanceCount = shadowCount;

        LastRenderMicroseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                                 * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
        ShadowCount = shadowCount;
    }

    /// <summary>Cost of buffer construction + upload, isolated from the rest of the frame.</summary>
    public double LastRenderMicroseconds { get; private set; }

    /// <summary>Real (shadow-casting) bullets on screen. The difference from Count is the
    /// number of hallucinations the player is currently being asked to read.</summary>
    public int ShadowCount { get; private set; }

    /// <summary>
    /// Godot's 2D MultiMesh buffer layout: two transform rows of 4 floats, then RGBA.
    ///   row0 = (basis.x.x, basis.y.x, 0, origin.x)
    ///   row1 = (basis.x.y, basis.y.y, 0, origin.y)
    /// </summary>
    private static void WriteInstance(
        float[] buffer, int index,
        float xx, float yx, float ox,
        float xy, float yy, float oy,
        float r, float g, float b, float a)
    {
        int o = index * Stride;
        buffer[o + 0] = xx;
        buffer[o + 1] = yx;
        buffer[o + 2] = 0f;
        buffer[o + 3] = ox;
        buffer[o + 4] = xy;
        buffer[o + 5] = yy;
        buffer[o + 6] = 0f;
        buffer[o + 7] = oy;
        buffer[o + 8] = r;
        buffer[o + 9] = g;
        buffer[o + 10] = b;
        buffer[o + 11] = a;
    }

    // ================================================================== DETERMINISM

    /// <summary>
    /// Order-independent hash of live bullet state, for the replay test (docs/09 §9).
    ///
    /// Must be order-INDEPENDENT: swap-remove permutes the arrays, so two identical
    /// simulations can hold the same bullets at different indices. Summing per-bullet
    /// hashes is therefore correct where a sequential FNV walk would produce false
    /// divergences.
    /// </summary>
    public ulong StateHash()
    {
        ulong acc = (ulong)_count * 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < _count; i++)
        {
            ulong h = 14695981039346656037UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(_posX[i])) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(_posY[i])) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(_velX[i])) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(_velY[i])) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(_life[i])) * 1099511628211UL;
            h = (h ^ (uint)_flags[i]) * 1099511628211UL;
            acc += h;
        }
        return acc;
    }
}
