using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// The Tide does something, and does it to the right things (docs/07 §3).
///
///   godot --path . --headless res://scenes/debug/TideTest.tscn
///
/// THE PROPERTY THIS GUARDS. docs/07 §3 does not promise water; it promises a *rhythm layer*
/// that is synchronised, predictable, and asymmetric — "the same water that costs you speed
/// buys it for the thing chasing you". Each of those is a separate way for the system to be
/// quietly dead, and none of them shows up as an error:
///
///   - a cycle that oscillates while nothing reads it,
///   - a field whose flood levels are compared the wrong way round, so the tide goes OUT as
///     it comes in and the shoreline is inverted,
///   - a speed multiplier applied to a body that was not going to move anyway.
///
/// THE CONTROL, and the reason this file is longer than it looks like it should be. The naive
/// assertion — "an enemy in water moves slower than its MoveSpeed" — passes perfectly on an
/// enemy that does not move at all, which is exactly how the wall-collision gate lied for a
/// milestone (HANDOVER §4). So every speed claim here is made twice: once at high tide and
/// once at low tide with the same body on the same tile, and the low-tide run must show full
/// movement. A test that cannot distinguish "slowed" from "frozen" is not measuring the tide.
/// </summary>
public sealed partial class TideTest : Node2D
{
    private int _failures;

    private const float Dt = 1f / 60f;
    private const int TileSize = 16;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" THE TIDE");
        GD.Print("================================================================");

        TestCycleOscillates();
        TestCycleIsPredictable();
        TestShorelineOrdering();
        TestDryFloorCostsNothing();
        TestWadersAreSlowedAndSwimmersAreNot();
        TestDrenchedLingers();
        TestTheDashDoesNotOutrunTheTide();
        TestSwimmersAreOnlyOnWetFloors();
        TestEveryRoomGetsAWaterline();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " THE TIDE: PASS" : $" THE TIDE: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    // ---------------------------------------------------------------- The clock

    /// <summary>It must reach both ends. A cycle stuck near its midpoint is water that is
    /// always half in, which is no rhythm at all.</summary>
    private void TestCycleOscillates()
    {
        var tide = new TideCycle();
        float lo = 1f, hi = 0f;

        for (int t = 0; t < Mathf.CeilToInt(Tune.TidePeriod / Dt); t++)
        {
            tide.Tick(Dt);
            lo = Mathf.Min(lo, tide.Level);
            hi = Mathf.Max(hi, tide.Level);
        }

        Check(hi > 0.99f, $"the tide comes fully in (peak {hi:F3})");
        Check(lo < 0.01f, $"the tide goes fully out (trough {lo:F3})");
        Check(hi - lo > 0.9f, $"and the swing is the whole range ({hi - lo:F3})");
    }

    /// <summary>
    /// docs/07 §3 — "predictable and can be planned around". Concretely: the same elapsed time
    /// gives the same water, every cycle, forever. A tide that drifts is a tide the player
    /// cannot learn, and drift is exactly what an accumulating float does if the wrap is wrong.
    /// </summary>
    private void TestCycleIsPredictable()
    {
        var a = new TideCycle();
        var b = new TideCycle();

        // Run one of them for six extra whole cycles. If the wrap is sound they end level.
        int ticksPerCycle = Mathf.RoundToInt(Tune.TidePeriod / Dt);
        for (int c = 0; c < 6; c++)
            for (int t = 0; t < ticksPerCycle; t++) a.Tick(Dt);

        for (int t = 0; t < 137; t++) { a.Tick(Dt); b.Tick(Dt); }

        Check(Mathf.Abs(a.Level - b.Level) < 0.02f,
              $"the cycle does not drift over six periods ({a.Level:F4} vs {b.Level:F4})");

        // And the turn countdown has to be a countdown, not a number that jumps around.
        var c2 = new TideCycle();
        float prev = c2.SecondsUntilTurn;
        int rises = 0;
        for (int t = 0; t < ticksPerCycle; t++)
        {
            c2.Tick(Dt);
            float now = c2.SecondsUntilTurn;
            if (now > prev + 0.001f) rises++;   // only legal when the tide has just turned
            prev = now;
        }
        Check(rises <= 2, $"the time-to-turn readout counts down ({rises} resets in a cycle)");
    }

    // ---------------------------------------------------------------- The field

    /// <summary>
    /// The shoreline sweeps in ONE direction: a tile that floods early must never be dry
    /// while a tile that floods late is wet. Get the comparison backwards and the water still
    /// moves — it just moves out as the level rises, which looks like a working tide until
    /// someone tries to plan around it.
    /// </summary>
    private void TestShorelineOrdering()
    {
        TideField field = MakeField(out _);

        bool everInverted = false;
        int everWet = 0;

        for (float level = 0f; level <= 1.0001f; level += 0.01f)
        {
            bool shallow = field.IsSubmergedTile(2, 2, level);   // flood level 1, floods first
            bool deep = field.IsSubmergedTile(2, 5, level);      // flood level 4, floods last
            if (deep && !shallow) everInverted = true;
            if (shallow) everWet++;
        }

        Check(!everInverted, "the shoreline never inverts — early tiles flood before late ones");
        Check(everWet > 0, "and something floods at all (control: the field is not inert)");
        Check(!field.IsSubmergedTile(2, 2, 0f), "at dead low tide, nothing is under water");
        Check(field.IsSubmergedTile(2, 5, 1f), "at full high tide, even the last band is under");
    }

    /// <summary>A floor with no authored water must cost nothing and change nothing. Every
    /// floor but the Wharfs is this case, so it is the one that runs most.</summary>
    private void TestDryFloorCostsNothing()
    {
        var dry = new TideField(32, 32, TileSize, Vector2.Zero);
        Check(!dry.AnyWater, "a floor with no authored water reports no water");
        Check(dry.WaterTiles == 0, "and counts zero water tiles");
        Check(!dry.IsSubmerged(new Vector2(64f, 64f), 1f),
              "and nothing is submerged even at full tide");
    }

    // ---------------------------------------------------------------- The asymmetry

    /// <summary>
    /// The mechanic itself: a wader is slowed by water and a swimmer is sped up by it, and
    /// BOTH are unaffected by the same tile when the tide is out.
    ///
    /// Driven through a real <see cref="EnemyManager"/> rather than by calling the multiplier
    /// function, because the multiplier being right is not the thing that has ever broken —
    /// the wiring is. FloorRunner not setting AttackTokens at all was this same shape of bug.
    /// </summary>
    private void TestWadersAreSlowedAndSwimmersAreNot()
    {
        EnemyData? baseData = GD.Load<EnemyData>("res://data/enemies/cellar_ghoul.tres");
        if (baseData is null) { Check(false, "cellar_ghoul.tres loads"); return; }

        var wader = (EnemyData)baseData.Duplicate();
        wader.SwimsInWater = false;
        var swimmer = (EnemyData)baseData.Duplicate();
        swimmer.SwimsInWater = true;

        float dryWade = MeasureTravel(wader, tideLevel: 0f);
        float wetWade = MeasureTravel(wader, tideLevel: 1f);
        float drySwim = MeasureTravel(swimmer, tideLevel: 0f);
        float wetSwim = MeasureTravel(swimmer, tideLevel: 1f);

        // THE CONTROL. Without this, everything below passes on a motionless enemy.
        Check(dryWade > 1f, $"control: a wader actually moves on dry ground ({dryWade:F1}px)");
        Check(drySwim > 1f, $"control: a swimmer actually moves on dry ground ({drySwim:F1}px)");

        // Against the Tune values rather than against "less" and "more", so the gate reports
        // the multiplier the game is actually applying. A tide that slowed waders by 2% would
        // satisfy "slower" and be no mechanic at all.
        //
        // BUT these two checks derive their expectation FROM Tune, so they can only catch the
        // wiring coming loose — never a bad number. Setting TideSwimSpeedMultiplier to 1f and
        // running this gate prints "speeds a swimmer to x1.00 (want x1.00)" and passes, which
        // is a test agreeing with a broken game. The absolute check below is what actually
        // holds the design: it names a floor the mechanic has to clear no matter what the
        // constants say, and it is the one that failed when the multiplier was sabotaged.
        float wadeRatio = wetWade / dryWade;
        float swimRatio = wetSwim / drySwim;

        Check(Mathf.Abs(wadeRatio - Tune.TideWadeSpeedMultiplier) < 0.05f,
              $"water slows a wader to x{wadeRatio:F2} (want x{Tune.TideWadeSpeedMultiplier:F2}) " +
              $"— {dryWade:F1}px dry -> {wetWade:F1}px submerged");
        Check(Mathf.Abs(swimRatio - Tune.TideSwimSpeedMultiplier) < 0.05f,
              $"the same water speeds a swimmer to x{swimRatio:F2} " +
              $"(want x{Tune.TideSwimSpeedMultiplier:F2}) — {drySwim:F1}px -> {wetSwim:F1}px");
        Check(wetSwim > wetWade * 2f,
              $"and the gap between them is the mechanic ({wetWade:F1}px vs {wetSwim:F1}px)");

        // The tide must not be a blanket slow that happens to favour swimmers: at low tide
        // the two are the same creature.
        Check(Mathf.Abs(dryWade - drySwim) < dryWade * 0.05f,
              $"with the tide out they are identical ({dryWade:F1}px vs {drySwim:F1}px)");
    }

    /// <summary>
    /// Walk one enemy across a flooded floor for a second and report how far it got.
    ///
    /// The enemy is placed in water and the player target is placed further into it, so the
    /// whole path is submerged — measuring a body that leaves the water halfway would report
    /// an average of two states and hide either one.
    /// </summary>
    private float MeasureTravel(EnemyData data, float tideLevel)
    {
        var bounds = new Rect2(-1000, -1000, 2000, 2000);
        var bullets = new BulletManager { Bounds = bounds };
        AddChild(bullets);

        var manager = new EnemyManager();
        AddChild(manager);
        manager.Initialise(bullets, bullets, bounds, new Rng(7));

        TideField field = MakeField(out Vector2 wetPoint);
        manager.Water = field;
        manager.TideLevel = tideLevel;

        Enemy e = manager.Spawn(data, wetPoint);
        Vector2 start = e.Position;

        // The target is deep in the same water, far enough that the enemy never arrives and
        // switches to an attack state mid-measurement.
        //
        // 12 tiles was not far enough and the failure was silent in the right direction: the
        // swimmer covered 186.7px of a 192px gap in the measured second, decelerated into its
        // attack state, and reported 1.50x instead of 2.0x. Every assertion still passed. A
        // measurement that lands just inside a threshold because the subject ran out of room
        // is the kind that starts failing later for reasons nobody connects to this line.
        manager.PlayerPosition = wetPoint + new Vector2(0f, 24f * TileSize);

        for (int t = 0; t < 60; t++) manager._PhysicsProcess(Dt);

        float travelled = e.Position.DistanceTo(start);

        manager.QueueFree();
        bullets.QueueFree();
        return travelled;
    }

    /// <summary>
    /// A field whose whole interior is water, banded so flood level rises with Y. Returns a
    /// point inside the deepest band, which is submerged only at full tide.
    /// </summary>
    private static TideField MakeField(out Vector2 wetPoint)
    {
        var field = new TideField(48, 48, TileSize, Vector2.Zero);

        // Rows 2..5 carry flood levels 1..4; everything below row 5 stays at 4 so a body can
        // walk a long way without leaving the water.
        for (int y = 2; y < 40; y++)
        {
            int level = Mathf.Min(TideField.MaxFloodLevel, Mathf.Max(1, y - 1));
            for (int x = 1; x < 40; x++) field.SetFlood(x, y, level);
        }

        wetPoint = new Vector2(20f * TileSize, 20f * TileSize);
        return field;
    }

    // ---------------------------------------------------------------- Drenched

    /// <summary>
    /// docs/03 §Elements — Drenched follows you out of the water and then dries. Both halves
    /// matter: one that never expires is a permanent debuff, and one that expires instantly is
    /// indistinguishable from wading.
    /// </summary>
    private void TestDrenchedLingers()
    {
        var player = new Player.PlayerController();
        AddChild(player);

        Check(!player.IsDrenched, "a dry player is not Drenched");

        player.Drench();
        Check(player.IsDrenched, "touching water Drenches");
        Check(player.IncomingLightningMultiplier > 1f,
              $"and Drenched raises lightning damage taken (x{player.IncomingLightningMultiplier:F2})");

        // Half the duration: still wet, having long since left the water.
        float half = Tune.DrenchedDuration * 0.5f;
        StepDrenched(player, half);
        Check(player.IsDrenched, $"still Drenched {half:F1}s after leaving the water");

        StepDrenched(player, Tune.DrenchedDuration);
        Check(!player.IsDrenched, "and dry again once the duration elapses");
        Check(Mathf.IsEqualApprox(player.IncomingLightningMultiplier, 1f),
              "with the lightning penalty gone with it");

        player.QueueFree();
    }

    private static void StepDrenched(Player.PlayerController player, float seconds)
    {
        for (int t = 0; t < Mathf.CeilToInt(seconds / Dt); t++) player.TickDrenched(Dt);
    }

    // ---------------------------------------------------------------- The dash

    /// <summary>
    /// The Blink Step is slowed by water, and its FRAME DATA is not touched.
    ///
    /// Both halves are the assertion. Water slowing the dash is what stops the tide being
    /// optional — the dodge is free post-F4, so an unslowed dash crossed a channel faster
    /// than wading and with invulnerability, which made "hold SPACE" the counter-play to the
    /// whole floor. And docs/02 §4 calls the 24-frame + 0.12s cycle an invariant that must be
    /// protected, so a fix that bought the first property by spending the second would be a
    /// worse bug than the one it replaced.
    ///
    /// This is the gap that existed for four commits with the tide fully gated: every
    /// assertion was about walking, so nothing looked wrong. It was found by playing.
    /// </summary>
    private void TestTheDashDoesNotOutrunTheTide()
    {
        var dry = new Player.PlayerController();
        AddChild(dry);
        var wet = new Player.PlayerController();
        AddChild(wet);

        // Same everything except the ground underfoot.
        wet.TerrainSpeedMultiplier = Tune.TideWadeSpeedMultiplier;

        (float dryDist, int dryFrames, int dryInvuln) = DriveDash(dry);
        (float wetDist, int wetFrames, int wetInvuln) = DriveDash(wet);

        Check(dryDist > 1f, $"control: a dash on dry ground covers ground ({dryDist:F1}px)");

        float ratio = dryDist > 0f ? wetDist / dryDist : 0f;
        Check(Mathf.Abs(ratio - Tune.TideWadeSpeedMultiplier) < 0.05f,
              $"water shortens the dash to x{ratio:F2} (want x{Tune.TideWadeSpeedMultiplier:F2}) " +
              $"— {dryDist:F1}px dry -> {wetDist:F1}px wading");

        // THE OTHER HALF. Distance may move; the cycle may not (docs/02 §4).
        Check(dryFrames == wetFrames,
              $"and the cycle is identical wet or dry ({dryFrames} vs {wetFrames} frames)");
        Check(dryInvuln == wetInvuln && wetInvuln == Tune.BlinkInvulnFrames,
              $"a dash in water keeps all {Tune.BlinkInvulnFrames} invulnerable frames " +
              $"({wetInvuln}) — water costs distance, not safety");

        dry.QueueFree();
        wet.QueueFree();
    }

    /// <summary>
    /// Drive one dash to completion, reporting distance covered, total frames and
    /// invulnerable frames.
    ///
    /// Through the real _PhysicsProcess and TryBeginBlink, exactly as BlinkTest does — and
    /// for the reason recorded in HANDOVER §4, which is that synthetic Input reports "just
    /// pressed" on every manually driven tick and made the first frame measurement in this
    /// project read 3/49/3 against a real 2/14/8.
    /// </summary>
    private static (float Distance, int Frames, int Invuln) DriveDash(Player.PlayerController p)
    {
        if (!p.TryBeginBlink()) return (0f, -1, -1);

        // Distance is INTEGRATED FROM VELOCITY rather than read off the transform. A bare
        // controller has no collision shape — the owning scene adds it — and MoveAndSlide
        // against no shape in a physics world nothing has stepped moves the body zero pixels,
        // which the first version of this reported as a 0.0px dash on dry land. Velocity is
        // also the exact quantity the multiplier scales, so this measures the thing under
        // test rather than a consequence of it two systems downstream.
        float distance = 0f;
        int frames = 0, invuln = 0;

        while (frames < 240)
        {
            p._PhysicsProcess(Dt);
            if (p.Phase == Player.BlinkPhase.None) break;
            if (p.Phase == Player.BlinkPhase.Invulnerable) invuln++;
            distance += p.Velocity.Length() * Dt;
            frames++;
        }

        return (distance, frames, invuln);
    }

    // ---------------------------------------------------------------- The waterline

    /// <summary>
    /// Every body of water on the floor shows its own surface.
    ///
    /// THE BUG THIS ENCODES. The waterline used to be "the top row of the band", computed as
    /// a single minimum Y across the WHOLE FLOOR — so the bright shore edge was drawn only in
    /// whichever room happened to contain the topmost water tile, and every other room's water
    /// had no surface at all. The flood demo floods every room identically, so the one room
    /// that drew it looked right and nothing looked wrong.
    ///
    /// The assertion is per-room, because that is the axis the bug lived on. A floor-wide
    /// count would have passed the broken version too — it drew edges, just not in the right
    /// places, which is the difference between "some output" and "correct output".
    /// </summary>
    private void TestEveryRoomGetsAWaterline()
    {
        var gen = new Generation.FloorGenerator(
            Generation.UndercroftContent.Flows(), Generation.UndercroftContent.Rooms());

        Generation.GeneratedFloor? floor = gen.Generate(Hash.ParseSeed("tide"), 2, out string failure);
        if (floor is null) { Check(false, $"a floor generates for the waterline test ({failure})"); return; }

        var geometry = new Rooms.FloorGeometry(floor);
        geometry.FloodDemo(floor);

        // Count the rooms that hold water, and the rooms that show a surface. With the demo
        // every room is flooded, so the two must agree.
        int roomsWithWater = 0, roomsWithWaterline = 0;
        foreach (Generation.PlacedRoom r in floor.Rooms)
        {
            Rect2 bounds = geometry.RoomRectWorld(r);
            bool water = false, line = false;

            for (int band = 1; band <= TideField.MaxFloodLevel; band++)
            {
                foreach (Rect2 w in geometry.BuildWaterRects(band))
                    if (bounds.HasPoint(w.Position + w.Size * 0.5f)) { water = true; break; }
                foreach (Rect2 w in geometry.BuildWaterEdgeRects(band))
                    if (bounds.HasPoint(w.Position + w.Size * 0.5f)) { line = true; break; }
            }

            if (water) roomsWithWater++;
            if (line) roomsWithWaterline++;
        }

        Check(roomsWithWater > 1,
              $"control: the test floor has water in more than one room ({roomsWithWater})");
        Check(roomsWithWaterline == roomsWithWater,
              $"every room with water shows a waterline " +
              $"({roomsWithWaterline} of {roomsWithWater} rooms)");
    }

    // ---------------------------------------------------------------- The roster

    /// <summary>
    /// A swimmer must not appear on a floor with no water.
    ///
    /// This is the failure the whole floor-gating change exists to prevent, and it is
    /// invisible from every other angle: a Deep One dropped into the Undercroft spawns, paths,
    /// claws and dies exactly like a Cellar Ghoul with a worse silhouette. Nothing errors, the
    /// encounter budget balances, the autorun wins the run. It is simply the wrong monster,
    /// and the only thing that would ever have caught it is somebody noticing.
    ///
    /// Asserted both ways, because "floor 1 has no swimmers" is also satisfied by a bestiary
    /// that has no swimmers at all — which is what the game shipped with until this commit.
    /// </summary>
    private void TestSwimmersAreOnlyOnWetFloors()
    {
        var floor1 = Bestiary.ForFloor(1);
        var floor2 = Bestiary.ForFloor(2);

        int swimmersOn1 = 0;
        foreach (EnemyData d in floor1) if (d.SwimsInWater) swimmersOn1++;
        int swimmersOn2 = 0;
        foreach (EnemyData d in floor2) if (d.SwimsInWater) swimmersOn2++;

        Check(floor1.Count > 0, $"floor 1 has a roster ({floor1.Count} enemies)");
        Check(swimmersOn1 == 0, $"no swimmer is on the waterless Undercroft ({swimmersOn1} found)");

        // The control. Without it the check above passes on a game with no swimmers at all.
        Check(swimmersOn2 > 0,
              $"control: the Wharfs do have swimmers ({swimmersOn2} of {floor2.Count})");
        Check(floor2.Count > floor1.Count,
              $"and floor 2 adds to the roster rather than replacing it " +
              $"({floor1.Count} -> {floor2.Count})");

        // Every floor must be able to fill a room, or the encounter director reports a
        // satisfied budget for an empty one.
        for (int f = 1; f <= FloorScaling.DeepestFloor; f++)
        {
            if (Bestiary.ForFloor(f).Count != 0) continue;
            Check(false, $"floor {f} has no enemies at all");
            return;
        }
        Check(true, $"every floor to {FloorScaling.DeepestFloor} has enemies to spawn");
    }
}
