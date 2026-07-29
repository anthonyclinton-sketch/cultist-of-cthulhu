using Godot;

namespace CultistOfCthulhu.Enemies;

/// <summary>
/// A single BFS flow field over a coarse grid, shared by every enemy in the room
/// (docs/05 §8).
///
/// Why one field instead of per-agent A*: 60 agents each running A* is 60 searches per
/// repath; one BFS from the player produces a direction for EVERY cell at once, so all 60
/// agents read it for free. Cost is O(cells), independent of agent count. This is the
/// standard answer for many-agents-one-target and it is why the enemy budget can be 60
/// without pathing showing up in the frame time at all.
///
/// Regenerated on a timer rather than every tick — the player moves ~1.5px per tick, far
/// less than a cell, so a stale field is indistinguishable from a fresh one.
/// </summary>
public sealed class FlowField
{
    /// <summary>
    /// Raised from 24 to 40 when rooms were scaled up.
    ///
    /// The field covers the WHOLE floor, so its cost is quadratic in floor size: at 24px
    /// cells a 450-tile floor is 7200px across = 300x300 = 90,000 cells to BFS every
    /// repath. At 40px that falls to ~32,000 — and the coarser resolution costs nothing in
    /// practice because Enemy.MoveTowardPreferredRange already falls back to a direct
    /// vector inside the last stride, where cell granularity would otherwise show as jitter.
    /// </summary>
    public const int CellSize = 40;

    private readonly int _w, _h;
    private readonly Vector2 _origin;
    private readonly bool[] _blocked;
    private readonly int[] _dist;
    private readonly Vector2[] _dir;

    // Preallocated BFS queue — this runs several times a second and must not allocate.
    private readonly int[] _queue;
    private int _qHead, _qTail;

    public FlowField(Rect2 bounds)
    {
        _origin = bounds.Position;
        _w = Mathf.Max(1, Mathf.CeilToInt(bounds.Size.X / CellSize));
        _h = Mathf.Max(1, Mathf.CeilToInt(bounds.Size.Y / CellSize));

        int n = _w * _h;
        _blocked = new bool[n];
        _dist = new int[n];
        _dir = new Vector2[n];
        _queue = new int[n];
    }

    public void SetBlocked(int cx, int cy, bool blocked)
    {
        if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) return;
        _blocked[cy * _w + cx] = blocked;
    }

    /// <summary>
    /// Mark cells solid from the floor's tile mask. Until this was called the field had no
    /// obstacles at all — <see cref="SetBlocked"/> existed and nothing ever invoked it, so
    /// every enemy steered on a straight line to the player through whatever was in the way.
    ///
    /// A cell is blocked when its CENTRE is solid, which is the ordinary way to rasterise a
    /// fine grid onto a coarse one. The two alternatives are both worse here: "blocked if
    /// any tile is solid" closes 48px doorways against 40px cells, and "blocked only if
    /// every sample is solid" leaves the 32px partition between two flush rooms open, so the
    /// field would route enemies at a wall and leave hard collision to explain why they
    /// cannot get there.
    ///
    /// Cell-resolution error is bounded by half a cell and is absorbed by the collision
    /// resolution in <see cref="Core.TileMask.MoveCircle"/>; the field is steering, not the
    /// guarantee.
    /// </summary>
    public void ApplyMask(Core.TileMask mask)
    {
        for (int cy = 0; cy < _h; cy++)
        {
            for (int cx = 0; cx < _w; cx++)
            {
                float wx = _origin.X + (cx + 0.5f) * CellSize;
                float wy = _origin.Y + (cy + 0.5f) * CellSize;
                _blocked[cy * _w + cx] = mask.IsSolid(wx, wy);
            }
        }
    }

    private (int cx, int cy) ToCell(Vector2 world) => (
        Mathf.Clamp(Mathf.FloorToInt((world.X - _origin.X) / CellSize), 0, _w - 1),
        Mathf.Clamp(Mathf.FloorToInt((world.Y - _origin.Y) / CellSize), 0, _h - 1));

    /// <summary>Rebuild toward `target`. Allocation-free.</summary>
    public void Rebuild(Vector2 target)
    {
        int n = _w * _h;
        for (int i = 0; i < n; i++) _dist[i] = int.MaxValue;

        var (tx, ty) = ToCell(target);
        int start = ty * _w + tx;

        _qHead = _qTail = 0;
        _dist[start] = 0;
        _queue[_qTail++] = start;

        // 4-connected BFS. Diagonal movement is handled by the steering layer, which
        // interpolates between neighbouring cells' directions — an 8-connected field
        // produces visible diagonal bias on open ground.
        while (_qHead < _qTail)
        {
            int cur = _queue[_qHead++];
            int cx = cur % _w, cy = cur / _w;
            int d = _dist[cur] + 1;

            TryVisit(cx - 1, cy, d);
            TryVisit(cx + 1, cy, d);
            TryVisit(cx, cy - 1, d);
            TryVisit(cx, cy + 1, d);
        }

        // Derive a direction per cell: downhill toward the lowest-distance neighbour.
        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                if (_blocked[i] || _dist[i] == int.MaxValue) { _dir[i] = Vector2.Zero; continue; }

                int best = _dist[i];
                Vector2 bestDir = Vector2.Zero;
                Consider(x - 1, y, ref best, ref bestDir, Vector2.Left);
                Consider(x + 1, y, ref best, ref bestDir, Vector2.Right);
                Consider(x, y - 1, ref best, ref bestDir, Vector2.Up);
                Consider(x, y + 1, ref best, ref bestDir, Vector2.Down);
                _dir[i] = bestDir;
            }
        }
    }

    private void TryVisit(int cx, int cy, int d)
    {
        if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) return;
        int i = cy * _w + cx;
        if (_blocked[i] || _dist[i] <= d) return;
        _dist[i] = d;
        _queue[_qTail++] = i;
    }

    private void Consider(int cx, int cy, ref int best, ref Vector2 bestDir, Vector2 dir)
    {
        if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) return;
        int i = cy * _w + cx;
        if (_blocked[i] || _dist[i] >= best) return;
        best = _dist[i];
        bestDir = dir;
    }

    /// <summary>Direction toward the target from a world position. Zero if unreachable.</summary>
    public Vector2 Sample(Vector2 world)
    {
        var (cx, cy) = ToCell(world);
        return _dir[cy * _w + cx];
    }

    /// <summary>Grid distance in cells. Used to decide whether a Turret has line of sight.</summary>
    public int SampleDistance(Vector2 world)
    {
        var (cx, cy) = ToCell(world);
        return _dist[cy * _w + cx];
    }
}
