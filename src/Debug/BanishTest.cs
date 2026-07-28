using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Verifies Banish's radius clear (docs/02 §5.2).
///
///   godot --path . --headless res://scenes/debug/BanishTest.tscn
///
/// The specific risk being guarded: ClearRadius walks a dense array with swap-remove,
/// which moves the LAST element into the current index. Advancing the loop counter after
/// a removal silently skips that element — leaving survivors inside the blast radius.
/// The player experiences that as "Banish sometimes doesn't work", which is close to
/// unreportable and near-impossible to reproduce by hand, so it gets a test.
/// </summary>
public sealed partial class BanishTest : Node2D
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" BANISH");
        GD.Print("================================================================");

        var bullets = new BulletManager { Bounds = new Rect2(-5000, -5000, 10000, 10000) };
        AddChild(bullets);

        TestClearsEverythingInside(bullets);
        TestSpareseverythingOutside(bullets);
        TestDenseFieldLeavesNoSurvivors(bullets);
        TestAffordability();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " BANISH: PASS" : $" BANISH: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    private static void FillRing(BulletManager b, Vector2 centre, float radius, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * Mathf.Tau;
            var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            b.Spawn(p, Vector2.Zero, 4f, 100f, Colors.White, 9f);
        }
    }

    private void TestClearsEverythingInside(BulletManager b)
    {
        b.Clear();
        FillRing(b, Vector2.Zero, Tune.BanishRadius * 0.5f, 40);

        int cleared = b.ClearRadius(Vector2.Zero, Tune.BanishRadius);
        Check(cleared == 40, $"clears every bullet inside the radius (cleared {cleared}/40)");
        Check(b.Count == 0, $"no survivors remain (count {b.Count})");
    }

    private void TestSpareseverythingOutside(BulletManager b)
    {
        b.Clear();
        FillRing(b, Vector2.Zero, Tune.BanishRadius * 2f, 30);

        int cleared = b.ClearRadius(Vector2.Zero, Tune.BanishRadius);
        Check(cleared == 0, $"spares bullets outside the radius (cleared {cleared}, expected 0)");
        Check(b.Count == 30, $"outside bullets survive intact (count {b.Count}/30)");
    }

    /// <summary>
    /// The one that catches the swap-remove bug. Interleaving inside and outside bullets
    /// means a skipped index almost certainly leaves a survivor in the blast — a test that
    /// only fills the inside would pass even with the bug, because everything gets removed
    /// eventually regardless of order.
    /// </summary>
    private void TestDenseFieldLeavesNoSurvivors(BulletManager b)
    {
        b.Clear();
        var rng = new Rng(0xBEEF);
        int inside = 0;

        for (int i = 0; i < 2000; i++)
        {
            // Alternate near and far so removals and skips interleave.
            float r = i % 2 == 0
                ? rng.Range(0f, Tune.BanishRadius * 0.95f)
                : rng.Range(Tune.BanishRadius * 1.05f, Tune.BanishRadius * 3f);
            float a = rng.NextAngle();
            var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (r <= Tune.BanishRadius) inside++;
            b.Spawn(p, Vector2.Zero, 4f, 100f, Colors.White, 9f);
        }

        int before = b.Count;
        int cleared = b.ClearRadius(Vector2.Zero, Tune.BanishRadius);

        Check(cleared == inside, $"interleaved field: cleared {cleared}, expected {inside}");
        Check(b.Count == before - cleared, "surviving count is consistent");

        // Direct survivor check — the property that actually matters to the player.
        int survivorsInside = b.ClearRadius(Vector2.Zero, Tune.BanishRadius);
        Check(survivorsInside == 0,
              $"a second Banish at the same point clears nothing ({survivorsInside} survivors were left behind)");
    }

    private void TestAffordability()
    {
        // THE design property of the 45 cost: entering Fraying takes your panic button
        // away, at exactly the moment a fight has gone badly enough to want it.
        Check(Tune.SanityBanishCost > Tune.BandFraying,
              $"Banish ({Tune.SanityBanishCost}) is unaffordable at the Fraying boundary ({Tune.BandFraying})");

        // Late-floor Banish must be a major commitment rather than a freebie.
        //
        // This assertion previously read "Banish cost >= Lucid Ceiling floor", which
        // passed only because both happened to be 45. That was a COINCIDENCE, not a
        // design property, and raising the ceiling floor to 60 broke a test that was
        // encoding an accident. The real property is the RATIO: Banish should cost most
        // of what a late-floor player has to spend.
        float fractionOfLateCeiling = Tune.SanityBanishCost / Tune.LucidCeilingFloor;
        Check(fractionOfLateCeiling >= 0.6f,
              $"Banish costs {fractionOfLateCeiling * 100:F0}% of the late-floor ceiling " +
              $"({Tune.SanityBanishCost}/{Tune.LucidCeilingFloor}) — a real decision, not a freebie");

        // And it must drop you at least one band, or it is not a sacrifice.
        Check(Tune.LucidCeilingFloor - Tune.SanityBanishCost < Tune.BandFraying,
              "Banishing at the late-floor ceiling drops the player into Fraying or below");
    }
}
