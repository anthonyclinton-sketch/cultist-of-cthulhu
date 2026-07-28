using System.Collections.Generic;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// Floor 1 content: flows and room templates.
///
/// Authored in code for now. For FLOWS this is the right call — a graph topology is
/// structure rather than tuning, Godot's inspector cannot usefully edit one, and docs/06
/// §10 schedules a GraphEdit plugin for exactly this job.
///
/// For ROOM TEMPLATES it is scaffolding, the same as Tune.cs: real rooms are hand-authored
/// TileMap scenes with spawn anchors and props, and these placeholder rectangles exist so
/// the generator can be built and gated before the level-design pipeline lands. docs/11
/// puts room authoring on the critical path at ~4/day; nothing here substitutes for that.
/// </summary>
public static class UndercroftContent
{
    /// <summary>
    /// docs/06 §3.2 — six flows for floor 1. Each is a tree plus loop-closing edges, and
    /// each must produce a recognisably different SHAPE of floor or there is no point
    /// having more than one.
    /// </summary>
    public static List<FloorFlow> Flows()
    {
        var flows = new List<FloorFlow>();

        // 1 — "The Single Ring". One tight loop off the entrance, boss beyond it.
        {
            var f = new FloorFlow("undercroft_ring");
            int entrance = f.Add(RoomRole.Entrance);
            int a = f.Add(RoomRole.CombatEasy, expandable: true);
            int hub = f.Add(RoomRole.Hub);
            int b = f.Add(RoomRole.CombatMed, expandable: true);
            int c = f.Add(RoomRole.Connector);
            int d = f.Add(RoomRole.CombatMed);
            int hard = f.Add(RoomRole.CombatHard);
            int foyer = f.Add(RoomRole.BossFoyer);
            int boss = f.Add(RoomRole.Boss);

            f.Root(entrance)
             .Chain(entrance, a, hub)
             .Chain(hub, b, c, d)
             .Link(d, hub)                    // closes the ring
             .Chain(hub, hard, foyer, boss);
            flows.Add(f);
        }

        // 2 — "The Figure Eight". Two loops sharing the hub; more route choice.
        {
            var f = new FloorFlow("undercroft_figure_eight");
            int entrance = f.Add(RoomRole.Entrance);
            int a = f.Add(RoomRole.CombatEasy);
            int hub = f.Add(RoomRole.Hub);
            int l1 = f.Add(RoomRole.CombatEasy, expandable: true);
            int l2 = f.Add(RoomRole.Connector);
            int r1 = f.Add(RoomRole.CombatMed, expandable: true);
            int r2 = f.Add(RoomRole.CombatMed);
            int hard = f.Add(RoomRole.CombatHard);
            int foyer = f.Add(RoomRole.BossFoyer);
            int boss = f.Add(RoomRole.Boss);

            f.Root(entrance)
             .Chain(entrance, a, hub)
             .Chain(hub, l1, l2).Link(l2, hub)
             .Chain(hub, r1, r2).Link(r2, hub)
             .Chain(hub, hard, foyer, boss);
            flows.Add(f);
        }

        // 3 — "The Long Descent". A long spine with one loop late; slower, more linear.
        {
            var f = new FloorFlow("undercroft_descent");
            int entrance = f.Add(RoomRole.Entrance);
            int a = f.Add(RoomRole.CombatEasy, expandable: true, expandMax: 4);
            int b = f.Add(RoomRole.Connector);
            int c = f.Add(RoomRole.CombatMed, expandable: true);
            int hub = f.Add(RoomRole.Hub);
            int d = f.Add(RoomRole.CombatMed);
            int e = f.Add(RoomRole.CombatHard);
            int foyer = f.Add(RoomRole.BossFoyer);
            int boss = f.Add(RoomRole.Boss);

            f.Root(entrance)
             .Chain(entrance, a, b, c, hub)
             .Chain(hub, d, e).Link(e, hub)
             .Chain(hub, foyer, boss);
            flows.Add(f);
        }

        return flows;
    }

    /// <summary>
    /// Placeholder room templates. Exit offsets are deliberately varied — a set of rooms
    /// that all put their doors at the centre of each wall produces layouts that look
    /// gridded, which is the tell that gives away a procedural dungeon.
    /// </summary>
    public static List<RoomTemplate> Rooms()
    {
        var list = new List<RoomTemplate>();

        void Room(string id, RoomRole role, int w, int h, int[] n, int[] s, int[] e, int[] west,
                  float capacity = 60f, float weight = 1f)
        {
            list.Add(new RoomTemplate
            {
                Id = id, Role = role, WidthTiles = w, HeightTiles = h,
                NorthExits = n, SouthExits = s, EastExits = e, WestExits = west,
                ThreatCapacity = capacity, Weight = weight, FloorTag = "undercroft",
            });
        }

        // Entrances — safe, several exits so the first choice happens immediately.
        Room("entrance_stair", RoomRole.Entrance, 16, 12, new[] { 8 }, new[] { 6 }, new[] { 6 }, new[] { 5 }, 0f);
        Room("entrance_vault", RoomRole.Entrance, 20, 14, new[] { 10 }, new[] { 8 }, new[] { 7 }, new[] { 4 }, 0f);
        Room("entrance_salt", RoomRole.Entrance, 14, 14, new[] { 7 }, new[] { 4 }, new[] { 7 }, new[] { 7 }, 0f);

        // Easy combat.
        Room("cellar_small", RoomRole.CombatEasy, 16, 12, new[] { 5 }, new[] { 9 }, new[] { 6 }, new[] { 5 }, 45f);
        Room("cellar_pillars", RoomRole.CombatEasy, 20, 14, new[] { 6, 14 }, new[] { 10 }, new[] { 8 }, new[] { 4 }, 55f);
        Room("cellar_long", RoomRole.CombatEasy, 26, 10, new[] { 8 }, new[] { 18 }, new[] { 5 }, new[] { 5 }, 50f);
        Room("cold_store", RoomRole.CombatEasy, 14, 16, new[] { 7 }, new[] { 5 }, new[] { 6, 12 }, new[] { 8 }, 48f);

        // Medium combat.
        Room("ossuary", RoomRole.CombatMed, 22, 16, new[] { 6, 16 }, new[] { 11 }, new[] { 8 }, new[] { 6 }, 80f);
        Room("long_table", RoomRole.CombatMed, 30, 12, new[] { 10 }, new[] { 20 }, new[] { 6 }, new[] { 6 }, 85f);
        Room("chandler", RoomRole.CombatMed, 18, 18, new[] { 9 }, new[] { 6 }, new[] { 9 }, new[] { 12 }, 75f);
        Room("flooded_cellar", RoomRole.CombatMed, 24, 14, new[] { 8 }, new[] { 15 }, new[] { 7 }, new[] { 5 }, 78f);

        // Hard combat.
        Room("crypt_deep", RoomRole.CombatHard, 26, 20, new[] { 8, 18 }, new[] { 13 }, new[] { 10 }, new[] { 8 }, 120f);
        Room("bone_gallery", RoomRole.CombatHard, 32, 14, new[] { 12 }, new[] { 20 }, new[] { 7 }, new[] { 7 }, 115f);

        // Hubs — 3+ exits by definition, and they are the degree bottleneck of every flow.
        // A flow node cannot have more neighbours than its room has exits, so the widest
        // hub here sets the ceiling on how interconnected any authored flow may be.
        Room("undercroft_crossing", RoomRole.Hub, 24, 20, new[] { 8, 16 }, new[] { 12 }, new[] { 10 }, new[] { 10 }, 70f);
        Room("great_cistern", RoomRole.Hub, 28, 22, new[] { 10 }, new[] { 14, 22 }, new[] { 11 }, new[] { 8 }, 75f);
        // Six exits, for double-loop flows like the figure eight.
        Room("nine_angles", RoomRole.Hub, 30, 24, new[] { 9, 21 }, new[] { 9, 21 }, new[] { 12 }, new[] { 12 }, 80f);

        // Connectors — pacing rest, no enemies.
        Room("salt_threshold", RoomRole.Connector, 14, 8, new[] { 7 }, new[] { 7 }, new[] { 4 }, new[] { 4 }, 0f);
        Room("narrow_stair", RoomRole.Connector, 10, 14, new[] { 5 }, new[] { 5 }, new[] { 7 }, new[] { 7 }, 0f);
        Room("graffiti_hall", RoomRole.Connector, 20, 8, new[] { 6 }, new[] { 14 }, new[] { 4 }, new[] { 4 }, 0f);

        // Dead-end specials.
        //
        // These have exits on ALL FOUR SIDES despite being graph dead ends, and that
        // distinction cost a lot of failed seeds to learn: "dead end in the flow" is not
        // "one door in the geometry". The FLOW decides how many connections a node uses;
        // the TEMPLATE only offers places one could attach. Giving these a single south
        // door meant they could only ever hang off a host's north wall, which made them
        // the hardest rooms on the floor to place and was a major source of layout
        // failure. In the finished art each of these still reads as a one-door room —
        // the unused sides are simply never opened.
        Room("reward_alcove", RoomRole.Reward, 12, 10, new[] { 6 }, new[] { 6 }, new[] { 5 }, new[] { 5 }, 0f);
        Room("gaunts_stall", RoomRole.Shop, 18, 12, new[] { 9 }, new[] { 9 }, new[] { 6 }, new[] { 6 }, 0f);
        Room("black_font", RoomRole.Shrine, 12, 12, new[] { 6 }, new[] { 6 }, new[] { 6 }, new[] { 6 }, 0f);
        Room("hidden_ossuary", RoomRole.Secret, 10, 10, new[] { 5 }, new[] { 5 }, new[] { 5 }, new[] { 5 }, 0f);
        Room("hidden_cache", RoomRole.Secret, 12, 8, new[] { 6 }, new[] { 6 }, new[] { 4 }, new[] { 4 }, 0f);

        // Boss approach.
        Room("boss_foyer", RoomRole.BossFoyer, 16, 12, new[] { 8 }, new[] { 8 }, new[] { 6 }, new[] { 6 }, 0f);
        Room("doorstep_arena", RoomRole.Boss, 34, 26, new[] { 17 }, new[] { 17 }, new[] { 13 }, new[] { 13 }, 0f);

        return list;
    }
}
