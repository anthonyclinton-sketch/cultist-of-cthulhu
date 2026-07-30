using System.Collections.Generic;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// Floor 2 — the Drowned Wharfs of Innsmouth (docs/07 §3).
///
/// ONE ROOM, and it is here to answer a question rather than to start a floor. The tide's
/// renderer has been verified only against `--flood-demo`, which floods every room with an
/// identical horizontal band — and that uniformity has now hidden two separate bugs, most
/// recently a waterline that only drew in one room per floor and looked correct because every
/// room looked the same. Authored Wharf water is channels and margins (docs/07 §3), so the
/// first honest test of the waterline is a channel that is not a band.
///
/// It also measures something the room-template pipeline argument has been guessing at. All
/// 32 existing templates are rectangles with rectangular obstacle blocks; docs/11 puts a
/// hand-built TileMap pipeline on the critical path at ~4 rooms/day and it does not exist. The
/// open question is whether the rectangle system can express a Wharf room at all. This is that
/// question asked in the form of a room.
///
/// NOT wired into the generator's template pool by floor yet. `RoomTemplate.FloorTag` is
/// authored on all 32 Undercroft templates and read by nothing — template selection filters
/// only on `MinFloor > floorIndex`, a lower bound — so floor 2 currently builds itself out of
/// Undercroft rooms. That is the next piece of work and it is deliberately not smuggled in
/// here. MinFloor 2 at least keeps this room off floor 1.
/// </summary>
public static class WharfContent
{
    public static List<RoomTemplate> Rooms()
    {
        var list = new List<RoomTemplate>();

        // THE CHANNEL, painted outermost band first so the inner bands overwrite it.
        //
        // Flood level 1 floods at the faintest tide and 4 only at the peak, so level 1 is the
        // channel's DEEPEST line and 4 is the last margin to go under. Nesting four L-shaped
        // rects gives a shoreline that creeps outward along a bend — which is precisely the
        // shape the old "top row of the band" waterline could not draw, and the reason this
        // room exists.
        //
        // Order matters and it is the same rule the obstacle carve follows: later blocks win.
        int[] channel =
        {
            // level 4 — the last margin to flood, and the widest.
            24,  2, 14, 22, 4,
            24, 11, 34, 15, 4,
            // level 3
            25,  3, 12, 20, 3,
            25, 12, 32, 13, 3,
            // level 2
            26,  4, 10, 18, 2,
            26, 13, 30, 11, 2,
            // level 1 — the deep line, underwater for most of the cycle.
            27,  5,  8, 16, 1,
            27, 14, 28,  9, 1,
        };

        // Pilings. Solid, so they are never water — the renderer skips unwalkable tiles — and
        // they are the only dry footing inside the bend at high tide. Cover that disappears
        // is not cover; cover that STAYS while the ground around it floods is the whole
        // reason to build a room around a channel.
        int[] pilings =
        {
            30,  8, 2, 2,
            33, 17, 2, 2,
            44, 16, 2, 2,
            50, 19, 2, 2,
            // Dry-side cover, so the room is not "the channel and nothing else".
             8,  9, 4, 3,
             8, 23, 4, 3,
            52,  5, 3, 4,
        };

        list.Add(new RoomTemplate
        {
            Id = "wharf_tide_bend",
            Role = RoomRole.CombatMed,
            FloorTag = "wharfs",
            MinFloor = 2,
            WidthTiles = 62,
            HeightTiles = 34,
            NorthExits = new[] { 18, 46 },
            SouthExits = new[] { 16, 42 },
            EastExits = new[] { 16 },
            WestExits = new[] { 11 },
            ThreatCapacity = 312f,
            Obstacles = pilings,
            Water = channel,
        });

        return list;
    }
}
