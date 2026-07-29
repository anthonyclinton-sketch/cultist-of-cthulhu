using System.Collections.Generic;
using CultistOfCthulhu.Generation;
using Godot;

namespace CultistOfCthulhu.Rooms;

/// <summary>A door punched between two connected rooms. Sealed during encounters.</summary>
public sealed class Doorway
{
    public int RoomA, RoomB;
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

        foreach (PlacedRoom r in floor.Rooms) CarveRoom(r);
        CarveConnections(floor);
    }

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

                if (!TryPunchDoor(a, b)) CarveCorridor(a, b);
            }
        }
    }

    /// <summary>
    /// Open a passage where two rooms sit flush. Returns false when they do not touch, in
    /// which case the caller runs a corridor instead.
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

            Doors.Add(new Doorway
            {
                RoomA = a.NodeId, RoomB = b.NodeId, Horizontal = true,
                WorldRect = new Rect2((edgeX - 1) * Tile, (cy - DoorHalfWidth) * Tile,
                                      2 * Tile, (2 * DoorHalfWidth + 1) * Tile),
            });
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

            Doors.Add(new Doorway
            {
                RoomA = a.NodeId, RoomB = b.NodeId, Horizontal = false,
                WorldRect = new Rect2((cx - DoorHalfWidth) * Tile, (edgeY - 1) * Tile,
                                      (2 * DoorHalfWidth + 1) * Tile, 2 * Tile),
            });
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
