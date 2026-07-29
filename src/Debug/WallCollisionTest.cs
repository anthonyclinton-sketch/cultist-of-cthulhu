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

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" WALL COLLISION");
        GD.Print("================================================================");

        foreach (string seed in Seeds) RunSeed(seed);

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
    /// Drive enemies at walls and confirm their bodies never enter one.
    ///
    /// The player is placed OUTSIDE the room, so the flow field points every enemy at a
    /// wall and holds it there for the whole run. Steering toward a reachable player would
    /// test the flow field's routing rather than the collision resolution, and it is the
    /// resolution that has to hold when a Rusher's lunge or a Banish knockback ignores the
    /// field entirely.
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

        // Far outside the floor: unreachable, so every enemy walks into whatever is between
        // it and that direction and keeps pushing.
        manager.PlayerPosition = bounds.Position + bounds.Size + new Vector2(4000f, 4000f);

        int worst = 0;
        for (int t = 0; t < 300; t++)
        {
            manager._PhysicsProcess(1.0 / 60.0);
            int inWalls = SpawnedClearOfWalls(manager, mask);
            if (inWalls > worst) worst = inWalls;
        }

        Check(worst == 0,
              $"seed {seedText}: no enemy body entered a wall over 300 ticks of pushing " +
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
