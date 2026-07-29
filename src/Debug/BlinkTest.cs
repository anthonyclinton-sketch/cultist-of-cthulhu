using CultistOfCthulhu.Core;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Blink Step frame data, measured rather than asserted from the constants.
///
///   godot --path . --headless res://scenes/debug/BlinkTest.tscn
///
/// docs/02 §4 opens with "the single most-pressed button. Its spec is the spec of the game",
/// and it names one number as an invariant: **the full cycle is 24 frames + 0.12s ≈ 0.52s,
/// and it must be protected.** Post-F4 the dodge is free, so that cycle and the 8-frame
/// vulnerable recovery tail are the ONLY things standing between the player and dodge-spam.
///
/// The constants in <see cref="Tune"/> say the right thing. That is not the same as the
/// state machine behaving the right way, and a frame-data spec that is only checked by
/// reading the constants it was transcribed into is not checked at all — so this drives the
/// real controller and counts real frames.
///
/// What it measures: how often a hammering player can actually dodge, and what fraction of
/// their frames are spent invulnerable.
/// </summary>
public sealed partial class BlinkTest : Node2D
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" BLINK STEP — FRAME DATA");
        GD.Print("================================================================");

        MeasureHammering();
        MeasureSingleDodge();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " BLINK STEP: PASS" : $" BLINK STEP: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    private PlayerController MakePlayer()
    {
        var p = new PlayerController { Name = "TestPlayer" };
        AddChild(p);
        return p;
    }

    /// <summary>
    /// The reported bug: hammering the dodge chains it with almost no gap.
    ///
    /// Drives the real controller with the action held down every tick, which is the
    /// worst case a mashing player can produce, and counts the frames between dodge starts.
    /// </summary>
    private void MeasureHammering()
    {
        PlayerController p = MakePlayer();

        const int Frames = 1200;   // 20 seconds
        int dodges = 0;
        int invulnFrames = 0;
        int firstStart = -1, lastStart = -1;
        int minGap = int.MaxValue, maxGap = 0;
        BlinkPhase previous = BlinkPhase.None;

        for (int f = 0; f < Frames; f++)
        {
            // Request every tick — a mash faster than any human, which is the point: the
            // floor on the cycle has to come from the state machine, not from how fast the
            // player's thumb is.
            p.TryBeginBlink();
            p._PhysicsProcess(1.0 / 60.0);

            // A dodge STARTED this frame if we went from not-dashing into startup.
            bool started = p.Phase == BlinkPhase.Startup && previous != BlinkPhase.Startup;
            if (started)
            {
                dodges++;
                if (firstStart < 0) firstStart = f;
                else
                {
                    int gap = f - lastStart;
                    if (gap < minGap) minGap = gap;
                    if (gap > maxGap) maxGap = gap;
                }
                lastStart = f;
            }

            if (p.IsInvulnerable) invulnFrames++;
            previous = p.Phase;
        }

        float invulnFraction = invulnFrames / (float)Frames;
        float meanGap = dodges > 1 ? (lastStart - firstStart) / (float)(dodges - 1) : 0f;

        // docs/02 §4 — 24 frames of animation plus a 0.12s cooldown is 24 + 7.2 = 31.2.
        const float DocumentedCycleFrames = Tune.BlinkTotalFrames + Tune.BlinkCooldown * 60f;

        GD.Print($" mashed {Frames} frames: {dodges} dodges");
        GD.Print($"   gap between dodge starts   min {minGap}  mean {meanGap:F1}  max {maxGap} frames");
        GD.Print($"   documented full cycle      {DocumentedCycleFrames:F1} frames " +
                 $"({DocumentedCycleFrames / 60f:F2}s)");
        GD.Print($"   invulnerable               {invulnFraction * 100:F1}% of all frames");

        Check(dodges > 1, $"the player dodged more than once ({dodges})");

        // THE INVARIANT. A mashing player must not beat the documented cycle.
        Check(minGap >= Mathf.FloorToInt(DocumentedCycleFrames),
              $"the shortest gap between dodges ({minGap} frames) respects the documented " +
              $"{DocumentedCycleFrames:F1}-frame cycle");

        // The consequence that actually matters. With 14 invulnerable frames in a 31.2-frame
        // cycle the ceiling is ~45%; anything approaching permanent invulnerability means
        // the recovery tail is being skipped, and post-F4 the tail is the only brake there is.
        const float Ceiling = (float)Tune.BlinkInvulnFrames / DocumentedCycleFrames;
        Check(invulnFraction <= Ceiling + 0.05f,
              $"invulnerable {invulnFraction * 100:F1}% of frames, against a {Ceiling * 100:F1}% " +
              "ceiling implied by the frame data");

        p.QueueFree();
    }

    /// <summary>
    /// One dodge, frame by frame, against the table in docs/02 §4: 2 startup, 14
    /// invulnerable, 8 recovery.
    /// </summary>
    private void MeasureSingleDodge()
    {
        PlayerController p = MakePlayer();

        Check(p.TryBeginBlink(), "a dodge can be started from rest");

        // Sample AFTER ticking. Sampling first counts the pre-tick state as a frame, which
        // reported 3 startup frames for a 2-frame startup — the harness being off by one
        // rather than the controller.
        int startup = 0, invuln = 0, recovery = 0;
        for (int f = 0; f < 60; f++)
        {
            p._PhysicsProcess(1.0 / 60.0);
            if (p.Phase == BlinkPhase.None) break;

            switch (p.Phase)
            {
                case BlinkPhase.Startup: startup++; break;
                case BlinkPhase.Invulnerable: invuln++; break;
                case BlinkPhase.Recovery: recovery++; break;
            }
        }

        GD.Print($" one dodge: {startup} startup / {invuln} invulnerable / {recovery} recovery");

        Check(startup == Tune.BlinkStartupFrames,
              $"{startup} startup frames == {Tune.BlinkStartupFrames} documented");
        Check(invuln == Tune.BlinkInvulnFrames,
              $"{invuln} invulnerable frames == {Tune.BlinkInvulnFrames} documented");
        Check(recovery == Tune.BlinkRecoveryFrames,
              $"{recovery} recovery frames == {Tune.BlinkRecoveryFrames} documented");

        p.QueueFree();
    }
}
