using CultistOfCthulhu.Core;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Proves the invariants that make Ascension safe (docs/02 §6).
///
///   godot --path . --headless res://scenes/debug/AscensionTest.tscn
///
/// Ascension is the one system in the game where a balance mistake is not a balance
/// mistake but an exploit: it grants invulnerability, and Fable's review found the
/// original spec allowed it to be farmed forever for a cost that converged to zero. Low
/// health became SAFER than high health, inverting the entire damage model.
///
/// These are therefore correctness tests, not tuning checks, and they belong in CI.
/// </summary>
public sealed partial class AscensionTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" ASCENSION INVARIANTS");
        GD.Print("================================================================");

        TestSpendingToZeroTriggers();
        TestDrainingToZeroTriggers();
        TestDurationDiminishes();
        TestCannotBeFarmed();
        TestLowHealthIsNotCheaper();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " ASCENSION INVARIANTS: PASS" : $" ASCENSION INVARIANTS: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool condition, string what)
    {
        if (condition) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    // ---------------------------------------------------------------- The bug that started this

    private void TestSpendingToZeroTriggers()
    {
        var s = new SanitySystem();
        s.DebugSetCurrent(Tune.SanityBanishCost);          // exactly enough for one Banish
        bool afforded = s.TrySpend(Tune.SanityBanishCost);

        Check(afforded, "Banish at exactly its cost is affordable");
        Check(s.Current <= 0f, "spending everything reaches zero");
        Check(s.ConsumeAscensionTrigger(),
              "SPENDING to zero triggers Ascension (the Banish-at-45 bug)");
    }

    private void TestDrainingToZeroTriggers()
    {
        var s = new SanitySystem();
        s.DebugSetCurrent(8f);
        s.Drain(Tune.SanityHitCost);

        Check(s.Current <= 0f, "draining past zero floors at zero");
        Check(s.ConsumeAscensionTrigger(), "being HIT to zero triggers Ascension");

        // Path independence is the actual property under test — two routes to the same
        // state must behave identically, which is what the SetCurrent funnel guarantees.
        var a = new SanitySystem(); a.DebugSetCurrent(20f); a.TrySpend(20f);
        var b = new SanitySystem(); b.DebugSetCurrent(20f); b.Drain(20f);
        Check(a.ConsumeAscensionTrigger() == b.ConsumeAscensionTrigger(),
              "spend-to-zero and drain-to-zero are path-independent");
    }

    private void TestDurationDiminishes()
    {
        var c = new AscensionController();
        var s = new SanitySystem();

        float previous = float.MaxValue;
        bool monotonic = true;

        for (int i = 0; i < 6; i++)
        {
            float d = c.DurationForNext();
            if (d > previous) monotonic = false;
            previous = d;

            c.Begin(s);
            c.ResolveExit(s, 99f, 99f, out _, out _, out _);   // rich player, always pays
        }

        Check(monotonic, "Ascended duration never increases across a run");
        Check(previous <= Tune.AscensionMinDuration + 0.001f,
              $"duration floors at {Tune.AscensionMinDuration}s (got {previous}s)");
    }

    // ---------------------------------------------------------------- The core invariant

    /// <summary>
    /// The one that matters. A player who does nothing but Ascend must run out of
    /// resources, or invulnerability is infinite and free.
    /// </summary>
    private void TestCannotBeFarmed()
    {
        var c = new AscensionController();
        var s = new SanitySystem();

        float hearts = 3f, maxHearts = 3f;
        int ascensions = 0;
        bool died = false;

        for (int i = 0; i < 50; i++)
        {
            c.Begin(s);
            c.ResolveExit(s, hearts, maxHearts, out float deduct, out float debt, out bool defaulted);

            hearts = Mathf.Max(Tune.AscensionHeartFloor, hearts - deduct);
            if (debt > 0f)
            {
                maxHearts = Mathf.Max(Tune.AscensionMinContainers, maxHearts - debt);
                hearts = Mathf.Min(hearts, maxHearts);
            }
            ascensions++;

            if (defaulted) { died = true; break; }
        }

        GD.Print($"        farming loop ended after {ascensions} ascensions " +
                 $"(hearts {hearts:F1}/{maxHearts:F1}, max sanity {s.Max:F0})");

        Check(died, "repeated Ascension eventually defaults and kills the player");
        Check(ascensions <= 8, $"Ascension count per run is hard-bounded (got {ascensions})");
    }

    /// <summary>
    /// Fable's specific finding: "cannot kill you, floors at half a heart" meant that at
    /// low health the heart cost simply vanished, so the cheapest place to Ascend was at
    /// death's door. The debt rule must make low health cost MORE, not less.
    /// </summary>
    private void TestLowHealthIsNotCheaper()
    {
        var rich = new AscensionController();
        var poor = new AscensionController();
        var s1 = new SanitySystem();
        var s2 = new SanitySystem();

        rich.Begin(s1);
        rich.ResolveExit(s1, currentHearts: 6f, maxHearts: 6f, out float rDeduct, out float rDebt, out _);

        poor.Begin(s2);
        poor.ResolveExit(s2, currentHearts: 0.5f, maxHearts: 6f, out float pDeduct, out float pDebt, out _);

        float richTotal = rDeduct + rDebt * 2f;   // permanent loss weighted double
        float poorTotal = pDeduct + pDebt * 2f;

        GD.Print($"        healthy: −{rDeduct:F1} hearts, −{rDebt:F1} max   " +
                 $"|   near-death: −{pDeduct:F1} hearts, −{pDebt:F1} max");

        Check(pDebt > rDebt, "Ascending at low health costs PERMANENT max hearts");
        Check(poorTotal >= richTotal, "Ascending at low health is never cheaper than at full health");
    }
}
