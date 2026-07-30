using System.Collections.Generic;
using CultistOfCthulhu.Items;
using CultistOfCthulhu.Sigils;
using CultistOfCthulhu.Weapons;

namespace CultistOfCthulhu.Meta;

public enum RunOutcome { InProgress, Dead, Won }

/// <summary>
/// One weapon as it is carried between floors: what it is, and what has been etched on it.
///
/// Inscriptions are stored as data references rather than as a live <see cref="Weapon"/>,
/// because a weapon's runtime state — magazine, reload timer, the Perfect Recitation
/// window — is floor-scoped and meaningless across a transition. Carrying the live object
/// would carry a half-finished reload into the next floor's first room.
/// </summary>
public sealed class CarriedWeapon
{
    public WeaponData Data = null!;
    public readonly List<InscriptionData> Inscriptions = new();
    public int Reserve;
}

/// <summary>
/// The RUN, as opposed to the floor.
///
/// This is the thing that was missing. <c>FloorRunner</c> built a floor in _Ready and reset
/// it on death, so every piece of progression the player accumulated — their Circle, their
/// inscriptions, their gold, their Ascension debt — existed only for as long as one floor
/// did, and killing the boss cleared a room and then nothing happened at all. docs/11's M2
/// exit criterion is "a complete, replayable, winnable Floor 1", and the missing words
/// were *complete* and *winnable*.
///
/// One authority, held by <see cref="Core.GameRoot"/>. The player LOADS from it at the
/// start of a floor and WRITES BACK at the end, rather than the two holding separate
/// copies — two sources of truth for the same number is how a max-Sanity penalty gets
/// silently refunded halfway through a run.
///
/// Everything here is plain data or a plain class on purpose: this is what a save file
/// will serialise, and a save that has to reach into the scene tree is a save that breaks
/// every time the scene changes.
/// </summary>
public sealed class RunState
{
    public ulong Seed;
    public int FloorIndex = 1;
    public RunOutcome Outcome = RunOutcome.InProgress;

    /// <summary>
    /// The last floor of a run. **One, because one floor of content exists.**
    ///
    /// docs/07 plans six, and this rises as each is authored. It is a field rather than a
    /// constant so the run loop is already the real one — beating the boss on the final
    /// floor wins the run, beating it on any earlier floor descends — instead of a special
    /// case that has to be untangled when floor 2 lands.
    ///
    /// <c>--floors=N</c> raises it, which is the only way to exercise the floor scaling in
    /// the sigil tier table and the shop price bands until there is real content to scale.
    /// </summary>
    public int FinalFloor = 1;

    /// <summary>
    /// The floor this run BEGAN on. 1 in a real run; <c>--start-floor=N</c> moves it.
    ///
    /// Recorded because two run-scoped assertions quietly assumed floor 1. "Every floor was
    /// cleared" compared FloorsCleared against FinalFloor, which are different units the
    /// moment a run does not start at the top — begin on floor 2 and finish it and you have
    /// cleared one floor out of a final floor of two, which reads as a failure and is not one.
    /// A false failure in gate output is how people learn to stop reading gate output.
    /// </summary>
    public int StartFloor = 1;

    /// <summary>Floors this run has to clear to win, given where it started.</summary>
    public int FloorsToClear => System.Math.Max(1, FinalFloor - StartFloor + 1);

    // --- Progression that survives a floor ----------------------------------

    public float Hearts = 3f;
    public float MaxHearts = 3f;
    public int Armour;
    public int Gold;
    public int Keys;
    public float Corruption;

    /// <summary>Current Sanity. Carries; the Lucid Ceiling does NOT — docs/02 §3.3.1 makes
    /// the ceiling a per-floor descent, and a new floor resets it.</summary>
    public float Sanity = Core.Tune.SanityMax;

    /// <summary>Max Sanity granted from outside the Circle (the Altar of Nodens).</summary>
    public float BonusMaxSanity;

    /// <summary>Ascension's permanent max-Sanity debt. Kept apart from the bonus above so
    /// that re-deriving Max from the Circle cannot refund it.</summary>
    public float MaxSanityPenalty;

    /// <summary>Ascensions so far. Drives the diminishing duration and the escalating heart
    /// cost, both of which are per-RUN and would reset into an exploit if they were not
    /// carried (docs/02 §6).</summary>
    public int AscensionCount;

    /// <summary>The build. A live object rather than data, because it is a plain class with
    /// no scene presence and rebuilding it from a layout would mean re-running placement
    /// validation on something already known to be valid.</summary>
    public SigilCircle Circle = new();

    public readonly List<CarriedWeapon> Weapons = new();

    // --- Run-scoped systems -------------------------------------------------

    /// <summary>
    /// Telemetry spans the RUN, not the floor. It used to be constructed by the floor, so
    /// the M1 metrics would have silently restarted at every floor transition once one
    /// existed.
    /// </summary>
    public readonly Telemetry Telemetry = new();

    /// <summary>Drop pity is a run-length promise — "if you have had no key for five rooms,
    /// the next drop is a key" (docs/08 §1.3). Resetting it per floor would break it at
    /// exactly the boundary where the player is most likely to be short.</summary>
    public readonly DropTable Drops = new();

    // --- Tallies for the summary --------------------------------------------

    public int RoomsCleared;
    public int FloorsCleared;
    public float Duration;

    public bool IsFinalFloor => FloorIndex >= FinalFloor;

    /// <summary>Advance to the next floor. The caller has already decided the run continues.</summary>
    public void AdvanceFloor()
    {
        FloorsCleared++;
        FloorIndex++;
    }

    /// <summary>A seed for this floor, derived from the run seed and the floor index, so
    /// floor 2 of a given run is always the same floor 2 (docs/06 §7).</summary>
    public ulong FloorSeed => Core.Hash.Combine(Core.Hash.Combine(Seed, "floor"), FloorIndex);
}
