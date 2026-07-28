using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Items;

/// <summary>
/// Room-clear drops and the pity system (docs/06 §6.3).
///
/// The pity counters are the point. A pure probability table produces runs where a player
/// sees no keys for six rooms or runs dry on ammo with no relief, and those runs end for
/// reasons the player cannot influence or even perceive. docs/08 states the principle
/// directly — "bad seeds should not end runs" — and the pity system is how that is
/// enforced rather than hoped for.
/// </summary>
public sealed class DropTable
{
    // Base room-clear odds (docs/06 §6.3). Gold is guaranteed.
    private const float AmmoChance = 0.22f;
    private const float CandleChance = 0.15f;
    private const float KeyChance = 0.08f;
    private const float HeartChance = 0.03f;
    private const float ArmourChance = 0.06f;

    // Pity thresholds.
    private const int RoomsWithoutKeyBeforePity = 5;
    private const float ReserveFractionForAmmoPity = 0.20f;

    private int _roomsSinceKey;
    private int _roomsSinceCandle;

    /// <summary>
    /// Roll the drops for one cleared room and spawn them.
    ///
    /// <paramref name="playerKeys"/> and <paramref name="reserveFraction"/> feed the pity
    /// counters; <paramref name="sanityHeadroom"/> is how far below their ceiling the
    /// player is sitting, which biases candle drops toward the players who actually need
    /// them.
    /// </summary>
    public void RollRoomClear(PickupManager pickups, Vector2 at, Rng rng, int floor,
                              int playerKeys, float reserveFraction, float sanityHeadroom)
    {
        _roomsSinceKey++;
        _roomsSinceCandle++;

        // Gold: always. The one guaranteed payout, so a cleared room is never nothing.
        int gold = rng.NextInt(4, 9) + floor * 4;
        for (int i = 0; i < Mathf.Min(6, 1 + gold / 6); i++)
            pickups.Spawn(PickupKind.Gold, Scatter(at, rng), gold / Mathf.Max(1, 1 + gold / 6), rng);

        // --- Ammo, with pity. Running dry is a legitimate pressure; running dry with no
        // path back is a dead run.
        bool ammoPity = reserveFraction < ReserveFractionForAmmoPity;
        if (ammoPity || rng.Chance(AmmoChance))
            pickups.Spawn(PickupKind.Ammo, Scatter(at, rng), 1f, rng);

        // --- Sanity candle. The only thing that can push Sanity back above the Lucid
        // Ceiling (docs/02 §3.3.1), so its drop rate is effectively the strength of the
        // player's counter-play against the descent.
        //
        // Weighted by need: a player pinned far below their ceiling is more likely to see
        // one. This is a deliberate soft rubber-band, and it is the ONLY one in the
        // economy — Fable's review removed the rubber-banding from the ladder itself, so
        // reintroducing it here has to be conscious and narrow. It biases availability,
        // never power.
        float candleChance = CandleChance + Mathf.Clamp(sanityHeadroom / 100f, 0f, 1f) * 0.12f;
        bool candlePity = _roomsSinceCandle >= 6;
        if (candlePity || rng.Chance(candleChance))
        {
            pickups.Spawn(PickupKind.SanityCandle, Scatter(at, rng), Tune.SanityCandleValue, rng);
            _roomsSinceCandle = 0;
        }

        // --- Key, with pity. Locks are M2; the counter runs now so the pity behaviour is
        // already tuned when they land.
        bool keyPity = playerKeys == 0 && _roomsSinceKey >= RoomsWithoutKeyBeforePity;
        if (keyPity || rng.Chance(KeyChance))
        {
            pickups.Spawn(PickupKind.Key, Scatter(at, rng), 1f, rng);
            _roomsSinceKey = 0;
        }

        if (rng.Chance(ArmourChance)) pickups.Spawn(PickupKind.Armour, Scatter(at, rng), 1f, rng);
        if (rng.Chance(HeartChance)) pickups.Spawn(PickupKind.Heart, Scatter(at, rng), 0.5f, rng);
    }

    /// <summary>Small per-enemy drops so kills feel like they pay out immediately, without
    /// competing with the room-clear roll.</summary>
    public void RollEnemyDeath(PickupManager pickups, Vector2 at, Rng rng)
    {
        if (rng.Chance(0.35f)) pickups.Spawn(PickupKind.Gold, at, rng.NextInt(1, 4), rng);
    }

    private static Vector2 Scatter(Vector2 at, Rng rng)
        => at + rng.NextUnitVector() * rng.Range(0f, 26f);

    public void ResetForRun()
    {
        _roomsSinceKey = 0;
        _roomsSinceCandle = 0;
    }
}
