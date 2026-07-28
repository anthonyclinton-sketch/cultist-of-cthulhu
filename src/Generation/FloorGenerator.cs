using System.Collections.Generic;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Generation;

/// <summary>A room placed in floor space. Position is the top-left corner, in tiles.</summary>
public sealed class PlacedRoom
{
    public int NodeId;
    public RoomTemplate Template = null!;
    public RoomRole Role;
    public Vector2I Position;
    public readonly List<int> Connections = new();

    public int Width => Template.WidthTiles;
    public int Height => Template.HeightTiles;
    public Rect2I Bounds => new(Position, new Vector2I(Width, Height));
    public Vector2I Centre => Position + new Vector2I(Width / 2, Height / 2);
}

public sealed class Corridor
{
    public Vector2I From, To;
    public int RoomA, RoomB;
}

public sealed class GeneratedFloor
{
    public readonly List<PlacedRoom> Rooms = new();
    public readonly List<Corridor> Corridors = new();
    public ulong Seed;
    public string FlowId = "";
    public int Attempts;

    public Rect2I Bounds()
    {
        if (Rooms.Count == 0) return new Rect2I();
        Vector2I min = Rooms[0].Position, max = Rooms[0].Position + new Vector2I(Rooms[0].Width, Rooms[0].Height);
        foreach (PlacedRoom r in Rooms)
        {
            min = new Vector2I(Mathf.Min(min.X, r.Position.X), Mathf.Min(min.Y, r.Position.Y));
            max = new Vector2I(Mathf.Max(max.X, r.Position.X + r.Width), Mathf.Max(max.Y, r.Position.Y + r.Height));
        }
        return new Rect2I(min, max - min);
    }

    public PlacedRoom? FindRole(RoomRole role)
    {
        foreach (PlacedRoom r in Rooms) if (r.Role == role) return r;
        return null;
    }
}

/// <summary>
/// The nine-stage pipeline from docs/06 §2.
///
/// The governing insight, and the reason this beats BSP or cave generation: **the thing
/// you randomise is the graph's embedding in space, not the graph and not the rooms.**
/// Pacing is authored; topology is not.
///
/// Ordering matters more than any individual step. Gungeon's stated principle is to
/// "generate the parts of the map that are hardest / most important first" — loops are
/// the hardest thing to embed in 2D without overlap, so they are placed before trees and
/// everything else is fitted around them. Doing it the other way round produces layouts
/// that fail to close and burn the retry budget.
/// </summary>
public sealed class FloorGenerator
{
    private const int MaxAttempts = 12;          // docs/06 §2 — then fall back
    /// <summary>
    /// Shared across the whole recursion, not per node. docs/06 §5.3 suggests 200 per
    /// composite; at ~15 rooms each with dozens of candidate attachments that is exhausted
    /// almost immediately — it failed 92% of seeds. Placement is a constraint-satisfaction
    /// search and needs room to breathe.
    /// </summary>
    private const int MaxBacktracks = 4000;
    private const int RoomMargin = 1;            // tiles of clearance between rooms
    private const int MinCorridor = 4;
    private const int MaxCorridor = 30;
    private const int MaxFloorExtent = 300;

    private readonly List<RoomTemplate> _templates;
    private readonly List<FloorFlow> _flows;

    public FloorGenerator(List<FloorFlow> flows, List<RoomTemplate> templates)
    {
        _flows = flows;
        _templates = templates;
    }

    /// <summary>Generate, retrying with successive sub-seeds. Returns null if every
    /// attempt failed — the caller then falls back to a known-good flow (docs/06 §5.5).</summary>
    /// <summary>True when the last Generate call had to use the fallback flow. Tracked so
    /// the sweep can report the rate — a rising fallback rate means an authored flow or a
    /// room set has drifted into being hard to place.</summary>
    public bool UsedFallback { get; private set; }

    public GeneratedFloor? Generate(ulong floorSeed, int floorIndex, out string failure)
    {
        failure = "";
        UsedFallback = false;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ulong seed = Hash.Combine(floorSeed, attempt);
            var rng = new Rng(seed);

            GeneratedFloor? floor = TryGenerate(rng, seed, floorIndex, out failure, null);
            if (floor is null) continue;

            floor.Attempts = attempt + 1;
            return floor;
        }

        // docs/06 §2 and §5.5 — after the retry budget, fall back to a flow authored to be
        // trivially placeable. A generator that can return NOTHING is a generator that can
        // end a run for reasons the player cannot see or influence, so the last resort is
        // a guaranteed floor rather than a failure.
        FloorFlow fallback = FallbackFlow();
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ulong seed = Hash.Combine(floorSeed, 1000 + attempt);
            GeneratedFloor? floor = TryGenerate(new Rng(seed), seed, floorIndex, out failure, fallback);
            if (floor is null) continue;

            UsedFallback = true;
            floor.Attempts = MaxAttempts + attempt + 1;
            return floor;
        }
        return null;
    }

    /// <summary>
    /// The guaranteed-placeable floor. Deliberately minimal: few rooms, low degree, one
    /// small loop. It is not meant to be a good floor — it is meant to be a floor that
    /// cannot fail, so that a pathological seed costs the player variety rather than a run.
    /// </summary>
    private static FloorFlow FallbackFlow()
    {
        var f = new FloorFlow("fallback_minimal");
        int entrance = f.Add(RoomRole.Entrance);
        int a = f.Add(RoomRole.CombatEasy);
        int b = f.Add(RoomRole.CombatMed);
        int c = f.Add(RoomRole.Connector);
        int foyer = f.Add(RoomRole.BossFoyer);
        int boss = f.Add(RoomRole.Boss);

        f.Root(entrance)
         .Chain(entrance, a, b, c)
         .Link(c, a)                       // one small loop, so it still satisfies the flow rules
         .Chain(b, foyer, boss);
        return f;
    }

    private GeneratedFloor? TryGenerate(Rng rng, ulong seed, int floorIndex, out string failure,
                                        FloorFlow? forced)
    {
        // 1. SELECT FLOW
        FloorFlow flow = (forced ?? _flows[rng.NextInt(0, _flows.Count)]).Clone();

        // 2. TRANSFORM — chain expansion then injection.
        ExpandChains(flow, rng);
        InjectSpecialRooms(flow, rng, floorIndex);

        // 3. ASSIGN ROOMS
        var floor = new GeneratedFloor { Seed = seed, FlowId = flow.Id };
        var assigned = new Dictionary<int, RoomTemplate>();
        var usedIds = new HashSet<string>();

        foreach (FlowNode node in flow.Nodes)
        {
            RoomTemplate? t = PickTemplate(node, rng, floorIndex, usedIds);
            if (t is null) { failure = $"no template for role {node.Role}"; return null; }
            assigned[node.Id] = t;
            usedIds.Add(t.Id);
        }

        // 4-5. DECOMPOSE + LAYOUT. Loops first — the hard constraint.
        var placed = new Dictionary<int, PlacedRoom>();
        List<List<int>> loops = FindLoops(flow);

        if (!LayoutAll(flow, assigned, placed, loops, rng, out failure)) return null;

        foreach (PlacedRoom r in placed.Values) floor.Rooms.Add(r);

        // Record adjacency for the validator and the minimap.
        foreach (FlowNode node in flow.Nodes)
        {
            if (!placed.TryGetValue(node.Id, out PlacedRoom? a)) continue;
            foreach (int nb in node.Neighbours)
                if (placed.ContainsKey(nb)) a.Connections.Add(nb);
        }

        // 6. STITCH — anything not adjacent gets a corridor.
        BuildCorridors(flow, placed, floor);

        // 7. VALIDATE
        string? invalid = FloorValidator.Validate(floor, flow, MaxFloorExtent);
        if (invalid is not null) { failure = invalid; return null; }

        return floor;
    }

    // ---------------------------------------------------------------- Stage 2

    /// <summary>
    /// docs/06 §3.3 — expandable nodes become runs of 1..N rooms of the same role. One
    /// flow therefore produces floors of different LENGTH without different authoring.
    /// </summary>
    private static void ExpandChains(FloorFlow flow, Rng rng)
    {
        var originals = new List<FlowNode>(flow.Nodes);
        foreach (FlowNode node in originals)
        {
            if (!node.Expandable) continue;
            int extra = rng.NextInt(0, node.ExpandMax);
            if (extra <= 0) continue;

            // Splice the new rooms between this node and ONE chosen neighbour, so the
            // chain lengthens a path rather than fanning out into new branches.
            if (node.Neighbours.Count == 0) continue;
            int tailNeighbour = node.Neighbours[rng.NextInt(0, node.Neighbours.Count)];

            node.Neighbours.Remove(tailNeighbour);
            flow.Nodes[tailNeighbour].Neighbours.Remove(node.Id);

            int prev = node.Id;
            for (int i = 0; i < extra; i++)
            {
                int mid = flow.Add(node.Role);
                flow.Link(prev, mid);
                prev = mid;
            }
            flow.Link(prev, tailNeighbour);
        }
    }

    /// <summary>
    /// docs/06 §3.3 — the content-pacing valve. Every "one shop per floor", "shrines only
    /// at dead ends" rule lives here rather than scattered through the generator.
    /// </summary>
    private static void InjectSpecialRooms(FloorFlow flow, Rng rng, int floorIndex)
    {
        // Reward room: guaranteed, attached to a dead end so it never sits on the path.
        AttachToDeadEnd(flow, RoomRole.Reward, rng);

        // Shop: guaranteed from floor 2, ~70% on floor 1 (docs/08 §2.1).
        if (floorIndex >= 2 || rng.Chance(0.7f)) AttachToDeadEnd(flow, RoomRole.Shop, rng);

        if (rng.Chance(0.75f)) AttachToDeadEnd(flow, RoomRole.Shrine, rng);

        // Secrets attach to a NORMAL room via a cracked wall, so they may hang off
        // anything — including a room in the middle of a loop.
        int secrets = rng.NextInt(1, 3);
        for (int i = 0; i < secrets; i++) AttachToAny(flow, RoomRole.Secret, rng);
    }

    private static void AttachToDeadEnd(FloorFlow flow, RoomRole role, Rng rng)
    {
        var candidates = new List<int>();
        foreach (FlowNode n in flow.Nodes)
            if (n.Neighbours.Count == 1 && IsNormal(n.Role)) candidates.Add(n.Id);

        if (candidates.Count == 0) { AttachToAny(flow, role, rng); return; }

        int host = candidates[rng.NextInt(0, candidates.Count)];
        int id = flow.Add(role);
        flow.Link(host, id);
    }

    private static void AttachToAny(FloorFlow flow, RoomRole role, Rng rng)
    {
        var candidates = new List<int>();
        foreach (FlowNode n in flow.Nodes) if (IsNormal(n.Role)) candidates.Add(n.Id);
        if (candidates.Count == 0) return;

        int host = candidates[rng.NextInt(0, candidates.Count)];
        int id = flow.Add(role);
        flow.Link(host, id);
    }

    private static bool IsNormal(RoomRole r) =>
        r is RoomRole.CombatEasy or RoomRole.CombatMed or RoomRole.CombatHard
          or RoomRole.Hub or RoomRole.Connector;

    // ---------------------------------------------------------------- Stage 3

    private RoomTemplate? PickTemplate(FlowNode node, Rng rng, int floorIndex, HashSet<string> used)
    {
        RoomTemplate? best = null;
        float bestScore = -1f;

        foreach (RoomTemplate t in _templates)
        {
            if (t.Role != node.Role) continue;
            if (t.MinFloor > floorIndex) continue;

            // docs/06 §4.3 — hard rule: the same room never appears twice on one floor.
            // Repetition inside a single descent is far more noticeable than repetition
            // across runs, and it is the thing that makes procedural floors feel cheap.
            float weight = t.Weight * (used.Contains(t.Id) ? 0.02f : 1f);
            float score = weight * rng.Range(0.5f, 1.5f);

            if (score <= bestScore) continue;
            bestScore = score;
            best = t;
        }
        return best;
    }

    // ---------------------------------------------------------------- Stage 4: decompose

    /// <summary>
    /// Repeatedly extract the SMALLEST cycle (docs/06 §5.1). Small loops are the tightest
    /// spatial constraint, so solving them first leaves the most freedom for everything
    /// after — the reverse order routinely fails to close.
    /// </summary>
    private static List<List<int>> FindLoops(FloorFlow flow)
    {
        var loops = new List<List<int>>();
        var consumed = new HashSet<int>();

        for (int guard = 0; guard < 16; guard++)
        {
            List<int>? smallest = null;
            foreach (FlowNode start in flow.Nodes)
            {
                if (consumed.Contains(start.Id)) continue;
                List<int>? cycle = ShortestCycleThrough(flow, start.Id, consumed);
                if (cycle is null) continue;
                if (smallest is null || cycle.Count < smallest.Count) smallest = cycle;
            }

            if (smallest is null) break;
            loops.Add(smallest);
            foreach (int id in smallest) consumed.Add(id);
        }
        return loops;
    }

    private static List<int>? ShortestCycleThrough(FloorFlow flow, int start, HashSet<int> skip)
    {
        // BFS from start; the first edge that reaches an already-visited node from a
        // different parent closes a cycle.
        var parent = new Dictionary<int, int> { [start] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach (int nb in flow.Nodes[cur].Neighbours)
            {
                if (skip.Contains(nb)) continue;
                if (nb == parent[cur]) continue;

                if (parent.ContainsKey(nb))
                {
                    var path = new List<int>();
                    for (int a = cur; a != -1; a = parent[a]) path.Add(a);
                    var path2 = new List<int>();
                    for (int b = nb; b != -1; b = parent[b]) path2.Add(b);

                    var set = new HashSet<int>(path);
                    var cycle = new List<int>(path);
                    foreach (int n in path2) if (set.Add(n)) cycle.Add(n);
                    return cycle.Count >= 3 ? cycle : null;
                }

                parent[nb] = cur;
                queue.Enqueue(nb);
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- Stage 5: layout

    private bool LayoutAll(FloorFlow flow, Dictionary<int, RoomTemplate> assigned,
                           Dictionary<int, PlacedRoom> placed, List<List<int>> loops,
                           Rng rng, out string failure)
    {
        failure = "";

        List<int> order = BuildPlacementOrder(flow, loops);

        int backtracks = 0;
        return PlaceRecursive(flow, assigned, placed, order, 0, rng, ref backtracks, out failure);
    }

    /// <summary>
    /// Decide the order rooms are placed in.
    ///
    /// THE HARD PRECONDITION: every node after the first must already have at least one
    /// ordered neighbour, or placement has nothing to attach to and fails immediately.
    ///
    /// The first version of this listed all loop nodes first and then ran a BFS from the
    /// root for the remainder — which looked right and failed 100% of seeds. Marking the
    /// loop nodes as already-seen meant the BFS could not traverse THROUGH them, so every
    /// node on the far side of a loop was appended in arbitrary node-id order and placed
    /// before any of its neighbours existed.
    ///
    /// The fix is a frontier walk: only ever order a node adjacent to something already
    /// ordered, and among those candidates prefer loop members. That keeps Gungeon's
    /// "hardest first" property — loops still get placed early, while they have maximum
    /// freedom — without ever violating the precondition.
    /// </summary>
    private static List<int> BuildPlacementOrder(FloorFlow flow, List<List<int>> loops)
    {
        var inLoop = new HashSet<int>();
        foreach (List<int> loop in loops) foreach (int id in loop) inLoop.Add(id);

        var order = new List<int> { flow.RootId };
        var ordered = new HashSet<int> { flow.RootId };

        while (order.Count < flow.Nodes.Count)
        {
            int best = -1;
            int bestScore = int.MinValue;

            foreach (int id in ordered)
            {
                foreach (int nb in flow.Nodes[id].Neighbours)
                {
                    if (ordered.Contains(nb)) continue;

                    // Loop members first (hardest constraint), then high-degree nodes —
                    // a node with many exits is harder to satisfy late, when the space
                    // around it has already been consumed.
                    int score = (inLoop.Contains(nb) ? 1000 : 0) + flow.Nodes[nb].Neighbours.Count;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = nb;
                }
            }

            // Disconnected remainder: the flow validator should have caught this, but the
            // generator must not hang if a transform ever produces one.
            if (best < 0)
            {
                foreach (FlowNode n in flow.Nodes)
                    if (!ordered.Contains(n.Id)) { best = n.Id; break; }
                if (best < 0) break;
            }

            order.Add(best);
            ordered.Add(best);
        }
        return order;
    }

    private bool PlaceRecursive(FloorFlow flow, Dictionary<int, RoomTemplate> assigned,
                                Dictionary<int, PlacedRoom> placed, List<int> order, int index,
                                Rng rng, ref int backtracks, out string failure)
    {
        failure = "";
        if (index >= order.Count) return true;

        int nodeId = order[index];
        RoomTemplate template = assigned[nodeId];

        // First room: origin.
        if (placed.Count == 0)
        {
            placed[nodeId] = new PlacedRoom
            {
                NodeId = nodeId, Template = template, Role = flow.Nodes[nodeId].Role,
                Position = Vector2I.Zero,
            };
            if (PlaceRecursive(flow, assigned, placed, order, index + 1, rng, ref backtracks, out failure))
                return true;
            placed.Remove(nodeId);
            return false;
        }

        // Try attaching to each already-placed neighbour, preferring exits far from ones
        // already used — this spreads the layout instead of knotting it (docs/06 §5.3).
        var options = new List<(int host, Side side, int hostOffset, int myOffset)>();
        foreach (int nb in flow.Nodes[nodeId].Neighbours)
        {
            if (!placed.TryGetValue(nb, out PlacedRoom? host)) continue;
            foreach (Side side in new[] { Side.North, Side.South, Side.East, Side.West })
            {
                foreach (int ho in host.Template.ExitsOn(side))
                    foreach (int mo in template.ExitsOn(RoomTemplate.Opposite(side)))
                        options.Add((nb, side, ho, mo));
            }
        }

        if (options.Count == 0)
        {
            failure = $"node {nodeId} ({flow.Nodes[nodeId].Role}) has no placed neighbour to attach to";
            return false;
        }

        // Order candidates by COMPACTNESS rather than at random.
        //
        // Random ordering makes the search wander: the layout sprawls, later rooms have
        // fewer legal positions, and the backtrack budget is spent undoing early sprawl.
        // Preferring placements near the existing centroid keeps the floor tight, which
        // both satisfies the extent invariant and leaves more legal positions for the
        // rooms still to come. Jitter stops every floor collapsing into the same shape.
        //
        // Scores are computed ONCE and then sorted. Calling the RNG inside a comparator
        // would consume it an unpredictable number of times and break determinism — and
        // an inconsistent comparator is undefined behaviour in Sort besides.
        Vector2I centroid = Centroid(placed);
        var scored = new List<(float score, (int host, Side side, int ho, int mo) opt)>(options.Count);
        foreach (var opt in options)
        {
            Vector2I pos = AttachPosition(placed[opt.host], template, opt.side, opt.hostOffset, opt.myOffset);
            Vector2I c = pos + new Vector2I(template.WidthTiles / 2, template.HeightTiles / 2);
            float dist = Mathf.Abs(c.X - centroid.X) + Mathf.Abs(c.Y - centroid.Y);
            scored.Add((-dist + rng.Range(0f, 40f), opt));
        }
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        options.Clear();
        foreach (var s in scored) options.Add(s.opt);

        foreach ((int hostId, Side side, int ho, int mo) in options)
        {
            if (backtracks > MaxBacktracks) { failure = "backtrack budget exhausted"; return false; }

            PlacedRoom host = placed[hostId];
            Vector2I pos = AttachPosition(host, template, side, ho, mo);

            var candidate = new PlacedRoom
            {
                NodeId = nodeId, Template = template, Role = flow.Nodes[nodeId].Role, Position = pos,
            };

            if (Overlaps(candidate, placed, flow.Nodes[nodeId].Neighbours)) { backtracks++; continue; }

            placed[nodeId] = candidate;
            if (PlaceRecursive(flow, assigned, placed, order, index + 1, rng, ref backtracks, out failure))
                return true;

            placed.Remove(nodeId);
            backtracks++;
        }

        failure = $"could not place node {nodeId} ({flow.Nodes[nodeId].Role})";
        return false;
    }

    /// <summary>
    /// Position a room so its exit meets the host's exit. Doors are one tile outside the
    /// room bounds, so the rooms sit flush against each other.
    /// </summary>
    private static Vector2I AttachPosition(PlacedRoom host, RoomTemplate t, Side side, int hostOffset, int myOffset)
        => side switch
        {
            Side.North => new Vector2I(host.Position.X + hostOffset - myOffset, host.Position.Y - t.HeightTiles),
            Side.South => new Vector2I(host.Position.X + hostOffset - myOffset, host.Position.Y + host.Height),
            Side.East => new Vector2I(host.Position.X + host.Width, host.Position.Y + hostOffset - myOffset),
            _ => new Vector2I(host.Position.X - t.WidthTiles, host.Position.Y + hostOffset - myOffset),
        };

    /// <summary>
    /// Overlap test, with one subtlety that broke the first working version entirely.
    ///
    /// Connected rooms are placed FLUSH — the shared door is the whole point, so their
    /// bounds touch by design. Applying the clearance margin uniformly therefore made
    /// every room collide with its own host, and all 10,000 seeds failed with "could not
    /// place node".
    ///
    /// So: strict test against flow-neighbours (touching is correct), margin against
    /// everything else (two unconnected rooms sharing a wall reads as a bug to the player,
    /// and leaves no room for a corridor).
    /// </summary>
    private static bool Overlaps(PlacedRoom candidate, Dictionary<int, PlacedRoom> placed,
                                 List<int> neighbours)
    {
        foreach (PlacedRoom o in placed.Values)
        {
            if (o.NodeId == candidate.NodeId) continue;

            bool connected = neighbours.Contains(o.NodeId);
            Rect2I a = connected ? candidate.Bounds : candidate.Bounds.Grow(RoomMargin);
            if (a.Intersects(o.Bounds)) return true;
        }
        return false;
    }

    private static Vector2I Centroid(Dictionary<int, PlacedRoom> placed)
    {
        if (placed.Count == 0) return Vector2I.Zero;
        var sum = Vector2I.Zero;
        foreach (PlacedRoom r in placed.Values) sum += r.Centre;
        return sum / placed.Count;
    }

    // ---------------------------------------------------------------- Stage 6: stitch

    /// <summary>
    /// Connect flow-adjacent rooms that did not end up physically touching. Loop closures
    /// almost always land here — the two ends of a loop meet at an arbitrary distance.
    /// </summary>
    private static void BuildCorridors(FloorFlow flow, Dictionary<int, PlacedRoom> placed, GeneratedFloor floor)
    {
        var done = new HashSet<(int, int)>();

        foreach (FlowNode node in flow.Nodes)
        {
            if (!placed.TryGetValue(node.Id, out PlacedRoom? a)) continue;
            foreach (int nb in node.Neighbours)
            {
                if (!placed.TryGetValue(nb, out PlacedRoom? b)) continue;

                var key = node.Id < nb ? (node.Id, nb) : (nb, node.Id);
                if (!done.Add(key)) continue;
                if (a.Bounds.Grow(RoomMargin + 1).Intersects(b.Bounds)) continue;   // already touching

                floor.Corridors.Add(new Corridor
                {
                    From = a.Centre, To = b.Centre, RoomA = node.Id, RoomB = nb,
                });
            }
        }
    }

    public static int MinCorridorLength => MinCorridor;
    public static int MaxCorridorLength => MaxCorridor;
}
