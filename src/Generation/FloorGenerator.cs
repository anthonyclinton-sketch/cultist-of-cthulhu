using System;
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
    /// <summary>
    /// Retries before falling back. **Raised from 12 to 40** when flow selection stopped
    /// being re-rolled per attempt.
    ///
    /// The two changes are one change. Re-rolling the flow made a retry a chance to try an
    /// EASIER topology, so 12 attempts were plenty — the search escaped the hard flow
    /// rather than solving it. Fixing the flow means the budget has to be large enough to
    /// actually place the hardest one authored, and the figure eight (two loops sharing a
    /// six-exit hub) is roughly three times harder to embed in 2D than the linear descent.
    /// At 12 it fell back on 6% of seeds; at 40 it does not.
    ///
    /// Cost is bounded and paid only by seeds that need it: generation is one-off per
    /// floor, and the sweep's mean is under four attempts.
    /// </summary>
    private const int MaxAttempts = 40;          // docs/06 §2 — then fall back
    /// <summary>
    /// Shared across the whole recursion, not per node. docs/06 §5.3 suggests 200 per
    /// composite; at ~15 rooms each with dozens of candidate attachments that is exhausted
    /// almost immediately — it failed 92% of seeds. Placement is a constraint-satisfaction
    /// search and needs room to breathe.
    /// </summary>
    private const int MaxBacktracks = 12000;
    private const int RoomMargin = 1;            // tiles of clearance between rooms
    private const int MinCorridor = 4;
    private const int MaxCorridor = 30;
    /// <summary>
    /// Raised from 300 when rooms were scaled to be screen-relative. Rooms grew ~2.5x
    /// linearly, so floors did too — a 15-room floor now spans 250-450 tiles rather than
    /// 90-120, and the old ceiling rejected almost every layout.
    /// </summary>
    private const int MaxFloorExtent = 1400;

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

    /// <summary>
    /// Why the AUTHORED flow gave up, when <see cref="UsedFallback"/> is true.
    ///
    /// Needed because the fallback loop reuses the same `out failure` and overwrites it with
    /// the empty string of its own success — so the one moment the cause matters is the one
    /// moment it was being discarded, and the sweep reported "135x " with a blank reason.
    /// </summary>
    public string LastAuthoredFailure { get; private set; } = "";

    /// <summary>The flow and room target the authored attempts were working with. Reported
    /// alongside <see cref="LastAuthoredFailure"/> so a fallback rate can be attributed to a
    /// flow and a length rather than guessed at — raising the backtrack budget was the first
    /// guess, and it bought 0.2 points of fallback rate for 75% more sweep time.</summary>
    public string LastAuthoredFlowId { get; private set; } = "";
    public int LastRoomTarget { get; private set; } = -1;

    public GeneratedFloor? Generate(ulong floorSeed, int floorIndex, out string failure)
    {
        failure = "";
        UsedFallback = false;
        LastAuthoredFailure = "";

        // THE FLOW IS CHOSEN ONCE, and every retry keeps it.
        //
        // It used to be re-rolled inside each attempt, and the 10k sweep showed what that
        // does: the reported flow is always the one that happened to SUCCEED, so the
        // easiest topology to place wins by attrition. Usage came out 60% / 25% / 14%
        // against three flows authored to be equally likely, which quietly turned "three
        // recognisably different floor shapes" (docs/06 §3.2) into one shape most of the
        // time — and the sweep's own "every authored flow is reachable" check passed
        // throughout, because reachable is not the same as fair.
        //
        // Keeping it fixed means a hard-to-place flow spends its whole retry budget rather
        // than silently handing the floor to an easier one. That is the intended cost: the
        // retry budget exists to find a layout for THIS floor, not to shop for a floor.
        FloorFlow chosen = _flows[new Rng(Hash.Combine(floorSeed, "flow")).NextInt(0, _flows.Count)];

        // THE ROOM COUNT IS CHOSEN ONCE TOO, for exactly the same reason and it is not a
        // hypothetical: rolling it per attempt cost 0.6 rooms of mean floor length. A longer
        // floor is harder to place, so it fails validation more often, so the attempt that
        // SUCCEEDS is biased toward the short end — the retry loop was quietly shopping for a
        // small floor the same way it used to shop for an easy flow. Floor 4 came out at 15.4
        // rooms against a 16.0 target with the band 14–18, which looks fine and is wrong.
        int roomTarget = RoomTarget(floorSeed, floorIndex);
        LastAuthoredFlowId = chosen.Id;
        LastRoomTarget = roomTarget;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ulong seed = Hash.Combine(floorSeed, attempt);
            var rng = new Rng(seed);

            GeneratedFloor? floor = TryGenerate(rng, seed, floorIndex, roomTarget, out failure, chosen);
            if (floor is null) continue;

            floor.Attempts = attempt + 1;
            return floor;
        }

        // docs/06 §2 and §5.5 — after the retry budget, fall back to a flow authored to be
        // trivially placeable. A generator that can return NOTHING is a generator that can
        // end a run for reasons the player cannot see or influence, so the last resort is
        // a guaranteed floor rather than a failure.
        LastAuthoredFailure = failure;

        FloorFlow fallback = FallbackFlow();
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // Target -1: the fallback flow has no expandable chain, so it cannot hit a band
            // however hard it is asked to. Passing the target anyway would make its injections
            // suppress themselves down to a 8-room floor with no shop — the escape hatch's job
            // is to hand the player a complete floor, and content beats length here.
            ulong seed = Hash.Combine(floorSeed, 1000 + attempt);
            GeneratedFloor? floor = TryGenerate(new Rng(seed), seed, floorIndex, -1, out failure, fallback);
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

    /// <summary>
    /// Rooms this floor should end up with, or -1 for "unconstrained" — floor 5 is open
    /// (docs/07 §2) and the fallback flow opts out. Rolled from the floor seed alone so it is
    /// stable across retries; see the note in <see cref="Generate"/>.
    /// </summary>
    private static int RoomTarget(ulong floorSeed, int floorIndex) =>
        FloorScaling.TryRoomCount(floorIndex, out int min, out int max)
            ? new Rng(Hash.Combine(floorSeed, "rooms")).NextInt(min, max + 1)
            : -1;

    private GeneratedFloor? TryGenerate(Rng rng, ulong seed, int floorIndex, int roomTarget,
                                        out string failure, FloorFlow? forced)
    {
        // 1. SELECT FLOW
        FloorFlow flow = (forced ?? _flows[rng.NextInt(0, _flows.Count)]).Clone();

        // 2. TRANSFORM — chain expansion then injection.
        //
        // The injection DECISIONS are rolled first and applied last. Expansion needs to know
        // how many rooms injection will add in order to hit docs/07 §2's room count, and
        // injection needs the EXPANDED flow to choose hosts from — before expansion there are
        // barely any dead ends, so "shrines only at dead ends" would degrade into
        // AttachToAny and put specials on the critical path.
        Injections injections = RollInjections(rng, floorIndex, roomTarget, flow.Nodes.Count);
        ExpandChains(flow, rng, ExpansionBudget(flow, roomTarget, injections));
        ApplyInjections(flow, rng, injections);

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
    /// How many extra rooms expansion should add, or -1 for "no target — roll per node".
    ///
    /// docs/07 §2 gives each floor a room count (11–14 on the Undercroft rising to 14–18 on
    /// the Mountains) and docs/06 §3.3 names chain expansion as the mechanism — "this is how
    /// the same flow produces a 12-room and an 18-room floor". Expansion is therefore where
    /// the floor index has to enter, and it did not: the result was a mean of 14.8 rooms on a
    /// floor whose band tops out at 14. The SAME missing input left floor 1 long and floor 4
    /// short, which is why "the floors feel samey" was never going to lead anyone here.
    /// </summary>
    private static int ExpansionBudget(FloorFlow flow, int roomTarget, Injections injections)
    {
        if (roomTarget < 0) return -1;

        // Never negative. Overshoot is injection's problem to solve, not expansion's — it can
        // only add rooms, and RollInjections has already trimmed what it is allowed to trim.
        return Mathf.Max(0, roomTarget - flow.Nodes.Count - injections.Count);
    }

    /// <summary>
    /// docs/06 §3.3 — expandable nodes become runs of 1..N rooms of the same role. One
    /// flow therefore produces floors of different LENGTH without different authoring.
    ///
    /// <paramref name="budget"/> is the total extra rooms wanted across the whole flow, or -1
    /// to roll each node independently (floor 5 is "open" in docs/07 §2 and has no band).
    /// A budget larger than the flow's capacity is spent as far as it goes: capacity is a
    /// property of the authored flow, and inventing rooms it has no chain to hold would fan
    /// out into new branches, which is the one thing expansion exists not to do.
    /// </summary>
    private static void ExpandChains(FloorFlow flow, Rng rng, int budget)
    {
        // Collected before anything is spliced, so the rooms expansion ADDS cannot themselves
        // expand. A node with no neighbours has nothing to splice between and is skipped here
        // rather than mid-loop, so it does not silently absorb part of the budget.
        //
        // ACYCLIC CHAINS FIRST, and this is the whole ballgame for the fallback rate.
        //
        // Lengthening a chain inside a loop makes a CYCLE longer, and a long cycle has to
        // close back on itself in 2D; lengthening an acyclic chain just grows a snake, which
        // always fits. The measured difference is not subtle. Asked for 17–18 room floors:
        //
        //   undercroft_descent      both expandable nodes acyclic     0 fallbacks
        //   undercroft_ring         one of two acyclic                4 fallbacks
        //   undercroft_figure_eight both inside loops               124 fallbacks
        //
        // So spend the budget on acyclic chains first and only spill into cyclic ones when
        // the acyclic capacity runs out. Raising MaxBacktracks was the obvious first guess and
        // it is the wrong one — doubling it to 24000 moved the rate 1.35% -> 1.15% and cost
        // 75% more sweep time, because the search was not short of budget, it was being asked
        // for layouts that are genuinely hard to embed.
        var expandable = new List<FlowNode>();
        foreach (FlowNode node in flow.Nodes)
            if (node.Expandable && node.Neighbours.Count > 0) expandable.Add(node);
        if (expandable.Count == 0) return;

        var onCycle = new HashSet<int>();
        foreach (List<int> loop in FindLoops(flow)) foreach (int id in loop) onCycle.Add(id);
        expandable.Sort((x, y) => (onCycle.Contains(x.Id) ? 1 : 0) - (onCycle.Contains(y.Id) ? 1 : 0));
        int acyclicCount = 0;
        foreach (FlowNode n in expandable) if (!onCycle.Contains(n.Id)) acyclicCount++;

        var extras = new int[expandable.Count];

        if (budget < 0)
        {
            for (int i = 0; i < expandable.Count; i++)
                extras[i] = rng.NextInt(0, expandable[i].ExpandMax);
        }
        else
        {
            // Shuffled, so it is not always the first-authored chain that grows. Two passes:
            // a random share each, then a greedy top-up. The random pass alone undershoots
            // the target most of the time; the greedy pass alone makes every floor of a given
            // length identical in shape.
            //
            // Shuffled WITHIN the acyclic and cyclic groups rather than across them, so the
            // cheap chains keep their priority while still varying between floors.
            var order = new int[expandable.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            rng.Shuffle(order.AsSpan(0, acyclicCount));
            rng.Shuffle(order.AsSpan(acyclicCount));

            int remaining = budget;

            // A random share to each acyclic chain, so their shapes differ between floors.
            for (int k = 0; k < acyclicCount && remaining > 0; k++)
            {
                int i = order[k];
                int cap = Mathf.Min(Capacity(expandable[i]), remaining);
                extras[i] = rng.NextInt(0, cap + 1);
                remaining -= extras[i];
            }

            // Then fill the acyclic chains to capacity before a single room goes to a chain
            // inside a loop. This ordering is the fix for the fallback rate; the random pass
            // above must not be allowed to hand rooms to a cyclic chain while an acyclic one
            // still has space.
            for (int k = 0; k < acyclicCount && remaining > 0; k++)
                remaining -= Give(order[k], remaining);

            // Only now does the remainder spill into the loops.
            for (int k = acyclicCount; k < order.Length && remaining > 0; k++)
                remaining -= Give(order[k], remaining);

            int Give(int i, int room)
            {
                int add = Mathf.Min(Capacity(expandable[i]) - extras[i], room);
                extras[i] += add;
                return add;
            }
        }

        for (int i = 0; i < expandable.Count; i++)
            if (extras[i] > 0) SpliceChain(flow, expandable[i], extras[i], rng);
    }

    /// <summary>Extra rooms a node can absorb. ExpandMax is the run LENGTH (docs/06 §3.3's
    /// "runs of 1..N"), so the node itself is one of them.</summary>
    private static int Capacity(FlowNode node) => Mathf.Max(0, node.ExpandMax - 1);

    /// <summary>
    /// Splice <paramref name="extra"/> rooms of the node's own role between it and ONE chosen
    /// neighbour, so the chain lengthens a path rather than fanning out into new branches.
    /// </summary>
    private static void SpliceChain(FloorFlow flow, FlowNode node, int extra, Rng rng)
    {
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

    /// <summary>
    /// docs/06 §3.3 — the content-pacing valve. Every "one shop per floor", "shrines only
    /// at dead ends" rule lives here rather than scattered through the generator.
    ///
    /// Split into a roll and an apply so <see cref="ExpansionBudget"/> can count the rooms
    /// before they exist. The rolls stay here; nothing else decides what a floor stocks.
    /// </summary>
    private readonly struct Injections
    {
        public Injections(bool shop, bool shrine, int secrets)
        {
            Shop = shop; Shrine = shrine; Secrets = secrets;
        }

        public bool Shop { get; }
        public bool Shrine { get; }
        public int Secrets { get; }

        /// <summary>Rooms this will add. The reward room is unconditional.</summary>
        public int Count => 1 + (Shop ? 1 : 0) + (Shrine ? 1 : 0) + Secrets;
    }

    /// <summary>
    /// Roll a floor's optional stock, then trim it to fit <paramref name="roomTarget"/>.
    ///
    /// THE TRIM IS A DESIGN DECISION, not arithmetic. The base flows are 9–10 nodes and full
    /// stock is 5 more rooms, so a 10-node flow with everything reaches 15 on a floor whose
    /// band tops out at 14 — and expansion cannot fix that, because expansion only adds. One
    /// of the two has to yield, and length wins: docs/07 §2's count is a pacing promise
    /// (5–8 minutes on the Undercroft) while docs/06 §3.3's "1–3 secret rooms" is a range.
    ///
    /// Trimmed lowest content value first — the second secret, then the shrine. The reward
    /// room and the shop are never dropped; both are guarantees other systems depend on
    /// (docs/08 §2.1), and a floor 1 with no shop is a strictly worse failure than a floor 1
    /// with one room too many.
    ///
    /// Everything is rolled before anything is trimmed, so the number of RNG draws does not
    /// depend on the target. A generator whose draw COUNT varies with an input is one whose
    /// later stages shift when that input changes, which makes every seed comparison useless.
    /// </summary>
    private static Injections RollInjections(Rng rng, int floorIndex, int roomTarget, int baseRooms)
    {
        bool shop = floorIndex >= 2 || rng.Chance(0.7f);   // guaranteed from floor 2 (docs/08 §2.1)
        bool shrine = rng.Chance(0.75f);
        int secrets = rng.NextInt(1, 3);

        if (roomTarget < 0) return new Injections(shop, shrine, secrets);

        int Total() => baseRooms + 1 + (shop ? 1 : 0) + (shrine ? 1 : 0) + secrets;

        // docs/06 §3.3 puts the floor at 1 secret room, so that is where trimming stops.
        while (secrets > 1 && Total() > roomTarget) secrets--;
        if (shrine && Total() > roomTarget) shrine = false;

        return new Injections(shop, shrine, secrets);
    }

    private static void ApplyInjections(FloorFlow flow, Rng rng, Injections injections)
    {
        // Reward room: guaranteed, attached to a dead end so it never sits on the path.
        AttachToDeadEnd(flow, RoomRole.Reward, rng);

        if (injections.Shop) AttachToDeadEnd(flow, RoomRole.Shop, rng);
        if (injections.Shrine) AttachToDeadEnd(flow, RoomRole.Shrine, rng);

        // Secrets attach to a NORMAL room via a cracked wall, so they may hang off
        // anything — including a room in the middle of a loop.
        for (int i = 0; i < injections.Secrets; i++) AttachToAny(flow, RoomRole.Secret, rng);
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

    /// <summary>
    /// Pick a room for a node, PREFERRING this floor's own theme.
    ///
    /// Two passes rather than a hard filter, and the fallback is load-bearing. Floor 2 has
    /// authored Wharf rooms for the combat roles only — its shops, shrines, entrances and boss
    /// arena are still Undercroft — so a strict tag filter would fail generation outright on
    /// every floor whose set is incomplete, which is all of them. Preferring the theme gets
    /// water into every combat room on the Wharfs, which is the point of the floor, without
    /// requiring sixteen rooms to exist before any of them can be used.
    ///
    /// The cost is honest and worth stating: a Wharf floor still has a cellar for a shop.
    /// </summary>
    private RoomTemplate? PickTemplate(FlowNode node, Rng rng, int floorIndex, HashSet<string> used)
    {
        string theme = Core.FloorScaling.ThemeTag(floorIndex);
        return PickTemplate(node, rng, floorIndex, used, theme)
               ?? PickTemplate(node, rng, floorIndex, used, requiredTag: null);
    }

    private RoomTemplate? PickTemplate(FlowNode node, Rng rng, int floorIndex,
                                       HashSet<string> used, string? requiredTag)
    {
        RoomTemplate? best = null;
        float bestScore = -1f;

        foreach (RoomTemplate t in _templates)
        {
            if (t.Role != node.Role) continue;
            if (t.MinFloor > floorIndex) continue;
            if (requiredTag is not null && t.FloorTag != requiredTag) continue;

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
            // Jitter scales with room size. A fixed 40px wobble was meaningful when rooms
            // were 16 tiles across and negligible once they were 70 — every floor started
            // collapsing into the same tightly-wound spiral, and tightly-wound layouts are
            // exactly the ones that fail to place the last few rooms.
            float jitter = (template.WidthTiles + template.HeightTiles) * 0.9f;
            scored.Add((-dist + rng.Range(0f, jitter), opt));
        }
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        // Try only the best few placements per node — BEAM WIDTH, not exhaustive search.
        //
        // The backtrack budget is shared across the whole recursion, so trying every
        // candidate (easily 60+ once rooms have two doors per wall) burns it exploring a
        // very wide tree at shallow depth and never reaches a complete layout. Adding
        // exits actually made the fallback rate WORSE for exactly this reason, which is
        // the tell that the search was budget-limited rather than option-limited.
        //
        // Packing problems want depth. Capping the branching factor spends the same budget
        // going further down, and the options are already sorted best-first by compactness
        // so the ones dropped are the sprawling placements that would likely fail anyway.
        const int BeamWidth = 10;

        options.Clear();
        for (int i = 0; i < scored.Count && i < BeamWidth; i++) options.Add(scored[i].opt);

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
