using System.Collections.Generic;
using CultistOfCthulhu.Generation;
using Godot;

namespace CultistOfCthulhu.Rooms;

/// <summary>
/// An opening in one room's wall ring. Sealed while that room is contested.
///
/// Belongs to ONE room rather than to a pair, and that is the fix for a real bug: the
/// previous version recorded a doorway only where <see cref="FloorGeometry.TryPunchDoor"/>
/// cut through two flush rooms, and corridors recorded nothing at all. Rooms joined by a
/// corridor therefore had openings that no code knew about and no seal could ever cover, so
/// roughly half the combat rooms on a floor could be walked straight out of mid-fight.
///
/// Deriving one-sided openings from the finished grid instead means "seal this room" is
/// simply "close every hole in its own ring", which needs no knowledge of what is on the
/// other side and cannot miss a case.
/// </summary>
public sealed class Doorway
{
    /// <summary>Unique within the floor. Keys the seal table.</summary>
    public int Index;
    /// <summary>The room whose wall ring this opening is in.</summary>
    public int Room;
    public Rect2 WorldRect;      // pixels
    public bool Horizontal;      // true if the passage runs east-west
}

/// <summary>
/// Turns a <see cref="GeneratedFloor"/> — which is pure topology and rectangles — into
/// walkable space, walls and doors.
///
/// Works on a tile occupancy grid rather than per-room wall segments. Rooms, corridors and
/// doors all write into the same grid, so the collision shell is derived once from the
/// finished walkable region and every case (flush rooms, corridor joins, L-bends) is
/// handled by the same code instead of three special cases that disagree at the seams.
///
/// Rooms are inset by one tile so that two flush rooms have a wall between them. Without
/// the inset their interiors are contiguous and the whole floor becomes one open space —
/// technically walkable, and completely wrong.
/// </summary>
public sealed class FloorGeometry
{
    public const int Tile = 16;                 // px per tile, docs/02 §1.1
    private const int DoorHalfWidth = 1;        // 3 tiles wide
    private const int CorridorHalfWidth = 1;

    private readonly bool[,] _walkable;

    /// <summary>Authored flood levels, 0 where there is no water. Filled alongside
    /// <see cref="_walkable"/> so the two grids cannot drift apart.</summary>
    private readonly byte[,] _flood;
    private readonly Vector2I _origin;
    public int Width { get; }
    public int Height { get; }

    public readonly List<Doorway> Doors = new();

    public FloorGeometry(GeneratedFloor floor)
    {
        Rect2I b = floor.Bounds();
        _origin = b.Position - new Vector2I(2, 2);
        Width = b.Size.X + 4;
        Height = b.Size.Y + 4;
        _walkable = new bool[Width, Height];
        _flood = new byte[Width, Height];

        foreach (PlacedRoom r in floor.Rooms) CarveRoom(r);
        CarveConnections(floor);
        FindDoorways(floor);
    }

    /// <summary>
    /// Find every opening in every room's wall ring, from the FINISHED grid.
    ///
    /// This is the class's own stated principle finally applied to doors. The comment above
    /// says rooms, corridors and doors all write into one grid so that the collision shell
    /// can be derived once and "every case is handled by the same code instead of three
    /// special cases that disagree at the seams" — and then doorways were collected as a
    /// special case by the flush-room path only, which is exactly the disagreement it warns
    /// about. Corridor mouths were invisible.
    ///
    /// Openings are grouped by 4-connectivity within the ring rather than scanned side by
    /// side, so a corridor that clips a corner produces one doorway instead of two halves or
    /// a missed tile.
    /// </summary>
    private void FindDoorways(GeneratedFloor floor)
    {
        Doors.Clear();
        var visited = new HashSet<Vector2I>();

        foreach (PlacedRoom r in floor.Rooms)
        {
            var ring = new HashSet<Vector2I>();
            Rect2I b = r.Bounds;

            // Ordered as well as set-membered. The set answers "is this tile part of the
            // ring" during the flood; the LIST fixes the order doorways are discovered in,
            // and therefore the indices they get. Walking the set instead would key the
            // seal table off HashSet iteration order, which is not a contract — and this
            // project treats reproducibility as one (docs/09 §4).
            var ordered = new List<Vector2I>();

            for (int x = b.Position.X; x < b.Position.X + b.Size.X; x++)
            {
                Add(ring, ordered, new Vector2I(x, b.Position.Y));
                Add(ring, ordered, new Vector2I(x, b.Position.Y + b.Size.Y - 1));
            }
            for (int y = b.Position.Y; y < b.Position.Y + b.Size.Y; y++)
            {
                Add(ring, ordered, new Vector2I(b.Position.X, y));
                Add(ring, ordered, new Vector2I(b.Position.X + b.Size.X - 1, y));
            }

            visited.Clear();
            foreach (Vector2I tile in ordered)
            {
                if (!visited.Add(tile)) continue;

                // Flood this run of open ring tiles and take its bounding box.
                var run = new List<Vector2I> { tile };
                var queue = new Queue<Vector2I>();
                queue.Enqueue(tile);

                while (queue.Count > 0)
                {
                    Vector2I c = queue.Dequeue();
                    foreach (Vector2I d in Neighbours)
                    {
                        var n = new Vector2I(c.X + d.X, c.Y + d.Y);
                        if (!ring.Contains(n) || !visited.Add(n)) continue;
                        run.Add(n);
                        queue.Enqueue(n);
                    }
                }

                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (Vector2I c in run)
                {
                    minX = Mathf.Min(minX, c.X); maxX = Mathf.Max(maxX, c.X);
                    minY = Mathf.Min(minY, c.Y); maxY = Mathf.Max(maxY, c.Y);
                }

                int w = maxX - minX + 1;
                int h = maxY - minY + 1;

                Doors.Add(new Doorway
                {
                    Index = Doors.Count,
                    Room = r.NodeId,
                    // Taller than wide means the passage runs east-west through it.
                    Horizontal = h > w,
                    WorldRect = new Rect2(minX * Tile, minY * Tile, w * Tile, h * Tile),
                });
            }
        }
    }

    /// <summary>Add a ring tile to the candidate set, but only if it is actually open.
    /// Corners appear on two sides, so the set membership also dedupes the list.</summary>
    private void Add(HashSet<Vector2I> ring, List<Vector2I> ordered, Vector2I world)
    {
        if (IsWalkableWorldTile(world) && ring.Add(world)) ordered.Add(world);
    }

    private static readonly Vector2I[] Neighbours =
        { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private Vector2I ToLocal(Vector2I world) => world - _origin;

    public bool IsWalkable(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && _walkable[x, y];

    public Vector2 TileToWorld(int x, int y) => new((x + _origin.X) * Tile, (y + _origin.Y) * Tile);

    public Vector2 RoomCentreWorld(PlacedRoom r) => new(
        (r.Position.X + r.Width * 0.5f) * Tile,
        (r.Position.Y + r.Height * 0.5f) * Tile);

    /// <summary>
    /// A point in this room that is guaranteed to be standable: the centre if it is clear,
    /// otherwise the nearest open interior tile.
    ///
    /// Everything that PLACES something in a room must use this rather than the geometric
    /// centre. Authored interiors made that distinction real — long_table's whole design is
    /// a block through the middle of the room, and great_cistern's basin sits dead centre —
    /// so "the centre" is now a position that may well be inside a wall. The player spawn,
    /// the boss's opening position, the shop's furniture and the corridor endpoints all
    /// went through the geometric centre, and every one of them would have been placed
    /// inside solid rock.
    /// </summary>
    public Vector2 RoomAnchorWorld(PlacedRoom r)
    {
        Vector2I t = AnchorTile(r);
        return new Vector2((t.X + 0.5f) * Tile, (t.Y + 0.5f) * Tile);
    }

    /// <summary>Nearest open interior tile to the room's centre, in world tile coordinates.</summary>
    private Vector2I AnchorTile(PlacedRoom r)
    {
        Vector2I centre = r.Centre;
        if (IsWalkableWorldTile(centre)) return centre;

        int maxRing = Mathf.Max(r.Width, r.Height) / 2 + 1;
        for (int ring = 1; ring <= maxRing; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    // Perimeter of this ring only; the interior was covered by earlier ones.
                    if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue;

                    var c = new Vector2I(centre.X + dx, centre.Y + dy);
                    if (c.X <= r.Position.X || c.Y <= r.Position.Y) continue;
                    if (c.X >= r.Position.X + r.Width - 1 || c.Y >= r.Position.Y + r.Height - 1) continue;
                    if (IsWalkableWorldTile(c)) return c;
                }
            }
        }
        return centre;
    }

    private bool IsWalkableWorldTile(Vector2I world)
    {
        Vector2I l = ToLocal(world);
        return IsWalkable(l.X, l.Y);
    }

    public Rect2 RoomRectWorld(PlacedRoom r) => new(
        r.Position.X * Tile, r.Position.Y * Tile, r.Width * Tile, r.Height * Tile);

    /// <summary>
    /// The walkable interior, excluding the wall ring.
    ///
    /// Use this for "is the player in this room?" rather than the full bounds. A doorway is
    /// carved through the wall ring of BOTH adjacent rooms, so a player in a threshold sits
    /// inside both rooms' bounds at once — which made room tracking flip the moment you
    /// touched a door instead of when you actually arrived.
    /// </summary>
    public Rect2 RoomInteriorWorld(PlacedRoom r) => new(
        (r.Position.X + 1) * Tile, (r.Position.Y + 1) * Tile,
        (r.Width - 2) * Tile, (r.Height - 2) * Tile);

    private void CarveRoom(PlacedRoom r)
    {
        // Inset by one tile: the outer ring is wall, so flush rooms are separated.
        Vector2I p = ToLocal(r.Position);
        for (int y = p.Y + 1; y < p.Y + r.Height - 1; y++)
            for (int x = p.X + 1; x < p.X + r.Width - 1; x++)
                if (x >= 0 && y >= 0 && x < Width && y < Height) _walkable[x, y] = true;

        // Then punch the authored interior back out. Order matters: obstacles are carved
        // AFTER the interior so a block always wins over the floor beneath it, and they are
        // written into the same walkable grid rather than tracked separately — which is
        // what makes every system that already respects walls respect a pillar for free.
        //
        // Rooms may be placed rotated in a later milestone; today they are not, so the
        // template's local coordinates are the room's coordinates.
        RoomTemplate t = r.Template;
        for (int i = 0; i < t.ObstacleCount; i++)
        {
            Rect2I o = t.ObstacleAt(i);
            for (int y = 0; y < o.Size.Y; y++)
            {
                for (int x = 0; x < o.Size.X; x++)
                {
                    int gx = p.X + o.Position.X + x;
                    int gy = p.Y + o.Position.Y + y;
                    if (gx >= 0 && gy >= 0 && gx < Width && gy < Height) _walkable[gx, gy] = false;
                }
            }
        }

        // Water last, and into its OWN grid. It does not touch _walkable: water is a cost,
        // not a wall (see RoomTemplate.Water). Carving it after obstacles means a block
        // standing in a channel is still solid and still dry on top, which is the pier the
        // player is meant to stand on.
        for (int i = 0; i < t.WaterCount; i++)
        {
            Rect2I w = t.WaterAt(i);
            byte level = (byte)t.WaterFloodLevel(i);
            for (int y = 0; y < w.Size.Y; y++)
            {
                for (int x = 0; x < w.Size.X; x++)
                {
                    int gx = p.X + w.Position.X + x;
                    int gy = p.Y + w.Position.Y + y;
                    if (gx >= 0 && gy >= 0 && gx < Width && gy < Height) _flood[gx, gy] = level;
                }
            }
        }
    }

    private void CarveConnections(GeneratedFloor floor)
    {
        var byId = new Dictionary<int, PlacedRoom>();
        foreach (PlacedRoom r in floor.Rooms) byId[r.NodeId] = r;

        var done = new HashSet<(int, int)>();

        foreach (PlacedRoom a in floor.Rooms)
        {
            foreach (int nb in a.Connections)
            {
                if (!byId.TryGetValue(nb, out PlacedRoom? b)) continue;
                var key = a.NodeId < nb ? (a.NodeId, nb) : (nb, a.NodeId);
                if (!done.Add(key)) continue;

                if (TryPunchDoor(a, b)) PunchedDoors++;
                else { CarveCorridor(a, b); Corridors++; }
            }
        }
    }

    /// <summary>
    /// How this floor is joined together. Reported because the ratio was the shape of a real
    /// bug: doorways used to be recorded only by the flush-room path, so the corridor count
    /// was exactly the number of room connections that could never be sealed.
    /// </summary>
    public int PunchedDoors { get; private set; }
    public int Corridors { get; private set; }

    /// <summary>
    /// Open a passage where two rooms sit flush. Returns false when they do not touch, in
    /// which case the caller runs a corridor instead.
    ///
    /// Only CARVES. It no longer records a Doorway — <see cref="FindDoorways"/> derives
    /// those from the finished grid, so this method and <see cref="CarveCorridor"/> both
    /// get their openings sealed by the same mechanism instead of one of them being
    /// forgotten.
    /// </summary>
    private bool TryPunchDoor(PlacedRoom a, PlacedRoom b)
    {
        Rect2I ra = a.Bounds, rb = b.Bounds;

        // Vertical shared edge (a left of b, or b left of a)
        if (ra.Position.X + ra.Size.X == rb.Position.X || rb.Position.X + rb.Size.X == ra.Position.X)
        {
            int y0 = Mathf.Max(ra.Position.Y, rb.Position.Y) + 1;
            int y1 = Mathf.Min(ra.Position.Y + ra.Size.Y, rb.Position.Y + rb.Size.Y) - 1;
            if (y1 - y0 < 2 * DoorHalfWidth + 1) return false;

            int cy = (y0 + y1) / 2;
            int edgeX = ra.Position.X + ra.Size.X == rb.Position.X
                ? ra.Position.X + ra.Size.X
                : rb.Position.X + rb.Size.X;

            // Two tiles wide: the wall ring of each room.
            for (int y = cy - DoorHalfWidth; y <= cy + DoorHalfWidth; y++)
                for (int x = edgeX - 1; x <= edgeX; x++)
                    Set(ToLocal(new Vector2I(x, y)));

            return true;
        }

        // Horizontal shared edge
        if (ra.Position.Y + ra.Size.Y == rb.Position.Y || rb.Position.Y + rb.Size.Y == ra.Position.Y)
        {
            int x0 = Mathf.Max(ra.Position.X, rb.Position.X) + 1;
            int x1 = Mathf.Min(ra.Position.X + ra.Size.X, rb.Position.X + rb.Size.X) - 1;
            if (x1 - x0 < 2 * DoorHalfWidth + 1) return false;

            int cx = (x0 + x1) / 2;
            int edgeY = ra.Position.Y + ra.Size.Y == rb.Position.Y
                ? ra.Position.Y + ra.Size.Y
                : rb.Position.Y + rb.Size.Y;

            for (int x = cx - DoorHalfWidth; x <= cx + DoorHalfWidth; x++)
                for (int y = edgeY - 1; y <= edgeY; y++)
                    Set(ToLocal(new Vector2I(x, y)));

            return true;
        }

        return false;
    }

    /// <summary>L-shaped corridor between two room centres. Horizontal leg first, then
    /// vertical — consistent so corridors read as deliberate rather than organic.</summary>
    private void CarveCorridor(PlacedRoom a, PlacedRoom b)
    {
        // From ANCHOR to anchor, not centre to centre. A corridor is carved as a 3-wide
        // channel along its whole length, including the part inside the rooms — so running
        // it from a centre that sits inside an authored block would bore a hole straight
        // through the long table, which is the one feature that room exists for.
        Vector2I p0 = AnchorTile(a);
        Vector2I p1 = AnchorTile(b);

        int step = p1.X >= p0.X ? 1 : -1;
        for (int x = p0.X; x != p1.X + step; x += step) CarveWide(x, p0.Y);

        step = p1.Y >= p0.Y ? 1 : -1;
        for (int y = p0.Y; y != p1.Y + step; y += step) CarveWide(p1.X, y);
    }

    private void CarveWide(int wx, int wy)
    {
        for (int dy = -CorridorHalfWidth; dy <= CorridorHalfWidth; dy++)
            for (int dx = -CorridorHalfWidth; dx <= CorridorHalfWidth; dx++)
                Set(ToLocal(new Vector2I(wx + dx, wy + dy)));
    }

    private void Set(Vector2I local)
    {
        if (local.X < 0 || local.Y < 0 || local.X >= Width || local.Y >= Height) return;
        _walkable[local.X, local.Y] = true;
    }

    /// <summary>
    /// Collision shell: only non-walkable tiles that TOUCH walkable space need bodies.
    /// Merged into horizontal runs, which turns roughly 40,000 candidate tiles into a few
    /// hundred rectangles.
    /// </summary>
    public List<Rect2> BuildWallRects()
    {
        var rects = new List<Rect2>();

        for (int y = 0; y < Height; y++)
        {
            int runStart = -1;
            for (int x = 0; x <= Width; x++)
            {
                bool isWall = x < Width && !_walkable[x, y] && TouchesWalkable(x, y);

                if (isWall && runStart < 0) runStart = x;
                else if (!isWall && runStart >= 0)
                {
                    Vector2 tl = TileToWorld(runStart, y);
                    rects.Add(new Rect2(tl, new Vector2((x - runStart) * Tile, Tile)));
                    runStart = -1;
                }
            }
        }
        return rects;
    }

    private bool TouchesWalkable(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (IsWalkable(x + dx, y + dy)) return true;
        return false;
    }

    /// <summary>
    /// The walkable grid as a <see cref="Core.TileMask"/>, for the systems that simulate
    /// their own movement and therefore never meet the collision shell above.
    ///
    /// Derived from the SAME grid as <see cref="BuildWallRects"/> rather than from the
    /// rects it produces. Those rects only cover wall tiles that touch walkable space —
    /// they are a collision shell, not a description of solid ground — so a body far inside
    /// the rock between two rooms would find nothing to collide with.
    /// </summary>
    public Core.TileMask BuildSolidMask()
    {
        var mask = new Core.TileMask(Width, Height, Tile, new Vector2(_origin.X * Tile, _origin.Y * Tile));
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                mask.SetSolid(x, y, !_walkable[x, y]);
        return mask;
    }

    /// <summary>
    /// Synthesise water into every room — debug only, for looking at the tide before a single
    /// Wharf template exists (--flood-demo).
    ///
    /// It writes into <see cref="_flood"/> rather than into a built TideField, and that is the
    /// whole point: the field and the renderer both derive from this grid, so flooding
    /// anywhere else gives a floor that is wet to the physics and dry to the eye. Which is
    /// exactly what the first version did.
    ///
    /// Bands step 1 at the bottom wall up to MaxFloodLevel, so the shoreline sweeps UP the
    /// room as the tide comes in rather than the whole room changing at once.
    /// </summary>
    public void FloodDemo(GeneratedFloor floor)
    {
        int bands = Core.TideField.MaxFloodLevel;
        foreach (PlacedRoom r in floor.Rooms)
        {
            // ToLocal, exactly as CarveRoom does. PlacedRoom.Position is in FLOOR space and
            // the grid is offset from it by _origin — using the raw position writes the water
            // two tiles off the room, or clean off the array, and silently does nothing.
            Vector2I p = ToLocal(r.Position);
            int bandHeight = Mathf.Max(1, (r.Height - 2) / (bands + 2));

            for (int band = 0; band < bands; band++)
            {
                int yTop = p.Y + r.Height - 1 - (band + 1) * bandHeight;
                for (int y = yTop; y < yTop + bandHeight; y++)
                    for (int x = p.X + 1; x < p.X + r.Width - 1; x++)
                        if (x >= 0 && y >= 0 && x < Width && y < Height && _walkable[x, y])
                            _flood[x, y] = (byte)(band + 1);
            }
        }
    }

    /// <summary>
    /// The authored water as a <see cref="Core.TideField"/>, on the same grid as
    /// <see cref="BuildSolidMask"/>.
    ///
    /// Built from a mask rather than from raw dimensions so the two grids share an origin by
    /// construction — a water field a tile out from the wall field puts the shoreline
    /// somewhere other than the shore, and nothing in the game would report it.
    /// </summary>
    public Core.TideField BuildTideField(Core.TileMask mask)
    {
        Core.TideField field = Core.TideField.FromMask(mask);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (_flood[x, y] > 0) field.SetFlood(x, y, _flood[x, y]);
        return field;
    }

    /// <summary>Water tiles at a given flood level, merged into runs for the renderer. Same
    /// row-run batching as <see cref="BuildFloorRects"/>, for the same reason.</summary>
    public List<Rect2> BuildWaterRects(int floodLevel) =>
        BuildWaterRuns(floodLevel, edgeOnly: false);

    /// <summary>
    /// The WATERLINE at a given flood level: water tiles whose neighbour above is not covered
    /// when this band is the last one submerged. Drawn as the bright shore edge.
    ///
    /// Defined per tile rather than as "the top row of the band", which is what it was and
    /// which was wrong in a way the flood demo could not show. That version took one minimum
    /// Y across the WHOLE FLOOR, so the highlight appeared only in whichever room happened to
    /// contain the topmost water tile on the floor and every other room's shoreline was
    /// missing. Every room in the demo is flooded identically, so the one room that drew it
    /// looked correct and the bug read as "working".
    ///
    /// This definition does not care about shape, which is the point: authored Wharf water is
    /// channels, pools and margins around a pier (docs/07 §3), not a band across the bottom
    /// of a rectangle, and a waterline that only works on bands would have failed on the
    /// first real room.
    /// </summary>
    public List<Rect2> BuildWaterEdgeRects(int floodLevel) =>
        BuildWaterRuns(floodLevel, edgeOnly: true);

    private List<Rect2> BuildWaterRuns(int floodLevel, bool edgeOnly)
    {
        var rects = new List<Rect2>();
        for (int y = 0; y < Height; y++)
        {
            int runStart = -1;
            for (int x = 0; x <= Width; x++)
            {
                bool water = x < Width && _flood[x, y] == floodLevel && _walkable[x, y];

                // An edge tile is one the water's surface is visible at: the tile above is
                // either not water at all, or belongs to a band that floods LATER and is
                // therefore still dry while this one is the waterline.
                if (water && edgeOnly)
                {
                    int above = y > 0 ? _flood[x, y - 1] : 0;
                    water = above == 0 || above > floodLevel;
                }

                if (water && runStart < 0) runStart = x;
                else if (!water && runStart >= 0)
                {
                    Vector2 tl = TileToWorld(runStart, y);
                    rects.Add(new Rect2(tl, new Vector2((x - runStart) * Tile, Tile)));
                    runStart = -1;
                }
            }
        }
        return rects;
    }

    public List<Rect2> BuildFloorRects()
    {
        var rects = new List<Rect2>();
        for (int y = 0; y < Height; y++)
        {
            int runStart = -1;
            for (int x = 0; x <= Width; x++)
            {
                bool walk = x < Width && _walkable[x, y];
                if (walk && runStart < 0) runStart = x;
                else if (!walk && runStart >= 0)
                {
                    Vector2 tl = TileToWorld(runStart, y);
                    rects.Add(new Rect2(tl, new Vector2((x - runStart) * Tile, Tile)));
                    runStart = -1;
                }
            }
        }
        return rects;
    }
}
