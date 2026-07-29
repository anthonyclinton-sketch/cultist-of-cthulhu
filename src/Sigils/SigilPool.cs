using System.Collections.Generic;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Sigils;

/// <summary>
/// Every sigil in the game, and the rules for drawing one.
///
/// The pool is an explicit list rather than a directory scan. Godot can enumerate
/// <c>res://</c> at runtime, but the order it returns is filesystem-dependent, and this
/// pool is drawn from with a seeded Rng — an order that differs between two machines makes
/// the same seed produce different loot, which quietly breaks the one property docs/06 §7
/// asks the generator to guarantee. An explicit list is also greppable, which a scan is not.
/// </summary>
public static class SigilPool
{
    private static readonly string[] Paths =
    {
        "res://data/sigils/bloodletters_nail.tres",
        "res://data/sigils/candle_stub.tres",
        "res://data/sigils/ossuary_ring.tres",
        "res://data/sigils/yellow_ledger.tres",
        "res://data/sigils/salt_ward.tres",
        "res://data/sigils/tekeli_li.tres",
        "res://data/sigils/gaunts_favour.tres",
        "res://data/sigils/sovereigns_brand.tres",
        "res://data/sigils/chandlers_thumb.tres",
        "res://data/sigils/antiquarians_loupe.tres",
        "res://data/sigils/brine_knot.tres",
        "res://data/sigils/bone_lattice.tres",
        "res://data/sigils/deep_ones_gill.tres",
        "res://data/sigils/rite_of_the_open_wound.tres",
        "res://data/sigils/ledger_of_names.tres",
        "res://data/sigils/dreamers_ballast.tres",
        "res://data/sigils/innsmouth_blood.tres",
        "res://data/sigils/drowned_choir.tres",
        "res://data/sigils/elder_sign.tres",
        "res://data/sigils/the_unblinking.tres",
    };

    private static readonly string[] HeartPaths =
    {
        "res://data/sigils/heart_steady_pulse.tres",
        "res://data/sigils/heart_open_eye.tres",
    };

    private static List<SigilData>? _all;
    private static List<SigilData>? _hearts;

    public static IReadOnlyList<SigilData> All => _all ??= Load(Paths);
    public static IReadOnlyList<SigilData> Hearts => _hearts ??= Load(HeartPaths);

    private static List<SigilData> Load(string[] paths)
    {
        var list = new List<SigilData>(paths.Length);
        foreach (string p in paths)
        {
            var d = GD.Load<SigilData>(p);
            if (d is not null) list.Add(d);
            else GD.PrintErr($"[SigilPool] failed to load {p}");
        }
        return list;
    }

    public static SigilData? ById(string id)
    {
        foreach (SigilData s in All) if (s.Id == id) return s;
        foreach (SigilData s in Hearts) if (s.Id == id) return s;
        return null;
    }

    /// <summary>
    /// docs/08 §3 — the reward-room tier table, by floor. Rows are D / C / B / A / S and
    /// each sums to 1.
    /// </summary>
    private static readonly float[,] TierByFloor =
    {
        { 0.30f, 0.45f, 0.22f, 0.03f, 0.00f },   // floor 1
        { 0.15f, 0.42f, 0.33f, 0.09f, 0.01f },
        { 0.06f, 0.33f, 0.40f, 0.18f, 0.03f },
        { 0.00f, 0.24f, 0.41f, 0.28f, 0.07f },
        { 0.00f, 0.14f, 0.36f, 0.38f, 0.12f },
        { 0.00f, 0.06f, 0.28f, 0.44f, 0.22f },   // floor 6
    };

    /// <summary>
    /// Roll a tier for a floor, then let Corruption push it up.
    ///
    /// docs/08 §3: Corruption shifts a roll up one tier with probability 20/45/70% at
    /// thresholds 1/3/5. That is the cleanest statement of the game's central trade in the
    /// whole economy — the loot gets better because you are getting worse — so it lives
    /// here rather than being folded into the base weights, where it would be invisible.
    /// </summary>
    public static SigilTier RollTier(int floor, float corruption, Rng rng)
    {
        int row = Mathf.Clamp(floor - 1, 0, TierByFloor.GetLength(0) - 1);

        float roll = rng.NextFloat();
        float acc = 0f;
        var tier = SigilTier.D;
        for (int t = 0; t < 5; t++)
        {
            acc += TierByFloor[row, t];
            if (roll > acc) continue;
            tier = (SigilTier)t;
            break;
        }

        float shift = corruption >= 5f ? 0.70f : corruption >= 3f ? 0.45f : corruption >= 1f ? 0.20f : 0f;
        if (shift > 0f && rng.NextFloat() < shift && tier != SigilTier.S) tier++;

        return tier;
    }

    /// <summary>
    /// Draw a sigil at (or near) a rolled tier, excluding anything already held.
    ///
    /// Falls back through neighbouring tiers rather than failing. At M2 the pool is 20
    /// sigils across five tiers, so an exact-tier draw runs out quickly — and a reward room
    /// that offers nothing because the S row happens to be empty is a far worse outcome
    /// than one that offers an A.
    /// </summary>
    public static SigilData? Draw(int floor, float corruption, Rng rng, ICollection<SigilData>? exclude = null)
    {
        SigilTier want = RollTier(floor, corruption, rng);

        for (int spread = 0; spread < 5; spread++)
        {
            SigilData? pick = DrawAtTier((int)want - spread, rng, exclude)
                              ?? DrawAtTier((int)want + spread, rng, exclude);
            if (pick is not null) return pick;
        }
        return null;
    }

    private static SigilData? DrawAtTier(int tier, Rng rng, ICollection<SigilData>? exclude)
    {
        if (tier < 0 || tier > 4) return null;

        // Reservoir sample so the scan is one pass and needs no temporary list. With 20
        // candidates the list would be cheap, but this runs from the shop's restock too and
        // the pool is expected to reach ~70.
        SigilData? chosen = null;
        int seen = 0;

        foreach (SigilData s in All)
        {
            if ((int)s.Tier != tier) continue;
            if (exclude is not null && exclude.Contains(s)) continue;
            seen++;
            if (rng.NextInt(0, seen) == 0) chosen = s;
        }
        return chosen;
    }
}
