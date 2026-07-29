using CultistOfCthulhu.Meta;
using CultistOfCthulhu.Sigils;
using Godot;

namespace CultistOfCthulhu.UI;

/// <summary>
/// Where a run ends.
///
/// Its absence was the gap this whole change closes. Dying rebuilt the floor instantly
/// with no acknowledgement that anything had happened, and winning did not exist at all —
/// the boss died, the room cleared, and the player stood in an empty arena. A roguelike
/// whose runs do not end cannot be replayed, because there is nothing to replay FROM.
///
/// It shows the M1 metrics as well as the fiction. That is deliberate: docs/11's test
/// design needs testers to be able to read back what their run did, and a tester who can
/// see "you spent 39% of combat below 40 Sanity" gives a far more useful answer to "how did
/// that feel?" than one working from memory.
/// </summary>
public sealed partial class RunSummaryScreen : Control
{
    [Signal] public delegate void RestartRequestedEventHandler();

    private RunState? _run;
    private float _age;

    private static readonly Color Backdrop = new(0.03f, 0.04f, 0.06f, 0.94f);
    private static readonly Color Ink = new("E8E1D5");
    private static readonly Color InkDim = new("8B8578");
    private static readonly Color WonColour = new("7FE0D4");
    private static readonly Color DeadColour = new("B0122A");

    /// <summary>A beat before input is accepted, so the key that killed you cannot also
    /// dismiss the screen telling you about it.</summary>
    private const float InputDelay = 0.8f;

    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(RunState run)
    {
        _run = run;
        _age = 0f;
        IsOpen = true;
        Visible = true;
        GetTree().Paused = true;
        QueueRedraw();
    }

    private void Dismiss()
    {
        IsOpen = false;
        Visible = false;
        GetTree().Paused = false;
        EmitSignal(SignalName.RestartRequested);
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        _age += (float)delta;
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || _age < InputDelay) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false }) return;

        Dismiss();
        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        if (!IsOpen || _run is null) return;

        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, 640, 360), Backdrop);

        bool won = _run.Outcome == RunOutcome.Won;
        Color accent = won ? WonColour : DeadColour;

        DrawString(font, new Vector2(40, 48), won ? "THE WAY IS OPEN" : "THE UNDERCROFT KEEPS YOU",
                   HorizontalAlignment.Left, -1, 18, accent);
        DrawString(font, new Vector2(40, 66),
                   won
                       ? $"You came back up. {_run.FloorsCleared} floor(s), {_run.RoomsCleared} rooms."
                       : $"Floor {_run.FloorIndex}, after {_run.RoomsCleared} rooms.",
                   HorizontalAlignment.Left, -1, 10, InkDim);

        float y = 100f;
        void Line(string label, string value)
        {
            DrawString(font, new Vector2(40, y), label, HorizontalAlignment.Left, -1, 10, InkDim);
            DrawString(font, new Vector2(190, y), value, HorizontalAlignment.Left, -1, 10, Ink);
            y += 15f;
        }

        Line("time", $"{_run.Duration:F0}s");
        Line("gold", $"{_run.Gold}");
        Line("keys", $"{_run.Keys}");
        Line("corruption", $"{_run.Corruption:0.##}");
        Line("hearts", $"{_run.Hearts:0.#} / {_run.MaxHearts:0.#}");
        Line("ascensions", $"{_run.AscensionCount}");

        DrawCircleSummary(font);
        DrawMetrics(font);

        if (_age >= InputDelay)
        {
            DrawString(font, new Vector2(40, 336), "any key — begin again",
                       HorizontalAlignment.Left, -1, 10, accent);
        }
    }

    /// <summary>
    /// The build the player finished with. This is the single most interesting thing on
    /// the screen for docs/04's purposes — it is the only moment a tester is shown what
    /// they actually assembled, and "I didn't realise that's what I'd built" is exactly the
    /// feedback the Circle needs.
    /// </summary>
    private void DrawCircleSummary(Font font)
    {
        if (_run is null) return;
        SigilCircle c = _run.Circle;

        DrawString(font, new Vector2(330, 100), "THE CIRCLE", HorizontalAlignment.Left, -1, 10, Ink);
        DrawString(font, new Vector2(330, 115),
                   $"{c.UsedCells} / {SigilCircle.TotalCells} cells · {c.Synergies.Count} synergies",
                   HorizontalAlignment.Left, -1, 9, InkDim);

        float y = 132f;
        int shown = 0;
        foreach (PlacedSigil p in c.Placed)
        {
            if (shown++ >= 9) break;
            DrawString(font, new Vector2(336, y),
                       p.Locked ? $"{p.Data.DisplayName} (Heart)" : p.Data.DisplayName,
                       HorizontalAlignment.Left, 260, 9, p.Locked ? InkDim : Ink);
            y += 12f;
        }
        if (c.Placed.Count > 9)
            DrawString(font, new Vector2(336, y), $"... and {c.Placed.Count - 9} more",
                       HorizontalAlignment.Left, -1, 9, InkDim);
    }

    private void DrawMetrics(Font font)
    {
        if (_run is null) return;
        Telemetry t = _run.Telemetry;

        DrawString(font, new Vector2(40, 262), "M1 METRICS", HorizontalAlignment.Left, -1, 10, Ink);

        float below = t.TimeBelowFrayingFraction();
        float ladder = t.LadderFireRate();

        DrawString(font, new Vector2(40, 278),
                   $"time below 40 Sanity   {below * 100:F0}%   (target 25-45%)",
                   HorizontalAlignment.Left, -1, 9, InkDim);
        DrawString(font, new Vector2(40, 292),
                   $"rooms reaching Fraying {ladder * 100:F0}%   (target 70%+)",
                   HorizontalAlignment.Left, -1, 9, InkDim);
        DrawString(font, new Vector2(40, 306),
                   $"could not afford to act {t.TotalDeniedSustain()} times",
                   HorizontalAlignment.Left, -1, 9, InkDim);
        DrawString(font, new Vector2(40, 320),
                   $"telemetry written to user://m1_telemetry.csv",
                   HorizontalAlignment.Left, -1, 8, InkDim);
    }
}
