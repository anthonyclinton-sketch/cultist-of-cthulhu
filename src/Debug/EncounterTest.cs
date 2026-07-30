using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Generation;
using CultistOfCthulhu.Rooms;
using CultistOfCthulhu.Sigils;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// The Dread Budget and the wave system (docs/06 §6).
///
///   godot --path . --headless res://scenes/debug/EncounterTest.tscn
///
/// Two things here are asserted because a doc asked for them by name, and one because the
/// design's own wording turns on a single word.
///
/// docs/06 §6.1 carries a review note requiring an explicit test that "a full 41-cell circle
/// of D/C sigils does not produce a higher playerPowerMult than a half-full circle of A/S
/// sigils" — the failure mode being that counting SIGILS rather than tier-weighted cells
/// taxes the player for engaging with the Circle at all.
///
/// docs/06 §6.2 says the next wave arrives when the current one is at 30% remaining,
/// "**never on a timer, so careful play is never punished**". A timer is the natural way to
/// write this and it is the wrong one: it means a cautious player fights wave two while wave
/// one is still alive, converting patience into a difficulty spike. So the test lets a full
/// wave stand alive for ten seconds and insists nothing else arrives.
/// </summary>
public sealed partial class EncounterTest : Node2D
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" ENCOUNTERS — DREAD BUDGET AND WAVES");
        GD.Print("================================================================");

        TestPowerClamp();
        TestTierWeightingBeatsCellCount();
        TestBudgetResponds();
        ReportFloorCurve();
        TestWaves();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " ENCOUNTERS: PASS" : $" ENCOUNTERS: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    private void TestPowerClamp()
    {
        float fresh = DreadBudget.PlayerPower(2f, 1, 0, 3f);
        float maxed = DreadBudget.PlayerPower(200f, 4, 20, 8f);

        Check(fresh >= DreadBudget.PowerMin - 0.001f && fresh <= DreadBudget.PowerMax,
              $"a fresh run sits near the floor of the clamp ({fresh:F2})");
        Check(maxed <= DreadBudget.PowerMax + 0.001f,
              $"an absurd build is clamped at {DreadBudget.PowerMax} ({maxed:F2})");
        Check(maxed > fresh, $"power responds to the build ({fresh:F2} -> {maxed:F2})");

        // Monotonic in each input, or a player is rewarded for NOT taking something.
        Check(DreadBudget.PlayerPower(40f, 1, 0, 3f) > DreadBudget.PlayerPower(20f, 1, 0, 3f),
              "more tier-weighted cells means more power");
        Check(DreadBudget.PlayerPower(20f, 3, 0, 3f) > DreadBudget.PlayerPower(20f, 1, 0, 3f),
              "a better weapon means more power");
        Check(DreadBudget.PlayerPower(20f, 1, 4, 3f) > DreadBudget.PlayerPower(20f, 1, 0, 3f),
              "more inscriptions means more power");
    }

    /// <summary>
    /// The assertion docs/06 §6.1's review note asks for by name.
    ///
    /// If the metric were a sigil or cell COUNT, a circle stuffed with cheap tiles would read
    /// as stronger than a half-empty circle of expensive ones — so solving the puzzle the
    /// Circle exists for would raise the difficulty of every later room, and it would fall
    /// hardest on the character whose whole identity is holding the most sigils.
    /// </summary>
    private void TestTierWeightingBeatsCellCount()
    {
        // A full circle of the cheapest tiles. 36 usable cells outside the Heart.
        float cheapCells = 36f * 1.0f;                       // D, tier multiplier 1.0
        float cheapC = 36f * 1.4f;                           // all C
        // Half a circle of the most expensive.
        float richA = 18f * 3.0f;                            // A
        float richS = 18f * 4.5f;                            // S

        float cheapPower = DreadBudget.PlayerPower(cheapCells, 1, 0, 3f);
        float cheapCPower = DreadBudget.PlayerPower(cheapC, 1, 0, 3f);
        float richAPower = DreadBudget.PlayerPower(richA, 1, 0, 3f);
        float richSPower = DreadBudget.PlayerPower(richS, 1, 0, 3f);

        Check(cheapPower < richAPower,
              $"a FULL circle of D sigils rates below a HALF circle of A ({cheapPower:F3} < {richAPower:F3})");
        Check(cheapCPower < richAPower,
              $"a full circle of C rates below a half circle of A ({cheapCPower:F3} < {richAPower:F3})");
        Check(cheapCPower < richSPower,
              $"a full circle of C rates below a half circle of S ({cheapCPower:F3} < {richSPower:F3})");

        // And the control: raw cell count would get this backwards, which is why the metric
        // is weighted. 36 cells is more cells than 18.
        Check(36f > 18f, "the control holds — by raw cell count the cheap circle would win");
    }

    private void TestBudgetResponds()
    {
        List<RoomTemplate> rooms = UndercroftContent.Rooms();
        RoomTemplate? hard = null;
        foreach (RoomTemplate t in rooms) if (t.Role == RoomRole.CombatHard) { hard = t; break; }
        if (hard is null) { Check(false, "a CombatHard template exists"); return; }

        float clean = DreadBudget.For(1, 0, hard, RoomRole.CombatHard, 0f, 1f);
        float corrupt = DreadBudget.For(1, 0, hard, RoomRole.CombatHard, 5f, 1f);
        float later = DreadBudget.For(1, 6, hard, RoomRole.CombatHard, 0f, 1f);
        float deeper = DreadBudget.For(3, 0, hard, RoomRole.CombatHard, 0f, 1f);

        // docs/06 §6.1 — (1 + 0.06 x Corruption). This term did not exist.
        Check(corrupt > clean, $"Corruption raises the budget ({clean:F0} -> {corrupt:F0} at 5)");
        Check(later > clean, $"progress within a floor raises it ({clean:F0} -> {later:F0} after 6 rooms)");
        Check(deeper > clean, $"a deeper floor raises it ({clean:F0} -> {deeper:F0} on floor 3)");

        // The authored ceiling still wins, or a room is asked to hold more than it can.
        float absurd = DreadBudget.For(6, 40, hard, RoomRole.CombatHard, 10f, DreadBudget.PowerMax);
        Check(absurd <= hard.ThreatCapacity + 0.01f,
              $"ThreatCapacity caps the result ({absurd:F0} <= {hard.ThreatCapacity:F0})");
    }

    /// <summary>
    /// Print the whole difficulty curve, floor by floor, and assert only its shape.
    ///
    /// docs/07 §2 has floors escalating in length and docs/06 §6.1 has base(floor) escalating
    /// the budget, but neither states the rate — so the numbers below are reported rather
    /// than gated, and the two things asserted are the ones the design clearly requires: a
    /// deeper floor must be harder than a shallower one at the same point, and the run must
    /// escalate overall.
    ///
    /// It is printed because the shape is the interesting part. Within-floor progression and
    /// floor-over-floor progression are separate terms and it is entirely possible for one to
    /// swamp the other, which no single assertion would reveal.
    /// </summary>
    private void ReportFloorCurve()
    {
        List<RoomTemplate> rooms = UndercroftContent.Rooms();
        RoomTemplate? med = null;
        foreach (RoomTemplate t in rooms) if (t.Role == RoomRole.CombatMed) { med = t; break; }
        if (med is null) { Check(false, "a CombatMed template exists"); return; }

        GD.Print($" budget curve on '{med.Id}' at Corruption 0, power 1.0 " +
                 $"(capacity {med.ThreatCapacity:F0}):");

        var first = new float[7];
        var last = new float[7];
        int capped = 0;

        for (int floor = 1; floor <= 6; floor++)
        {
            // Each floor's OWN last room, not a fixed tenth. Floors 3 and 4 run to 17 and 18
            // rooms (docs/07 §2), so sampling room 10 everywhere reported a deep floor's
            // MIDPOINT against a shallow floor's end and made the descent look flatter than
            // it is. The room count is part of the difficulty curve, not separate from it.
            int roomCount = Core.FloorScaling.TryRoomCount(floor, out _, out int max) ? max : 15;

            first[floor] = DreadBudget.For(floor, 0, med, RoomRole.CombatMed, 0f, 1f);
            last[floor] = DreadBudget.For(floor, roomCount - 1, med, RoomRole.CombatMed, 0f, 1f);
            bool wasCapped = DreadBudget.LastWasCapped;
            if (wasCapped) capped++;

            GD.Print($"   floor {floor}   first {first[floor],6:F0}   " +
                     $"last (room {roomCount,2}) {last[floor],6:F0}" +
                     (wasCapped ? "   [AT ROOM CAPACITY]" : ""));
        }

        Check(first[6] > first[1],
              $"a deeper floor opens harder than a shallower one ({first[1]:F0} -> {first[6]:F0})");
        Check(last[6] > last[1],
              $"and ends harder ({last[1]:F0} -> {last[6]:F0})");

        // THE SHAPE. The descent should be one curve, not six ramps — floor 6's peak being a
        // rounding error above floor 1's means the whole game happens on floor 1's difficulty.
        // Reported with a number rather than asserted against one, because the docs give no
        // target and this is a tuning judgement.
        float peakLift = last[1] > 0f ? last[6] / last[1] : 0f;
        GD.Print($"   peak lift floor 1 -> 6: x{peakLift:F2}");

        // WHERE THE DESCENT ACTUALLY LIVES, once the ceiling binds.
        //
        // Peak Dread per room stops being the measure the moment rooms saturate — and they
        // saturate from floor 2, because floor 1 is already tuned to nearly fill its rooms
        // (280 of a 320 capacity by its last room) and it plays well there. There is simply
        // no headroom above a well-tuned floor 1 for five more floors of "more enemies".
        //
        // What still varies, and what a player actually feels, is HOW MUCH OF THE FLOOR is
        // spent at that ceiling. Floor 1 reaches it in its last room or not at all; a deep
        // floor arrives at maximum pressure a third of the way in and stays there. That is a
        // real descent, and it is invisible in a peak-versus-peak comparison.
        GD.Print("   rooms until the ceiling binds (the real descent once rooms saturate):");
        for (int floor = 1; floor <= 6; floor++)
        {
            int roomCount = Core.FloorScaling.TryRoomCount(floor, out _, out int max) ? max : 15;
            int at = -1;
            for (int r = 0; r < roomCount; r++)
            {
                DreadBudget.For(floor, r, med, RoomRole.CombatMed, 0f, 1f);
                if (!DreadBudget.LastWasCapped) continue;
                at = r + 1;
                break;
            }

            GD.Print(at < 0
                ? $"     floor {floor}   never — peaks at {last[floor]:F0} of {med.ThreatCapacity:F0}"
                : $"     floor {floor}   room {at,2} of {roomCount,2}   " +
                  $"({(roomCount - at + 1) * 100 / roomCount}% of the floor at maximum pressure)");
        }

        if (capped > 0)
        {
            GD.Print($"   [note] {capped} of 6 floors hit '{med.Id}'s capacity " +
                     $"({med.ThreatCapacity:F0}). Past that, depth cannot buy difficulty with " +
                     $"Dread — only bigger rooms, or enemies worth more per point, can. Floors " +
                     $"3-6 have no authored rooms yet; that is where the headroom has to come " +
                     $"from.");
        }
    }

    /// <summary>The wave machinery, driven against a real floor.</summary>
    private void TestWaves()
    {
        var gen = new FloorGenerator(UndercroftContent.Flows(), UndercroftContent.Rooms());
        GeneratedFloor? floor = gen.Generate(Hash.ParseSeed("cthulhu"), 1, out string failure);
        if (floor is null) { Check(false, $"floor generated ({failure})"); return; }

        var geometry = new FloorGeometry(floor);
        TileMask mask = geometry.BuildSolidMask();
        Rect2 bounds = new(
            (floor.Bounds().Position.X - 4) * FloorGeometry.Tile,
            (floor.Bounds().Position.Y - 4) * FloorGeometry.Tile,
            (floor.Bounds().Size.X + 8) * FloorGeometry.Tile,
            (floor.Bounds().Size.Y + 8) * FloorGeometry.Tile);

        var roster = new List<EnemyData>();
        foreach (string p in new[]
                 {
                     "res://data/enemies/acolyte.tres", "res://data/enemies/cellar_ghoul.tres",
                     "res://data/enemies/tallow_man.tres", "res://data/enemies/netcaster.tres",
                     "res://data/enemies/chanter.tres",
                 })
        {
            var d = GD.Load<EnemyData>(p);
            if (d is not null) roster.Add(d);
        }
        if (roster.Count == 0) { Check(false, "the enemy roster loads"); return; }

        var enemyBullets = new BulletManager { Bounds = bounds, Walls = mask };
        var playerBullets = new BulletManager { Bounds = bounds, Walls = mask, CollideWithEnemies = true };
        var enemies = new EnemyManager();
        AddChild(enemyBullets);
        AddChild(playerBullets);
        AddChild(enemies);
        enemies.Initialise(enemyBullets, playerBullets, bounds, new Rng(0xE11));
        enemies.SetWalls(mask);

        PlacedRoom? hard = null;
        foreach (PlacedRoom r in floor.Rooms) if (r.Role == RoomRole.CombatHard) { hard = r; break; }
        if (hard is null) { Check(false, "the floor has a hard room"); return; }

        var director = new EncounterDirector(roster, enemies, geometry, new Rng(0xE2));
        Vector2 at = geometry.RoomAnchorWorld(hard);

        // A budget big enough to demand three waves.
        Check(director.Begin(hard, 260f, mask), "a hard room produces an encounter");
        Check(director.WaveCount >= 2, $"a 260-budget room splits into waves ({director.WaveCount})");

        int firstWave = enemies.AliveCount;
        Check(firstWave > 0, $"wave one spawned ({firstWave})");
        Check(director.WavesSpawned == 1, "only wave one has spawned");

        // THE "NEVER ON A TIMER" ASSERTION. Ten seconds with the whole wave alive.
        enemies.PlayerPosition = at;
        for (int t = 0; t < 600; t++)
        {
            director.Tick(1f / 60f, at);
            if (director.WavesSpawned > 1) break;
        }
        Check(director.WavesSpawned == 1,
              "wave two does NOT arrive on a timer — 10s with wave one alive and nothing came");
        Check(!director.Finished, "the room is not finished while waves remain unspawned");

        // Now kill wave one down past the 30% threshold and let the telegraph run.
        int toKill = firstWave - Mathf.Max(0, Mathf.FloorToInt(firstWave * 0.25f));
        int killed = 0;
        foreach (Enemy e in enemies.Enemies)
        {
            if (killed >= toKill) break;
            if (!e.Alive) continue;
            e.TakeDamage(99999f);
            killed++;
        }
        enemies._PhysicsProcess(1.0 / 60.0);

        int waveTwoArrivedAt = -1;
        for (int t = 0; t < 300; t++)
        {
            director.Tick(1f / 60f, at);
            if (director.WavesSpawned > 1) { waveTwoArrivedAt = t; break; }
        }

        Check(waveTwoArrivedAt >= 0, "killing wave one down to 30% brings wave two");

        // 0.6s of telegraph, at 60Hz, is 36 frames. Allow a tick either side.
        Check(waveTwoArrivedAt >= 34,
              $"wave two was telegraphed for ~0.6s before arriving ({waveTwoArrivedAt} frames) — docs/05 R4");

        // Fodder floor and Support cap, across the whole room rather than per wave.
        float total = 0f, fodder = 0f;
        int supports = 0;
        foreach (List<EnemyData> wave in director.Waves)
        {
            foreach (EnemyData d in wave)
            {
                total += d.DreadCost;
                if (d.Role == EnemyRole.Fodder) fodder += d.DreadCost;
                if (d.Role == EnemyRole.Support) supports++;
            }
        }

        Check(total > 0f && fodder / total >= 0.30f,
              $"at least 30% of the room's Dread is fodder ({fodder / Mathf.Max(1f, total):P0}) — " +
              "the Sanity economy constraint from docs/05 §2");
        Check(supports <= 1,
              $"a non-hub room holds at most one Support ({supports}) — docs/06 §6.1");

        director.Reset();
        enemies.QueueFree();
        enemyBullets.QueueFree();
        playerBullets.QueueFree();
    }
}
