using Godot;

namespace CultistOfCthulhu.Sigils;

/// <summary>
/// docs/04 §3.1 — the polyomino vocabulary. Seven shapes, 1–5 cells, and deliberately no
/// more: the Circle is a puzzle the player has to be able to hold in their head, and a
/// shape they have never seen before is a shape they cannot plan around.
/// </summary>
public enum SigilShapeKind
{
    Mote,       // 1 cell
    Bar,        // 2
    Angle,      // 3, an L
    Slab,       // 4, a square
    Tee,        // 4
    Serpent,    // 4, an S
    Cross,      // 5
}

/// <summary>
/// Shape geometry and the two fitting verbs — rotation and mirroring.
///
/// Cells are stored as offsets from the shape's top-left, normalised after every
/// transform so that a rotated shape can be placed by the same "origin + cells" rule as an
/// unrotated one. Without normalisation a rotation drags the piece across the grid, which
/// is the classic Tetris-rotation bug and reads to the player as the shape jumping out
/// from under the cursor.
///
/// docs/04 §3.1 calls rotation "the main fitting verb", so it is free, unlimited and
/// applies to every shape — including the symmetric ones, where it does nothing and costs
/// nothing to allow.
/// </summary>
public static class SigilShape
{
    private static readonly Vector2I[] Mote = { new(0, 0) };
    private static readonly Vector2I[] Bar = { new(0, 0), new(1, 0) };
    private static readonly Vector2I[] Angle = { new(0, 0), new(0, 1), new(1, 1) };
    private static readonly Vector2I[] Slab = { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
    private static readonly Vector2I[] Tee = { new(0, 0), new(1, 0), new(2, 0), new(1, 1) };
    private static readonly Vector2I[] Serpent = { new(0, 0), new(1, 0), new(1, 1), new(2, 1) };
    private static readonly Vector2I[] Cross = { new(1, 0), new(0, 1), new(1, 1), new(2, 1), new(1, 2) };

    public static Vector2I[] BaseCells(SigilShapeKind kind) => kind switch
    {
        SigilShapeKind.Mote => Mote,
        SigilShapeKind.Bar => Bar,
        SigilShapeKind.Angle => Angle,
        SigilShapeKind.Slab => Slab,
        SigilShapeKind.Tee => Tee,
        SigilShapeKind.Serpent => Serpent,
        _ => Cross,
    };

    public static int CellCount(SigilShapeKind kind) => BaseCells(kind).Length;

    /// <summary>
    /// The shape's cells after <paramref name="rotation"/> quarter-turns and an optional
    /// mirror, normalised so the minimum X and Y are both zero.
    ///
    /// Allocates a fresh array per call. That is fine here and nowhere near the bullet
    /// loop: this runs when the player drags a tile in a paused screen, not per tick.
    /// </summary>
    public static Vector2I[] Cells(SigilShapeKind kind, int rotation, bool mirrored)
    {
        Vector2I[] src = BaseCells(kind);
        var outCells = new Vector2I[src.Length];

        rotation = ((rotation % 4) + 4) % 4;

        int minX = int.MaxValue, minY = int.MaxValue;
        for (int i = 0; i < src.Length; i++)
        {
            int x = src[i].X;
            int y = src[i].Y;

            if (mirrored) x = -x;

            // (x, y) -> (-y, x) per quarter-turn.
            for (int r = 0; r < rotation; r++)
            {
                (x, y) = (-y, x);
            }

            outCells[i] = new Vector2I(x, y);
            if (x < minX) minX = x;
            if (y < minY) minY = y;
        }

        for (int i = 0; i < outCells.Length; i++)
        {
            outCells[i] = new Vector2I(outCells[i].X - minX, outCells[i].Y - minY);
        }
        return outCells;
    }

    /// <summary>
    /// Where a directional sigil points after the same transform (docs/04 §3.2). The
    /// facing is part of the tile, so it has to rotate with it — a rear-facing Watcher's
    /// Eye that keeps pointing right when the tile is turned is the exact thing the
    /// "ghost overlay on the character portrait" requirement exists to prevent.
    /// </summary>
    public static Vector2I Facing(int rotation, bool mirrored)
    {
        int x = mirrored ? -1 : 1;
        int y = 0;
        rotation = ((rotation % 4) + 4) % 4;
        for (int r = 0; r < rotation; r++) (x, y) = (-y, x);
        return new Vector2I(x, y);
    }
}
