using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Enemies;

/// <summary>
/// Owns every enemy in the room and, crucially, the ATTACK TOKEN POOL.
///
/// docs/05 §8 calls the token pool "the single most important knob for making a room
/// fair", and it is also how R7's 600-bullet ceiling is honoured by DESIGN rather than by
/// clamping at runtime: if only N enemies may be in Attack at once, the on-screen bullet
/// count has a hard analytic bound. Clamping bullets at spawn time would instead delete
/// projectiles the player had already started dodging, which is far worse than a dense
/// screen.
/// </summary>
public sealed partial class EnemyManager : Node2D
{
    private readonly List<Enemy> _enemies = new(96);
    private BulletManager _enemyBullets = null!;
    private BulletManager _playerBullets = null!;
    private FlowField _field = null!;
    private Rng _rng = null!;

    private int _nextId = 1;
    private float _repathTimer;

    /// <summary>Concurrent attackers allowed. Scales by floor: 4 on Floor 1, 9 on Floor 6.</summary>
    public int AttackTokens { get; set; } = 4;

    public Vector2 PlayerPosition;
    public Vector2 PlayerVelocity;

    /// <summary>docs/02 §6 — "enemies flee or become erratic". Set while the player is
    /// Ascended. This is what sells the transformation: the room's behaviour changes, not
    /// just the player's stats.</summary>
    public bool PlayerAscended;

    public int AliveCount { get; private set; }
    public int KilledThisRoom { get; private set; }

    /// <summary>Sanity owed to the player from kills this tick. Polled and cleared by the
    /// player — a value rather than an event, to keep the tick allocation-free.</summary>
    public float PendingSanityReward { get; private set; }

    /// <summary>Enemies killed this tick, for hit-stop and mote spawning.</summary>
    public int KillsThisTick { get; private set; }

    public IReadOnlyList<Enemy> Enemies => _enemies;

    public void Initialise(BulletManager enemyBullets, BulletManager playerBullets, Rect2 bounds, Rng rng)
    {
        _enemyBullets = enemyBullets;
        _playerBullets = playerBullets;
        _rng = rng;
        _field = new FlowField(bounds);
    }

    public Enemy Spawn(EnemyData data, Vector2 position)
    {
        var e = new Enemy(_nextId++, data, position, _enemyBullets, _rng);
        _enemies.Add(e);

        // Updated HERE and not only in the tick. Godot ticks parents before children, so
        // a room owner that spawns enemies and then checks AliveCount in the same frame
        // would see 0 and immediately declare the room cleared. That bug made every room
        // in the M1 slice complete instantly, and it looked like correct behaviour in the
        // log because the next room started normally.
        AliveCount++;
        return e;
    }

    public void ClearAll()
    {
        _enemies.Clear();
        AliveCount = 0;
        KilledThisRoom = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        PendingSanityReward = 0f;
        KillsThisTick = 0;

        // Repath ~10x/sec. The player moves ~1.5px per tick against a 24px cell, so a
        // field refreshed every 6 ticks is indistinguishable from one refreshed every tick.
        _repathTimer -= dt;
        if (_repathTimer <= 0f)
        {
            _repathTimer = 0.1f;
            _field.Rebuild(PlayerPosition);
        }

        ResolvePlayerBulletHits();
        AllocateAttackTokens();

        AliveCount = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (!e.Alive) continue;
            e.Ascended = PlayerAscended;
            e.Tick(dt, PlayerPosition, PlayerVelocity, _field);
            AliveCount++;
        }

        ApplySeparation(dt);
        PublishTargets();
        QueueFree_DeadEnemies();
    }

    /// <summary>
    /// Hand out the limited attack tokens. Nearest-first, because a distant enemy holding
    /// a token while an adjacent one waits reads as the AI being asleep.
    /// </summary>
    private void AllocateAttackTokens()
    {
        int held = 0;
        for (int i = 0; i < _enemies.Count; i++)
            if (_enemies[i].Alive && _enemies[i].HoldsAttackToken) held++;

        int free = AttackTokens - held;
        if (free <= 0) return;

        while (free > 0)
        {
            Enemy? best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _enemies.Count; i++)
            {
                Enemy e = _enemies[i];
                if (!e.Alive || e.HoldsAttackToken || !e.WantsToAttack) continue;
                float d = e.Position.DistanceSquaredTo(PlayerPosition);
                if (d >= bestDist) continue;
                bestDist = d;
                best = e;
            }

            if (best is null) break;
            best.GrantToken();
            free--;
        }
    }

    private void ResolvePlayerBulletHits()
    {
        int hits = _playerBullets.EnemyHitCount;
        if (hits == 0) return;

        for (int h = 0; h < hits; h++)
        {
            int id = _playerBullets.GetHitId(h);
            float dmg = _playerBullets.GetHitDamage(h);

            for (int i = 0; i < _enemies.Count; i++)
            {
                Enemy e = _enemies[i];
                if (e.Id != id || !e.Alive) continue;

                if (e.TakeDamage(dmg))
                {
                    // Post-F4 this is what funds the player's next reload (docs/02 §3.3).
                    PendingSanityReward += e.Data.SanityValue;
                    KilledThisRoom++;
                    KillsThisTick++;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Push overlapping enemies apart. This is a READABILITY requirement, not polish
    /// (docs/05 §8): stacked enemies merge into one silhouette, and the player cannot
    /// count threats they cannot distinguish.
    /// </summary>
    private void ApplySeparation(float dt)
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy a = _enemies[i];
            if (!a.Alive) continue;

            for (int j = i + 1; j < _enemies.Count; j++)
            {
                Enemy b = _enemies[j];
                if (!b.Alive) continue;

                Vector2 delta = b.Position - a.Position;
                float minDist = a.Data.BodyRadius + b.Data.BodyRadius;
                float distSq = delta.LengthSquared();
                if (distSq >= minDist * minDist || distSq < 0.0001f) continue;

                float dist = Mathf.Sqrt(distSq);
                Vector2 push = delta / dist * (minDist - dist) * 0.5f;
                a.Position -= push;
                b.Position += push;
            }
        }
    }

    private void PublishTargets()
    {
        _playerBullets.BeginTargetRegistration();
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (e.Alive) _playerBullets.RegisterTarget(e.Id, e.Position, e.Data.BodyRadius);
        }
    }

    private void QueueFree_DeadEnemies()
    {
        // Compacted lazily: removing mid-tick would invalidate the hit-resolution indices
        // above. Dead enemies are inert, so carrying them for one tick costs nothing.
        for (int i = _enemies.Count - 1; i >= 0; i--)
            if (!_enemies[i].Alive) _enemies.RemoveAt(i);
    }

    /// <summary>Melee resolution (docs/03 §2 Family V). Returns enemies struck.</summary>
    public int ResolveMeleeArc(Vector2 origin, Vector2 facing, float reach, float arcDegrees,
                               float damage, float knockback, out float sanityGained)
    {
        sanityGained = 0f;
        int struck = 0;
        float halfArc = Mathf.DegToRad(arcDegrees) * 0.5f;
        float facingAngle = Mathf.Atan2(facing.Y, facing.X);

        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (!e.Alive) continue;

            Vector2 delta = e.Position - origin;
            float dist = delta.Length();
            if (dist > reach + e.Data.BodyRadius) continue;

            float angle = Mathf.Atan2(delta.Y, delta.X);
            if (Mathf.Abs(Mathf.AngleDifference(facingAngle, angle)) > halfArc) continue;

            struck++;
            // Knockback resets contact, so melee is a SPACING weapon rather than a
            // hugging one — the fix for the contact-damage finding in docs/03 §2.
            e.ApplyKnockback(delta.Normalized() * knockback);

            if (e.TakeDamage(damage))
            {
                sanityGained += e.Data.SanityValue;
                KilledThisRoom++;
                KillsThisTick++;
            }
        }
        return struck;
    }

    /// <summary>
    /// Banish shove (docs/02 §5.2). Knockback falls off with distance so the edge of the
    /// radius nudges and the centre throws — a flat impulse makes the whole room lurch
    /// identically and reads as a cutscene rather than a shockwave.
    /// Returns the number of enemies affected.
    /// </summary>
    public int ApplyBanish(Vector2 centre, float radius, float knockback, float stunSeconds)
    {
        int affected = 0;
        float r2 = radius * radius;

        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (!e.Alive) continue;

            Vector2 delta = e.Position - centre;
            float distSq = delta.LengthSquared();
            if (distSq > r2) continue;

            float dist = Mathf.Sqrt(distSq);
            Vector2 dir = dist > 0.01f ? delta / dist : Vector2.Right;
            float falloff = 1f - Mathf.Clamp(dist / radius, 0f, 1f);

            e.ApplyBanish(dir * knockback * (0.4f + 0.6f * falloff), stunSeconds);
            affected++;
        }
        return affected;
    }

    /// <summary>Contact damage check. Returns the largest contact damage overlapping.</summary>
    public float QueryContactDamage(Vector2 playerPos, float playerRadius)
    {
        float worst = 0f;
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (!e.Alive || e.Data.ContactDamage <= 0f) continue;
            float rr = playerRadius + e.Data.BodyRadius;
            if (e.Position.DistanceSquaredTo(playerPos) <= rr * rr && e.Data.ContactDamage > worst)
                worst = e.Data.ContactDamage;
        }
        return worst;
    }
}
