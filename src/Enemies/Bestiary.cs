using System.Collections.Generic;
using Godot;

namespace CultistOfCthulhu.Enemies;

/// <summary>
/// Every enemy in the game, and which floors each belongs to.
///
/// It exists because the roster was a hardcoded array of five paths copy-pasted into
/// <see cref="Rooms.FloorRunner"/> and <see cref="Rooms.CombatArena"/>, with no floor filter
/// in either. That was fine while one floor of content existed and stops being fine the
/// moment a second floor's enemies are authored: a Deep One's whole identity is that it
/// swims, and dropping it into the waterless Undercroft makes it a Cellar Ghoul with a worse
/// silhouette. Two copies of the list also means adding an enemy in one place and wondering
/// later why the arena never shows it.
///
/// One list, filtered by floor, loaded once. The paths stay here rather than being globbed
/// from the directory: a roster that discovers its own contents cannot be reviewed, and an
/// enemy half-authored into data/ would silently join the game.
/// </summary>
public static class Bestiary
{
    private static readonly string[] Paths =
    {
        // Floor 1 — Arkham Undercroft
        "res://data/enemies/acolyte.tres",
        "res://data/enemies/cellar_ghoul.tres",
        "res://data/enemies/tallow_man.tres",
        "res://data/enemies/chanter.tres",

        // The Netcaster is a FLOOR 2 enemy in docs/05 §3 — its own Codex entry calls it
        // "Innsmouth stock, this far inland" — and it is deliberately still MinFloor 1.
        //
        // It is the only Zoner in the game. Moving it to the Wharfs where it belongs would
        // leave floor 1 with no Zoner at all, and docs/11's M1 checklist is explicit that the
        // roster must cover every role. Losing role coverage to gain thematic tidiness is a
        // bad trade, so this stays wrong on purpose until floor 1 has a Zoner of its own.
        // Written down rather than quietly left, because the next person to read the docs
        // and the data together will otherwise "fix" it.
        "res://data/enemies/netcaster.tres",

        // Floor 2 — The Drowned Wharfs
        "res://data/enemies/deep_one.tres",
        "res://data/enemies/brine_priest.tres",
    };

    private static List<EnemyData>? _all;

    /// <summary>Every authored enemy, loaded once. Godot caches the Resources, so the second
    /// scene to ask pays nothing.</summary>
    public static IReadOnlyList<EnemyData> All
    {
        get
        {
            if (_all is not null) return _all;

            _all = new List<EnemyData>(Paths.Length);
            foreach (string path in Paths)
            {
                var data = GD.Load<EnemyData>(path);
                if (data is not null) _all.Add(data);
                else GD.PrintErr($"[Bestiary] failed to load {path}");
            }
            return _all;
        }
    }

    /// <summary>
    /// The roster for one floor.
    ///
    /// Never returns empty: a floor with no enemies is a floor of empty rooms, and the
    /// encounter director would report a satisfied budget rather than a broken one. If a
    /// filter ever excludes everything, this falls back to the whole bestiary and says so —
    /// a wrong enemy is a bug someone can see, and no enemies is a bug that looks like
    /// success.
    /// </summary>
    public static List<EnemyData> ForFloor(int floor)
    {
        var roster = new List<EnemyData>();
        foreach (EnemyData d in All) if (d.AppearsOnFloor(floor)) roster.Add(d);

        if (roster.Count == 0)
        {
            GD.PrintErr($"[Bestiary] no enemy is authored for floor {floor} — " +
                        $"falling back to the whole bestiary so the floor is not empty.");
            roster.AddRange(All);
        }
        return roster;
    }
}
