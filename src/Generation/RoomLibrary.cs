using System.Collections.Generic;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// Every authored room template, from every floor's content set.
///
/// One pool with one owner, for the reason <see cref="Enemies.Bestiary"/> exists: the enemy
/// roster was a flat list copy-pasted into two scenes with no floor concept, and the copies
/// drifted. The template pool was heading the same way the moment a second floor's rooms were
/// authored — the generator took `UndercroftContent.Rooms()` and the generation sweep took its
/// own call to the same method, so a Wharf room added to one would be invisible to the other
/// and simply never validated.
///
/// THIS IS NOT FLOOR ROUTING YET, and the distinction matters. `RoomTemplate.FloorTag` is
/// authored on all 32 Undercroft templates and read by nothing; template selection filters
/// only on `MinFloor > floorIndex`, which is a lower bound, so an Undercroft room stays
/// eligible on every floor forever. Floor 2 is currently built out of Undercroft rooms and
/// this class does not change that — it only makes sure every authored room is in one pool
/// and gets validated. <see cref="ForFloor"/> is where the routing will go.
/// </summary>
public static class RoomLibrary
{
    private static List<RoomTemplate>? _all;

    public static List<RoomTemplate> All()
    {
        if (_all is not null) return new List<RoomTemplate>(_all);

        _all = new List<RoomTemplate>();
        _all.AddRange(UndercroftContent.Rooms());
        _all.AddRange(WharfContent.Rooms());
        return new List<RoomTemplate>(_all);
    }
}
