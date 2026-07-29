using Godot;

namespace CultistOfCthulhu.Generation;

/// <summary>docs/06 §4.2 — room roles. Encounters and pacing are composed by role.</summary>
public enum RoomRole
{
    Entrance,
    CombatEasy,
    CombatMed,
    CombatHard,
    Hub,
    Connector,
    Reward,
    Shop,
    Shrine,
    Secret,
    Warden,
    BossFoyer,
    Boss,
}

public enum Side { North, South, East, West }

/// <summary>
/// A hand-authored room (docs/06 §4).
///
/// Pillar IV: **nothing about the interior is procedural.** A designer builds and
/// playtests every one of these; the generator only decides which rooms appear and how
/// they connect. That is the whole reason this architecture was chosen over BSP or
/// cellular automata — it guarantees pacing while randomising topology.
///
/// Exits are stored as four offset arrays rather than a list of exit objects. Nested
/// Resources are painful to author in Godot's inspector and produce unreadable .tres
/// diffs; four int arrays are editable in place and review cleanly.
/// </summary>
[GlobalClass]
public partial class RoomTemplate : Resource
{
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "unnamed";
    [Export] public RoomRole Role { get; set; } = RoomRole.CombatEasy;
    [Export] public string FloorTag { get; set; } = "undercroft";
    [Export] public int MinFloor { get; set; } = 1;
    /// <summary>Selection weight. Recency penalties are applied on top (docs/06 §4.3).</summary>
    [Export] public float Weight { get; set; } = 1f;

    [ExportGroup("Geometry")]
    [Export] public int WidthTiles { get; set; } = 16;
    [Export] public int HeightTiles { get; set; } = 12;

    /// <summary>Tile offsets along each side where a door may sit. Offsets are measured
    /// from the room's top-left corner: along X for north/south, along Y for east/west.</summary>
    [Export] public int[] NorthExits { get; set; } = System.Array.Empty<int>();
    [Export] public int[] SouthExits { get; set; } = System.Array.Empty<int>();
    [Export] public int[] EastExits { get; set; } = System.Array.Empty<int>();
    [Export] public int[] WestExits { get; set; } = System.Array.Empty<int>();

    [ExportGroup("Interior")]
    /// <summary>
    /// Solid blocks inside the room, as flat quads: x, y, width, height in tiles, measured
    /// from the room's top-left. Four ints per obstacle.
    ///
    /// This is what stops a room being a rectangle. Pillars, tombs, a long table — cover to
    /// break line of fire, corners to dodge behind, and something for a radial pattern to
    /// be shaped by. docs/06 Pillar IV is explicit that nothing about a room's interior is
    /// procedural, so these are authored per template and never generated.
    ///
    /// A flat int array rather than a list of Rect2I, for the same reason the exits are
    /// four int arrays: nested Resources are painful to author in Godot's inspector and
    /// produce unreadable .tres diffs.
    ///
    /// They are carved into the same walkable grid the walls come from, so every system
    /// that already respects walls respects these for free — bullets stop on them, enemies
    /// path around them, the flow field marks them blocked, and spawn placement avoids them.
    /// </summary>
    [Export] public int[] Obstacles { get; set; } = System.Array.Empty<int>();

    public int ObstacleCount => Obstacles.Length / 4;

    public Rect2I ObstacleAt(int i) =>
        new(Obstacles[i * 4], Obstacles[i * 4 + 1], Obstacles[i * 4 + 2], Obstacles[i * 4 + 3]);

    [ExportGroup("Encounter")]
    /// <summary>Upper bound on Dread this room can hold — a function of its floor area and
    /// cover, authored per room (docs/06 §6.1).</summary>
    [Export] public float ThreatCapacity { get; set; } = 60f;
    [Export] public int SpawnAnchorCount { get; set; } = 8;

    public int ExitCount => NorthExits.Length + SouthExits.Length + EastExits.Length + WestExits.Length;

    public int[] ExitsOn(Side side) => side switch
    {
        Side.North => NorthExits,
        Side.South => SouthExits,
        Side.East => EastExits,
        _ => WestExits,
    };

    public static Side Opposite(Side s) => s switch
    {
        Side.North => Side.South,
        Side.South => Side.North,
        Side.East => Side.West,
        _ => Side.East,
    };

    public string? Validate()
    {
        if (WidthTiles < 6 || HeightTiles < 5)
            return $"{Id}: {WidthTiles}x{HeightTiles} is below the 6x5 minimum playable size.";

        // Exits are OFFERS, not commitments — the flow decides how many are used, so even
        // a graph dead end should offer several. A template with one exit can only attach
        // to one side of one host, which makes it the hardest thing on the floor to place.
        if (ExitCount == 0) return $"{Id}: no exits.";
        if (ExitCount < 2)
            return $"{Id}: only {ExitCount} exit. Templates should offer attachment points on " +
                   $"several sides even when the room is a dead end in the flow.";

        if (Role == RoomRole.Hub && ExitCount < 3)
            return $"{Id}: hubs need 3+ exits (has {ExitCount}) — that is what makes them hubs.";

        foreach (int o in NorthExits) if (o < 1 || o >= WidthTiles - 1) return $"{Id}: north exit {o} out of range.";
        foreach (int o in SouthExits) if (o < 1 || o >= WidthTiles - 1) return $"{Id}: south exit {o} out of range.";
        foreach (int o in EastExits) if (o < 1 || o >= HeightTiles - 1) return $"{Id}: east exit {o} out of range.";
        foreach (int o in WestExits) if (o < 1 || o >= HeightTiles - 1) return $"{Id}: west exit {o} out of range.";

        return ValidateObstacles();
    }

    /// <summary>
    /// Obstacles must not seal anything off.
    ///
    /// A pillar in the wrong place is not a cosmetic mistake — it is a room the player
    /// walks into and cannot leave, or a doorway nothing can path through, and doors seal
    /// behind them during a fight. The generator's own validation works on the ROOM GRAPH
    /// and would report such a floor as perfectly connected.
    ///
    /// So this floods the interior and insists on two things: every free tile is reachable
    /// from every other, and every tile a doorway could be carved through is among them.
    /// </summary>
    private string? ValidateObstacles()
    {
        if (Obstacles.Length == 0) return null;
        if (Obstacles.Length % 4 != 0)
            return $"{Id}: Obstacles has {Obstacles.Length} ints; it must be four per block (x, y, w, h).";

        // Interior only: the outer ring is wall, and the ring inside that has to stay clear
        // so a body can always walk the perimeter to reach any door.
        var solid = new bool[WidthTiles, HeightTiles];

        for (int i = 0; i < ObstacleCount; i++)
        {
            Rect2I r = ObstacleAt(i);
            if (r.Size.X <= 0 || r.Size.Y <= 0) return $"{Id}: obstacle {i} has a non-positive size.";

            if (r.Position.X < 2 || r.Position.Y < 2
                || r.Position.X + r.Size.X > WidthTiles - 2
                || r.Position.Y + r.Size.Y > HeightTiles - 2)
            {
                return $"{Id}: obstacle {i} at {r.Position} size {r.Size} touches the perimeter lane. " +
                       "Leave one clear tile inside the wall ring so every door stays walkable.";
            }

            for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
                for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
                    solid[x, y] = true;
        }

        // Flood the interior from the first free tile.
        int freeTotal = 0;
        var start = new Vector2I(-1, -1);
        for (int y = 1; y < HeightTiles - 1; y++)
        {
            for (int x = 1; x < WidthTiles - 1; x++)
            {
                if (solid[x, y]) continue;
                freeTotal++;
                if (start.X < 0) start = new Vector2I(x, y);
            }
        }
        if (freeTotal == 0) return $"{Id}: obstacles fill the entire interior.";

        var seen = new bool[WidthTiles, HeightTiles];
        var queue = new System.Collections.Generic.Queue<Vector2I>();
        queue.Enqueue(start);
        seen[start.X, start.Y] = true;
        int reached = 1;

        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            foreach (Vector2I d in Neighbours)
            {
                int nx = c.X + d.X, ny = c.Y + d.Y;
                if (nx < 1 || ny < 1 || nx >= WidthTiles - 1 || ny >= HeightTiles - 1) continue;
                if (solid[nx, ny] || seen[nx, ny]) continue;
                seen[nx, ny] = true;
                reached++;
                queue.Enqueue(new Vector2I(nx, ny));
            }
        }

        if (reached != freeTotal)
            return $"{Id}: obstacles split the interior into separate regions " +
                   $"({reached} of {freeTotal} tiles reachable).";

        // And every doorway must open into that region. A door carved into the back of a
        // pillar is a door the room does not really have.
        foreach (int o in NorthExits) if (!seen[o, 1]) return DoorBlocked("north", o);
        foreach (int o in SouthExits) if (!seen[o, HeightTiles - 2]) return DoorBlocked("south", o);
        foreach (int o in EastExits) if (!seen[WidthTiles - 2, o]) return DoorBlocked("east", o);
        foreach (int o in WestExits) if (!seen[1, o]) return DoorBlocked("west", o);

        return null;
    }

    private static readonly Vector2I[] Neighbours =
        { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private string DoorBlocked(string side, int offset) =>
        $"{Id}: the {side} exit at {offset} is blocked by an obstacle.";
}
