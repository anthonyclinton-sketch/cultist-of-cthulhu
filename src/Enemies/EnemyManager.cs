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

    /// <summary>Concurrent attackers allowed. Set per floor by the owning scene from
    /// <see cref="FloorScaling.AttackTokens"/> — the default is floor 1's value, and that is
    /// exactly why the missing assignment in FloorRunner went unnoticed for a milestone.</summary>
    public int AttackTokens { get; set; } = Tune.AttackTokensFirstFloor;

    public Vector2 PlayerPosition;
    public Vector2 PlayerVelocity;

    /// <summary>docs/02 §6 — "enemies flee or become erratic". Set while the player is
    /// Ascended. This is what sells the transformation: the room's behaviour changes, not
    /// just the player's stats.</summary>
    public bool PlayerAscended;

    /// <summary>Driven from the player's Sanity band each tick (docs/02 §3.4).</summary>
    public float HallucinationRatio;

    /// <summary>The tide, pushed down each tick (docs/07 §3). Null on every floor but the
    /// Wharfs, and the null check is the whole cost of the tide elsewhere.</summary>
    public Core.TideField? Water;
    public float TideLevel;

    /// <summary>
    /// Extra damage against enemies below 30% health — docs/04 §5.1, Rite of the Open Wound.
    ///
    /// A bare float pushed down by the player, not a reference to the Sigil Circle. This is
    /// the only place in the codebase that can see a target's remaining health at the moment
    /// of the hit, so the condition has to be evaluated here; that is not a reason for the
    /// enemy system to learn what a sigil is.
    /// </summary>
    public float ExecuteDamageBonus;

    /// <summary>Health fraction below which <see cref="ExecuteDamageBonus"/> applies.</summary>
    private const float ExecuteThreshold = 0.30f;

    public int AliveCount { get; private set; }
    public int KilledThisRoom { get; private set; }

    /// <summary>Every enemy this manager has ever spawned. Never reset per room — it is the
    /// autorun's control that its rooms held anything, and a per-room counter would be zero
    /// by the time anyone asked.</summary>
    public int TotalSpawned { get; private set; }

    /// <summary>Sanity owed to the player from kills this tick. Polled and cleared by the
    /// player — a value rather than an event, to keep the tick allocation-free.</summary>
    public float PendingSanityReward { get; private set; }

    /// <summary>Enemies killed this tick, for hit-stop and mote spawning.</summary>
    public int KillsThisTick { get; private set; }

    private const int MaxKillsPerTick = 32;
    private readonly float[] _killValues = new float[MaxKillsPerTick];
    private readonly Vector2[] _killPositions = new Vector2[MaxKillsPerTick];

    /// <summary>Base Sanity value of the i-th kill this tick, before chain and i-frame
    /// multipliers — which the player applies, because only the player knows its own
    /// dash state and kill timing.</summary>
    public float GetKillValue(int i) => _killValues[Mathf.Min(i, MaxKillsPerTick - 1)];
    public Vector2 GetKillPosition(int i) => _killPositions[Mathf.Min(i, MaxKillsPerTick - 1)];

    /// <summary>Weak-point hits this tick, for feedback and telemetry.</summary>
    public int WeakPointHitsThisTick { get; private set; }

    /// <summary>
    /// Latched when the boss dies, and consumed exactly once.
    ///
    /// Not a per-tick flag, for the reason <see cref="Boss.ConsumePhaseChange"/> spells
    /// out: the death happens during hit resolution here, and Godot ticks the room owner
    /// BEFORE this node, so a flag cleared at the top of each tick would be set and
    /// destroyed within a frame without anyone seeing it.
    /// </summary>
    private bool _bossKilled;

    public bool ConsumeBossKilled()
    {
        if (!_bossKilled) return false;
        _bossKilled = false;
        return true;
    }

    /// <summary>Player bullets that landed on the boss this tick, for hit feedback.</summary>
    public int BossHitsThisTick { get; private set; }

    /// <summary>Mark every enemy overlapping a point — the dash-through (docs/02 §4).</summary>
    public int MarkOverlapping(Vector2 position, float radius)
    {
        int marked = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (!e.Alive || e.IsMarked) continue;
            float rr = radius + e.Data.BodyRadius;
            if (e.Position.DistanceSquaredTo(position) > rr * rr) continue;
            e.ApplyMark();
            marked++;
        }
        return marked;
    }

    public IReadOnlyList<Enemy> Enemies => _enemies;

    public void Initialise(BulletManager enemyBullets, BulletManager playerBullets, Rect2 bounds, Rng rng)
    {
        _enemyBullets = enemyBullets;
        _playerBullets = playerBullets;
        _rng = rng;
        _field = new FlowField(bounds);
    }

    /// <summary>
    /// The room's boss, if it has one.
    ///
    /// Registered and hit-resolved HERE rather than by the boss itself, because
    /// <see cref="PublishTargets"/> clears the player bullet manager's target list every
    /// tick — anything registering outside this method is erased before the next
    /// simulation step. Routing it through the same path also means weak points, Marked
    /// and the execute bonus apply to a boss for free, instead of via a second copy of the
    /// damage pipeline that would drift.
    /// </summary>
    public Boss? Boss { get; set; }

    /// <summary>Solid geometry for pathing and for enemy bodies. Null in the fixed arena,
    /// where the only walls are the bounds.</summary>
    public Core.TileMask? Walls { get; private set; }

    public void SetWalls(Core.TileMask mask)
    {
        Walls = mask;
        _field.ApplyMask(mask);
    }

    /// <summary>
    /// Re-read the mask into the flow field. Call after the geometry changes — a door
    /// sealing or opening.
    ///
    /// The field caches blocked cells at <see cref="SetWalls"/> time and only recomputes
    /// distances on a repath, so a seal written into the mask would stop enemy BODIES while
    /// still steering them at the door. Hard collision would hold, but the pathing would
    /// spend the whole fight pushing enemies into a wall.
    /// </summary>
    public void RefreshPathing()
    {
        if (Walls is not null) _field.ApplyMask(Walls);
    }

    /// <summary>
    /// Set from the player's Corruption each time a room populates (docs/02 §7.2). Read at
    /// SPAWN rather than per tick, so an enemy's toughness cannot change under the player
    /// mid-fight — Banishing four times during a room would otherwise awaken the things
    /// already fighting them.
    /// </summary>
    public bool SpawnAwakened { get; set; }

    public Enemy Spawn(EnemyData data, Vector2 position)
    {
        // Callers pick spawn points by area, which does not know about geometry. An enemy
        // placed inside a wall can never move out of it — MoveCircle refuses every step
        // that keeps it overlapping — so it stands there until shot.
        if (Walls is not null) position = Walls.NearestOpen(position, data.BodyRadius);

        var e = new Enemy(_nextId++, data, position, _enemyBullets, _rng, SpawnAwakened);
        _enemies.Add(e);
        TotalSpawned++;

        // Updated HERE and not only in the tick. Godot ticks parents before children, so
        // a room owner that spawns enemies and then checks AliveCount in the same frame
        // would see 0 and immediately declare the room cleared. That bug made every room
        // in the M1 slice complete instantly, and it looked like correct behaviour in the
        // log because the next room started normally.
        AliveCount++;
        return e;
    }

    /// <summary>
    /// Push anything standing in newly-solid ground back out, toward <paramref name="towards"/>.
    ///
    /// Called after a door seals. An enemy caught in the doorway at that moment is entombed:
    /// <see cref="Core.TileMask.MoveCircle"/> refuses every step that keeps a body
    /// overlapping, so a body that STARTS overlapping can never move again and stands there
    /// until shot.
    ///
    /// Biased toward a point rather than simply nearest-open, and the bias is the whole
    /// point: the two sides of a doorway are a contested room and a corridor, and an enemy
    /// evicted to the corridor side is outside a sealed room it cannot re-enter — so the
    /// encounter can never be cleared and the run is over. Pushing toward the room's own
    /// anchor keeps it in the fight.
    /// </summary>
    public void EvictFromSolid(Vector2 towards)
    {
        if (Walls is null) return;

        foreach (Enemy e in _enemies)
        {
            if (!e.Alive) continue;
            if (!Walls.CircleOverlaps(e.Position.X, e.Position.Y, e.Data.BodyRadius)) continue;

            Vector2 dir = (towards - e.Position).Normalized();
            if (dir.LengthSquared() < 0.01f) dir = Vector2.Right;

            // Walk it inward a tile at a time before falling back to a ring search, so the
            // result is a position in the room rather than merely the closest gap.
            bool freed = false;
            for (int step = 1; step <= 8; step++)
            {
                Vector2 candidate = e.Position + dir * (Walls.TileSize * step);
                if (Walls.CircleOverlaps(candidate.X, candidate.Y, e.Data.BodyRadius)) continue;
                e.Position = candidate;
                freed = true;
                break;
            }

            if (!freed) e.Position = Walls.NearestOpen(e.Position, e.Data.BodyRadius);
        }
    }

    public void ClearAll()
    {
        _enemies.Clear();
        AliveCount = 0;
        KilledThisRoom = 0;
        Boss = null;
        _bossKilled = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        PendingSanityReward = 0f;
        KillsThisTick = 0;
        WeakPointHitsThisTick = 0;
        BossHitsThisTick = 0;

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
            e.HallucinationRatio = HallucinationRatio;
            e.TideSpeedMultiplier = TideSpeedFor(e);
            e.Tick(dt, PlayerPosition, PlayerVelocity, _field, Walls);
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
            Vector2 at = _playerBullets.GetHitPosition(h);

            if (id == Boss.TargetId)
            {
                if (Boss is not null)
                {
                    if (Boss.TakeDamage(dmg)) _bossKilled = true;
                    BossHitsThisTick++;
                }
                continue;
            }

            for (int i = 0; i < _enemies.Count; i++)
            {
                Enemy e = _enemies[i];
                if (e.Id != id || !e.Alive) continue;

                // Weak point (docs/02 §3.4) — always live, only VISIBLE below Fraying.
                bool weakPoint = at.DistanceSquaredTo(e.WeakPointPosition)
                                 <= e.WeakPointRadius * e.WeakPointRadius;
                if (weakPoint)
                {
                    dmg *= Enemy.WeakPointDamageBonus;
                    WeakPointHitsThisTick++;
                }

                // Marked (docs/02 §4) — you dashed through it.
                if (e.IsMarked) dmg *= Enemy.MarkedDamageMultiplier;
                dmg *= ExecuteMultiplier(e);

                if (e.TakeDamage(dmg)) RecordKill(e);
                break;
            }
        }
    }

    private float ExecuteMultiplier(Enemy e)
    {
        if (ExecuteDamageBonus <= 0f) return 1f;
        // Against the instance's OWN maximum, not the authored one — an Awakened enemy has
        // more health, and reading Data here would make the execute window fire early on it.
        return e.Health <= e.MaxHealth * ExecuteThreshold ? 1f + ExecuteDamageBonus : 1f;
    }

    private void RecordKill(Enemy e)
    {
        // Post-F4 kill Sanity is what funds the player's next reload (docs/02 §3.3).
        // Values are recorded PER KILL rather than summed, because the chain bonus and
        // the i-frame multiplier are applied per kill by the player.
        if (KillsThisTick < MaxKillsPerTick)
        {
            _killValues[KillsThisTick] = e.Data.SanityValue;
            _killPositions[KillsThisTick] = e.Position;
        }
        PendingSanityReward += e.Data.SanityValue;
        KilledThisRoom++;
        KillsThisTick++;
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

                // Routed through the mask rather than assigned. Separation is the one force
                // that moves an enemy without consulting its velocity, so a pair squeezed
                // against a wall would otherwise push each other straight into it — and an
                // enemy that starts a tick inside solid ground never gets back out.
                if (Walls is null)
                {
                    a.Position -= push;
                    b.Position += push;
                }
                else
                {
                    a.Position = Walls.MoveCircle(a.Position, -push, a.Data.BodyRadius);
                    b.Position = Walls.MoveCircle(b.Position, push, b.Data.BodyRadius);
                }
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

        // The boss is registered even while invulnerable, so bullets still visibly stop on
        // it during a phase transition. Registering it only when damageable would make
        // shots pass through the boss for a second and a half, which reads as the fight
        // having broken rather than as the boss being briefly untouchable.
        if (Boss is { Alive: true }) _playerBullets.RegisterTarget(Boss.TargetId, Boss.Position, Boss.BodyRadius);
    }

    private void QueueFree_DeadEnemies()
    {
        // Compacted lazily: removing mid-tick would invalidate the hit-resolution indices
        // above. Dead enemies are inert, so carrying them for one tick costs nothing.
        for (int i = _enemies.Count - 1; i >= 0; i--)
            if (!_enemies[i].Alive) _enemies.RemoveAt(i);
    }

    /// <summary>
    /// Melee resolution (docs/03 §2 Family V). Returns enemies struck.
    ///
    /// Kill Sanity is NOT returned here — it goes through PendingSanityReward like every
    /// other kill. Returning it separately double-counted melee kills once RecordKill
    /// existed, and worse, it routed melee around the chain and i-frame multipliers, so a
    /// melee player silently got a different economy from a gun player.
    /// </summary>
    public int ResolveMeleeArc(Vector2 origin, Vector2 facing, float reach, float arcDegrees,
                               float damage, float knockback)
    {
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

            float dmg = damage;
            if (e.IsMarked) dmg *= Enemy.MarkedDamageMultiplier;
            dmg *= ExecuteMultiplier(e);
            if (e.TakeDamage(dmg)) RecordKill(e);
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

    /// <summary>
    /// docs/07 §3 — what the water does to this enemy right now. Swimmers gain, everything
    /// else wades and loses, and on dry land or a dry floor it is 1.
    ///
    /// Evaluated per enemy per tick rather than cached on the enemy when it enters water:
    /// the tide moves the shoreline under a stationary body, so "when it entered" is not a
    /// moment that exists.
    /// </summary>
    private float TideSpeedFor(Enemy e)
    {
        if (Water is null || !Water.AnyWater) return 1f;
        if (!Water.IsSubmerged(e.Position, TideLevel)) return 1f;
        return e.Data.SwimsInWater
            ? Core.Tune.TideSwimSpeedMultiplier
            : Core.Tune.TideWadeSpeedMultiplier;
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

        // The boss has a body too. Excluded in phase 3, where the passenger has no body at
        // all and its only means of touching you is the grab — contact damage there would
        // quietly double-charge the player for the same collision, once in hearts and once
        // in Sanity.
        if (Boss is { Alive: true, Phase: < 3 } b && b.Data.ContactDamage > worst)
        {
            float rr = playerRadius + b.BodyRadius;
            if (b.Position.DistanceSquaredTo(playerPos) <= rr * rr) worst = b.Data.ContactDamage;
        }

        return worst;
    }
}
