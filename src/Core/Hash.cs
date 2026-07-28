using System;

namespace CultistOfCthulhu.Core;

/// <summary>
/// Deterministic sub-seed derivation (docs/06 §7).
///
/// The rule this enforces: every generation stage gets its own seed derived from the run
/// seed plus a STABLE STRING LABEL. Because the label is part of the hash, adding a new
/// stage later cannot shift the seeds of existing stages — which means a content patch
/// does not invalidate every shared seed in the wild.
///
///     ulong floorSeed    = Hash.Combine(runSeed, floorIndex);
///     ulong layoutSeed   = Hash.Combine(floorSeed, "layout");
///     ulong populateSeed = Hash.Combine(floorSeed, "populate");
/// </summary>
public static class Hash
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static ulong Combine(ulong seed, ulong value)
    {
        ulong x = seed ^ (value + 0x9E3779B97F4A7C15UL + (seed << 6) + (seed >> 2));
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static ulong Combine(ulong seed, int value) => Combine(seed, (ulong)(uint)value);

    /// <summary>
    /// Combine with a string label. Hashed byte-wise with FNV-1a rather than using
    /// string.GetHashCode(), which is randomised per-process in .NET and would make
    /// every run non-reproducible.
    /// </summary>
    public static ulong Combine(ulong seed, string label)
    {
        ulong h = FnvOffset;
        for (int i = 0; i < label.Length; i++)
        {
            h = (h ^ label[i]) * FnvPrime;
        }
        return Combine(seed, h);
    }

    /// <summary>Derive a fresh Rng for a named subsystem.</summary>
    public static Rng Derive(ulong seed, string label) => new Rng(Combine(seed, label));

    public static Rng Derive(ulong seed, string label, int index)
        => new Rng(Combine(Combine(seed, label), index));

    /// <summary>
    /// Parse a user-entered seed. Accepts decimal, 0x-prefixed hex, or arbitrary text
    /// (hashed). Never throws — an unparseable seed becomes a stable hash of itself, so
    /// "cthulhu" is a valid, shareable, reproducible seed.
    /// </summary>
    public static ulong ParseSeed(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0UL;
        input = input.Trim();

        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(input.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out ulong hex))
        {
            return hex;
        }
        if (ulong.TryParse(input, out ulong dec)) return dec;

        ulong h = FnvOffset;
        for (int i = 0; i < input.Length; i++) h = (h ^ input[i]) * FnvPrime;
        return h;
    }

    /// <summary>Render a seed the way it is shown in the pause menu (docs/06 §7).</summary>
    public static string FormatSeed(ulong seed) => seed.ToString("X16");
}
