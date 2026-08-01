using System.Collections.Generic;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Sigils;
using Godot;

namespace CultistOfCthulhu.Weapons;

/// <summary>
/// Every weapon a player can ACQUIRE, and the rules for drawing one.
///
/// This pool is the thing that was missing. Five weapons were authored and
/// content-validated; three were handed out by a hardcoded array at run start and the other
/// two — Trench Sweeper and Nitro Express — were reachable by no means at all.
/// <c>Interactable.Weapon</c> was a field with no writer, drop tables held no weapons, and
/// Gaunt's stall stocked everything except the slot docs/08 §2.1 gives it. Authoring more
/// weapons before this existed would have added them to the same unreachable pile.
///
/// An explicit list rather than a directory scan, for the same reason as
/// <see cref="SigilPool"/>: the pool is drawn from with a seeded Rng, and a filesystem
/// enumeration order that differs between machines makes the same seed produce different
/// loot.
///
/// **Bound Arms are excluded on purpose.** docs/03 §1.1 makes them character-bound starters
/// with infinite ammo that cannot be dropped — a lootable Bound Arm is a weapon that can
/// never be replaced once taken, occupying a slot permanently. They enter a loadout through
/// character selection, which does not exist yet, not through the floor.
/// </summary>
public static class WeaponPool
{
    /// <summary>
    /// Every authored weapon, Bound Arms included.
    ///
    /// Listed in full and filtered by <see cref="Acquirable"/> rather than simply omitting
    /// the Bound Arms, so the exclusion is a RULE the pool applies and not an accident of
    /// which lines someone remembered to write. The difference matters the first time a
    /// weapon is authored with the flag set: it lands here, gets excluded for a stated
    /// reason, and the gate says so — instead of silently never appearing.
    /// </summary>
    private static readonly string[] Paths =
    {
        "res://data/weapons/webley_mk_vi.tres",
        "res://data/weapons/cantrip_withering.tres",
        "res://data/weapons/sacrificial_kris.tres",
        "res://data/weapons/trench_sweeper.tres",
        "res://data/weapons/nitro_express.tres",
    };

    private static List<WeaponData>? _all;

    /// <summary>Every loadable weapon, Bound Arms included. Use <see cref="Acquirable"/> for
    /// anything the player can be given.</summary>
    public static IReadOnlyList<WeaponData> All => _all ??= Load();

    private static List<WeaponData>? _acquirable;

    /// <summary>What the floor may hand out — everything in the pool that is not a Bound Arm.</summary>
    public static IReadOnlyList<WeaponData> Acquirable
    {
        get
        {
            if (_acquirable is not null) return _acquirable;
            _acquirable = new List<WeaponData>();
            foreach (WeaponData w in All) if (!w.IsBoundArm) _acquirable.Add(w);
            return _acquirable;
        }
    }

    private static List<WeaponData> Load()
    {
        var list = new List<WeaponData>(Paths.Length);
        foreach (string p in Paths)
        {
            var d = GD.Load<WeaponData>(p);
            if (d is not null) list.Add(d);
            else GD.PrintErr($"[WeaponPool] failed to load {p}");
        }
        return list;
    }

    /// <summary>
    /// Draw a weapon at (or near) a rolled tier, excluding anything already carried.
    ///
    /// The tier roll and the Corruption bump come from <see cref="SigilPool.RollTier"/>
    /// rather than a second copy of docs/08 §3's table. The two tables are the same table —
    /// duplicating it here is one tuning pass away from weapons and sigils disagreeing about
    /// what Corruption 5 is worth, and that disagreement would be invisible.
    ///
    /// Falls back through neighbouring tiers rather than failing, for the same reason the
    /// sigil draw does: the pool is two weapons deep, so an exact-tier draw almost always
    /// misses, and a shop with an empty weapon slot is a worse outcome than one offering a
    /// tier off.
    /// </summary>
    public static WeaponData? Draw(int floor, float corruption, Rng rng,
                                   ICollection<WeaponData>? exclude = null)
    {
        var want = (int)SigilPool.RollTier(floor, corruption, rng);

        for (int spread = 0; spread < 5; spread++)
        {
            WeaponData? pick = DrawAtTier(want - spread, rng, exclude)
                               ?? DrawAtTier(want + spread, rng, exclude);
            if (pick is not null) return pick;
        }
        return null;
    }

    private static WeaponData? DrawAtTier(int tier, Rng rng, ICollection<WeaponData>? exclude)
    {
        if (tier < 0 || tier > 4) return null;

        var candidates = new List<WeaponData>();
        foreach (WeaponData w in Acquirable)
        {
            if ((int)w.Tier != tier) continue;
            if (exclude is not null && exclude.Contains(w)) continue;
            candidates.Add(w);
        }

        if (candidates.Count == 0) return null;
        return candidates[rng.NextInt(0, candidates.Count)];
    }

    /// <summary>
    /// Resolve a `--weapons=a,b,c` loadout spec into weapons to hand the player.
    ///
    /// The testing bench docs/09 §10 asks for by name. That section specifies a cheat console
    /// with `give <weapon>` and calls it *"non-negotiable for content velocity — you will
    /// otherwise waste hundreds of hours replaying floor 1"*; this is the cheap half of it,
    /// and it exists because the alternative way to hold a Nitro Express was to earn 210 gold.
    ///
    /// **Resolves against the DIRECTORY, not <see cref="All"/>.** A weapon that has been
    /// authored but not yet registered is exactly the one someone most wants to shoot — a
    /// bench that can only load registered weapons cannot help you decide whether to register
    /// one. Draws still come from the pool; only this bypasses it.
    ///
    /// Accepts a file stem (`nitro_express`) or a display name (`Nitro Express`, `nitro
    /// express`), because remembering which of those a given weapon uses is friction the
    /// bench exists to remove.
    /// </summary>
    public static List<WeaponData> ResolveLoadout(string spec)
    {
        var picked = new List<WeaponData>();
        if (spec.Length == 0) return picked;

        foreach (string raw in spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            string id = raw.Trim();
            WeaponData? found = GD.Load<WeaponData>($"res://data/weapons/{id}.tres") ?? ByDisplayName(id);

            if (found is null)
            {
                GD.PrintErr($"[loadout] no weapon matches '{id}'. Available: {string.Join(", ", Stems())}");
                continue;
            }

            // Reported rather than swallowed: a bench that silently drops the fourth weapon
            // looks like the fourth weapon is broken.
            if (picked.Count >= WeaponHolder.MaxSlots)
            {
                GD.PrintErr($"[loadout] {found.DisplayName} dropped — only " +
                            $"{WeaponHolder.MaxSlots} slots (docs/03 §1.1).");
                continue;
            }

            string? err = found.Validate();
            if (err is not null) GD.PrintErr($"[loadout] {found.DisplayName}: {err}");

            picked.Add(found);
            GD.Print($"[loadout] {found.DisplayName} [{found.Tier}] {found.Family}" +
                     $"  recite {found.SanityCostToReload:0} Sanity" +
                     (found.IsBoundArm ? "  (Bound Arm — infinite ammo)" : ""));
        }

        // Said once, here, rather than discovered as "the gun stopped working". Every weapon
        // except a Bound Arm has finite reserve, and the bench has no shop to refill at.
        bool anyBound = false;
        foreach (WeaponData w in picked) if (w.IsBoundArm) anyBound = true;
        if (picked.Count > 0 && !anyBound)
            GD.Print("[loadout] no Bound Arm in this loadout — reserve ammo is finite and " +
                     "there is no safety net when it runs dry (docs/03 §1.1).");

        return picked;
    }

    private static WeaponData? ByDisplayName(string name)
    {
        string want = Normalise(name);
        foreach (string stem in Stems())
        {
            var d = GD.Load<WeaponData>($"res://data/weapons/{stem}.tres");
            if (d is not null && Normalise(d.DisplayName) == want) return d;
        }
        return null;
    }

    private static string Normalise(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Every authored weapon's file stem, read from disk so the error message lists
    /// what is actually there rather than what the pool remembers.</summary>
    private static List<string> Stems()
    {
        var stems = new List<string>();
        using DirAccess? dir = DirAccess.Open("res://data/weapons");
        if (dir is null) return stems;

        foreach (string file in dir.GetFiles())
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (name.EndsWith(".tres")) stems.Add(name[..^5]);
        }
        return stems;
    }

    /// <summary>
    /// docs/08 §2.1 slot 3 — a weapon at 100–320 gold, scaled by tier and by floor.
    ///
    /// The band is spread across the five tiers the same way the sigil slots spread 80–260,
    /// so a D weapon and a D sigil are priced against the same idea of what a tier is worth.
    /// </summary>
    public static int Price(WeaponData w, int floorIndex, float priceMultiplier)
    {
        float floorScale = InscriptionData.FloorScale(floorIndex);
        float band = Mathf.Lerp(100f, 320f, (int)w.Tier / 4f);
        return Mathf.RoundToInt(band * floorScale * priceMultiplier);
    }
}
