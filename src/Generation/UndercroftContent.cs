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
        //
        // `a` is expandable and that is load-bearing, not decoration. Both of the loop chains
        // are inside cycles, so before this the flow's ONLY way to grow was to lengthen two
        // cycles that share a hub and both have to close in 2D. Asked for the 17–18 room
        // floors docs/07 §2 wants on floors 3–4, it fell back on 124 seeds out of 10,000
        // against 4 for the ring and 0 for the descent. The approach corridor is acyclic, so
        // growing it is free, and a one-room approach was thin anyway.
        {
            var f = new FloorFlow("undercroft_figure_eight");
            int entrance = f.Add(RoomRole.Entrance);
            int a = f.Add(RoomRole.CombatEasy, expandable: true, expandMax: 4);
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
    ///
    /// SIZING IS RELATIVE TO THE SCREEN, not to a tile count that sounds reasonable. The
    /// viewport is 640x360 native = 40 x 22.5 tiles, and the first pass authored combat
    /// rooms at 16x12 to 26x20 — smaller than one screen. That is fatal for a bullet hell:
    /// if the whole room fits on screen there is nowhere to dodge TO, radial patterns have
    /// no room to expand before hitting a wall, and the camera never moves so the space
    /// reads as a box rather than a place.
    ///
    /// Roughly, by role:
    ///   connector   ~0.9 x 0.7 screens   a rest beat, deliberately tight
    ///   easy        ~1.2 x 1.1 screens   just over one screen
    ///   medium      ~1.6 x 1.4 screens
    ///   hard        ~1.9 x 1.9 screens
    ///   hub         ~1.8 x 2.3 screens   the biggest non-boss space
    ///   boss        ~2.4 x 2.9 screens
    ///
    /// That lands every room 4-8x its previous AREA.
    /// </summary>
    public static List<RoomTemplate> Rooms()
    {
        var list = new List<RoomTemplate>();

        void Room(string id, RoomRole role, int w, int h, int[] n, int[] s, int[] e, int[] west,
                  float capacity = 60f, float weight = 1f, int[]? obstacles = null)
        {
            list.Add(new RoomTemplate
            {
                Id = id, Role = role, WidthTiles = w, HeightTiles = h,
                NorthExits = n, SouthExits = s, EastExits = e, WestExits = west,
                ThreatCapacity = capacity, Weight = weight, FloorTag = "undercroft",
                Obstacles = obstacles ?? System.Array.Empty<int>(),
            });
        }

        // Obstacle quads are x, y, w, h in tiles from the room's top-left. They are what
        // stops a room being a rectangle: cover to break line of fire, corners to dodge
        // behind, and something for a radial pattern to be shaped by.
        //
        // Every one of them has to leave the interior connected and every doorway
        // reachable, which RoomTemplate.Validate floods for — a pillar in the wrong place
        // is not a cosmetic mistake but a sealed room the player cannot leave, and the
        // generator's own validation works on the room GRAPH and would call that floor
        // perfectly connected.
        //
        // Non-combat rooms are left bare on purpose. Their furniture is placed at runtime
        // by RoomContent, which positions from the room centre, and an obstacle there would
        // put a pedestal inside a pillar.

        // Entrances — safe, several exits so the first choice happens immediately.
        Room("entrance_stair", RoomRole.Entrance, 40, 26, new[] { 20 }, new[] { 14 }, new[] { 13 }, new[] { 10 }, 0f);
        Room("entrance_vault", RoomRole.Entrance, 46, 30, new[] { 24 }, new[] { 18 }, new[] { 16 }, new[] { 9 }, 0f);
        Room("entrance_salt", RoomRole.Entrance, 34, 30, new[] { 17 }, new[] { 10 }, new[] { 15 }, new[] { 15 }, 0f);

        // EXIT COUNT SCALES WITH SIZE, and that is a placement requirement as much as a
        // design one. Scaling rooms up while leaving one door per wall meant a 70-tile room
        // offered no more attachment points than a 26-tile one, so the layout search had
        // far fewer options per unit of area and the fallback rate rose from 0.2% to 1.7%.
        // Widening the search barely helped; adding doors fixed it. A big room with a
        // single door per wall also just reads as sparse.

        // Easy combat — just over one screen. Sparse cover: an early room should teach the
        // player that walls exist to hide behind without punishing them for not noticing.
        Room("cellar_small", RoomRole.CombatEasy, 44, 26, new[] { 12, 32 }, new[] { 26 }, new[] { 13 }, new[] { 11 }, 180f,
             obstacles: new[] { 12, 9, 3, 3, 28, 14, 3, 3 });
        Room("cellar_pillars", RoomRole.CombatEasy, 52, 30, new[] { 14, 36 }, new[] { 16, 38 }, new[] { 17 }, new[] { 9 }, 220f,
             obstacles: new[] { 12, 8, 3, 3, 12, 19, 3, 3, 36, 8, 3, 3, 36, 19, 3, 3, 24, 13, 4, 4 });
        Room("cellar_long", RoomRole.CombatEasy, 68, 22, new[] { 20, 50 }, new[] { 22, 46 }, new[] { 11 }, new[] { 11 }, 200f,
             obstacles: new[] { 16, 6, 12, 2, 40, 14, 12, 2 });
        Room("cold_store", RoomRole.CombatEasy, 36, 36, new[] { 18 }, new[] { 12 }, new[] { 13, 26 }, new[] { 12, 26 }, 190f,
             obstacles: new[] { 8, 8, 4, 4, 24, 24, 4, 4 });
        Room("the_sump", RoomRole.CombatEasy, 40, 32, new[] { 14, 30 }, new[] { 20 }, new[] { 16 }, new[] { 14 }, 200f,
             obstacles: new[] { 17, 13, 6, 6 });

        // Medium combat. Enough cover to make positioning a choice rather than a formality.
        Room("ossuary", RoomRole.CombatMed, 58, 36, new[] { 15, 42 }, new[] { 16, 40 }, new[] { 18 }, new[] { 13 }, 320f,
             obstacles: new[] { 10, 8, 2, 9, 20, 8, 2, 9, 30, 8, 2, 9, 40, 8, 2, 9, 24, 24, 10, 3 });
        // The table is the room. A long central block splits every radial pattern in two
        // and turns the fight into a decision about which side to be on.
        Room("long_table", RoomRole.CombatMed, 76, 28, new[] { 26, 56 }, new[] { 22, 52 }, new[] { 14 }, new[] { 14 }, 340f,
             obstacles: new[] { 18, 12, 40, 4 });
        Room("chandler", RoomRole.CombatMed, 46, 42, new[] { 23 }, new[] { 15 }, new[] { 13, 29 }, new[] { 16, 28 }, 300f,
             obstacles: new[] { 19, 17, 8, 8, 8, 8, 3, 3, 35, 8, 3, 3, 8, 31, 3, 3, 35, 31, 3, 3 });
        Room("flooded_cellar", RoomRole.CombatMed, 62, 34, new[] { 20, 44 }, new[] { 18, 40 }, new[] { 17 }, new[] { 12 }, 312f,
             obstacles: new[] { 14, 10, 5, 3, 30, 7, 3, 5, 42, 20, 5, 3, 22, 24, 4, 4 });
        Room("rope_walk", RoomRole.CombatMed, 66, 30, new[] { 22, 48 }, new[] { 18, 44 }, new[] { 15 }, new[] { 15 }, 320f,
             obstacles: new[] { 12, 7, 3, 16, 30, 7, 3, 16, 48, 7, 3, 16 });

        // Hard combat — nearly two screens each way. Radial patterns need this much room
        // to expand before they reach a wall, so the cover here is heavy but open-centred.
        Room("crypt_deep", RoomRole.CombatHard, 70, 48, new[] { 20, 48 }, new[] { 22, 50 }, new[] { 16, 32 }, new[] { 19 }, 480f,
             obstacles: new[] { 10, 10, 6, 4, 54, 10, 6, 4, 10, 34, 6, 4, 54, 34, 6, 4, 30, 21, 10, 6 });
        Room("bone_gallery", RoomRole.CombatHard, 84, 36, new[] { 30, 60 }, new[] { 24, 54 }, new[] { 18 }, new[] { 18 }, 460f,
             obstacles: new[] { 14, 8, 3, 8, 14, 20, 3, 8, 40, 8, 3, 8, 40, 20, 3, 8, 66, 8, 3, 8, 66, 20, 3, 8 });
        Room("the_kennels", RoomRole.CombatHard, 76, 44, new[] { 26, 56 }, new[] { 20, 50 }, new[] { 20 }, new[] { 20 }, 470f,
             obstacles: new[] { 12, 12, 8, 3, 56, 12, 8, 3, 12, 29, 8, 3, 56, 29, 8, 3, 34, 19, 8, 6 });

        // Hubs — 3+ exits by definition, and they are the degree bottleneck of every flow.
        // A flow node cannot have more neighbours than its room has exits, so the widest
        // hub here sets the ceiling on how interconnected any authored flow may be.
        Room("undercroft_crossing", RoomRole.Hub, 64, 48, new[] { 20, 42 }, new[] { 22, 44 }, new[] { 16, 32 }, new[] { 24 }, 280f,
             obstacles: new[] { 14, 12, 4, 4, 46, 12, 4, 4, 14, 32, 4, 4, 46, 32, 4, 4 });
        Room("great_cistern", RoomRole.Hub, 72, 52, new[] { 26, 50 }, new[] { 34, 54 }, new[] { 26 }, new[] { 20 }, 300f,
             obstacles: new[] { 28, 20, 16, 12 });
        // Six exits, for double-loop flows like the figure eight. Nine pillars, hence
        // the name — the room is the landmark players will describe the floor by.
        Room("nine_angles", RoomRole.Hub, 76, 56, new[] { 22, 52 }, new[] { 22, 52 }, new[] { 18, 38 }, new[] { 18, 38 }, 320f,
             obstacles: new[]
             {
                 16, 12, 3, 3, 36, 12, 3, 3, 56, 12, 3, 3,
                 16, 26, 3, 3, 36, 26, 3, 3, 56, 26, 3, 3,
                 16, 40, 3, 3, 36, 40, 3, 3, 56, 40, 3, 3,
             });

        // Connectors — pacing rest, no enemies. Deliberately the tightest spaces on the
        // floor, so the big rooms read as big by contrast.
        Room("salt_threshold", RoomRole.Connector, 34, 18, new[] { 17 }, new[] { 17 }, new[] { 9 }, new[] { 9 }, 0f);
        Room("narrow_stair", RoomRole.Connector, 24, 34, new[] { 12 }, new[] { 12 }, new[] { 17 }, new[] { 17 }, 0f);
        Room("graffiti_hall", RoomRole.Connector, 48, 18, new[] { 14 }, new[] { 34 }, new[] { 9 }, new[] { 9 }, 0f);
        Room("the_long_stair", RoomRole.Connector, 28, 40, new[] { 14 }, new[] { 14 }, new[] { 20 }, new[] { 20 }, 0f);

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
        Room("reward_alcove", RoomRole.Reward, 32, 24, new[] { 16 }, new[] { 16 }, new[] { 12 }, new[] { 12 }, 0f);
        Room("tallow_vault", RoomRole.Reward, 36, 26, new[] { 18 }, new[] { 18 }, new[] { 13 }, new[] { 13 }, 0f);
        // The stall needs floor space: seven stock positions plus the bowl are laid out
        // around the room centre by RoomContent, and a 44-wide room crowds them.
        Room("gaunts_stall", RoomRole.Shop, 52, 34, new[] { 26 }, new[] { 26 }, new[] { 17 }, new[] { 17 }, 0f);
        Room("black_font", RoomRole.Shrine, 30, 28, new[] { 15 }, new[] { 15 }, new[] { 14 }, new[] { 14 }, 0f);
        Room("sisters_font", RoomRole.Shrine, 34, 30, new[] { 17 }, new[] { 17 }, new[] { 15 }, new[] { 15 }, 0f);
        Room("hidden_ossuary", RoomRole.Secret, 26, 24, new[] { 13 }, new[] { 13 }, new[] { 12 }, new[] { 12 }, 0f);
        Room("hidden_cache", RoomRole.Secret, 30, 20, new[] { 15 }, new[] { 15 }, new[] { 10 }, new[] { 10 }, 0f);

        // Boss approach. The arena is the largest space in the game — a four-phase fight
        // with screen-filling patterns cannot happen in a room you can cross in two dashes.
        Room("boss_foyer", RoomRole.BossFoyer, 38, 28, new[] { 19 }, new[] { 19 }, new[] { 14 }, new[] { 14 }, 0f);
        // The boss arena keeps its centre and its long axes completely clear. Four corner
        // blocks give the player something to break line of fire against during phase 2's
        // radial volleys, without ever giving them a spot the phase-3 grab cannot reach —
        // a boss whose signature attack can be camped out of is a boss with two phases.
        Room("doorstep_arena", RoomRole.Boss, 96, 66, new[] { 48 }, new[] { 48 }, new[] { 33 }, new[] { 33 }, 0f,
             obstacles: new[] { 14, 12, 6, 6, 76, 12, 6, 6, 14, 48, 6, 6, 76, 48, 6, 6 });

        return list;
    }
}
