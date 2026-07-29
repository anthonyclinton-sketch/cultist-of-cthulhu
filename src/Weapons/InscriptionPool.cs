using System.Collections.Generic;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Weapons;

/// <summary>
/// Every Inscription in the game, and the bench's offer draw.
///
/// Explicit list rather than a directory scan, for the same reason as
/// <see cref="Sigils.SigilPool"/>: the draw is seeded, and a filesystem-dependent order
/// would make the same seed produce different shop stock on different machines.
/// </summary>
public static class InscriptionPool
{
    private static readonly string[] Paths =
    {
        "res://data/inscriptions/keen_etching.tres",
        "res://data/inscriptions/swift_etching.tres",
        "res://data/inscriptions/deep_etching.tres",
        "res://data/inscriptions/hoarders_mark.tres",
        "res://data/inscriptions/light_etching.tres",
        "res://data/inscriptions/steady_hand.tres",
        "res://data/inscriptions/longreach.tres",
        "res://data/inscriptions/piercing_rune.tres",
        "res://data/inscriptions/rebounding_rune.tres",
        "res://data/inscriptions/whispering_rounds.tres",
        "res://data/inscriptions/sanguine_etching.tres",
        "res://data/inscriptions/yellow_ink.tres",
        "res://data/inscriptions/gaunts_bargain.tres",
        "res://data/inscriptions/the_unblinking_eye.tres",
        "res://data/inscriptions/sovereigns_mark.tres",
    };

    private static List<InscriptionData>? _all;

    public static IReadOnlyList<InscriptionData> All => _all ??= Load();

    private static List<InscriptionData> Load()
    {
        var list = new List<InscriptionData>(Paths.Length);
        foreach (string p in Paths)
        {
            var d = GD.Load<InscriptionData>(p);
            if (d is not null) list.Add(d);
            else GD.PrintErr($"[InscriptionPool] failed to load {p}");
        }
        return list;
    }

    /// <summary>
    /// Draw the bench's three offers (docs/08 §2.2).
    ///
    /// Weighted toward Lesser on early floors and toward Forbidden late, because the tiers
    /// are price bands as much as power bands: a Floor-1 bench stocked with 130-gold
    /// Forbidden offers is a bench the player cannot use, and the first shop is where they
    /// learn what a bench is for.
    /// </summary>
    public static List<InscriptionData> DrawOffers(int floor, Rng rng, int count = 3)
    {
        var chosen = new List<InscriptionData>(count);
        int guard = 0;

        while (chosen.Count < count && guard++ < 128)
        {
            InscriptionTier tier = RollTier(floor, rng);
            InscriptionData? pick = DrawAtTier(tier, rng, chosen);
            if (pick is not null) chosen.Add(pick);
        }

        // Backstop: if the weighted draw could not fill three slots (a small pool with an
        // unlucky tier run), take anything not already offered. An empty bench slot is a
        // worse outcome than a slightly off-tier offer.
        foreach (InscriptionData d in All)
        {
            if (chosen.Count >= count) break;
            if (!chosen.Contains(d)) chosen.Add(d);
        }
        return chosen;
    }

    private static InscriptionTier RollTier(int floor, Rng rng)
    {
        float r = rng.NextFloat();
        float lesser = Mathf.Lerp(0.60f, 0.20f, Mathf.Clamp((floor - 1) / 5f, 0f, 1f));
        float greater = 0.35f;
        if (r < lesser) return InscriptionTier.Lesser;
        if (r < lesser + greater) return InscriptionTier.Greater;
        return InscriptionTier.Forbidden;
    }

    private static InscriptionData? DrawAtTier(InscriptionTier tier, Rng rng, List<InscriptionData> exclude)
    {
        InscriptionData? chosen = null;
        int seen = 0;
        foreach (InscriptionData d in All)
        {
            if (d.Tier != tier || exclude.Contains(d)) continue;
            seen++;
            if (rng.NextInt(0, seen) == 0) chosen = d;
        }
        return chosen;
    }

    public static InscriptionData? ById(string id)
    {
        foreach (InscriptionData d in All) if (d.Id == id) return d;
        return null;
    }
}
