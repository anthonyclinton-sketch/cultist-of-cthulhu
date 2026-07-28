using System;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// The M0 gate, as an automated measurement rather than an eyeballed overlay
/// (docs/11 §2 exit criteria).
///
///   godot --path . --headless res://scenes/debug/Benchmark.tscn --seed cthulhu
///
/// Three numbers decide the gate, all at the full 4096-bullet array capacity — which is
/// a deliberate ~6.8x overload of the 600-bullet design ceiling in docs/05 R7:
///
///   1. BulletManager._PhysicsProcess cost           budget 0.40 ms  (docs/09 §8)
///   2. BulletManager._Process buffer build/upload   budget 0.60 ms
///   3. Bytes allocated per tick in steady state     budget ZERO     (docs/09 §8)
///
/// Reports p99 rather than absolute worst: a single 3ms spike in a headless process is
/// OS scheduling noise, and gating on it would make the test flaky in CI without telling
/// us anything about the code.
///
/// Exits non-zero on failure so CI can consume it.
/// </summary>
public sealed partial class Benchmark : Node2D
{
    private const int WarmupFrames = 120;
    private const int MeasureFrames = 600;

    /// <summary>Ticks after warmup that are measured for TIME but excluded from the
    /// ALLOCATION gate, so tiered-JIT promotion of the tick path cannot fail the run.</summary>
    private const int AllocSettleFrames = 60;

    private const int TargetBullets = Tune.MaxBullets;
    private const double PhysicsBudgetMs = 0.40;
    private const double RenderBudgetMs = 0.60;

    private BulletManager _bullets = null!;
    private Rng _rng = null!;

    private int _frame;
    private readonly double[] _physSamples = new double[MeasureFrames];
    private readonly double[] _renderSamples = new double[MeasureFrames];
    private int _samples;

    private long _allocBaseline;
    private long _lastAlloc;
    private int _allocatingTicks;
    private int _worstTickAlloc;
    private int _worstTickFrame = -1;
    private int _gen0AtStart, _gen1AtStart, _gen2AtStart;

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "benchmark");

        _bullets = new BulletManager
        {
            Name = nameof(BulletManager),
            Bounds = new Rect2(-100000, -100000, 200000, 200000),   // nothing expires by leaving

            // The target must be VULNERABLE, or `canHit` is false and the circle-circle
            // test — the actual hot path — is branched over and never measured. It sits
            // far outside the bullet field so the distance test runs for every real
            // bullet every tick and never passes: exactly the common case in play.
            TargetPosition = new Vector2(50000, 50000),
            TargetRadius = Tune.PlayerHitboxRadius,
            TargetInvulnerable = false,
        };
        AddChild(_bullets);

        FillToCapacity();

        GD.Print("================================================================");
        GD.Print(" CULTIST OF CTHULHU — M0 BULLET GATE");
        GD.Print("================================================================");
        GD.Print($" seed            {Hash.FormatSeed(GameRoot.Instance.RunSeed)}");
        GD.Print($" bullets         {_bullets.Count} / {_bullets.Capacity}   " +
                 $"({_bullets.Count / (float)Tune.EnemyBulletDesignCap:F1}x the {Tune.EnemyBulletDesignCap}-bullet design ceiling)");
        // ShadowCount is only populated once _Process has run, so it is reported at the end.
        GD.Print($" renderer        {(DisplayServer.GetName() == "headless" ? "headless — sim + buffer build, no GPU" : DisplayServer.GetName())}");
        GD.Print($" warmup {WarmupFrames}   measure {MeasureFrames}   alloc settle {AllocSettleFrames}");
        GD.Print("----------------------------------------------------------------");
    }

    /// <summary>
    /// Fill with long-lived bullets on slow orbits so the count stays pinned at capacity
    /// for the whole measurement. A benchmark whose bullet count drifts is measuring the
    /// wrong thing. 25% are hallucinations, matching the Unravelled band (docs/02 §3.4) —
    /// the worst case for the shadow layer, since it must branch per bullet.
    /// </summary>
    private void FillToCapacity()
    {
        while (_bullets.Count < TargetBullets)
        {
            float a = _rng.NextAngle();
            float r = _rng.Range(40f, 900f);
            var pos = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            var vel = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * _rng.Range(20f, 120f);
            var flags = _rng.NextFloat() < Tune.HallucinationRatioUnravelled
                ? BulletFlags.Hallucination
                : BulletFlags.None;

            if (!_bullets.Spawn(pos, vel, 4f, 100000f, Colors.White, 9f, flags)) break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        if (_frame == WarmupFrames)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _gen0AtStart = GC.CollectionCount(0);
            _gen1AtStart = GC.CollectionCount(1);
            _gen2AtStart = GC.CollectionCount(2);
            return;
        }
        if (_frame < WarmupFrames) return;

        int measured = _frame - WarmupFrames;

        // Time samples start immediately; allocation samples start after the settle window.
        if (measured == AllocSettleFrames)
        {
            _allocBaseline = GC.GetAllocatedBytesForCurrentThread();
            _lastAlloc = _allocBaseline;
        }
        else if (measured > AllocSettleFrames)
        {
            long now = GC.GetAllocatedBytesForCurrentThread();
            int tickAlloc = (int)(now - _lastAlloc);
            _lastAlloc = now;
            if (tickAlloc > 0)
            {
                _allocatingTicks++;
                if (tickAlloc > _worstTickAlloc)
                {
                    _worstTickAlloc = tickAlloc;
                    _worstTickFrame = measured;
                }
            }
        }

        if (_samples < MeasureFrames)
        {
            _physSamples[_samples] = _bullets.LastTickMicroseconds / 1000.0;
            _renderSamples[_samples] = _bullets.LastRenderMicroseconds / 1000.0;
            _samples++;
        }

        if (measured >= MeasureFrames) Report();
    }

    private static (double avg, double p50, double p99, double max) Stats(double[] data, int n)
    {
        var copy = new double[n];
        Array.Copy(data, copy, n);
        Array.Sort(copy);
        double sum = 0;
        for (int i = 0; i < n; i++) sum += copy[i];
        return (sum / n, copy[n / 2], copy[(int)(n * 0.99)], copy[n - 1]);
    }

    private void Report()
    {
        // Capture FIRST. Stats() allocates its sort buffers and GD.Print allocates strings;
        // reading the counter after them attributes the reporting code's own allocations to
        // the simulation. That is exactly the kind of measurement bug that makes a team
        // chase a phantom leak for a day.
        int allocSamples = Math.Max(1, _samples - AllocSettleFrames);
        long allocTotal = GC.GetAllocatedBytesForCurrentThread() - _allocBaseline;
        double allocPerTick = allocTotal / (double)allocSamples;

        var phys = Stats(_physSamples, _samples);
        var rend = Stats(_renderSamples, _samples);

        bool headless = DisplayServer.GetName() == "headless";

        GD.Print($" BulletManager._PhysicsProcess");
        GD.Print($"   avg {phys.avg:F4} ms   p50 {phys.p50:F4}   p99 {phys.p99:F4}   max {phys.max:F4}");
        GD.Print($" BulletManager._Process  (buffer build + upload)");
        GD.Print($"   avg {rend.avg:F4} ms   p50 {rend.p50:F4}   p99 {rend.p99:F4}   max {rend.max:F4}");
        GD.Print("----------------------------------------------------------------");
        GD.Print($" alloc steady    {allocPerTick:F2} B/tick over {allocSamples} ticks");
        GD.Print($" allocating ticks {_allocatingTicks} of {allocSamples}" +
                 (_worstTickFrame >= 0 ? $"   (worst {_worstTickAlloc} B at frame {_worstTickFrame})" : ""));
        GD.Print($" GC during run   gen0 {GC.CollectionCount(0) - _gen0AtStart}  " +
                 $"gen1 {GC.CollectionCount(1) - _gen1AtStart}  gen2 {GC.CollectionCount(2) - _gen2AtStart}");
        int hallucinated = _bullets.Count - _bullets.ShadowCount;
        GD.Print($" bullets held    {_bullets.Count}   overflow {_bullets.OverflowCount}");
        GD.Print($" shadow layer    {_bullets.ShadowCount} real / {hallucinated} hallucinated " +
                 $"({hallucinated * 100f / Mathf.Max(1, _bullets.Count):F1}%, target {Tune.HallucinationRatioUnravelled * 100:F0}%)");
        GD.Print("----------------------------------------------------------------");

        // Gate on p99, not max — see class comment.
        bool physPass = phys.p99 <= PhysicsBudgetMs;
        bool rendPass = rend.p99 <= RenderBudgetMs;
        bool allocPass = _allocatingTicks == 0;

        GD.Print($" [{(physPass ? "PASS" : "FAIL")}] sim p99      {phys.p99:F4} <= {PhysicsBudgetMs:F2} ms");
        GD.Print($" [{(rendPass ? "PASS" : "FAIL")}] render p99   {rend.p99:F4} <= {RenderBudgetMs:F2} ms" +
                 (headless ? "   (buffer build only; GPU submit not included)" : ""));
        GD.Print($" [{(allocPass ? "PASS" : "FAIL")}] zero steady-state allocation");

        bool pass = physPass && rendPass && allocPass;
        GD.Print("================================================================");
        GD.Print(pass ? " M0 BULLET GATE: PASS" : " M0 BULLET GATE: FAIL");
        GD.Print("================================================================");

        GetTree().Quit(pass ? 0 : 1);
    }
}
