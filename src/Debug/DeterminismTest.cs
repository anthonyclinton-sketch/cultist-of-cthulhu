using System;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// The other half of the M0 gate (docs/11 §2): same seed + same input replay produces a
/// byte-identical end state.
///
///   godot --path . --headless res://scenes/debug/DeterminismTest.tscn --seed cthulhu
///
/// docs/09 §9 calls this "the single best regression test the project can have", and the
/// reason is that it fails LOUDLY and EARLY for a whole class of bugs that are otherwise
/// nearly undiagnosable: an accidental use of a global RNG, a float accumulated across
/// ticks, a Dictionary iterated in hash order, a subsystem reading Time.
///
/// It runs two independent simulations in-process with identical scripted input and
/// compares a state hash every tick, so a divergence is reported at the exact tick it
/// first appears rather than at the end.
///
/// This must stay in CI from M1 onward — determinism that is not tested continuously is
/// determinism you do not have.
/// </summary>
public sealed partial class DeterminismTest : Node2D
{
    private const int Ticks = 1800;          // 30 seconds of simulation
    private const float Dt = 1f / 60f;

    public override void _Ready()
    {
        ulong seed = GameRoot.Instance.RunSeed;

        GD.Print("================================================================");
        GD.Print(" CULTIST OF CTHULHU — M0 DETERMINISM GATE");
        GD.Print("================================================================");
        GD.Print($" seed            {Hash.FormatSeed(seed)}");
        GD.Print($" ticks           {Ticks} ({Ticks / 60f:F0}s of simulation)");
        GD.Print("----------------------------------------------------------------");

        var a = new Sim(seed);
        var b = new Sim(seed);

        int divergedAt = -1;
        ulong hashA = 0, hashB = 0;

        for (int t = 0; t < Ticks; t++)
        {
            a.Step(t);
            b.Step(t);

            hashA = a.StateHash();
            hashB = b.StateHash();

            if (hashA != hashB)
            {
                divergedAt = t;
                break;
            }
        }

        // A different seed MUST produce a different result, or the test is vacuous — a
        // simulation that ignores its seed entirely would otherwise "pass" trivially.
        var c = new Sim(seed ^ 0xA5A5A5A5A5A5A5A5UL);
        for (int t = 0; t < Ticks; t++) c.Step(t);
        ulong hashC = c.StateHash();

        bool identical = divergedAt < 0;
        bool sensitive = hashC != hashA;

        GD.Print($" run A hash      {hashA:X16}   ({a.BulletCount} bullets, rng {a.RngHash():X16})");
        GD.Print($" run B hash      {hashB:X16}   ({b.BulletCount} bullets, rng {b.RngHash():X16})");
        GD.Print($" run C hash      {hashC:X16}   (different seed — must differ)");
        GD.Print("----------------------------------------------------------------");

        if (identical) GD.Print($" [PASS] identical seed -> identical state across {Ticks} ticks");
        else GD.PrintErr($" [FAIL] DIVERGED at tick {divergedAt}   A={hashA:X16}  B={hashB:X16}");

        GD.Print($" [{(sensitive ? "PASS" : "FAIL")}] different seed -> different state (test is not vacuous)");

        bool pass = identical && sensitive;
        GD.Print("================================================================");
        GD.Print(pass ? " M0 DETERMINISM GATE: PASS" : " M0 DETERMINISM GATE: FAIL");
        GD.Print("================================================================");

        GetTree().Quit(pass ? 0 : 1);
    }

    /// <summary>
    /// A headless simulation with no scene dependencies.
    ///
    /// Deliberately reimplements the integration rather than instantiating BulletManager,
    /// because BulletManager is a Node and needs a scene tree + MultiMesh. What is being
    /// verified here is that the RNG stream and the arithmetic are reproducible; the
    /// BulletManager-in-tree version of this test lands at M1 with the input replay
    /// harness, once there is a player to replay inputs for.
    /// </summary>
    private sealed class Sim
    {
        private const int Cap = 2048;
        private readonly float[] _x = new float[Cap];
        private readonly float[] _y = new float[Cap];
        private readonly float[] _vx = new float[Cap];
        private readonly float[] _vy = new float[Cap];
        private readonly float[] _life = new float[Cap];
        private int _count;

        private readonly Rng _rng;
        private readonly Rng _patternRng;
        private float _emitterAngle;

        public int BulletCount => _count;

        public Sim(ulong seed)
        {
            // Sub-seeded exactly as the real generator will be (docs/06 §7): two
            // independent streams, so consuming one cannot perturb the other.
            _rng = Hash.Derive(seed, "determinism.sim");
            _patternRng = Hash.Derive(seed, "determinism.pattern");
        }

        public ulong RngHash() => _rng.StateHash();

        public void Step(int tick)
        {
            // Scripted "input": a deterministic function of tick, standing in for the
            // recorded input stream the M1 replay harness will feed in.
            _emitterAngle += 0.037f;

            if (tick % 3 == 0)
            {
                int arms = 3 + (_patternRng.NextInt(0, 3));
                for (int a = 0; a < arms && _count < Cap; a++)
                {
                    float ang = _emitterAngle + a * (Mathf.Tau / arms) + _rng.Range(-0.05f, 0.05f);
                    _x[_count] = 0f;
                    _y[_count] = 0f;
                    _vx[_count] = Mathf.Cos(ang) * 95f;
                    _vy[_count] = Mathf.Sin(ang) * 95f;
                    _life[_count] = _rng.Range(2f, 5f);
                    _count++;
                }
            }

            int i = 0;
            while (i < _count)
            {
                _x[i] += _vx[i] * Dt;
                _y[i] += _vy[i] * Dt;
                _life[i] -= Dt;

                if (_life[i] <= 0f)
                {
                    int last = --_count;
                    _x[i] = _x[last]; _y[i] = _y[last];
                    _vx[i] = _vx[last]; _vy[i] = _vy[last];
                    _life[i] = _life[last];
                    continue;
                }
                i++;
            }
        }

        /// <summary>
        /// Order-independent, matching BulletManager.StateHash. Swap-remove permutes the
        /// arrays, so two identical simulations can legitimately hold the same bullets at
        /// different indices — a sequential hash would report false divergence.
        /// </summary>
        public ulong StateHash()
        {
            ulong acc = (ulong)_count * 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < _count; i++)
            {
                ulong h = 14695981039346656037UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(_x[i])) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(_y[i])) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(_vx[i])) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(_vy[i])) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(_life[i])) * 1099511628211UL;
                acc += h;
            }
            return acc;
        }
    }
}
