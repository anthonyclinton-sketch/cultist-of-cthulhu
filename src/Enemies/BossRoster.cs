using System.Collections.Generic;
using Godot;

namespace CultistOfCthulhu.Enemies;

/// <summary>
/// Which boss (or bosses) a floor ends with — docs/05 §7.
///
/// It exists because the boss was loaded by a hardcoded path in
/// <see cref="Rooms.FloorRunner"/>, so every floor of a descent fought The Thing on the
/// Doorstep again. That was invisible while one floor existed and immediately obvious the
/// moment somebody played floor 2 and met the floor 1 boss in a Wharf.
///
/// The same shape as <see cref="Bestiary"/> and <see cref="Generation.RoomLibrary"/>, and for
/// the same reason: content selected by floor belongs in one table that knows the mapping,
/// not in a path literal at the point of use.
///
/// Returns a LIST, because floor 2 ends with two — Mother Hydra and her consort, fought at
/// once with the tide deciding which is vulnerable.
/// </summary>
public static class BossRoster
{
    private static readonly string[][] ByFloor =
    {
        System.Array.Empty<string>(),                          // index 0 — unused
        new[] { "res://data/bosses/thing_on_the_doorstep.tres" },
        new[]
        {
            "res://data/bosses/mother_hydra.tres",
            "res://data/bosses/hydras_consort.tres",
        },
    };

    /// <summary>
    /// The bosses for a floor. Floors past the authored end fall back to floor 1's, so a
    /// three-floor test run still reaches a boss it can kill and the descent still ends —
    /// returning nothing would leave the player in a sealed room with no way out, which is
    /// a worse failure than the wrong boss.
    /// </summary>
    public static List<BossData> ForFloor(int floor)
    {
        string[] paths = floor >= 0 && floor < ByFloor.Length && ByFloor[floor].Length > 0
            ? ByFloor[floor]
            : ByFloor[1];

        var list = new List<BossData>(paths.Length);
        foreach (string path in paths)
        {
            var data = GD.Load<BossData>(path);
            if (data is not null) list.Add(data);
            else GD.PrintErr($"[BossRoster] failed to load {path}");
        }
        return list;
    }

    /// <summary>Every authored boss, for the content gate. A boss nothing validates is a
    /// boss whose patterns are checked the first time a player meets it.</summary>
    public static List<BossData> All()
    {
        var list = new List<BossData>();
        var seen = new HashSet<string>();
        foreach (string[] floor in ByFloor)
            foreach (string path in floor)
            {
                if (!seen.Add(path)) continue;
                var data = GD.Load<BossData>(path);
                if (data is not null) list.Add(data);
            }
        return list;
    }
}
