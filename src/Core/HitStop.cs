using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// Hit stop (docs/02 §8): a very short time-scale dip on impact, which is what makes a
/// kill feel like it landed rather than like the enemy quietly vanished.
///
/// THE TRAP THIS CLASS EXISTS TO CLOSE. Engine.TimeScale scales the delta handed to
/// _Process and _PhysicsProcess. Counting a hit-stop timer down with that delta therefore
/// makes the effect last 1/TimeScale times too long — at the specified 0.05x scale, a 40ms
/// punch became an 800ms freeze, and a triple kill became nearly two and a half seconds of
/// the game apparently hanging. It read as a performance problem rather than a feel effect,
/// which is exactly how it was reported.
///
/// Durations here are REAL time, read from Time.GetTicksUsec, which TimeScale does not
/// touch. Both scenes share this rather than each keeping a float, so the bug cannot come
/// back in only one of them.
/// </summary>
public sealed class HitStop
{
    /// <summary>
    /// How far time slows during a stop. Lower is heavier.
    ///
    /// docs/02 §8 specified 0.05x, and in play that reads as a hard stop rather than a
    /// punch — 95% slowdown is close enough to a freeze that the eye calls it one, even at
    /// a correct 35ms. **Default is now 0.20x**, which lands as a hitch.
    ///
    /// This is taste, not correctness, so it is a live knob rather than a constant: cycle
    /// presets with F7 in any playable scene and the game prints what it switched to.
    /// </summary>
    public static float Scale { get; private set; } = 0.20f;

    public enum Preset { Off, Feather, Light, Standard, Heavy }

    private static readonly (Preset p, float scale, string label)[] Presets =
    {
        (Preset.Off,      1.00f, "off — no time dip at all"),
        (Preset.Feather,  0.55f, "feather — barely perceptible"),
        (Preset.Light,    0.35f, "light — a nudge"),
        (Preset.Standard, 0.20f, "standard — a hitch (default)"),
        (Preset.Heavy,    0.05f, "heavy — a hard stop (the original spec)"),
    };

    private static int _presetIndex = 3;

    public static Preset Current => Presets[_presetIndex].p;
    public static string CurrentLabel => Presets[_presetIndex].label;

    /// <summary>Cycle to the next preset and return a description. Bound to F7.</summary>
    public static string CyclePreset()
    {
        _presetIndex = (_presetIndex + 1) % Presets.Length;
        Scale = Presets[_presetIndex].scale;
        Enabled = Presets[_presetIndex].p != Preset.Off;
        return $"hit stop: {Presets[_presetIndex].label}  (scale {Scale:F2})";
    }

    /// <summary>
    /// Ceiling on a single stop. Multi-kills previously summed linearly (0.04 x kills), so
    /// clearing a pack chained several stops into a visible hang. Impact should scale
    /// sub-linearly: the second simultaneous kill adds much less than the first.
    /// </summary>
    public const float MaxSeconds = 0.07f;

    /// <summary>Accessibility toggle (docs/10 §7), alongside the screen-shake slider.
    /// Time-scale effects are a common motion-sensitivity complaint.</summary>
    public static bool Enabled = true;

    /// <summary>
    /// Minimum real time between the END of one stop and the start of the next.
    ///
    /// Without it, kills landing on consecutive ticks each push the end-time out, so a
    /// shotgun clearing three enemies produces one long smear instead of three thumps —
    /// and a busy room becomes continuous stutter. Each individual stop was correct; it
    /// was the compounding that read as heavy.
    /// </summary>
    private const float RefractorySeconds = 0.10f;

    private ulong _endUsec;
    private ulong _readyUsec;

    public bool Active => Enabled && Time.GetTicksUsec() < _endUsec;

    /// <summary>
    /// Request a stop. Ignored while one is already running or inside the refractory
    /// window — impact should punctuate, not accumulate.
    /// </summary>
    public void Request(float seconds)
    {
        if (seconds <= 0f || !Enabled) return;

        ulong now = Time.GetTicksUsec();
        if (now < _endUsec || now < _readyUsec) return;

        _endUsec = now + (ulong)(Mathf.Min(seconds, MaxSeconds) * 1_000_000f);
        _readyUsec = _endUsec + (ulong)(RefractorySeconds * 1_000_000f);
    }

    /// <summary>Call once per tick. Applies or releases the time scale.</summary>
    public void Apply() => Engine.TimeScale = Active ? Scale : 1.0;

    public void Clear()
    {
        _endUsec = 0;
        _readyUsec = 0;
        Engine.TimeScale = 1.0;
    }

    /// <summary>
    /// Duration for a kill event. Sub-linear in the number of simultaneous kills, so a
    /// pack clear reads as one satisfying thump instead of a stutter.
    /// </summary>
    public static float ForKills(int kills) =>
        kills <= 0 ? 0f : Mathf.Min(MaxSeconds, 0.035f + 0.012f * Mathf.Sqrt(kills - 1));

    /// <summary>docs/02 §8 — taking damage stops harder than dealing it.</summary>
    public const float PlayerDamaged = 0.06f;
}
