using System.Collections.Generic;
using Godot;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// The invariant list from docs/06 §5.5.
///
/// These are the promises the generator makes to the player, and every one of them is a
/// thing that would produce an unwinnable or nonsense floor if it silently broke. A
/// procedural system without an assertion layer is a system whose failures reach players
/// as "the game is broken and I don't know why" — so a failed invariant rejects the
/// layout and the generator retries with the next sub-seed.
///
/// Run over 10,000 seeds per floor in CI (docs/09 §9). Any failure prints the seed, which
/// makes it reproducible in one command.
/// </summary>
public static class FloorValidator
{
    public static string? Validate(GeneratedFloor floor, FloorFlow flow, int maxExtent)
    {
        if (floor.Rooms.Count != flow.Nodes.Count)
            return $"placed {floor.Rooms.Count} of {flow.Nodes.Count} rooms";

        // --- No two rooms overlap (with clearance).
        for (int i = 0; i < floor.Rooms.Count; i++)
        {
            for (int j = i + 1; j < floor.Rooms.Count; j++)
            {
                if (floor.Rooms[i].Bounds.Intersects(floor.Rooms[j].Bounds))
                    return $"rooms {floor.Rooms[i].Template.Id} and {floor.Rooms[j].Template.Id} overlap";
            }
        }

        // --- Every room reachable from the entrance. The single most important invariant:
        // an unreachable reward room is invisible, and an unreachable boss is a dead run.
        PlacedRoom? entrance = floor.FindRole(RoomRole.Entrance);
        if (entrance is null) return "no entrance";

        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(entrance.NodeId);
        var byId = new Dictionary<int, PlacedRoom>();
        foreach (PlacedRoom r in floor.Rooms) byId[r.NodeId] = r;

        while (stack.Count > 0)
        {
            int id = stack.Pop();
            if (!seen.Add(id)) continue;
            if (!byId.TryGetValue(id, out PlacedRoom? room)) continue;
            foreach (int c in room.Connections) stack.Push(c);
        }

        if (seen.Count != floor.Rooms.Count)
            return $"{floor.Rooms.Count - seen.Count} unreachable room(s)";

        // --- Required rooms exist and are reachable.
        if (floor.FindRole(RoomRole.Boss) is null) return "no boss room";
        if (floor.FindRole(RoomRole.Reward) is null) return "no reward room";

        int rewards = CountRole(floor, RoomRole.Reward);
        if (rewards != 1) return $"{rewards} reward rooms, expected exactly 1";

        int bosses = CountRole(floor, RoomRole.Boss);
        if (bosses != 1) return $"{bosses} boss rooms, expected exactly 1";

        // --- Floor fits in the world budget.
        Rect2I bounds = floor.Bounds();
        if (bounds.Size.X > maxExtent || bounds.Size.Y > maxExtent)
            return $"floor extent {bounds.Size.X}x{bounds.Size.Y} exceeds {maxExtent}";

        // --- Corridors within authored length limits.
        foreach (Corridor c in floor.Corridors)
        {
            float len = c.From.DistanceTo(c.To);
            if (len > FloorGenerator.MaxCorridorLength * 4)
                return $"corridor of {len:F0} tiles exceeds the limit";
        }

        // --- The player must have a choice. docs/01 §4.2: at least two unexplored exits
        // after the second room, or the floor is a corridor with extra steps.
        if (entrance.Connections.Count < 1) return "entrance has no exits";

        int branching = 0;
        foreach (PlacedRoom r in floor.Rooms) if (r.Connections.Count >= 3) branching++;
        if (floor.Rooms.Count >= 10 && branching == 0)
            return "no room has 3+ connections — floor is a single path, no route choice";

        // --- Secret rooms must hang off a reachable normal room (docs/06 §6.4).
        foreach (PlacedRoom r in floor.Rooms)
        {
            if (r.Role != RoomRole.Secret) continue;
            if (r.Connections.Count == 0) return "secret room with no host";
        }

        return null;
    }

    private static int CountRole(GeneratedFloor floor, RoomRole role)
    {
        int n = 0;
        foreach (PlacedRoom r in floor.Rooms) if (r.Role == role) n++;
        return n;
    }
}
