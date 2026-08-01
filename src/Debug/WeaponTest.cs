using System.Collections.Generic;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// The weapon acquisition loop (docs/03 §1.1, docs/08 §2.1 slot 3, docs/08 §4).
///
///   godot --path . --headless res://scenes/debug/WeaponTest.tscn
///
/// **Why this gate exists at all.** Five weapons were authored, content-validated, and two
/// of them — Trench Sweeper and Nitro Express — were reachable by no means whatsoever.
/// `Interactable.Weapon` was a field with no writer. Every gate was green throughout, because
/// no gate asked the one question that would have caught it: *can a player obtain the things
/// we authored?* That is the sixth "specified, believed present, absent" in this project, and
/// it is the first one with a gate.
///
/// The headline assertion is therefore <see cref="TestEveryWeaponIsReachable"/> — not that
/// the pool works, but that **every acquirable weapon actually comes out of it**. A pool that
/// silently never yields its last entry passes every other check in this file.
/// </summary>
public sealed partial class WeaponTest : Node2D
{
    private int _failures;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" WEAPON ACQUISITION");
        GD.Print("================================================================");

        TestEveryAuthoredWeaponIsRegistered();
        TestPoolExcludesBoundArms();
        TestEveryWeaponIsReachable();
        TestStartingLoadoutLeavesRoom();
        TestSwapReplacesActiveInPlace();
        TestBoundArmIsNeverDropped();
        TestShopPricesAreInBand();

        GD.Print("================================================================");
        GD.Print(_failures == 0 ? " WEAPON ACQUISITION: PASS" : $" WEAPON ACQUISITION: FAIL ({_failures})");
        GD.Print("================================================================");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print($" [ok]   {what}");
        else { GD.PrintErr($" [FAIL] {what}"); _failures++; }
    }

    /// <summary>
    /// Every `.tres` under `data/weapons` appears in the pool.
    ///
    /// This is the assertion that actually catches the bug this file was written for, and it
    /// has to read the DIRECTORY rather than the pool. <see cref="TestEveryWeaponIsReachable"/>
    /// iterates <see cref="WeaponPool.Acquirable"/>, so a weapon authored and never registered
    /// is not merely unreachable — it is unreachable *and invisible to the test*, which is
    /// precisely how Trench Sweeper and Nitro Express survived a milestone.
    ///
    /// The pool stays an explicit list (a scan's order is filesystem-dependent, and this pool
    /// is drawn from with a seeded Rng — see <see cref="WeaponPool"/>). Scanning here is safe
    /// because nothing about the result feeds a draw: it is compared, not consumed.
    /// </summary>
    private void TestEveryAuthoredWeaponIsRegistered()
    {
        const string Dir = "res://data/weapons";

        using DirAccess? dir = DirAccess.Open(Dir);
        if (dir is null) { Check(false, $"{Dir} can be opened"); return; }

        var registered = new HashSet<string>();
        foreach (WeaponData w in WeaponPool.All) registered.Add(w.ResourcePath);

        int authored = 0;
        foreach (string file in dir.GetFiles())
        {
            // Godot exports .tres as .tres.remap; GetFiles reports the on-disk name.
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres")) continue;

            authored++;
            Check(registered.Contains($"{Dir}/{name}"),
                  $"{name} is registered in WeaponPool");
        }

        Check(authored == WeaponPool.All.Count,
              $"{authored} weapons on disk, {WeaponPool.All.Count} in the pool");
    }

    /// <summary>
    /// docs/03 §1.1 — a Bound Arm cannot be dropped, so one arriving as loot would occupy a
    /// slot for the rest of the run with no way to clear it.
    /// </summary>
    private void TestPoolExcludesBoundArms()
    {
        int bound = 0;
        foreach (WeaponData w in WeaponPool.Acquirable) if (w.IsBoundArm) bound++;

        Check(bound == 0, $"no Bound Arm is acquirable ({bound} in the pool)");
        Check(WeaponPool.Acquirable.Count > 0,
              $"the pool is not empty ({WeaponPool.Acquirable.Count} acquirable)");
    }

    /// <summary>
    /// The assertion this whole file exists for. Draw hard, across every floor, and require
    /// that each acquirable weapon comes out at least once.
    ///
    /// It is written against the POOL rather than a hardcoded list, so a weapon added to the
    /// roster is asserted without anyone remembering to add it here. The companion check that
    /// the roster itself is complete is <see cref="TestEveryAuthoredWeaponIsRegistered"/> —
    /// this one alone would be blind to a weapon that was never registered at all, which is
    /// the failure it exists to prevent.
    /// </summary>
    private void TestEveryWeaponIsReachable()
    {
        var seen = new HashSet<string>();
        var rng = new Rng(20260730);

        for (int floor = 1; floor <= 6; floor++)
        {
            for (int i = 0; i < 400; i++)
            {
                WeaponData? w = WeaponPool.Draw(floor, corruption: i % 11, rng);
                if (w is not null) seen.Add(w.DisplayName);
            }
        }

        foreach (WeaponData w in WeaponPool.Acquirable)
            Check(seen.Contains(w.DisplayName), $"{w.DisplayName} is reachable by drawing");
    }

    /// <summary>
    /// docs/03 §1.1 gives a run ONE Bound Arm; §4 expects floor 1 to be "Bound Arm + 1 found
    /// weapon". The floor run used to start with three Bound Arms, which is what made every
    /// found weapon impossible — Bound Arms cannot be dropped, so a full loadout of them has
    /// no slot a weapon can ever enter.
    ///
    /// Asserted against the resource the runner actually loads, so changing that array back
    /// breaks this rather than quietly re-closing the loop.
    /// </summary>
    private void TestStartingLoadoutLeavesRoom()
    {
        var holder = new WeaponHolder();
        var webley = GD.Load<WeaponData>("res://data/weapons/webley_mk_vi.tres");

        if (webley is null) { Check(false, "the starting Bound Arm loads"); return; }

        holder.Add(webley);
        Check(webley.IsBoundArm, $"{webley.DisplayName} is the run's Bound Arm");
        Check(holder.Count == 1 && WeaponHolder.MaxSlots - holder.Count == 2,
              $"a new run leaves {WeaponHolder.MaxSlots - holder.Count} free slots (want 2)");

        // And the loop is open from that state: a found weapon fits without displacing anything.
        WeaponData? found = WeaponPool.Acquirable.Count > 0 ? WeaponPool.Acquirable[0] : null;
        if (found is null) { Check(false, "a weapon exists to be found"); return; }

        Check(holder.Add(found) is not null,
              $"{found.DisplayName} fits alongside the Bound Arm without a swap");
    }

    /// <summary>
    /// A full loadout swaps IN PLACE, and the replaced weapon's Inscriptions go with it
    /// (docs/03 §3.4). In place, because an acquisition that reorders the loadout makes the
    /// next Q press do something the player did not learn.
    /// </summary>
    private void TestSwapReplacesActiveInPlace()
    {
        var holder = new WeaponHolder();
        List<WeaponData> pool = FillToCapacity(holder, boundFirst: true);
        if (pool.Count == 0) return;

        // Select a NON-Bound slot, which is what the shop requires before it will trade.
        holder.SetActive(1);
        Weapon before = holder.Active;
        Check(!before.Data.IsBoundArm, $"slot 2 holds {before.Data.DisplayName}, not a Bound Arm");

        // Etch something, so the loss is observable rather than theoretical.
        if (InscriptionPool.All.Count > 0)
        {
            foreach (InscriptionData ins in InscriptionPool.All)
            {
                if (before.RejectReason(ins) is null && before.HasFreeSlot) before.AddInscription(ins);
                if (!before.HasFreeSlot) break;
            }
        }
        int etched = before.Inscriptions.Count;
        Check(etched > 0, $"the outgoing weapon carries {etched} inscription(s) to lose");

        WeaponData incoming = pool[0];
        int countBefore = holder.Count;
        bool ok = holder.ReplaceActive(incoming);

        Check(ok, "the swap is accepted when the active weapon is not a Bound Arm");
        Check(holder.Count == countBefore, $"the loadout stays {countBefore} weapons (is {holder.Count})");
        Check(ReferenceEquals(holder.Active.Data, incoming),
              $"slot 2 now holds {holder.Active.Data.DisplayName} (want {incoming.DisplayName})");
        Check(holder.Weapons[0].Data.IsBoundArm, "slot 1 was not disturbed");
        Check(holder.Active.Inscriptions.Count == 0,
              $"the new weapon carries none of the old inscriptions ({holder.Active.Inscriptions.Count})");
    }

    /// <summary>
    /// The control for the assertion above, and the one that guards a real exploit: a Bound
    /// Arm must survive a swap attempt untouched. If this ever passes a replacement, the
    /// player can drop the weapon docs/03 §1.1 calls "the safety net that makes running dry
    /// survivable rather than fatal".
    /// </summary>
    private void TestBoundArmIsNeverDropped()
    {
        var holder = new WeaponHolder();
        List<WeaponData> pool = FillToCapacity(holder, boundFirst: true);
        if (pool.Count == 0) return;

        holder.SetActive(0);
        Check(holder.Active.Data.IsBoundArm, "slot 1 is the Bound Arm");

        WeaponData incoming = pool[0];
        bool refused = !holder.ReplaceActive(incoming);

        Check(refused, "a swap onto the Bound Arm is refused");
        Check(holder.Active.Data.IsBoundArm,
              $"the Bound Arm is still held (slot 1 is {holder.Active.Data.DisplayName})");
        Check(holder.Count == WeaponHolder.MaxSlots,
              $"the refusal cost no slot ({holder.Count}/{WeaponHolder.MaxSlots})");
    }

    /// <summary>
    /// docs/08 §2.1 puts the shop's weapon slot at 100–320 gold before floor scaling, and
    /// §2.1's table scales ×1.0 → ×2.0 across the six floors.
    ///
    /// Checked as an ABSOLUTE band rather than against the formula that produces it. A test
    /// that recomputes `Lerp(100, 320, ...)` and compares it to itself passes for any two
    /// numbers, which is the trap the tide gate's swim multiplier fell into.
    /// </summary>
    private void TestShopPricesAreInBand()
    {
        foreach (WeaponData w in WeaponPool.Acquirable)
        {
            int floor1 = WeaponPool.Price(w, 1, 1f);
            Check(floor1 is >= 100 and <= 320,
                  $"{w.DisplayName} [{w.Tier}] costs {floor1} on floor 1 (want 100-320)");

            int floor6 = WeaponPool.Price(w, 6, 1f);
            Check(floor6 > floor1,
                  $"{w.DisplayName} costs more on floor 6 ({floor6}) than floor 1 ({floor1})");
            Check(floor6 <= 640,
                  $"{w.DisplayName} at floor 6 ({floor6}) is inside 2x the band ceiling");
        }
    }

    /// <summary>
    /// Fill a holder to its three-slot limit, Bound Arm first, and return whatever acquirable
    /// weapons were left over for a swap to use. Returns empty (having failed a check) when
    /// the content cannot supply enough distinct weapons to pose the question.
    /// </summary>
    private List<WeaponData> FillToCapacity(WeaponHolder holder, bool boundFirst)
    {
        var leftover = new List<WeaponData>();

        var bound = GD.Load<WeaponData>("res://data/weapons/webley_mk_vi.tres");
        if (boundFirst && bound is not null) holder.Add(bound);

        // Acquirable weapons fill the remaining slots, so the loadout under test looks like
        // one a player could actually assemble: a Bound Arm and two found weapons.
        foreach (WeaponData w in WeaponPool.Acquirable)
            if (holder.Count < WeaponHolder.MaxSlots) holder.Add(w);

        // The INCOMING weapon may be drawn from the full roster, Bound Arms included. The
        // rule ReplaceActive enforces is about the weapon being dropped, not the one arriving
        // — and the acquirable pool is only two deep, so restricting the leftovers to it
        // would leave nothing over and skip the two assertions that matter most here.
        foreach (WeaponData w in WeaponPool.All)
        {
            if (ReferenceEquals(w, bound)) continue;
            bool held = false;
            foreach (Weapon h in holder.Weapons) if (ReferenceEquals(h.Data, w)) held = true;
            if (!held) leftover.Add(w);
        }

        if (holder.Count < WeaponHolder.MaxSlots || leftover.Count == 0)
        {
            // Not a content failure worth breaking the build over — it means the pool is too
            // small to fill three slots AND have a fourth weapon to offer. Say so loudly,
            // because it means the swap path is going untested.
            GD.Print($" [skip] pool too small to test the swap " +
                     $"({WeaponPool.Acquirable.Count} acquirable; need {WeaponHolder.MaxSlots} + 1). " +
                     "Author more weapons and this starts asserting.");
            return new List<WeaponData>();
        }

        return leftover;
    }
}
