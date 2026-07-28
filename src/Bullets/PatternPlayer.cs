using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Bullets;

/// <summary>
/// Runs a <see cref="PatternData"/> timeline and emits into a <see cref="BulletManager"/>.
///
/// A plain struct-like class, not a Node: enemies own one each, and at 60 concurrent
/// enemies the allocation and tree-traversal cost of a Node per emitter is pure waste.
///
/// Owns the telegraph. docs/05 R3 is not advisory — the player must get a readable
/// wind-up before every volley, so the state machine here is Telegraph -> Firing -> Done
/// and there is no path that skips the first state.
/// </summary>
public sealed class PatternPlayer
{
    public enum State { Idle, Telegraph, Firing, Finished }

    private PatternData? _pattern;
    private BulletManager? _bullets;
    private Rng? _rng;

    private State _state = State.Idle;
    private float _timer;
    private int _burstIndex;
    private float _spiralAngle;
    private float _spiralDirection = 1f;
    private float _reverseTimer;

    public State Current => _state;
    public bool IsActive => _state is State.Telegraph or State.Firing;

    /// <summary>0..1 through the telegraph. Drives the wind-up animation and the audio cue.</summary>
    public float TelegraphProgress { get; private set; }

    public void Configure(PatternData pattern, BulletManager bullets, Rng rng)
    {
        _pattern = pattern;
        _bullets = bullets;
        _rng = rng;
    }

    public void Fire()
    {
        if (_pattern is null) return;
        _state = State.Telegraph;
        _timer = 0f;
        _burstIndex = 0;
        TelegraphProgress = 0f;
    }

    public void Cancel()
    {
        _state = State.Idle;
        TelegraphProgress = 0f;
    }

    /// <summary>
    /// Advance. `origin` is the emitter position, `facing` its aim direction, `targetPos`
    /// and `targetVel` the player (for Aimed lead and RingIn centring).
    /// </summary>
    public void Tick(float dt, Vector2 origin, Vector2 facing, Vector2 targetPos, Vector2 targetVel)
    {
        if (_pattern is null || _bullets is null || _rng is null) return;

        // The spiral emitter rotates continuously, including between volleys — that
        // persistence is what makes consecutive spiral bursts interlock instead of
        // stacking on the same angles.
        if (_pattern.Primitive == PatternPrimitive.Spiral)
        {
            _spiralAngle += Mathf.DegToRad(_pattern.SpiralRateDegPerSec) * _spiralDirection * dt;
            if (_pattern.SpiralReverseInterval > 0f)
            {
                _reverseTimer += dt;
                if (_reverseTimer >= _pattern.SpiralReverseInterval)
                {
                    _reverseTimer = 0f;
                    _spiralDirection = -_spiralDirection;
                }
            }
        }

        switch (_state)
        {
            case State.Telegraph:
                _timer += dt;
                TelegraphProgress = _pattern.TelegraphSeconds <= 0f
                    ? 1f
                    : Mathf.Clamp(_timer / _pattern.TelegraphSeconds, 0f, 1f);

                if (_timer >= _pattern.TelegraphSeconds)
                {
                    _state = State.Firing;
                    _timer = 0f;
                    EmitVolley(origin, facing, targetPos, targetVel);
                    _burstIndex = 1;
                    if (_burstIndex >= _pattern.BurstCount) _state = State.Finished;
                }
                break;

            case State.Firing:
                _timer += dt;
                while (_timer >= _pattern.BurstInterval && _burstIndex < _pattern.BurstCount)
                {
                    _timer -= _pattern.BurstInterval;
                    EmitVolley(origin, facing, targetPos, targetVel);
                    _burstIndex++;
                }
                if (_burstIndex >= _pattern.BurstCount) _state = State.Finished;
                break;
        }
    }

    private void EmitVolley(Vector2 origin, Vector2 facing, Vector2 targetPos, Vector2 targetVel)
    {
        PatternData p = _pattern!;
        BulletManager b = _bullets!;
        Rng rng = _rng!;

        float baseAngle = Mathf.Atan2(facing.Y, facing.X) + Mathf.DegToRad(p.OffsetDegrees);
        float spread = Mathf.DegToRad(p.SpreadDegrees);

        switch (p.Primitive)
        {
            case PatternPrimitive.Radial:
            {
                // Full circles must not double up a bullet at the seam, so the step
                // divides by Count rather than Count-1 when the spread closes.
                bool closed = p.SpreadDegrees >= 359.9f;
                float step = closed ? spread / p.Count : (p.Count > 1 ? spread / (p.Count - 1) : 0f);
                float start = closed ? baseAngle : baseAngle - spread * 0.5f;
                for (int i = 0; i < p.Count; i++) EmitOne(start + step * i, origin, p, b, rng);
                break;
            }

            case PatternPrimitive.Spiral:
            {
                float step = Mathf.Tau / p.Count;
                for (int i = 0; i < p.Count; i++) EmitOne(_spiralAngle + step * i, origin, p, b, rng);
                break;
            }

            case PatternPrimitive.Aimed:
            {
                // Lead the target by its own velocity. AimLead is in seconds, so a value
                // of 0.3 means "shoot where they'll be in 0.3s if they keep moving".
                Vector2 predicted = targetPos + targetVel * p.AimLead;
                float aim = Mathf.Atan2(predicted.Y - origin.Y, predicted.X - origin.X);
                float step = p.Count > 1 ? spread / (p.Count - 1) : 0f;
                float start = aim - spread * 0.5f;
                for (int i = 0; i < p.Count; i++) EmitOne(start + step * i, origin, p, b, rng);
                break;
            }

            case PatternPrimitive.Wall:
            {
                // A line perpendicular to `facing`, with gaps. The gap IS the dodge, so
                // its placement is randomised per volley to stop players memorising a lane.
                Vector2 dir = facing.Normalized();
                Vector2 perp = new(-dir.Y, dir.X);
                int gapStart = rng.NextInt(0, Mathf.Max(1, p.Count - p.WallGaps));

                for (int i = 0; i < p.Count; i++)
                {
                    if (i >= gapStart && i < gapStart + p.WallGaps) continue;
                    float t = p.Count > 1 ? i / (float)(p.Count - 1) - 0.5f : 0f;
                    Vector2 pos = origin + perp * (t * p.Extent);
                    EmitAt(pos, dir, p, b, rng);
                }
                break;
            }

            case PatternPrimitive.Arc:
            {
                float step = p.Count > 1 ? spread / (p.Count - 1) : 0f;
                float sweep = _burstIndex / (float)Mathf.Max(1, p.BurstCount) * spread;
                float start = baseAngle - spread * 0.5f + sweep;
                for (int i = 0; i < p.Count; i++) EmitOne(start + step * i * 0.25f, origin, p, b, rng);
                break;
            }

            case PatternPrimitive.RingIn:
            {
                // Spawns on a circle around the PLAYER and converges. Denies the "run to
                // the edge" answer that beats most radial patterns.
                float step = Mathf.Tau / p.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    float a = step * i + Mathf.DegToRad(p.OffsetDegrees);
                    Vector2 pos = targetPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * p.Extent;
                    EmitAt(pos, (targetPos - pos).Normalized(), p, b, rng);
                }
                break;
            }
        }
    }

    private static void EmitOne(float angle, Vector2 origin, PatternData p, BulletManager b, Rng rng)
        => EmitAt(origin, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), p, b, rng);

    private static void EmitAt(Vector2 pos, Vector2 dir, PatternData p, BulletManager b, Rng rng)
    {
        float speed = p.Speed;
        if (p.SpeedVariance > 0f) speed += rng.Range(-p.SpeedVariance, p.SpeedVariance);

        b.Spawn(
            position: pos,
            velocity: dir * speed,
            radius: p.Radius,
            lifetime: p.Lifetime,
            color: p.Colour,
            renderSize: p.RenderSize,
            flags: BulletFlags.None,
            behaviour: p.Behaviour,
            bhParam0: p.BehaviourP0,
            bhParam1: p.BehaviourP1);
    }
}
