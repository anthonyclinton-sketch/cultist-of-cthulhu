using System.Runtime.CompilerServices;
using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// A flat solid/open tile mask over the whole floor: the one place any system asks
/// "is this point inside a wall?".
///
/// It exists because two systems were solving the same problem by not solving it. The
/// player collides against real <c>StaticBody2D</c> wall rects, but bullets are simulated
/// by hand in <see cref="Bullets.BulletManager"/> and enemies are ticked by hand in
/// <see cref="Enemies.EnemyManager"/> — neither touches Godot's physics server, so neither
/// saw a wall. Both flew straight through. Handing each of them its own copy of the
/// geometry would have produced two subtly different answers at the seams; there is one
/// mask and one set of queries.
///
/// Flat <c>bool[]</c> rather than a callback or an interface: this is read up to 4096 times
/// per tick from the bullet loop, which must not allocate and must not pay for virtual
/// dispatch (docs/09 §3).
///
/// **Out of range is SOLID.** The mask covers the generated floor plus a small margin;
/// everything past that edge is rock, and treating it as open would let bullets and enemies
/// escape into the void rather than stopping at the boundary.
/// </summary>
public sealed class TileMask
{
    public readonly int Width;
    public readonly int Height;
    public readonly float TileSize;

    /// <summary>World position of the top-left corner of tile (0, 0).</summary>
    public readonly Vector2 Origin;

    private readonly bool[] _solid;

    // Precomputed, because IsSolid runs once per bullet per tick and a divide is not free
    // at 4096 of them. Same reason the class holds a flat array rather than a callback.
    private readonly float _invTileSize;
    private readonly float _halfTile;

    public TileMask(int width, int height, float tileSize, Vector2 origin)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        Origin = origin;
        _invTileSize = 1f / tileSize;
        _halfTile = tileSize * 0.5f;
        _solid = new bool[width * height];
    }

    public void SetSolid(int tx, int ty, bool solid)
    {
        if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return;
        _solid[ty * Width + tx] = solid;
    }

    /// <summary>
    /// Close or open every tile overlapping a world rect. This is how a sealed door becomes
    /// a wall.
    ///
    /// It has to exist because the mask was built ONCE from the static geometry, and door
    /// seals are dynamic — they are <c>StaticBody2D</c> nodes, which the player collides
    /// with because the player is a CharacterBody2D, and which bullets and enemies do not
    /// see at all because both simulate their own movement. So a sealed door was a wall to
    /// the player and open air to everything else, and enemies walked out of contested rooms
    /// through doors the player could not follow them through.
    ///
    /// Exactly the same failure as bullets passing through walls, one layer up: the fix
    /// there was to give the hand-simulated systems one shared mask, and the fix here is to
    /// let the dynamic half of the geometry into it.
    /// </summary>
    public void SetSolidWorldRect(Rect2 rect, bool solid)
    {
        int minTx = TileX(rect.Position.X);
        int minTy = TileY(rect.Position.Y);
        // Inclusive of the last tile the rect touches, exclusive of one it merely abuts:
        // doorway rects are tile-aligned, so a bare TileX of the far edge would close an
        // extra row of tiles beyond the door.
        int maxTx = TileX(rect.Position.X + rect.Size.X - 0.001f);
        int maxTy = TileY(rect.Position.Y + rect.Size.Y - 0.001f);

        for (int ty = minTy; ty <= maxTy; ty++)
            for (int tx = minTx; tx <= maxTx; tx++)
                SetSolid(tx, ty, solid);
    }

    public bool IsSolidTile(int tx, int ty) =>
        tx < 0 || ty < 0 || tx >= Width || ty >= Height || _solid[ty * Width + tx];

    public int TileX(float worldX) => FloorToInt((worldX - Origin.X) * _invTileSize);
    public int TileY(float worldY) => FloorToInt((worldY - Origin.Y) * _invTileSize);

    /// <summary>
    /// Point test. THE hot query — it runs for every live bullet every tick, so it is two
    /// multiplies, two truncations and one array read, and nothing else.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSolid(float worldX, float worldY)
    {
        int tx = FloorToInt((worldX - Origin.X) * _invTileSize);
        int ty = FloorToInt((worldY - Origin.Y) * _invTileSize);
        return tx < 0 || ty < 0 || tx >= Width || ty >= Height || _solid[ty * Width + tx];
    }

    /// <summary>
    /// Floor, without the call into <c>Math.Floor</c>. Truncation toward zero is wrong on
    /// the negative side — the floor's origin is usually at negative world coordinates, so
    /// that is not a rare case here — and one compare fixes it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorToInt(float v)
    {
        int i = (int)v;
        return v < i ? i - 1 : i;
    }

    /// <summary>
    /// Did a bullet's movement this tick cross solid ground?
    ///
    /// Sub-sampled rather than a bare endpoint test, because the endpoint alone tunnels.
    /// The thinnest wall on a generated floor is two tiles (each room is inset by one, so
    /// flush rooms have a 32px partition), and a fast projectile covers more than that in a
    /// single 60Hz step — it would begin the tick in one room, end it in the next, and
    /// never sample a solid tile in between. Stepping at half a tile makes the test
    /// independent of bullet speed, and costs nothing for the slow majority because the
    /// sample count is derived from the actual step length.
    /// </summary>
    public bool SegmentHitsSolid(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float lenSq = dx * dx + dy * dy;

        // Short step — the overwhelming majority — resolves to a single point test with no
        // square root at all. The sub-sampling below only pays for itself on projectiles
        // fast enough to skip a wall, and only those pay for it.
        if (lenSq <= _halfTile * _halfTile) return IsSolid(x1, y1);

        int steps = Mathf.CeilToInt(Mathf.Sqrt(lenSq) / _halfTile);
        float inv = 1f / steps;
        for (int s = 1; s <= steps; s++)
        {
            float t = s * inv;
            if (IsSolid(x0 + dx * t, y0 + dy * t)) return true;
        }
        return false;
    }

    /// <summary>Does a body of <paramref name="radius"/> centred here overlap solid ground?</summary>
    public bool CircleOverlaps(float x, float y, float radius)
    {
        int minTx = TileX(x - radius), maxTx = TileX(x + radius);
        int minTy = TileY(y - radius), maxTy = TileY(y + radius);
        float r2 = radius * radius;

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (!IsSolidTile(tx, ty)) continue;

                // Nearest point on the tile to the circle centre.
                float left = Origin.X + tx * TileSize;
                float top = Origin.Y + ty * TileSize;
                float nx = Mathf.Clamp(x, left, left + TileSize);
                float ny = Mathf.Clamp(y, top, top + TileSize);

                float ddx = x - nx, ddy = y - ny;
                if (ddx * ddx + ddy * ddy < r2) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Slide a circular body by <paramref name="delta"/>, cancelling whichever axis would
    /// put it inside a wall.
    ///
    /// Axis-separated on purpose: resolving the move as a single vector makes a body that
    /// runs into a wall at an angle stop dead, which reads as the enemy being stuck on
    /// nothing. Cancelling only the blocked component lets it slide along the surface,
    /// which is what Godot's own <c>MoveAndSlide</c> does for the player and is why the
    /// player has never had this problem.
    /// </summary>
    public Vector2 MoveCircle(Vector2 from, Vector2 delta, float radius)
    {
        float x = from.X + delta.X;
        if (CircleOverlaps(x, from.Y, radius)) x = from.X;

        float y = from.Y + delta.Y;
        if (CircleOverlaps(x, y, radius)) y = from.Y;

        return new Vector2(x, y);
    }

    /// <summary>
    /// Nearest open position to <paramref name="p"/>, searched outward in rings.
    ///
    /// Used when placing things — spawn points, props, pickups — where a caller has picked
    /// a position by area rather than by walkability. Returns the input unchanged when it
    /// is already clear, and gives up after <paramref name="maxRings"/> rather than
    /// searching the whole floor.
    /// </summary>
    public Vector2 NearestOpen(Vector2 p, float radius, int maxRings = 6)
    {
        if (!CircleOverlaps(p.X, p.Y, radius)) return p;

        for (int ring = 1; ring <= maxRings; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    // Perimeter of the ring only; the interior was covered by earlier rings.
                    if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue;

                    var c = new Vector2(p.X + dx * TileSize, p.Y + dy * TileSize);
                    if (!CircleOverlaps(c.X, c.Y, radius)) return c;
                }
            }
        }
        return p;
    }
}
