using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Corruption costs something (docs/02 §7).
///
///   godot --path . --headless res://scenes/debug/CorruptionTest.tscn
///
/// THE PROPERTY THIS GUARDS is not "the thresholds have the right numbers" — it is that
/// Corruption is not free. It accrued from four sources for two milestones while the only
/// thing reading it was the loot-tier bump, which is a reward, so every price in the game
/// denominated in Corruption was a discount and a Corruption build was strictly upside.
/// docs/02 §7.3 calls this stat "the game's real difficulty selector"; a difficulty selector
/// that only makes the game easier is the single worst thing it could quietly become.
///
/// So the assertions are directional: as Corruption rises, something must get WORSE, and it
/// must get worse at every threshold the design names.
/// </summary>
public sealed partial class CorruptionTest : Node2D
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" CORRUPTION");
        GD.Print("================================================================");

        TestTierBoundaries();
        TestCostsRiseMonotonically();
        TestAwakenedEnemiesAreWorse();
        TestEveryEnemyHasAnAwakenedAttack();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " CORRUPTION: PASS" : $" CORRUPTION: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    /// <summary>The thresholds in docs/02 §7.2 are 1 / 3 / 5 / 7 / 10, and they must fire
    /// AT the stated value rather than above it — Banish grants 0.25 at a time, so a
    /// threshold that needed to be exceeded would be reached a quarter-point late.</summary>
    private void TestTierBoundaries()
    {
        Check(CorruptionTiers.TierFor(0f) == 0, "0 corruption is tier 0");
        Check(CorruptionTiers.TierFor(0.75f) == 0, "0.75 (three Banishes) has not reached tier 1");
        Check(CorruptionTiers.TierFor(1f) == 1, "exactly 1 reaches tier 1");
        Check(CorruptionTiers.TierFor(2.99f) == 1, "2.99 is still tier 1");
        Check(CorruptionTiers.TierFor(3f) == 3, "exactly 3 reaches tier 3");
        Check(CorruptionTiers.TierFor(5f) == 5, "exactly 5 reaches tier 5");
        Check(CorruptionTiers.TierFor(7f) == 7, "exactly 7 reaches tier 7");
        Check(CorruptionTiers.TierFor(10f) == 10, "exactly 10 reaches the Yellow Sign");
        Check(CorruptionTiers.TierFor(99f) == 10, "the Yellow Sign is the cap");
    }

    /// <summary>
    /// The directional test, and the one that actually matters.
    ///
    /// At every level, the cost side must be at least as bad as it was at the level below.
    /// If a future tuning pass ever makes a threshold a net improvement, this fails — which
    /// is the whole point, because that is exactly the state the stat has been in since M1
    /// and nothing noticed.
    /// </summary>
    private void TestCostsRiseMonotonically()
    {
        float worstSoFar = -1f;
        float bestLootSoFar = -1f;
        bool anyCost = false;

        for (float c = 0f; c <= 12f; c += 0.25f)
        {
            // A crude severity score: the things that make the run harder.
            float severity = (CorruptionTiers.EnemiesAwakened(c) ? 1f : 0f)
                             + CorruptionTiers.ExtraEnemiesPerRoom(c)
                             + (CorruptionTiers.YellowSign(c) ? 1f : 0f);

            if (severity < worstSoFar)
            {
                Check(false, $"severity fell at corruption {c} ({worstSoFar} -> {severity})");
                return;
            }
            if (severity > 0f) anyCost = true;
            worstSoFar = severity;

            float loot = CorruptionTiers.LootTierBumpChance(c);
            if (loot < bestLootSoFar)
            {
                Check(false, $"loot bump fell at corruption {c} ({bestLootSoFar} -> {loot})");
                return;
            }
            bestLootSoFar = loot;
        }

        Check(true, "severity never decreases as Corruption rises");
        Check(anyCost, "Corruption has any cost at all — it is not pure upside");

        // And the specific inversion that was live: at the first threshold the player got a
        // loot bump and nothing else. A reward-only tier is a free lunch.
        Check(CorruptionTiers.LootTierBumpChance(3f) > 0f && CorruptionTiers.EnemiesAwakened(3f),
              "the tier that improves loot to 45% also awakens the enemies");
    }

    /// <summary>An Awakened enemy must be measurably harder than a normal one — more health
    /// AND a second pattern, not one or the other.</summary>
    private void TestAwakenedEnemiesAreWorse()
    {
        var data = GD.Load<EnemyData>("res://data/enemies/acolyte.tres");
        if (data is null) { Check(false, "acolyte.tres loads"); return; }

        var bullets = new BulletManager { Bounds = new Rect2(-2000, -2000, 4000, 4000) };
        AddChild(bullets);

        var normal = new Enemy(1, data, Vector2.Zero, bullets, new Rng(1), awakened: false);
        var awake = new Enemy(2, data, Vector2.Zero, bullets, new Rng(1), awakened: true);

        Check(awake.MaxHealth > normal.MaxHealth,
              $"Awakened health {awake.MaxHealth:F1} > normal {normal.MaxHealth:F1}");
        Check(!normal.Awakened && awake.Awakened, "the Awakened flag is set only when asked for");

        // Both must actually FIRE. An Awakened enemy that cannot use its second pattern is
        // the phantom this whole pass exists to remove.
        int normalShots = CountShots(normal, bullets);
        int awakeShots = CountShots(awake, bullets);
        Check(normalShots > 0, $"a normal acolyte fires ({normalShots} bullets in 20s)");
        Check(awakeShots > 0, $"an Awakened acolyte fires ({awakeShots} bullets in 20s)");
        Check(awakeShots > normalShots,
              $"the Awakened acolyte's second pattern adds output ({normalShots} -> {awakeShots})");

        bullets.QueueFree();
    }

    private static int CountShots(Enemy e, BulletManager bullets)
    {
        bullets.Clear();
        var field = new FlowField(new Rect2(-2000, -2000, 4000, 4000));
        field.Rebuild(new Vector2(200f, 0f));

        int total = 0;
        for (int t = 0; t < 1200; t++)
        {
            // Hand it a token every tick; the manager's budget is not what is under test.
            e.GrantToken();
            int before = bullets.Count;
            e.Tick(1f / 60f, new Vector2(200f, 0f), Vector2.Zero, field, null);
            bullets._PhysicsProcess(1.0 / 60.0);
            if (bullets.Count > before) total += bullets.Count - before;
        }
        return total;
    }

    /// <summary>
    /// Every enemy in the roster needs an Awakened attack, or Corruption 3 makes some of
    /// them tougher and no more dangerous — a pure health-bloat upgrade, which docs/05 §2
    /// specifically warns starves the Sanity economy.
    /// </summary>
    private void TestEveryEnemyHasAnAwakenedAttack()
    {
        // The REAL bestiary, not a copy of it. This used to be five hardcoded paths, which
        // meant an enemy authored after it was written was exempt from the one check that
        // Corruption 3 does anything to it — the new floor-2 enemies would have been.
        int missing = 0;
        foreach (EnemyData d in Bestiary.All)
        {
            if (d.AwakenedAttack is null)
            {
                GD.PrintErr($"   {d.DisplayName} has no AwakenedAttack");
                missing++;
                continue;
            }
            // The second pattern must be a DIFFERENT shape, or "one additional attack
            // pattern" is the same attack again.
            if (d.PrimaryAttack is not null && d.AwakenedAttack.Primitive == d.PrimaryAttack.Primitive
                && d.AwakenedAttack.Count == d.PrimaryAttack.Count)
            {
                GD.PrintErr($"   {d.DisplayName}'s Awakened attack duplicates its primary");
                missing++;
            }
        }

        Check(missing == 0, $"every enemy has a distinct Awakened attack ({missing} problems)");
    }
}
