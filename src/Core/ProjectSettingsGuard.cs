using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// Asserts that engine settings the design depends on still hold.
///
/// WHY THIS EXISTS. Godot rewrites project.godot on save and OMITS any setting whose
/// value equals the current engine default. Four settings this project deliberately
/// specified were silently dropped the first time the editor touched the file:
///
///     physics/common/physics_ticks_per_second   = 60
///     display/window/stretch/aspect             = "keep"
///     rendering/2d/snap/snap_2d_transforms_to_pixel = false
///     rendering/2d/snap/snap_2d_vertices_to_pixel   = false
///
/// They were correct, so nothing broke. But the INTENT was lost: the file no longer
/// records that sub-pixel bullet motion is a requirement (docs/10 §1.2) or that a 60Hz
/// tick is load-bearing for determinism (docs/09 §4). If a future engine version changes
/// one of those defaults, or someone flips a checkbox in the editor, the project silently
/// acquires a different physics rate or snaps bullets to the pixel grid — and the symptom
/// would be "the game feels wrong" or "the determinism test went red", days later, with no
/// diff to point at.
///
/// Writing the values back into project.godot does NOT fix this; they would be stripped
/// again on the next save. A runtime assertion is the only thing that survives.
///
/// Checked once at boot. Failures are loud, and fatal in headless runs so CI catches them.
/// </summary>
public static class ProjectSettingsGuard
{
    public static bool Verify()
    {
        bool ok = true;

        // docs/09 §4 — the sim is a locked 60Hz. Determinism, all frame data in
        // Tune.cs, and every authored bullet pattern assume this exact value.
        ok &= Expect("physics/common/physics_ticks_per_second", 60,
                     "Blink Step frame data and all pattern timing assume a 60Hz tick.");

        // docs/09 §4 — jitter fix interpolates physics deltas and destroys reproducibility.
        ok &= Expect("physics/common/physics_jitter_fix", 0.0,
                     "Non-zero jitter fix makes the simulation non-deterministic.");

        // docs/10 §1.2 — characters snap to the pixel grid, BULLETS DO NOT. Bullet-hell
        // readability depends on smooth trajectories; snapping makes slow projectiles
        // visibly stair-step, which is worst exactly where readability matters most.
        ok &= Expect("rendering/2d/snap/snap_2d_transforms_to_pixel", false,
                     "Bullets require sub-pixel motion.");
        ok &= Expect("rendering/2d/snap/snap_2d_vertices_to_pixel", false,
                     "Bullets require sub-pixel motion.");

        // docs/10 §1.2 — integer-scaled native 640x360.
        ok &= Expect("display/window/stretch/mode", "viewport", "Native-res pixel art scaling.");
        ok &= Expect("display/window/stretch/aspect", "keep", "Non-uniform stretch would distort the pixel grid.");

        // docs/09 §4 — render interpolation is what makes a 60Hz sim look like 144Hz.
        ok &= Expect("physics/common/physics_interpolation", true,
                     "Required for smooth rendering above the 60Hz sim rate.");

        if (!ok)
        {
            GD.PrintErr("[ProjectSettingsGuard] One or more required project settings have drifted. " +
                        "See src/Core/ProjectSettingsGuard.cs for why each one matters.");
        }
        return ok;
    }

    private static bool Expect(string setting, Variant expected, string why)
    {
        Variant actual = ProjectSettings.GetSettingWithOverride(setting);

        // Compare as strings: ProjectSettings returns ints for bools in some builds, and
        // Variant equality across numeric types is not reliable enough to gate on.
        string a = actual.ToString();
        string e = expected.ToString();

        // Normalise the bool/int ambiguity.
        if (e is "True" or "False") a = a is "True" or "1" ? "True" : a is "False" or "0" ? "False" : a;

        if (string.Equals(a, e, System.StringComparison.OrdinalIgnoreCase)) return true;

        GD.PrintErr($"[ProjectSettingsGuard] '{setting}' is '{a}', expected '{e}'. {why}");
        return false;
    }
}
