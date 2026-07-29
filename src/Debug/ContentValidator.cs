using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Loads every .tres under data/ and runs its Validate(). CI gate.
///
///   godot --path . --headless res://scenes/debug/ContentValidator.tscn
///
/// Why this is a build gate rather than a runtime warning: the rules being checked are
/// the READABILITY CONTRACT (docs/05 §1) and the design invariants that Fable's review
/// found broken by hand — a warm-hued enemy bullet, a melee weapon whose reach is inside
/// the contact radius, fodder too tough to fund the Sanity economy. Every one of those is
/// invisible in isolation and only shows up as "the game feels unfair" weeks later.
///
/// Exits non-zero on any violation.
/// </summary>
public sealed partial class ContentValidator : Node
{
    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" CONTENT VALIDATION");
        GD.Print("================================================================");

        int checkedCount = 0, failed = 0;

        failed += ValidateDir<PatternData>("res://data/patterns", p => p.Validate(), ref checkedCount);
        failed += ValidateDir<WeaponData>("res://data/weapons", w => w.Validate(), ref checkedCount);
        failed += ValidateDir<EnemyData>("res://data/enemies", e => e.Validate(), ref checkedCount);
        failed += ValidateDir<Sigils.SigilData>("res://data/sigils", s => s.Validate(), ref checkedCount);
        failed += ValidateDir<InscriptionData>("res://data/inscriptions", i => i.Validate(), ref checkedCount);

        failed += ValidateSigilPool();

        GD.Print("----------------------------------------------------------------");
        GD.Print($" {checkedCount} resources checked, {failed} invalid");
        GD.Print(failed == 0 ? " CONTENT VALIDATION: PASS" : " CONTENT VALIDATION: FAIL");
        GD.Print("================================================================");

        GetTree().Quit(failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// Pool-level rules — the ones no single .tres can violate on its own.
    ///
    /// docs/04 §8.4 wants at least six sigils per shape so that no shape is a dead draw.
    /// That is a rule about the FINISHED ~70-sigil pool, and M2 ships 20, so it cannot be
    /// met yet and is reported rather than failed. It is here anyway because a rule that
    /// only starts being checked once it can pass is a rule that gets discovered as broken
    /// at the point it is most expensive to fix — halfway through authoring 50 more sigils.
    ///
    /// The two things that DO fail are duplicate ids, which silently break every lookup by
    /// id, and an empty tier row, which makes the reward table's own weights unreachable.
    /// </summary>
    private static int ValidateSigilPool()
    {
        var byShape = new Dictionary<Sigils.SigilShapeKind, int>();
        var byTier = new Dictionary<Sigils.SigilTier, int>();
        var ids = new HashSet<string>();
        int failed = 0;

        foreach (Sigils.SigilData s in Sigils.SigilPool.All)
        {
            if (!ids.Add(s.Id))
            {
                GD.PrintErr($" [FAIL] duplicate sigil id '{s.Id}'");
                failed++;
            }
            byShape[s.Shape] = byShape.GetValueOrDefault(s.Shape) + 1;
            byTier[s.Tier] = byTier.GetValueOrDefault(s.Tier) + 1;
        }

        var shapeReport = new List<string>();
        int shapesBelowTarget = 0;
        foreach (Sigils.SigilShapeKind shape in System.Enum.GetValues<Sigils.SigilShapeKind>())
        {
            int n = byShape.GetValueOrDefault(shape);
            shapeReport.Add($"{shape} {n}");
            if (n == 0)
            {
                GD.PrintErr($" [FAIL] no sigils use the {shape} shape — it is a dead shape.");
                failed++;
            }
            else if (n < 6) shapesBelowTarget++;
        }

        GD.Print($" [info] sigil shapes: {string.Join(", ", shapeReport)}");
        if (shapesBelowTarget > 0)
        {
            GD.Print($" [info] {shapesBelowTarget} shapes below the 6-per-shape target (docs/04 §8.4). " +
                     $"Expected at {Sigils.SigilPool.All.Count}/70 sigils; revisit when the pool is complete.");
        }

        var tierReport = new List<string>();
        foreach (Sigils.SigilTier tier in System.Enum.GetValues<Sigils.SigilTier>())
            tierReport.Add($"{tier} {byTier.GetValueOrDefault(tier)}");
        GD.Print($" [info] sigil tiers: {string.Join(", ", tierReport)}");

        return failed;
    }

    private static int ValidateDir<T>(string dirPath, System.Func<T, string?> validate, ref int checkedCount)
        where T : Resource
    {
        using var dir = DirAccess.Open(dirPath);
        if (dir is null)
        {
            GD.PrintErr($" [SKIP] {dirPath} not found");
            return 0;
        }

        int failed = 0;
        var names = new List<string>(dir.GetFiles());
        names.Sort();

        foreach (string file in names)
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres")) continue;

            string path = $"{dirPath}/{name}";
            var res = GD.Load<T>(path);
            checkedCount++;

            if (res is null)
            {
                GD.PrintErr($" [FAIL] {path}: failed to load (script_class mismatch?)");
                failed++;
                continue;
            }

            string? err = validate(res);
            if (err is null)
            {
                GD.Print($" [ok]   {name}");
            }
            else
            {
                GD.PrintErr($" [FAIL] {name}: {err}");
                failed++;
            }
        }
        return failed;
    }
}
