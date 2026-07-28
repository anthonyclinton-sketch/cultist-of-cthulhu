using System.Collections.Generic;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// A node in an authored flow graph (docs/06 §3.1). Carries a ROLE, not a room — the
/// concrete room is chosen later, so one flow produces a different floor every run.
/// </summary>
public sealed class FlowNode
{
    public int Id;
    public RoomRole Role;

    /// <summary>Expands into a run of 1–N rooms of the same role at transform time. This
    /// is how one flow yields both a 12-room and an 18-room floor (docs/06 §3.3).</summary>
    public bool Expandable;
    public int ExpandMax = 3;

    public readonly List<int> Neighbours = new();

    public FlowNode(int id, RoomRole role) { Id = id; Role = role; }
}

/// <summary>
/// A pre-authored floor topology with NO spatial information (docs/06 §3.1).
///
/// Structure rule, taken from Gungeon and correct: a flow is a TREE with a root, plus a
/// small number of extra edges that CLOSE LOOPS. Every loop therefore has a well-defined
/// entrance and exit, which is what lets the layout stage embed it in 2D without the
/// result reading as a maze.
///
/// AUTHORED IN CODE, not .tres, and deliberately so. docs/09 §5's rule is that no gameplay
/// NUMBER lives in a .cs file; a graph topology is structure, not tuning. Godot's inspector
/// cannot usefully edit a graph, and docs/06 §10 schedules a GraphEdit plugin for exactly
/// this. Hand-writing adjacency as parallel arrays in .tres would be unreadable and
/// unreviewable in a diff. These move to data when the plugin exists.
/// </summary>
public sealed class FloorFlow
{
    public string Id { get; }
    public readonly List<FlowNode> Nodes = new();
    public int RootId { get; private set; }

    public FloorFlow(string id) { Id = id; }

    public int Add(RoomRole role, bool expandable = false, int expandMax = 3)
    {
        var n = new FlowNode(Nodes.Count, role) { Expandable = expandable, ExpandMax = expandMax };
        Nodes.Add(n);
        return n.Id;
    }

    public FloorFlow Root(int id) { RootId = id; return this; }

    public FloorFlow Link(int a, int b)
    {
        if (!Nodes[a].Neighbours.Contains(b)) Nodes[a].Neighbours.Add(b);
        if (!Nodes[b].Neighbours.Contains(a)) Nodes[b].Neighbours.Add(a);
        return this;
    }

    public FloorFlow Chain(params int[] ids)
    {
        for (int i = 0; i + 1 < ids.Length; i++) Link(ids[i], ids[i + 1]);
        return this;
    }

    /// <summary>Deep copy, so transformation never mutates the authored original.</summary>
    public FloorFlow Clone()
    {
        var c = new FloorFlow(Id) { RootId = RootId };
        foreach (FlowNode n in Nodes)
            c.Nodes.Add(new FlowNode(n.Id, n.Role) { Expandable = n.Expandable, ExpandMax = n.ExpandMax });
        for (int i = 0; i < Nodes.Count; i++)
            c.Nodes[i].Neighbours.AddRange(Nodes[i].Neighbours);
        return c;
    }

    public int CountRole(RoomRole role)
    {
        int n = 0;
        foreach (FlowNode node in Nodes) if (node.Role == role) n++;
        return n;
    }

    /// <summary>
    /// Author-time structural check. A malformed flow produces a generator failure that is
    /// very hard to diagnose from the other end, so it is caught here instead.
    /// </summary>
    public string? Validate()
    {
        if (Nodes.Count < 4) return $"{Id}: too few nodes ({Nodes.Count}).";
        if (CountRole(RoomRole.Boss) != 1) return $"{Id}: needs exactly one boss node.";
        if (CountRole(RoomRole.Entrance) != 1) return $"{Id}: needs exactly one entrance.";
        if (Nodes[RootId].Role != RoomRole.Entrance) return $"{Id}: root must be the entrance.";

        // Connectivity — an unreachable node is a room the player can never see.
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(RootId);
        while (stack.Count > 0)
        {
            int id = stack.Pop();
            if (!seen.Add(id)) continue;
            foreach (int nb in Nodes[id].Neighbours) stack.Push(nb);
        }
        if (seen.Count != Nodes.Count)
            return $"{Id}: {Nodes.Count - seen.Count} unreachable node(s).";

        // docs/06 §3.1 — loops are the point. A pure tree gives dead-end fatigue.
        int edges = 0;
        foreach (FlowNode n in Nodes) edges += n.Neighbours.Count;
        edges /= 2;
        if (edges < Nodes.Count) return $"{Id}: no loops (tree with {edges} edges) — flows must close at least one.";

        return null;
    }
}
