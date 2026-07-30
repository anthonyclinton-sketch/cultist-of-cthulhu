using System.Collections.Generic;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// Floor 2 — the Drowned Wharfs of Innsmouth (docs/07 §3).
///
/// THE COMBAT ROLES ONLY, and that is the point rather than a shortcut. docs/07 §3 sells this
/// floor on its tide, and a tide the player meets in one room out of thirteen is a set piece,
/// not a mechanic. Every combat room here has water in it, so the rhythm is something you
/// fight inside for the whole floor rather than a curiosity you walk past.
///
/// The non-combat roles — entrance, shop, shrine, reward, secret, foyer, boss — are still
/// Undercroft rooms, and <see cref="PickTemplate"/>'s fallback is what makes that work.
/// Authoring dry Wharf versions of those is content work with nothing to learn from it;
/// authoring the boss arena is part of Mother Hydra's fight, not part of this.
///
/// EVERY ROOM HAS A DIFFERENT WATER SHAPE, deliberately. The renderer has been fooled twice
/// by `--flood-demo`'s uniform bands, so a set of channels that were all the same shape would
/// be the same trap with better colours. A bend, two parallel runs, a centre pool, a crossing,
/// a corner and a full-width cut exercise the waterline from six different directions.
///
/// Water is five ints per block — x, y, w, h, flood level — painted OUTERMOST FIRST so the
/// inner bands overwrite. Level 1 is the deep line and floods at the faintest tide; level 4 is
/// the last margin to go under. Nesting them is what makes a shoreline creep rather than
/// blink.
/// </summary>
public static class WharfContent
{
    public const string Tag = "wharfs";

    public static List<RoomTemplate> Rooms()
    {
        var list = new List<RoomTemplate>();

        void Room(string id, RoomRole role, int w, int h, int[] n, int[] s, int[] e, int[] west,
                  float capacity, int[] water, int[]? obstacles = null)
        {
            list.Add(new RoomTemplate
            {
                Id = id, Role = role, WidthTiles = w, HeightTiles = h,
                NorthExits = n, SouthExits = s, EastExits = e, WestExits = west,
                ThreatCapacity = capacity, FloorTag = Tag, MinFloor = 2,
                Obstacles = obstacles ?? System.Array.Empty<int>(),
                Water = water,
            });
        }

        // ---- A BEND. The channel turns, so the shoreline turns with it.
        Room("wharf_tide_bend", RoomRole.CombatMed, 62, 34,
             new[] { 18, 46 }, new[] { 16, 42 }, new[] { 16 }, new[] { 11 }, 312f,
             water: new[]
             {
                 24,  2, 14, 22, 4,   24, 11, 34, 15, 4,
                 25,  3, 12, 20, 3,   25, 12, 32, 13, 3,
                 26,  4, 10, 18, 2,   26, 13, 30, 11, 2,
                 27,  5,  8, 16, 1,   27, 14, 28,  9, 1,
             },
             obstacles: new[] { 30, 8, 2, 2, 33, 17, 2, 2, 44, 16, 2, 2, 50, 19, 2, 2,
                                 8, 9, 4, 3,  8, 23, 4, 3, 52,  5, 3, 4 });

        // ---- TWO PARALLEL RUNS with a dry walkway between them. The player picks a side and
        // the tide decides whether that was a route or a trap.
        Room("wharf_pier_ends", RoomRole.CombatEasy, 52, 30,
             new[] { 14, 38 }, new[] { 12, 40 }, new[] { 14 }, new[] { 16 }, 200f,
             water: new[]
             {
                  7,  3, 13, 23, 4,   33,  3, 13, 23, 4,
                  8,  4, 11, 21, 3,   34,  4, 11, 21, 3,
                  9,  5,  9, 19, 2,   35,  5,  9, 19, 2,
                 10,  6,  7, 17, 1,   36,  6,  7, 17, 1,
             },
             obstacles: new[] { 14, 12, 2, 2, 40, 16, 2, 2, 24, 8, 3, 3, 26, 20, 3, 3 });

        // ---- A CENTRE POOL. Concentric, so the shoreline is a closed ring that shrinks —
        // the one shape where the water surrounds you rather than crossing your path.
        Room("wharf_sunken_yard", RoomRole.CombatMed, 58, 36,
             new[] { 16, 42 }, new[] { 20, 38 }, new[] { 18 }, new[] { 13 }, 300f,
             water: new[]
             {
                 14,  6, 30, 24, 4,
                 17,  8, 24, 20, 3,
                 20, 10, 18, 16, 2,
                 23, 12, 12, 12, 1,
             },
             obstacles: new[] { 28, 17, 3, 3, 8, 5, 3, 3, 47, 5, 3, 3, 8, 28, 3, 3, 47, 28, 3, 3 });

        // ---- A FULL-WIDTH CUT with pilings as stepping stones. At high tide the only dry
        // footing is four blocks, which is the hardest thing this floor asks of positioning.
        Room("wharf_cannery", RoomRole.CombatHard, 76, 44,
             new[] { 22, 54 }, new[] { 18, 50 }, new[] { 20 }, new[] { 24 }, 470f,
             water: new[]
             {
                  3, 12, 70, 20, 4,
                  3, 14, 70, 16, 3,
                  3, 16, 70, 12, 2,
                  3, 19, 70,  6, 1,
             },
             obstacles: new[] { 12, 20, 3, 3, 24, 18, 3, 3, 36, 22, 3, 3, 48, 18, 3, 3, 60, 21, 3, 3,
                                16,  5, 4, 4, 56,  5, 4, 4, 16, 35, 4, 4, 56, 35, 4, 4 });

        // ---- A CROSSING. Two arms meeting, so at low tide it is four routes and at high tide
        // it is one island. The biggest read the floor asks for.
        Room("wharf_slipway", RoomRole.Hub, 72, 52,
             new[] { 20, 36, 52 }, new[] { 18, 36, 54 }, new[] { 16, 38 }, new[] { 14, 36 }, 300f,
             water: new[]
             {
                 26,  4, 20, 44, 4,    6, 18, 60, 16, 4,
                 29,  6, 14, 40, 3,    8, 21, 56, 10, 3,
                 32,  8,  8, 36, 2,   10, 23, 52,  6, 2,
                 34, 10,  4, 32, 1,   12, 24, 48,  4, 1,
             },
             obstacles: new[] { 18, 10, 3, 3, 52, 10, 3, 3, 18, 42, 3, 3, 52, 42, 3, 3 });

        // ---- A CORNER. Water banked into one end, so most of the room is dry and the fight
        // drifts toward the wet part as the tide falls.
        Room("wharf_low_dock", RoomRole.CombatEasy, 44, 26,
             new[] { 12, 32 }, new[] { 16, 34 }, new[] { 12 }, new[] { 10 }, 190f,
             water: new[]
             {
                 20,  8, 22, 16, 4,
                 23, 10, 19, 14, 3,
                 26, 12, 16, 12, 2,
                 29, 14, 13, 10, 1,
             },
             obstacles: new[] { 8, 6, 4, 3, 10, 16, 3, 4, 32, 18, 2, 2 });

        // ---- A GANTRY. Connector, water down one side only — a rest beat that still tells
        // you the tide is running.
        Room("wharf_gantry", RoomRole.Connector, 48, 18,
             new[] { 14 }, new[] { 30 }, new[] { 8 }, new[] { 9 }, 0f,
             water: new[]
             {
                  3,  8, 42,  8, 4,
                  3, 10, 42,  6, 3,
                  3, 11, 42,  5, 2,
                  3, 13, 42,  3, 1,
             });

        return list;
    }
}
