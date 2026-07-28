using System;
using System.Runtime.CompilerServices;
using Godot;

namespace CultistOfCthulhu.Core;

/// <summary>
/// xoshiro256** — deterministic, seedable, allocation-free.
///
/// docs/06 §7 and docs/09 §4: there is NO global RNG in this project. Every subsystem
/// takes an explicit Rng instance derived from the run seed, so that changing (say) the
/// enemy roster can never perturb floor layout. Every method here is a pure function of
/// this instance's state.
///
/// Chosen over System.Random because System.Random's algorithm is not contractually
/// stable across .NET versions — a runtime upgrade would silently invalidate every
/// saved seed and break the determinism test in docs/09 §9.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        // SplitMix64 to expand a single seed into four well-distributed words.
        // A seed of 0 is legal here (unlike naive xorshift seeding) because SplitMix64
        // never produces an all-zero state.
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);
    }

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);

        return result;
    }

    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// <summary>Uniform float in [0, 1). Uses the top 24 bits — the mantissa width of a float.</summary>
    public float NextFloat() => (NextULong() >> 40) * (1.0f / 16777216.0f);

    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    public float Range(float minInclusive, float maxExclusive)
        => minInclusive + NextFloat() * (maxExclusive - minInclusive);

    /// <summary>
    /// Uniform int in [minInclusive, maxExclusive). Uses Lemire's debiased multiply-shift:
    /// unbiased, and branch-free in the overwhelmingly common case.
    /// </summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        uint range = (uint)(maxExclusive - minInclusive);
        ulong m = (ulong)NextUInt() * range;
        uint l = (uint)m;
        if (l < range)
        {
            uint threshold = (uint)(-(int)range) % range;
            while (l < threshold)
            {
                m = (ulong)NextUInt() * range;
                l = (uint)m;
            }
        }
        return minInclusive + (int)(m >> 32);
    }

    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>Uniform direction on the unit circle.</summary>
    public Vector2 NextUnitVector()
    {
        float a = NextFloat() * Mathf.Tau;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    /// <summary>Uniform angle in radians, [0, tau).</summary>
    public float NextAngle() => NextFloat() * Mathf.Tau;

    /// <summary>
    /// Fisher-Yates. Takes a span so it works on arrays and pooled buffers without
    /// allocating an enumerator.
    /// </summary>
    public void Shuffle<T>(Span<T> items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = NextInt(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// Snapshot of internal state. Used by the determinism test (docs/09 §9) to prove that
    /// two runs consumed the RNG identically — a divergence here localises the bug far
    /// faster than a divergence in world state.
    /// </summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603UL;
        h = (h ^ _s0) * 1099511628211UL;
        h = (h ^ _s1) * 1099511628211UL;
        h = (h ^ _s2) * 1099511628211UL;
        h = (h ^ _s3) * 1099511628211UL;
        return h;
    }
}
