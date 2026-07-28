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
    /// <summary>docs/02 §8 — the specified dip. Deep, but only for a few frames.</summary>
    public const float Scale = 0.05f;

    /// <summary>
    /// Ceiling on a single stop. Multi-kills previously summed linearly (0.04 x kills), so
    /// clearing a pack chained several stops into a visible hang. Impact should scale
    /// sub-linearly: the second simultaneous kill adds much less than the first.
    /// </summary>
    public const float MaxSeconds = 0.07f;

    /// <summary>Accessibility toggle (docs/10 §7), alongside the screen-shake slider.
    /// Time-scale effects are a common motion-sensitivity complaint.</summary>
    public static bool Enabled = true;

    private ulong _endUsec;

    public bool Active => Enabled && Time.GetTicksUsec() < _endUsec;

    /// <summary>Request a stop. Longer requests win; shorter ones never cut one short.</summary>
    public void Request(float seconds)
    {
        if (seconds <= 0f) return;
        ulong end = Time.GetTicksUsec() + (ulong)(Mathf.Min(seconds, MaxSeconds) * 1_000_000f);
        if (end > _endUsec) _endUsec = end;
    }

    /// <summary>Call once per tick. Applies or releases the time scale.</summary>
    public void Apply() => Engine.TimeScale = Active ? Scale : 1.0;

    public void Clear()
    {
        _endUsec = 0;
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
