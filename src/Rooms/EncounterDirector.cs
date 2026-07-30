using System.Collections.Generic;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Generation;
using Godot;

namespace CultistOfCthulhu.Rooms;

/// <summary>
/// Plans and runs one room's encounter: the budget, the roster picks, the spawn anchors, and
/// the waves.
///
/// Extracted from <see cref="FloorRunner"/> rather than added to it. The populator now owns
/// real state — planned waves, a trigger condition, a telegraph timer, pending spawn points
/// — and FloorRunner is already the largest class in the project and the one every other
/// system reaches into. Encounter composition is a self-contained job with a doc section of
/// its own (docs/06 §6), so it gets a class of its own.
///
/// WAVES (docs/06 §6.2). Rooms above a budget threshold split into 2-3 waves, and the next
/// wave arrives when the current one is at 30% remaining — **never on a timer, so careful
/// play is never punished.** That clause is the whole design: a timer would mean a cautious
/// player fights wave 2 while wave 1 is still alive, which converts patience into a
/// difficulty spike and teaches exactly the wrong lesson.
///
/// Before this, every enemy a room could afford spawned the instant the player walked in.
/// With rooms at 4-8x their original area and budgets scaling with the square root of that,
/// a hard room opened with roughly twenty enemies on screen at once.
/// </summary>
public sealed class EncounterDirector
{
    private readonly List<EnemyData> _roster;
    private readonly EnemyManager _enemies;
    private readonly FloorGeometry _geometry;
    private readonly Rng _rng;

    public EncounterDirector(List<EnemyData> roster, EnemyManager enemies,
                             FloorGeometry geometry, Rng rng)
    {
        _roster = roster;
        _enemies = enemies;
        _geometry = geometry;
        _rng = rng;
    }

    /// <summary>docs/06 §6.2 — the next wave lands when the current one is down to this.</summary>
    private const float WaveTriggerFraction = 0.30f;

    /// <summary>docs/05 R4 — 0.6s of warning before anything appears.</summary>
    private const float TelegraphSeconds = 0.6f;

    /// <summary>Budget below which a room is a single wave. A small room fed in instalments
    /// reads as the game trickling rather than pacing.</summary>
    private const float TwoWaveThreshold = 95f;
    private const float ThreeWaveThreshold = 190f;

    // ---------------------------------------------------------------- State

    private readonly List<List<EnemyData>> _waves = new();
    private readonly List<Vector2> _anchors = new();
    private readonly List<Vector2> _pendingSpawns = new();

    private int _nextWave;
    private int _waveSpawnCount;
    private float _telegraph;
    private PlacedRoom? _room;

    public bool Active { get; private set; }
    public float Budget { get; private set; }
    public int WaveCount => _waves.Count;

    /// <summary>
    /// Waves whose enemies are actually ON THE FLOOR.
    ///
    /// Distinct from the planning cursor, and the distinction matters: a wave spends 0.6s
    /// telegraphing before anything exists. Counting a telegraphing wave as spawned would
    /// report the room as further along than it is, and would let the clear check fire in the
    /// gap between the last kill and the next arrival.
    /// </summary>
    public int WavesSpawned => _emitted;

    /// <summary>The plan, for the gate. The fodder floor and Support cap are properties of
    /// the whole room, so they can only be checked against every wave at once.</summary>
    public IReadOnlyList<List<EnemyData>> Waves => _waves;

    /// <summary>Pending spawn positions during a telegraph, for the room to draw.</summary>
    public IReadOnlyList<Vector2> PendingSpawns => _pendingSpawns;
    public float TelegraphProgress => _telegraph <= 0f ? 0f : 1f - _telegraph / TelegraphSeconds;

    /// <summary>True once every wave has actually arrived and nothing is left alive.</summary>
    public bool Finished => Active && _emitted >= _waves.Count && _enemies.AliveCount == 0
                            && _pendingSpawns.Count == 0;

    // ---------------------------------------------------------------- Planning

    /// <summary>
    /// Plan the room and spawn its first wave. Returns false if the room got no enemies at
    /// all, which the caller treats as already cleared.
    /// </summary>
    public bool Begin(PlacedRoom room, float budget, TileMask? walls, Vector2 playerPos)
    {
        _room = room;
        Budget = budget;
        _waves.Clear();
        _pendingSpawns.Clear();
        _nextWave = 0;
        _emitted = 0;
        _spawnCursor = 0;
        _telegraph = 0f;

        BuildAnchors(room, walls);
        PlanWaves(room, budget);

        Active = true;
        if (_waves.Count == 0) { Active = false; return false; }

        // WAVE ONE TELEGRAPHS TOO, like every wave after it.
        //
        // It used to arrive untelegraphed on the reasoning that the player "walked in on it".
        // That reads well and plays badly: the room seals behind you, so the first wave is the
        // one instalment you cannot back out of, and it was the one arriving with no warning
        // and no reference to where you were standing. Reported from play as an enemy
        // materialising on the player and taking a heart before they had moved.
        //
        // The 0.6s telegraph is not a difficulty change — the same enemies arrive at the same
        // anchors — it is the difference between an encounter and an ambush.
        SpawnWave(0, telegraph: true, playerPos);
        return true;
    }

    /// <summary>
    /// Spend the budget into 2-3 waves.
    ///
    /// FRONT-LOADED (50/30/20 across three), not escalating. A room should read as dangerous
    /// the moment it is entered and then produce reinforcements; ramping upward instead makes
    /// the opening feel like a formality and the last wave like the only real fight, which is
    /// the pacing the Dread Budget exists to avoid.
    ///
    /// The 35% fodder floor and the Support cap are enforced across the WHOLE room rather
    /// than per wave. Per-wave fodder would force a token weak enemy into every instalment;
    /// the constraint exists to keep the Sanity economy solvent over the encounter
    /// (docs/05 §2), which is a property of the room.
    /// </summary>
    private void PlanWaves(PlacedRoom room, float budget)
    {
        int waveCount = budget >= ThreeWaveThreshold ? 3 : budget >= TwoWaveThreshold ? 2 : 1;

        float[] split = waveCount switch
        {
            3 => new[] { 0.50f, 0.30f, 0.20f },
            2 => new[] { 0.60f, 0.40f },
            _ => new[] { 1.00f },
        };

        float fodderFloor = budget * 0.35f;
        float fodderSpent = 0f;
        int supports = 0;
        int supportCap = room.Role == RoomRole.Hub ? 2 : 1;

        for (int w = 0; w < waveCount; w++)
        {
            var wave = new List<EnemyData>();
            float allowance = budget * split[w];
            float spent = 0f;
            int guard = 0;

            while (spent < allowance && guard++ < 48)
            {
                bool needFodder = fodderSpent < fodderFloor;
                EnemyData? pick = Pick(needFodder, supports >= supportCap);
                if (pick is null) break;
                if (pick.DreadCost > allowance - spent && spent > 0f) break;

                wave.Add(pick);
                spent += pick.DreadCost;
                if (pick.Role == EnemyRole.Fodder) fodderSpent += pick.DreadCost;
                if (pick.Role == EnemyRole.Support) supports++;
            }

            if (wave.Count > 0) _waves.Add(wave);
        }
    }

    /// <summary>
    /// Pick from the roster under the room's constraints.
    ///
    /// docs/06 §6.1 caps Support at one outside a hub, and that constraint was authored and
    /// never implemented — the populator only enforced the fodder floor. A room of two
    /// Chanters buffing each other is a different encounter from the one the budget priced.
    /// </summary>
    private EnemyData? Pick(bool needFodder, bool supportFull)
    {
        for (int i = 0; i < 32; i++)
        {
            EnemyData d = _roster[_rng.NextInt(0, _roster.Count)];
            if (needFodder && d.Role != EnemyRole.Fodder) continue;
            if (supportFull && d.Role == EnemyRole.Support) continue;
            return d;
        }

        // Fall back to anything legal rather than returning nothing: an empty wave is a room
        // that cannot be cleared if it was the only wave.
        foreach (EnemyData d in _roster)
        {
            if (supportFull && d.Role == EnemyRole.Support) continue;
            return d;
        }
        return null;
    }

    // ---------------------------------------------------------------- Anchors

    /// <summary>
    /// Candidate spawn positions: a grid over the room's interior, keeping only points with
    /// clearance for a body.
    ///
    /// This is what <c>RoomTemplate.SpawnAnchorCount</c> was authored for. It has been
    /// exported since M2 and read by nothing — spawns were uniform random points in the room
    /// rect, corrected after the fact by nudging anything that landed inside a wall. Deriving
    /// anchors from the mask instead means a spawn is never inside a pillar in the first
    /// place, and the authored count sets how spread out a room's spawning is.
    ///
    /// Real rooms will carry authored anchor positions; these are derived because the
    /// templates are still placeholder rectangles (docs/06 §4).
    /// </summary>
    private void BuildAnchors(PlacedRoom room, TileMask? walls)
    {
        _anchors.Clear();

        Rect2 interior = _geometry.RoomInteriorWorld(room).Grow(-FloorGeometry.Tile);
        const float Clearance = 14f;   // the largest body radius in the roster, plus a margin

        // Spacing derived from the authored anchor count, so a room asking for more anchors
        // gets a finer grid rather than a differently-shaped one.
        int want = Mathf.Max(6, room.Template.SpawnAnchorCount);
        float spacing = Mathf.Max(40f, Mathf.Sqrt(interior.Size.X * interior.Size.Y / want));

        for (float y = interior.Position.Y; y < interior.Position.Y + interior.Size.Y; y += spacing)
        {
            for (float x = interior.Position.X; x < interior.Position.X + interior.Size.X; x += spacing)
            {
                if (walls is not null && walls.CircleOverlaps(x, y, Clearance)) continue;
                _anchors.Add(new Vector2(x, y));
            }
        }

        // Shuffled once, so successive waves do not walk the grid in reading order.
        if (_anchors.Count > 1)
        {
            Vector2[] scratch = _anchors.ToArray();
            _rng.Shuffle(new System.Span<Vector2>(scratch));
            _anchors.Clear();
            _anchors.AddRange(scratch);
        }
    }

    /// <summary>
    /// Choose where a wave appears.
    ///
    /// Wave one uses the whole room, so walking in reveals the encounter's real shape.
    /// Later waves prefer anchors the PLAYER CAN SEE (docs/06 §6.2, "wave spawn points are
    /// always visible on screen"), because a reinforcement that materialises behind the
    /// player is indistinguishable from an ambush they had no way to read. Where no visible
    /// anchor is free, the marker is drawn clamped to the screen edge instead, which is
    /// R4's inbound-marker clause.
    /// </summary>
    /// <summary>
    /// Nothing spawns closer to the player than this. docs/05 R5 in spirit: an enemy that
    /// appears inside you is not a difficulty, it is a hit you were never offered a chance to
    /// avoid.
    ///
    /// ~5.6 tiles, which is comfortably outside the player's reach and far enough that even a
    /// Rusher's opening lunge can be seen coming.
    /// </summary>
    private const float MinSpawnDistance = 90f;

    /// <summary>
    /// Pick where one enemy appears.
    ///
    /// THE BUG THIS FIXES, reported from play: an enemy spawned on top of the player the
    /// instant they entered a room and took a heart off them. The minimum-distance rule
    /// existed, but only inside the telegraphed branch — the first wave took an early return
    /// three lines up and read straight out of a shuffled anchor grid with no reference to
    /// where the player was standing. The one wave that spawns while the player is walking
    /// through the door was the one wave that did not check.
    ///
    /// The distance rule is now unconditional and the FALLBACK respects it too. The old
    /// fallback returned `_anchors[index % count]` when nothing satisfied the constraints,
    /// which in a small or heavily-obstructed room is exactly the case where that arbitrary
    /// anchor is likely to be the one the player is standing on.
    /// </summary>
    private Vector2 ChooseAnchor(int index, bool preferVisible, Vector2 playerPos)
    {
        if (_anchors.Count == 0)
            return _room is not null ? _geometry.RoomAnchorWorld(_room) : playerPos;

        // Half a viewport, so "visible" means comfortably inside the frame rather than on its
        // lip. Native resolution is 640x360 (docs/10 §1.2).
        const float HalfW = 280f, HalfH = 150f;

        int best = -1;
        float bestDist = float.MaxValue;

        // Fallback: whatever is FURTHEST from the player. Used when no anchor satisfies the
        // preferences, and safe by construction — the worst case is an enemy arriving from
        // across the room, which is a fight rather than an ambush.
        int furthest = index % _anchors.Count;
        float furthestDist = -1f;

        for (int i = 0; i < _anchors.Count; i++)
        {
            int at = (index + i) % _anchors.Count;
            Vector2 d = _anchors[at] - playerPos;
            float dist = d.Length();

            if (dist > furthestDist) { furthestDist = dist; furthest = at; }

            // HARD, and checked before anything else. Every other rule here is a preference.
            if (dist < MinSpawnDistance) continue;

            if (preferVisible && (Mathf.Abs(d.X) > HalfW || Mathf.Abs(d.Y) > HalfH)) continue;

            // Closest of the acceptable ones when telegraphing, so a wave lands around the
            // player rather than in the far corner; otherwise first acceptable is fine.
            if (!preferVisible) return _anchors[at];
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = at;
        }

        return best >= 0 ? _anchors[best] : _anchors[furthest];
    }

    // ---------------------------------------------------------------- Tick

    public void Tick(float dt, Vector2 playerPos)
    {
        if (!Active) return;

        // A telegraph in flight resolves first.
        if (_pendingSpawns.Count > 0)
        {
            _telegraph -= dt;
            if (_telegraph > 0f) return;

            EmitPending();
            return;
        }

        if (_nextWave >= _waves.Count) return;

        // THE TRIGGER: kills, never a clock. docs/06 §6.2 is explicit that a timer would
        // punish careful play, and post-F4 a cautious player is exactly the one the Sanity
        // economy is hardest on.
        int threshold = Mathf.Max(1, Mathf.CeilToInt(_waveSpawnCount * WaveTriggerFraction));
        if (_enemies.AliveCount > threshold) return;

        SpawnWave(_nextWave, telegraph: true, playerPos);
    }

    private void SpawnWave(int index, bool telegraph, Vector2 playerPos = default)
    {
        List<EnemyData> wave = _waves[index];
        _nextWave = index + 1;
        _waveSpawnCount = wave.Count;

        _pendingWave = wave;
        _pendingSpawns.Clear();
        for (int i = 0; i < wave.Count; i++)
            _pendingSpawns.Add(ChooseAnchor(_spawnCursor + i, telegraph, playerPos));
        _spawnCursor += wave.Count;

        if (!telegraph) { EmitPending(); return; }

        _telegraph = TelegraphSeconds;
    }

    private List<EnemyData>? _pendingWave;
    private int _spawnCursor;

    private void EmitPending()
    {
        if (_pendingWave is null) { _pendingSpawns.Clear(); return; }

        for (int i = 0; i < _pendingWave.Count && i < _pendingSpawns.Count; i++)
            _enemies.Spawn(_pendingWave[i], _pendingSpawns[i]);

        _pendingWave = null;
        _pendingSpawns.Clear();
        _telegraph = 0f;
        _emitted++;
    }

    private int _emitted;

    public void Reset()
    {
        Active = false;
        _waves.Clear();
        _anchors.Clear();
        _pendingSpawns.Clear();
        _pendingWave = null;
        _nextWave = 0;
        _emitted = 0;
        _waveSpawnCount = 0;
        _spawnCursor = 0;
        _telegraph = 0f;
        _room = null;
    }
}
