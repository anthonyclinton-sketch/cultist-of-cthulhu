using CultistOfCthulhu.Sigils;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Generation;

/// <summary>
/// docs/06 §6.1 — encounters are not enemy lists, they are BUDGETS.
///
///   DreadBudget = base(floor) x sizeClassMult x roleMult
///               x (1 + 0.06 x Corruption) x playerPowerMult
///
/// Pure functions over primitives, deliberately. The formula has a review note attached to
/// it (§6.1) asking for a specific assertion — that a full circle of cheap sigils must not
/// out-scale a half circle of expensive ones — and an assertion like that is only writable
/// if the maths is reachable without standing up a floor, a player and a scene.
///
/// WHAT WAS MISSING. The populator computed
/// <c>min(capacity, (34 + roomsCleared x 13) x areaScale)</c> and stopped. There was no
/// Corruption term and no player-power term at all, so a player who filled their Circle,
/// kitted a weapon and bought hearts faced exactly the same rooms as one who had done none
/// of it — the reward for building well was pure, unopposed, and the floor never answered.
/// </summary>
public static class DreadBudget
{
    /// <summary>
    /// docs/06 §6.1 clamps the player-power response to [0.85, 1.35]. The floor scales
    /// SLIGHTLY to the player and never enough to erase the reward for building well — the
    /// clamp is what preserves that, so a strong build meets 35% more Dread while being far
    /// more than 35% stronger. This is the explicit rejection of the Oblivion trap.
    /// </summary>
    public const float PowerMin = 0.85f;
    public const float PowerMax = 1.35f;

    // Weights for the power score. FIRST PASS — the doc names the four inputs and the clamp
    // and does not name their relative weight, so these are chosen to put a fresh run near
    // the floor and a well-built late run at the cap, and they want a tuning pass against
    // real play. They are constants rather than .tres for the same reason Tune.cs still
    // exists; see docs/09 §5.
    private const float CellsReference = 60f;      // tier-weighted cells that count as "fully built"
    private const float WeaponWeight = 0.35f;
    private const float InscriptionWeight = 0.25f;
    private const float InscriptionReference = 6f;
    private const float HeartWeight = 0.15f;
    private const float PowerSpan = 0.42f;

    /// <summary>
    /// How strong the player is, as the multiplier the budget uses.
    ///
    /// The cell term is TIER-WEIGHTED, not a sigil count, and that is the fix Fable's review
    /// asked for rather than a detail: counting sigils rates a player who fills the Circle
    /// with many small efficient tiles as stronger than one holding three large ones, so
    /// engaging with the puzzle the Circle exists for would raise the difficulty of every
    /// later room. It also uniquely punished the character whose whole identity is holding
    /// the most sigils.
    /// </summary>
    public static float PlayerPower(float tierWeightedCells, int bestWeaponTier,
                                   int inscriptions, float maxHearts)
    {
        float score =
            tierWeightedCells / CellsReference
            + Mathf.Clamp(bestWeaponTier / 4f, 0f, 1f) * WeaponWeight
            + Mathf.Clamp(inscriptions / InscriptionReference, 0f, 1f) * InscriptionWeight
            + Mathf.Clamp((maxHearts - 3f) / 3f, 0f, 1f) * HeartWeight;

        return Mathf.Clamp(PowerMin + score * PowerSpan, PowerMin, PowerMax);
    }

    /// <summary>Tier-weighted cell count for a Circle. The Heart is included and is the same
    /// for everyone, so it shifts the baseline rather than differentiating builds.</summary>
    public static float TierWeightedCells(SigilCircle circle)
    {
        float total = 0f;
        foreach (PlacedSigil p in circle.Placed) total += p.Cells.Length * p.Data.TierMultiplier;
        return total;
    }

    /// <summary>Highest weapon tier carried, as an index (D=0 .. S=4).</summary>
    public static int BestWeaponTier(WeaponHolder weapons)
    {
        int best = 0;
        foreach (Weapon w in weapons.Weapons) best = Mathf.Max(best, (int)w.Data.Tier);
        return best;
    }

    public static int TotalInscriptions(WeaponHolder weapons)
    {
        int n = 0;
        foreach (Weapon w in weapons.Weapons) n += w.Inscriptions.Count;
        return n;
    }

    /// <summary>
    /// Room size class, from area. docs/06 §6.1's <c>sizeClassMult</c>.
    ///
    /// Scaled by the SQUARE ROOT of area, not area itself: enemy count rising linearly with
    /// floor space turns a big room into a slog rather than a bigger fight. Rooms are
    /// screen-relative and span 4-8x their first-pass area, so without this a four-screen
    /// room read as emptier than a one-screen one and the player simply ran past everything.
    /// </summary>
    public static float SizeClass(int widthTiles, int heightTiles) =>
        Mathf.Clamp(Mathf.Sqrt(widthTiles * heightTiles / 1100f), 0.85f, 2.3f);

    public static float RoleMultiplier(RoomRole role) => role switch
    {
        RoomRole.CombatEasy => 0.85f,
        RoomRole.CombatMed => 1.0f,
        RoomRole.CombatHard => 1.25f,
        RoomRole.Hub => 1.0f,
        _ => 1.0f,
    };

    /// <summary>
    /// The whole formula.
    ///
    /// <paramref name="roomsCleared"/> carries within-floor progression, which the spec folds
    /// into base(floor); keeping it explicit means an early room on floor 3 is still easier
    /// than a late room on floor 3, which is what the pacing actually needs.
    /// </summary>
    public static float For(int floorIndex, int roomsCleared, RoomTemplate template, RoomRole role,
                           float corruption, float playerPower)
    {
        float baseline = 26f + floorIndex * 8f + roomsCleared * 13f;

        float budget = baseline
                       * SizeClass(template.WidthTiles, template.HeightTiles)
                       * RoleMultiplier(role)
                       * (1f + 0.06f * corruption)
                       * playerPower;

        // The authored ceiling still wins. docs/06 §4 prices ThreatCapacity as a function of
        // the room's own floor area and cover, so it is the one number that knows whether the
        // room can physically hold what the formula asked for.
        return Mathf.Min(template.ThreatCapacity, budget);
    }
}
