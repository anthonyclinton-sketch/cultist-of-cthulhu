using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Drives The Thing on the Doorstep through all three phases and asserts the fight
/// actually happens.
///
///   godot --path . --headless res://scenes/debug/BossTest.tscn
///
/// The failure this exists for is the one the project has hit repeatedly and which no
/// smoke test can see: a system that is fully wired, throws nothing, reports healthy
/// numbers, and is INERT. A boss that never leaves phase 1, never fires because its
/// pattern player is stuck in Telegraph, or never grabs because its cooldown and its range
/// can never both be satisfied, would boot cleanly, run for the full 600 frames of the
/// floor smoke test, and pass.
///
/// So the assertions are about behaviour over time rather than about state at rest: did it
/// reach every phase, did every phase emit bullets, did the grab connect against a target
/// that does not dodge, and did it die.
/// </summary>
public sealed partial class BossTest : Node2D
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" BOSS — THE THING ON THE DOORSTEP");
        GD.Print("================================================================");

        var data = GD.Load<BossData>("res://data/bosses/thing_on_the_doorstep.tres");
        if (data is null)
        {
            Check(false, "boss data loads");
            Finish();
            return;
        }

        Check(data.Validate() is null, $"boss data validates ({data.Validate() ?? "ok"})");

        RunFight(data);
        TestGrabConnects(data);
        TestTelegraphsAreReadable(data);

        Finish();
    }

    private void Finish()
    {
        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " BOSS: PASS" : $" BOSS: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    /// <summary>
    /// The whole fight, at a fixed damage rate, against a target that circles at a constant
    /// radius. Not a simulation of a player — a simulation of TIME PASSING, which is all
    /// that is needed to prove the boss is not inert.
    /// </summary>
    private void RunFight(BossData data)
    {
        var bullets = new BulletManager { Bounds = new Rect2(-3000, -3000, 6000, 6000) };
        AddChild(bullets);

        var boss = new Boss(data, Vector2.Zero, bullets, new Rng(0xB055));
        var phasesSeen = new HashSet<int> { 1 };
        var bulletsPerPhase = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0 };
        int transitions = 0, grabs = 0;
        bool died = false;

        // 90 seconds at 60Hz. Long enough that a boss which simply refuses to advance is
        // distinguishable from one that is being fought slowly.
        const int Ticks = 5400;
        const float Dps = 22f;

        for (int t = 0; t < Ticks && !died; t++)
        {
            // A target orbiting at 200px: always in range of something, never standing still
            // enough for the strafe logic to degenerate.
            float a = t * 0.02f;
            var target = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 200f;
            var targetVel = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * 200f * 0.02f * 60f;

            int before = bullets.Count;
            boss.Tick(1f / 60f, target, targetVel);
            bullets._PhysicsProcess(1.0 / 60.0);

            int spawned = bullets.Count - before;
            if (spawned > 0) bulletsPerPhase[boss.Phase] += spawned;

            if (boss.GrabConnectedThisTick) grabs++;

            if (!boss.Invulnerable && boss.TakeDamage(Dps / 60f)) died = true;

            // Read the latch AFTER damage, the same way the room owner does. Reading it
            // before was what let the original per-tick flag look correct in isolation and
            // never fire in the game.
            int changed = boss.ConsumePhaseChange();
            if (changed > 0) { phasesSeen.Add(changed); transitions++; }
        }

        Check(phasesSeen.Contains(2), "reaches phase 2 (the inversion)");
        Check(phasesSeen.Contains(3), "reaches phase 3 (the passenger)");
        Check(transitions == 2, $"exactly two phase transitions ({transitions})");
        Check(died, "the fight ends — the boss can be killed");

        foreach (int phase in new[] { 1, 2, 3 })
        {
            Check(bulletsPerPhase[phase] > 0,
                  $"phase {phase} actually fired ({bulletsPerPhase[phase]} bullets)");
        }

        // Each phase must be a different fight, not the same one at a different tint. A
        // shared vocabulary across all three would pass every assertion above.
        Check(bulletsPerPhase[2] != bulletsPerPhase[1] && bulletsPerPhase[3] != bulletsPerPhase[2],
              $"phases differ in output ({bulletsPerPhase[1]} / {bulletsPerPhase[2]} / {bulletsPerPhase[3]})");

        bullets.QueueFree();
    }

    /// <summary>
    /// The grab is phase 3's whole identity (docs/05 §7), so it gets its own run with a
    /// STATIONARY target — if it cannot connect against something that never moves, it
    /// cannot connect at all, and the difference between "hard to land" and "impossible"
    /// is invisible in the full fight above.
    /// </summary>
    private void TestGrabConnects(BossData data)
    {
        var bullets = new BulletManager { Bounds = new Rect2(-3000, -3000, 6000, 6000) };
        AddChild(bullets);

        var boss = new Boss(data, new Vector2(180f, 0f), bullets, new Rng(0x6AB));
        // Drop it straight into phase 3.
        boss.TakeDamage(data.MaxHealth * (1f - data.Phase3At) + 1f);
        Check(boss.Phase == 3, $"damage past the phase-3 threshold enters phase 3 (phase {boss.Phase})");

        var target = Vector2.Zero;
        int grabs = 0;

        for (int t = 0; t < 1800; t++)
        {
            boss.Tick(1f / 60f, target, Vector2.Zero);
            bullets._PhysicsProcess(1.0 / 60.0);
            if (boss.GrabConnectedThisTick) grabs++;
        }

        Check(grabs > 0, $"the grab connects against a stationary target ({grabs} times in 30s)");

        // One connection per lunge. Without the latch it drains its cost every TICK of
        // contact, which at 60Hz empties the whole Sanity bar in half a second and reads as
        // an unavoidable instant kill.
        float perMinute = grabs / 30f * 60f;
        Check(perMinute <= 60f / data.GrabCooldown + 2f,
              $"grabs are rate-limited by the cooldown ({perMinute:F1}/min against a " +
              $"{60f / data.GrabCooldown:F1}/min ceiling)");

        bullets.QueueFree();
    }

    /// <summary>
    /// docs/05 R3 applies to bosses too. A boss volley with no wind-up is not a difficulty
    /// choice, it is a bug — and the boss's patterns are authored separately from the
    /// enemies', so the existing content gate covering data/patterns is the only thing that
    /// would have caught it and only because they happen to share a directory.
    /// </summary>
    private void TestTelegraphsAreReadable(BossData data)
    {
        int checkedCount = 0;
        for (int phase = 1; phase <= 3; phase++)
        {
            foreach (PatternData? p in data.PatternsFor(phase))
            {
                if (p is null) continue;
                checkedCount++;
                if (p.TelegraphSeconds >= 0.35f) continue;
                Check(false, $"phase {phase} pattern telegraphs in {p.TelegraphSeconds:F2}s (R3 minimum 0.35s)");
                return;
            }
        }
        Check(checkedCount > 0, $"every boss pattern clears the R3 telegraph minimum ({checkedCount} checked)");
    }
}
