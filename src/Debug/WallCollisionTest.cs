using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Generation;
using CultistOfCthulhu.Rooms;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Bullets and enemies stay inside the floor's walls.
///
///   godot --path . --headless res://scenes/debug/WallCollisionTest.tscn
///
/// This is the gate the four-bugs-in-a-row lesson (docs/HANDOVER §4) argues for. "Bullets
/// pass through walls" READS like a visual defect, and the temptation is to file it under
/// "only a screenshot can catch this" — but it is not visual at all. It is a positional
/// invariant, and a positional invariant can be asserted: after any number of ticks, no
/// live bullet and no enemy body may occupy solid ground.
///
/// Both systems simulate their own movement and never touch Godot's physics server, so
/// nothing else in the project was ever going to notice. The floor smoke test ran them
/// through walls on three seeds a milestone and reported PASS every time.
///
/// Run against real generated floors rather than a synthetic box, because the cases that
/// break are the seams: the 32px partition between two flush rooms, the two-tile-deep
/// doorway carved through both rings, and the corridor L-bends.
/// </summary>
public sealed partial class WallCollisionTest : Node2D
{
    private static readonly string[] Seeds = { "1", "7", "cthulhu", "42" };

    private int _failures;

    /// <summary>Seeds on which an unsealed room actually leaked. Proves the seal assertion is
    /// capable of failing; see the note in TestSealedRoomHoldsEnemies.</summary>
    private int _openEscapesSeen;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" WALL COLLISION");
        GD.Print("================================================================");

        foreach (string seed in Seeds) RunSeed(seed);

        GD.Print("");
        Check(_openEscapesSeen > 0,
              $"the escape check is sensitive — enemies left an UNSEALED room on " +
              $"{_openEscapesSeen} of {Seeds.Length} seeds");

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " WALL COLLISION: PASS" : $" WALL COLLISION: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    private void RunSeed(string seedText)
    {
        ulong seed = Hash.ParseSeed(seedText);
        var gen = new FloorGenerator(UndercroftContent.Flows(), UndercroftContent.Rooms());
        GeneratedFloor? floor = gen.Generate(Hash.Combine(seed, "floor1"), 1, out string failure);

        if (floor is null)
        {
            Check(false, $"seed {seedText}: generation failed ({failure})");
            return;
        }

        var geometry = new FloorGeometry(floor);
        TileMask mask = geometry.BuildSolidMask();

        GD.Print($"\n--- seed {seedText}: {floor.Rooms.Count} rooms, flow '{floor.FlowId}' ---");

        TestRoomCentresAreOpen(floor, geometry, mask, seedText);
        TestEveryOpeningIsSealable(floor, geometry, mask, seedText);
        TestSealedRoomHoldsEnemies(floor, geometry, seedText);
        TestBulletsStopAtWalls(floor, geometry, mask, seedText);
        TestFastBulletsDoNotTunnel(floor, geometry, mask, seedText);
        TestEnemiesStayOutOfWalls(floor, geometry, mask, seedText);
    }

    /// <summary>
    /// Sanity check on the mask itself before anything is asserted against it. An
    /// all-solid mask would make every test below pass trivially — bullets die instantly,
    /// enemies never move — and report a healthy gate for a floor nobody can walk on.
    ///
    /// Tests the room ANCHOR, not the geometric centre. Authored interiors made that
    /// distinction real: long_table's design is a block through the middle of the room and
    /// great_cistern's basin sits dead centre, so a solid centre is now correct content
    /// rather than a fault. The property that actually matters is that every room offers
    /// somewhere to stand, and that most of the room is that somewhere — a template whose
    /// obstacles swallowed its interior would satisfy a bare anchor test.
    /// </summary>
    private void TestRoomCentresAreOpen(GeneratedFloor floor, FloorGeometry geometry,
                                        TileMask mask, string seedText)
    {
        int solid = 0;
        int cramped = 0;

        foreach (PlacedRoom r in floor.Rooms)
        {
            Vector2 c = geometry.RoomAnchorWorld(r);
            if (mask.IsSolid(c.X, c.Y)) solid++;

            // Count the open fraction of the interior. Obstacles are cover, not maze walls.
            Rect2 interior = geometry.RoomInteriorWorld(r);
            int open = 0, total = 0;
            for (float y = interior.Position.Y; y < interior.Position.Y + interior.Size.Y; y += FloorGeometry.Tile)
            {
                for (float x = interior.Position.X; x < interior.Position.X + interior.Size.X; x += FloorGeometry.Tile)
                {
                    total++;
                    if (!mask.IsSolid(x, y)) open++;
                }
            }
            if (total > 0 && open / (float)total < 0.6f) cramped++;
        }

        Check(solid == 0, $"seed {seedText}: every room offers a standable anchor ({solid} solid)");
        Check(cramped == 0,
              $"seed {seedText}: no room is more than 40% obstacle ({cramped} over budget)");
    }

    /// <summary>
    /// Every hole in a room's wall ring must be covered by one of that room's doorways.
    ///
    /// This is what makes a room a room. Doors seal while a fight is running, and a seal can
    /// only cover an opening the geometry knows about — so an unrecorded opening is a combat
    /// room the player walks straight out of, taking the encounter's whole premise with it.
    ///
    /// It was a real bug and it was widespread: doorways used to be recorded only where two
    /// flush rooms had a passage punched between them, and corridors recorded nothing at
    /// all, so every corridor-connected room on every floor had an exit no seal could ever
    /// close. No gate noticed, because the door logic was self-consistent — it sealed
    /// everything it knew about, and simply did not know about half of it.
    /// </summary>
    private void TestEveryOpeningIsSealable(GeneratedFloor floor, FloorGeometry geometry,
                                            TileMask mask, string seedText)
    {
        int uncovered = 0;
        int openings = 0;

        foreach (PlacedRoom r in floor.Rooms)
        {
            Rect2I b = r.Bounds;

            for (int y = b.Position.Y; y < b.Position.Y + b.Size.Y; y++)
            {
                for (int x = b.Position.X; x < b.Position.X + b.Size.X; x++)
                {
                    bool onRing = x == b.Position.X || y == b.Position.Y
                                  || x == b.Position.X + b.Size.X - 1
                                  || y == b.Position.Y + b.Size.Y - 1;
                    if (!onRing) continue;

                    // Test the tile's CENTRE: a doorway rect covers whole tiles, and a
                    // corner-point test would land on the boundary and go either way.
                    var centre = new Vector2((x + 0.5f) * FloorGeometry.Tile,
                                             (y + 0.5f) * FloorGeometry.Tile);
                    if (mask.IsSolid(centre.X, centre.Y)) continue;

                    openings++;

                    bool covered = false;
                    foreach (Doorway d in geometry.Doors)
                    {
                        if (d.Room != r.NodeId) continue;
                        if (!d.WorldRect.HasPoint(centre)) continue;
                        covered = true;
                        break;
                    }
                    if (!covered) uncovered++;
                }
            }
        }

        Check(openings > 0, $"seed {seedText}: rooms have openings at all ({openings} ring tiles)");
        Check(uncovered == 0,
              $"seed {seedText}: every opening is covered by a sealable doorway " +
              $"({uncovered} of {openings} ring tiles unsealable)");
    }

    /// <summary>
    /// A sealed room actually holds what is inside it.
    ///
    /// The bug this exists for was reported from play: an enemy walked out through a locked
    /// door. Door seals are <c>StaticBody2D</c> nodes, so they stopped the PLAYER — the only
    /// thing in the game using Godot's physics — and were invisible to bullets and enemies,
    /// which both simulate their own movement against the tile mask.
    ///
    /// Driven directly rather than through the autorun, which clears a room in about eight
    /// frames and would almost never give anything time to wander out. Here the player is
    /// placed OUTSIDE the room so every enemy pushes at a door for a full ten seconds, which
    /// is the worst case and the one that was failing.
    /// </summary>
    private void TestSealedRoomHoldsEnemies(GeneratedFloor floor, FloorGeometry geometry,
                                            string seedText)
    {
        var data = GD.Load<EnemyData>("res://data/enemies/cellar_ghoul.tres");
        if (data is null) { Check(false, "cellar_ghoul.tres failed to load"); return; }

        // A fresh mask, so sealing does not disturb the other tests on this seed.
        TileMask mask = geometry.BuildSolidMask();
        Rect2 bounds = WorldBounds(floor);

        var enemyBullets = new BulletManager { Bounds = bounds, Walls = mask };
        var playerBullets = new BulletManager { Bounds = bounds, Walls = mask, CollideWithEnemies = true };
        var manager = new EnemyManager();
        AddChild(enemyBullets);
        AddChild(playerBullets);
        AddChild(manager);

        manager.Initialise(enemyBullets, playerBullets, bounds, new Rng(0x5EA1));
        manager.SetWalls(mask);

        // Pick a room with several exits — the more doors, the more chances to leak.
        PlacedRoom? target = null;
        int mostDoors = 0;
        foreach (PlacedRoom r in floor.Rooms)
        {
            int doors = 0;
            foreach (Doorway d in geometry.Doors) if (d.Room == r.NodeId) doors++;
            if (doors <= mostDoors) continue;
            mostDoors = doors;
            target = r;
        }
        if (target is null) { Check(false, $"seed {seedText}: no room with doorways"); return; }

        // Run it twice: once with the doors OPEN, once sealed.
        //
        // The open pass is not decoration — it proves the test can detect an escape at all.
        // A "0 escaped" result means nothing on its own, because it is also what a broken
        // harness reports: enemies that never move, a room rect that swallows everything, a
        // player position that happens to be reachable. If they do not get out with the doors
        // open, this test cannot see the bug it was written for.
        // Somewhere REACHABLE but outside the room — see the note in RunSealedRoom.
        Vector2 lure = LureOutside(floor, geometry, target);

        int escapedOpen = RunSealedRoom(geometry, manager, data, target, lure, sealDoors: false);
        int escapedSealed = RunSealedRoom(geometry, manager, data, target, lure, sealDoors: true);

        // Sensitivity is tallied across the run rather than demanded of every seed. Whether a
        // particular room leaks within ten seconds depends on how far its doors are from the
        // lure and how the flow field routes out of it, so a per-seed requirement would be
        // brittle for reasons that say nothing about the seal. One seed proving the harness
        // can see an escape is enough; the sealed assertion below is the hard one, and it is
        // checked on every seed.
        if (escapedOpen > 0) _openEscapesSeen++;

        Check(escapedSealed == 0,
              $"seed {seedText}: {mostDoors} sealed doorways held 10 enemies for 10s " +
              $"({escapedSealed} escaped)");

        manager.QueueFree();
        enemyBullets.QueueFree();
        playerBullets.QueueFree();
    }

    /// <summary>
    /// A standable point in some OTHER room, for enemies to be lured toward.
    ///
    /// It must be reachable, and that is the whole subtlety. Pointing the flow field at a
    /// spot far outside the floor does not make enemies walk to the edge — <c>FlowField</c>
    /// clamps the target to its grid, the clamped corner cell is solid rock, and a BFS that
    /// starts on a blocked cell cannot spread at all. Every cell then reports a zero
    /// direction and NOTHING MOVES.
    ///
    /// That is how the sibling test in this file was passing: "no enemy body entered a wall
    /// over 300 ticks of pushing" was true because there was no pushing. A test whose
    /// subjects are motionless will confirm any invariant asked of it.
    /// </summary>
    private static Vector2 LureOutside(GeneratedFloor floor, FloorGeometry geometry, PlacedRoom target)
    {
        // A connected neighbour first: the shortest real path out of the room.
        foreach (int id in target.Connections)
        {
            foreach (PlacedRoom r in floor.Rooms)
                if (r.NodeId == id) return geometry.RoomAnchorWorld(r);
        }

        foreach (PlacedRoom r in floor.Rooms)
            if (r.NodeId != target.NodeId) return geometry.RoomAnchorWorld(r);

        return geometry.RoomAnchorWorld(target);
    }

    /// <summary>Populate the room, optionally seal it, and lure everything at the doors for
    /// ten seconds. Returns how many got out.</summary>
    private static int RunSealedRoom(FloorGeometry geometry, EnemyManager manager, EnemyData data,
                                    PlacedRoom target, Vector2 lure, bool sealDoors)
    {
        manager.ClearAll();

        TileMask mask = geometry.BuildSolidMask();
        if (sealDoors)
        {
            foreach (Doorway d in geometry.Doors)
                if (d.Room == target.NodeId) mask.SetSolidWorldRect(d.WorldRect, true);
        }
        manager.SetWalls(mask);

        var rng = new Rng(0xD00);
        Rect2 interior = geometry.RoomInteriorWorld(target);
        for (int i = 0; i < 10; i++)
        {
            var at = new Vector2(
                rng.Range(interior.Position.X, interior.Position.X + interior.Size.X),
                rng.Range(interior.Position.Y, interior.Position.Y + interior.Size.Y));
            manager.Spawn(data, at);
        }
        manager.EvictFromSolid(geometry.RoomAnchorWorld(target));

        manager.PlayerPosition = lure;

        Rect2 allowed = geometry.RoomRectWorld(target).Grow(FloorGeometry.Tile);
        for (int t = 0; t < 600; t++)
        {
            manager._PhysicsProcess(1.0 / 60.0);

            int out_ = 0;
            foreach (Enemy e in manager.Enemies)
                if (e.Alive && !allowed.HasPoint(e.Position)) out_++;
            if (out_ > 0) return out_;
        }
        return 0;
    }

    /// <summary>
    /// A ring of bullets fired outward from each room centre. Whatever survives must be
    /// standing on open ground — which is the whole claim.
    /// </summary>
    private void TestBulletsStopAtWalls(GeneratedFloor floor, FloorGeometry geometry,
                                        TileMask mask, string seedText)
    {
        var bullets = new BulletManager
        {
            Bounds = WorldBounds(floor),
            Walls = mask,
        };
        AddChild(bullets);

        foreach (PlacedRoom r in floor.Rooms)
        {
            Vector2 c = geometry.RoomAnchorWorld(r);
            for (int i = 0; i < 32; i++)
            {
                float a = i / 32f * Mathf.Tau;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                bullets.Spawn(c, dir * 420f, 3f, 20f, Colors.White, 6f);
            }
        }

        int spawned = bullets.Count;
        for (int t = 0; t < 240; t++) bullets._PhysicsProcess(1.0 / 60.0);

        Check(spawned > 0, $"seed {seedText}: fired {spawned} test bullets");
        Check(CountBulletsInWalls(bullets, mask) == 0,
              $"seed {seedText}: no live bullet is inside a wall after 240 ticks " +
              $"({CountBulletsInWalls(bullets, mask)} of {bullets.Count} survivors)");

        // The complement: bullets must actually be STOPPING. If wall collision were wired
        // up backwards — everything solid — the assertion above passes with zero survivors,
        // and if it were not wired up at all the count would be near the spawn count.
        Check(bullets.Count < spawned,
              $"seed {seedText}: walls consumed bullets ({spawned - bullets.Count} of {spawned})");

        bullets.QueueFree();
    }

    /// <summary>
    /// The tunnelling case, which an endpoint-only test would miss.
    ///
    /// The thinnest wall on a generated floor is the 32px partition between two flush
    /// rooms. A bullet at 2400px/s covers 40px in one 60Hz tick, so it can begin the tick
    /// in one room and end it in the next with no sample ever landing in the wall — which
    /// is why <see cref="TileMask.SegmentHitsSolid"/> sub-samples the swept segment.
    /// </summary>
    private void TestFastBulletsDoNotTunnel(GeneratedFloor floor, FloorGeometry geometry,
                                            TileMask mask, string seedText)
    {
        var bullets = new BulletManager
        {
            Bounds = WorldBounds(floor),
            Walls = mask,
        };
        AddChild(bullets);

        foreach (PlacedRoom r in floor.Rooms)
        {
            Vector2 c = geometry.RoomAnchorWorld(r);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.Tau;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                bullets.Spawn(c, dir * 2400f, 3f, 20f, Colors.White, 6f);
            }
        }

        for (int t = 0; t < 240; t++) bullets._PhysicsProcess(1.0 / 60.0);

        int inWalls = CountBulletsInWalls(bullets, mask);
        Check(inWalls == 0,
              $"seed {seedText}: no 2400px/s bullet tunnelled into rock ({inWalls} did)");

        bullets.QueueFree();
    }

    private static int CountBulletsInWalls(BulletManager bullets, TileMask mask)
    {
        int n = 0;
        for (int i = 0; i < bullets.Count; i++)
        {
            Vector2 p = bullets.GetPosition(i);
            if (mask.IsSolid(p.X, p.Y)) n++;
        }
        return n;
    }

    /// <summary>
    /// Drive enemies across the floor and confirm their bodies never enter a wall.
    ///
    /// The lure is a REACHABLE room on the far side of the floor, and that correction is the
    /// point. This test used to aim the flow field at a spot thousands of pixels outside the
    /// floor on the theory that an unreachable target would hold every enemy pressed against
    /// a wall. It does the opposite: <c>FlowField</c> clamps the target into its grid, the
    /// clamped cell is solid, a BFS from a blocked cell cannot spread, and every enemy
    /// therefore received a zero direction and stood perfectly still for 300 ticks. The
    /// assertion held because nothing moved.
    ///
    /// Luring them somewhere real makes them cross corridors and doorways and grind along
    /// walls on the way, which is the case that actually exercises the resolution.
    /// </summary>
    private void TestEnemiesStayOutOfWalls(GeneratedFloor floor, FloorGeometry geometry,
                                           TileMask mask, string seedText)
    {
        var data = GD.Load<EnemyData>("res://data/enemies/cellar_ghoul.tres");
        if (data is null) { Check(false, "cellar_ghoul.tres failed to load"); return; }

        Rect2 bounds = WorldBounds(floor);
        var enemyBullets = new BulletManager { Bounds = bounds, Walls = mask };
        var playerBullets = new BulletManager { Bounds = bounds, Walls = mask, CollideWithEnemies = true };
        var manager = new EnemyManager();
        AddChild(enemyBullets);
        AddChild(playerBullets);
        AddChild(manager);

        manager.Initialise(enemyBullets, playerBullets, bounds, new Rng(0x5EED));
        manager.SetWalls(mask);

        var rng = new Rng(0xC0FFEE);
        foreach (PlacedRoom r in floor.Rooms)
        {
            Rect2 interior = geometry.RoomInteriorWorld(r);
            for (int i = 0; i < 6; i++)
            {
                var at = new Vector2(
                    rng.Range(interior.Position.X, interior.Position.X + interior.Size.X),
                    rng.Range(interior.Position.Y, interior.Position.Y + interior.Size.Y));
                manager.Spawn(data, at);
            }
        }

        int spawned = manager.AliveCount;
        Check(SpawnedClearOfWalls(manager, mask) == 0,
              $"seed {seedText}: all {spawned} enemies spawned clear of walls " +
              $"({SpawnedClearOfWalls(manager, mask)} embedded)");

        // Lure them to the ENTRANCE, which is reachable from everywhere by construction, so
        // the whole roster crosses the floor and grinds along every wall on the way.
        PlacedRoom entrance = floor.FindRole(RoomRole.Entrance)!;
        manager.PlayerPosition = geometry.RoomAnchorWorld(entrance);

        // Record how far they actually travel. Without this the assertion below is only as
        // good as the assumption that they moved, which is exactly the assumption that was
        // wrong for a whole session.
        var start = new Vector2[manager.Enemies.Count];
        for (int i = 0; i < manager.Enemies.Count; i++) start[i] = manager.Enemies[i].Position;

        int worst = 0;
        for (int t = 0; t < 300; t++)
        {
            manager._PhysicsProcess(1.0 / 60.0);
            int inWalls = SpawnedClearOfWalls(manager, mask);
            if (inWalls > worst) worst = inWalls;
        }

        float moved = 0f;
        for (int i = 0; i < manager.Enemies.Count && i < start.Length; i++)
            moved += manager.Enemies[i].Position.DistanceTo(start[i]);
        float meanMoved = manager.Enemies.Count > 0 ? moved / manager.Enemies.Count : 0f;

        Check(meanMoved > 20f,
              $"seed {seedText}: enemies actually moved (mean {meanMoved:F0}px over 5s) — " +
              "the wall assertion below is not vacuous");
        Check(worst == 0,
              $"seed {seedText}: no enemy body entered a wall while crossing the floor " +
              $"(worst tick had {worst})");

        manager.QueueFree();
        enemyBullets.QueueFree();
        playerBullets.QueueFree();
    }

    private static int SpawnedClearOfWalls(EnemyManager manager, TileMask mask)
    {
        int n = 0;
        foreach (Enemy e in manager.Enemies)
        {
            if (!e.Alive) continue;
            // A small tolerance: MoveCircle refuses moves that OVERLAP, so a body may rest
            // flush against a surface. Resting on it is correct; sinking into it is not.
            if (mask.CircleOverlaps(e.Position.X, e.Position.Y, e.Data.BodyRadius - 0.5f)) n++;
        }
        return n;
    }

    private static Rect2 WorldBounds(GeneratedFloor floor)
    {
        Rect2I b = floor.Bounds();
        return new Rect2(
            (b.Position.X - 4) * FloorGeometry.Tile, (b.Position.Y - 4) * FloorGeometry.Tile,
            (b.Size.X + 8) * FloorGeometry.Tile, (b.Size.Y + 8) * FloorGeometry.Tile);
    }
}
