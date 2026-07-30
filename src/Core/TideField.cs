using System.Runtime.CompilerServices;
using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// Where the water can reach: a per-tile flood level over the whole floor, read together with
/// a <see cref="TideCycle"/> to answer "is this point underwater right now?".
///
/// The split is deliberate. <see cref="TideCycle"/> is WHEN and this is WHERE, and neither
/// knows about the other — so the floor has exactly one clock and the geometry is static,
/// which is what makes docs/07 §3's "synchronised across the floor" true by construction
/// rather than by everybody remembering to use the same number.
///
/// Deliberately a sibling of <see cref="TileMask"/> rather than a second bit inside it.
/// Solidity and wetness are queried by different systems at different rates and mean
/// different things — a wall is a hard constraint on movement, water is a modifier of it —
/// and folding them together invites the bug where something checks the wrong plane and
/// walks through a pier. They share tile geometry, and <see cref="FromMask"/> exists so the
/// two grids cannot be built with mismatched origins.
///
/// Flat <c>byte[]</c> for the same reason TileMask is a flat <c>bool[]</c>: this is read per
/// entity per tick and must not allocate.
///
/// **Out of range is DRY.** The opposite convention to TileMask, and correct: out of range is
/// rock, and rock is not underwater. Treating it as flooded would drench anything standing in
/// a doorway at the floor's edge.
/// </summary>
public sealed class TideField
{
    /// <summary>The number of distinct flood levels an author can use. 1 floods at the
    /// faintest tide, <see cref="MaxFloodLevel"/> only at the peak. Small on purpose — this
    /// is a rhythm the player has to read at a glance, and eight indistinguishable shades of
    /// wet is not readable.</summary>
    public const int MaxFloodLevel = 4;

    public readonly int Width;
    public readonly int Height;
    public readonly float TileSize;
    public readonly Vector2 Origin;

    /// <summary>0 = never floods. 1..MaxFloodLevel = the level at which it does.</summary>
    private readonly byte[] _flood;

    private readonly float _invTileSize;

    /// <summary>False when no tile on the floor holds water, which is every floor but the
    /// Wharfs. Lets the whole system be skipped rather than ticked to no effect.</summary>
    public bool AnyWater { get; private set; }

    public TideField(int width, int height, float tileSize, Vector2 origin)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        Origin = origin;
        _invTileSize = 1f / tileSize;
        _flood = new byte[width * height];
    }

    /// <summary>Build a field matching an existing mask's grid exactly. The only intended
    /// way to make one — a water grid whose origin is a tile off from the wall grid produces
    /// a floor where the shoreline and the pier disagree, and nothing would report it.</summary>
    public static TideField FromMask(TileMask mask) =>
        new(mask.Width, mask.Height, mask.TileSize, mask.Origin);

    public int TileX(float worldX) => Mathf.FloorToInt((worldX - Origin.X) * _invTileSize);
    public int TileY(float worldY) => Mathf.FloorToInt((worldY - Origin.Y) * _invTileSize);

    public void SetFlood(int tx, int ty, int level)
    {
        if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return;
        byte v = (byte)Mathf.Clamp(level, 0, MaxFloodLevel);
        _flood[ty * Width + tx] = v;
        if (v > 0) AnyWater = true;
    }

    /// <summary>Flood a world-space rect. Mirrors TileMask.SetSolidWorldRect so authored
    /// water and authored obstacles are carved by the same kind of call.</summary>
    public void SetFloodWorldRect(Rect2 rect, int level)
    {
        int x0 = TileX(rect.Position.X);
        int y0 = TileY(rect.Position.Y);
        int x1 = TileX(rect.Position.X + rect.Size.X - 0.001f);
        int y1 = TileY(rect.Position.Y + rect.Size.Y - 0.001f);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                SetFlood(x, y, level);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int FloodLevelAt(int tx, int ty) =>
        tx < 0 || ty < 0 || tx >= Width || ty >= Height ? 0 : _flood[ty * Width + tx];

    /// <summary>
    /// Is this tile under water at the given tide level?
    ///
    /// A tile floods when the tide reaches its level, so flood level 1 is underwater for most
    /// of the cycle and <see cref="MaxFloodLevel"/> only around the peak. That ordering is
    /// what produces a tide LINE that sweeps rather than a whole room blinking wet.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSubmergedTile(int tx, int ty, float tideLevel)
    {
        int flood = FloodLevelAt(tx, ty);
        return flood > 0 && tideLevel >= flood / (float)MaxFloodLevel;
    }

    public bool IsSubmerged(float worldX, float worldY, float tideLevel) =>
        AnyWater && IsSubmergedTile(TileX(worldX), TileY(worldY), tideLevel);

    public bool IsSubmerged(Vector2 world, float tideLevel) =>
        IsSubmerged(world.X, world.Y, tideLevel);

    /// <summary>Water tiles regardless of the tide — the dry seabed included. What the
    /// renderer needs to draw a channel that is visibly a channel even when empty.</summary>
    public bool IsWaterTile(int tx, int ty) => FloodLevelAt(tx, ty) > 0;
}
