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

    // Previous-tick position, for render interpolation (docs/09 §4).
    private readonly float[] _prevX = new float[Cap];
    private readonly float[] _prevY = new float[Cap];
    private readonly float[] _prevRot = new float[Cap];

    private int _count;

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
        BulletFlags flags = BulletFlags.None)
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

            // --- Player -------------------------------------------------------------
            // Hallucinations skip this entirely: they are visually identical but cannot
            // interact (docs/02 §3.4).
            if (!dead && canHit && (flags & (int)BulletFlags.Hallucination) == 0)
            {
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
