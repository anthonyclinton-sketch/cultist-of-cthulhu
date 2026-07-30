using System.Collections.Generic;
using CultistOfCthulhu.Generation;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.UI;

/// <summary>
/// Top-right minimap (docs/10 §3). Hold TAB to enlarge.
///
/// Only rooms the player has VISITED are drawn solid; the rest are outlines. Showing the
/// whole floor immediately would remove the route choice the flow generator exists to
/// create — the point of a loop is deciding which way round it to go, and that decision
/// requires not already knowing.
/// </summary>
public sealed partial class Minimap : Node2D
{
    public GeneratedFloor Floor = null!;
    public PlayerController Player = null!;
    public HashSet<int> Cleared = null!;

    /// <summary>The current room's occupants. Rebuilt per floor, so assigned per floor.</summary>
    public Enemies.EnemyManager? Enemies;

    /// <summary>
    /// Set while an encounter is running. Enemy dots appear only for the contested room
    /// and only until it is cleared.
    ///
    /// Gated on the ENCOUNTER rather than on "are any enemies alive", even though only the
    /// current room ever holds any today. The two are the same thing right now and will
    /// stop being the same thing the moment anything survives a room — and the version
    /// where a stale dot lingers on a cleared room is worse than no dots, because the
    /// player walks back to check.
    /// </summary>
    public bool ShowEnemies;

    private readonly HashSet<int> _seen = new();

    /// <summary>
    /// Set by the Ledger Stone shrine (docs/08 §5): every room is drawn as if visited.
    ///
    /// This is what the shrine SELLS. It charges 15 Sanity and its entire stated reward is
    /// "reveals the full floor map" — a shrine whose payment produces no visible effect is
    /// the exact phantom feature docs/AUDIT exists to catch, and it would be indelible in a
    /// playtest as "the shrine did nothing".
    /// </summary>
    public bool Revealed;

    private static readonly Color Unvisited = new(0.35f, 0.38f, 0.45f, 0.55f);
    private static readonly Color Visited = new(0.55f, 0.60f, 0.70f, 0.85f);
    private static readonly Color ClearedCol = new(0.35f, 0.55f, 0.50f, 0.85f);
    private static readonly Color PlayerCol = new("FFB347");

    /// <summary>The same red the enemy health bars use, so "red mark = the thing hurting
    /// you" means one thing across the whole UI.</summary>
    private static readonly Color EnemyCol = new("D64545");

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Floor is null || Player is null) return;

        bool big = Input.IsActionPressed("map");
        float boxSize = big ? 300f : 130f;
        var origin = new Vector2(big ? 320f - boxSize * 0.5f : 640f - boxSize - 8f, big ? 30f : 8f);

        Rect2I b = Floor.Bounds();
        float scale = Mathf.Min(boxSize / Mathf.Max(1, b.Size.X), boxSize / Mathf.Max(1, b.Size.Y));

        DrawRect(new Rect2(origin - new Vector2(4, 4), new Vector2(boxSize + 8, boxSize + 8)),
                 new Color(0, 0, 0, 0.55f));

        Vector2 playerTile = Player.GlobalPosition / Rooms.FloorGeometry.Tile;

        foreach (PlacedRoom r in Floor.Rooms)
        {
            var world = new Rect2(r.Position.X, r.Position.Y, r.Width, r.Height);
            if (world.HasPoint(playerTile)) _seen.Add(r.NodeId);

            var box = new Rect2(
                origin + new Vector2((r.Position.X - b.Position.X) * scale, (r.Position.Y - b.Position.Y) * scale),
                new Vector2(r.Width * scale, r.Height * scale));

            bool seen = Revealed || _seen.Contains(r.NodeId);
            if (!seen) { DrawRect(box, Unvisited, filled: false, width: 1f); continue; }

            DrawRect(box, Cleared.Contains(r.NodeId) ? ClearedCol : Visited);

            // Only annotate rooms already found — a reward room visible from the entrance
            // is not a discovery.
            char glyph = r.Role switch
            {
                RoomRole.Reward => 'R', RoomRole.Shop => '$', RoomRole.Shrine => '!',
                RoomRole.Secret => '?', RoomRole.Boss => 'B', RoomRole.BossFoyer => 'F',
                RoomRole.Entrance => 'E', _ => ' ',
            };
            if (glyph != ' ' && big)
                DrawString(ThemeDB.FallbackFont, box.Position + box.Size * 0.5f + new Vector2(-3, 4),
                           glyph.ToString(), HorizontalAlignment.Left, -1, 11, new Color("14161C"));
        }

        DrawEnemies(origin, b, scale, big);

        // The player is drawn LAST so it is never buried under a pack. At two pixels a
        // warm dot and a red one are not that far apart, and the one the player needs to
        // find instantly is their own.
        Vector2 p = origin + new Vector2((playerTile.X - b.Position.X) * scale,
                                         (playerTile.Y - b.Position.Y) * scale);
        DrawCircle(p, big ? 4.5f : 3f, PlayerCol);
    }

    /// <summary>
    /// Live enemy positions in the contested room.
    ///
    /// NOT in docs/10 §3, which specifies the minimap as topology only — so this is a new
    /// affordance rather than an implementation of a written one, and it is recorded as
    /// such. What it changes is that a big room stops hiding its stragglers: rooms are now
    /// four to eight times their original area and several screens across, so "the door is
    /// still sealed and I cannot find the last acolyte" is a real failure state that the
    /// design never had to consider when a room fitted on one screen.
    ///
    /// It shows position only, never count, health or type. The player still has to look at
    /// the room to fight it.
    /// </summary>
    private void DrawEnemies(Vector2 origin, Rect2I b, float scale, bool big)
    {
        if (!ShowEnemies || Enemies is null) return;

        float r = big ? 3f : 2f;

        Vector2 ToMap(Vector2 world)
        {
            Vector2 tile = world / Rooms.FloorGeometry.Tile;
            return origin + new Vector2((tile.X - b.Position.X) * scale, (tile.Y - b.Position.Y) * scale);
        }

        foreach (Enemies.Enemy e in Enemies.Enemies)
        {
            if (!e.Alive) continue;
            DrawCircle(ToMap(e.Position), r, EnemyCol);
        }

        // The boss counts. It is the thing in the room that is trying to kill you, and
        // leaving the one mark the player most wants off a threat display would read as the
        // feature being broken. Larger, so it is distinguishable from its own adds.
        for (int i = 0; i < Enemies.Bosses.Count; i++)
        {
            Enemies.Boss boss = Enemies.Bosses[i];
            if (boss.Alive) DrawCircle(ToMap(boss.Position), r * 1.9f, EnemyCol);
        }
    }
}
