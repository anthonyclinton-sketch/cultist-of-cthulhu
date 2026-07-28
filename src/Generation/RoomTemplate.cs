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

        return null;
    }
}
