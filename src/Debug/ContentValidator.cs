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

        GD.Print("----------------------------------------------------------------");
        GD.Print($" {checkedCount} resources checked, {failed} invalid");
        GD.Print(failed == 0 ? " CONTENT VALIDATION: PASS" : " CONTENT VALIDATION: FAIL");
        GD.Print("================================================================");

        GetTree().Quit(failed == 0 ? 0 : 1);
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
