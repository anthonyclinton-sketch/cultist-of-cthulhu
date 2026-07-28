using System;
using System.Runtime;
using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// Autoload. Owns process-wide setup: the input map, the run seed, and GC latency policy.
///
/// Deliberately thin. docs/09 §6 warns against a god-object EventBus; the same applies
/// here. GameRoot boots things and holds the seed. It does not run gameplay.
/// </summary>
public sealed partial class GameRoot : Node
{
    public static GameRoot Instance { get; private set; } = null!;

    /// <summary>The master seed for the current run. Everything else derives from this.</summary>
    public ulong RunSeed { get; private set; }

    /// <summary>True when launched with --determinism-test or --headless benchmarks.</summary>
    public bool HeadlessTestMode { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;

        // A 60Hz sim loop cares about GC *pauses*, not GC throughput. SustainedLowLatency
        // keeps gen2 collections from being triggered opportunistically mid-frame.
        // This is a mitigation, not a licence: the tick loop must still allocate zero
        // (docs/09 §8), and DebugOverlay asserts that in dev builds.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        ParseCommandLine();
        ConfigureInputMap();

        // Fail loudly if an engine setting the design depends on has drifted. Fatal in
        // headless runs so CI catches it; a warning in the editor so it does not block
        // iteration on an unrelated change.
        if (!ProjectSettingsGuard.Verify() && DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("[GameRoot] Aborting: required project settings are wrong.");
            GetTree().Quit(2);
            return;
        }

        GD.Print($"[GameRoot] Cultist of Cthulhu — seed {Hash.FormatSeed(RunSeed)}");
    }

    private void ParseCommandLine()
    {
        string[] args = OS.GetCmdlineArgs();
        ulong? seed = null;
        bool metered = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--seed" && i + 1 < args.Length)
            {
                seed = Hash.ParseSeed(args[i + 1]);
            }
            else if (args[i].StartsWith("--seed=", StringComparison.Ordinal))
            {
                seed = Hash.ParseSeed(args[i][7..]);
            }
            else if (args[i] == "--determinism-test")
            {
                HeadlessTestMode = true;
            }
            else if (args[i] == "--metered-dodge")
            {
                metered = true;
            }
        }

        // Build B — the M1 control arm (docs/11 §M1 test design).
        Tune.SetMeteredDodge(metered);
        if (metered) GD.Print("[GameRoot] BUILD B — metered dodge, Blink Step costs " +
                              $"{Tune.SanityBlinkCostMetered:F0} Sanity.");

        RunSeed = seed ?? NewRandomSeed();
    }

    private static ulong NewRandomSeed()
    {
        // Godot's RNG is fine as an ENTROPY source for picking a fresh seed; it is never
        // used for gameplay. Once chosen, the seed is the only source of randomness.
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        return ((ulong)rng.Randi() << 32) | rng.Randi();
    }

    public void SetRunSeed(ulong seed) => RunSeed = seed;

    /// <summary>
    /// The input map from docs/02 §9, defined in code rather than in project.godot.
    ///
    /// Why: the .godot serialisation of InputEvent objects is enormous, unreviewable in a
    /// diff, and silently version-sensitive. Defining it here means the binding table is
    /// readable, greppable, and survives engine upgrades. Player remapping (required at
    /// 1.0) will layer over this as an override file.
    /// </summary>
    private static void ConfigureInputMap()
    {
        // Movement — physical keycodes so WASD stays WASD on AZERTY/Dvorak layouts.
        Bind("move_up", Key.W, JoyAxis.LeftY, -1f);
        Bind("move_down", Key.S, JoyAxis.LeftY, 1f);
        Bind("move_left", Key.A, JoyAxis.LeftX, -1f);
        Bind("move_right", Key.D, JoyAxis.LeftX, 1f);

        // Aim (gamepad only — KBM aims with the mouse cursor)
        Bind("aim_up", Key.None, JoyAxis.RightY, -1f);
        Bind("aim_down", Key.None, JoyAxis.RightY, 1f);
        Bind("aim_left", Key.None, JoyAxis.RightX, -1f);
        Bind("aim_right", Key.None, JoyAxis.RightX, 1f);

        BindButton("fire", MouseButton.Left, JoyButton.Invalid, JoyAxis.TriggerRight);
        Bind("blink_step", Key.Space, JoyButton.A);
        Bind("recite", Key.R, JoyButton.X);
        BindButton("banish", MouseButton.Right, JoyButton.LeftShoulder, JoyAxis.Invalid);
        Bind("swap_weapon", Key.Q, JoyButton.Y);
        Bind("interact", Key.E, JoyButton.B);
        Bind("reverie", Key.Tab, JoyButton.Back);
        Bind("map", Key.M, JoyButton.RightShoulder);
        Bind("pause", Key.Escape, JoyButton.Start);

        // Debug (dev builds)
        Bind("debug_overlay", Key.F3, JoyButton.Invalid);
        Bind("debug_console", Key.F1, JoyButton.Invalid);
    }

    private static void EnsureAction(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.22f); // deadzone, docs/02 §1.2
        else InputMap.ActionEraseEvents(action);
    }

    private static void Bind(string action, Key key, JoyButton button)
    {
        EnsureAction(action);
        if (key != Key.None)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
        if (button != JoyButton.Invalid)
        {
            InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
        }
    }

    private static void Bind(string action, Key key, JoyAxis axis, float axisValue)
    {
        EnsureAction(action);
        if (key != Key.None)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
        if (axis != JoyAxis.Invalid)
        {
            InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = axisValue });
        }
    }

    private static void BindButton(string action, MouseButton mouse, JoyButton button, JoyAxis trigger)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = mouse });
        if (button != JoyButton.Invalid)
        {
            InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
        }
        if (trigger != JoyAxis.Invalid)
        {
            InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = trigger, AxisValue = 1f });
        }
    }
}
