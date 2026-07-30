using System;
using System.Collections.Generic;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Items;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Headless simulation of the Sanity economy across a floor (docs/09 §9).
///
///   godot --path . --headless res://scenes/debug/EconomySim.tscn
///
/// WHAT THIS IS: a model of the economy, driven by the real SanitySystem, real DropTable
/// and real Tune constants. Combat is abstracted — a simulated player kills at a modelled
/// rate and takes hits at a modelled rate.
///
/// WHAT THIS IS NOT: a playtest. It cannot tell you whether the game is fun, whether the
/// ladder feels good, or whether hallucinations are fair. Those need a human.
///
/// What it CAN do, and the reason it exists: tell you whether the numbers are even in the
/// right neighbourhood before you spend a playtest finding out. If metric 9 reads 4% here,
/// no amount of human testing will rescue it — the ceiling decay is simply wrong, and it
/// is far cheaper to learn that from a 200ms headless run than from ten testers.
///
/// The three skill profiles matter because the economy is sensitive to hit rate: taking a
/// hit costs 10 Sanity, so a novice descends much faster than an expert. If the bands only
/// fire for bad players, the ladder is a rubber-band and Fable's finding has quietly
/// returned by a different route.
/// </summary>
public sealed partial class EconomySim : Node
{
    private sealed class Profile
    {
        public string Name = "";
        public float HitsPerMinute;      // how often they take damage
        public float DamageEfficiency;   // fraction of theoretical DPS achieved
        public float BanishesPerRoom;
        public float CandlePickupRate;   // fraction of dropped candles actually collected
    }

    private static readonly Profile[] Profiles =
    {
        new() { Name = "novice",  HitsPerMinute = 14f, DamageEfficiency = 0.45f, BanishesPerRoom = 0.5f,  CandlePickupRate = 0.7f },
        new() { Name = "average", HitsPerMinute = 7f,  DamageEfficiency = 0.65f, BanishesPerRoom = 0.25f, CandlePickupRate = 0.9f },
        new() { Name = "expert",  HitsPerMinute = 2f,  DamageEfficiency = 0.85f, BanishesPerRoom = 0.1f,  CandlePickupRate = 1.0f },
    };

    private const int RoomsPerFloor = 14;
    private const int Runs = 400;
    private const float Dt = 1f / 60f;

    public override void _Ready()
    {
        GD.Print("================================================================");
        GD.Print(" SANITY ECONOMY SIMULATION");
        GD.Print("================================================================");
        GD.Print($" {Runs} runs x {RoomsPerFloor} rooms per profile");
        GD.Print($" ceiling {Tune.LucidCeilingStart:F0} → {Tune.LucidCeilingFloor:F0} " +
                 $"at −{Tune.LucidCeilingDecayPerRoom:F0}/room   candle {Tune.SanityCandleValue:F0} (pierces)");
        GD.Print("----------------------------------------------------------------");
        GD.Print($" {"profile",-9} {"below40",8} {"ladder",8} {"net/room",9} {"wasted",7} {"candles",8} {"denied",7} {"ascend",7}");

        bool anyFail = false;

        foreach (Profile prof in Profiles)
        {
            var r = RunProfile(prof);
            GD.Print($" {prof.Name,-9} {r.BelowForty * 100,7:F1}% {r.LadderFire * 100,7:F0}% " +
                     $"{r.MedianNet,9:+0.0;-0.0} {r.WastedFraction * 100,6:F0}% {r.CandlesPerRoom,8:F2} " +
                     $"{r.DeniedPerRun,7:F1} {r.AscensionsPerRun,7:F2}");

            // Gates only on the average profile. Novice and expert are context: the
            // question is whether the DESIGNED player experiences the ladder, not whether
            // every skill level does.
            if (prof.Name == "average")
            {
                anyFail |= !Report("metric 1 — time below 40", r.BelowForty, 0.25f, 0.45f);
                anyFail |= !Report("metric 9 — ladder fires", r.LadderFire, 0.70f, 1.01f);
                // METRIC 5 IS GONE. It printed [OUT] on every single run for two milestones.
                //
                // Fable defined "median Sanity net per room, target ±15" to detect
                // income/cost mis-tuning, and it was written before the Lucid Ceiling existed
                // (option D in the same review, untested at the time). With a ceiling,
                // in-combat net is structurally negative BY DESIGN: kill income is capped and
                // the corridor top-up between rooms closes the gap. A player who ends every
                // room down 28 and is refilled before the next one is in perfect equilibrium,
                // and metric 5 called that a failure — for ever, in red, in output people are
                // supposed to read.
                //
                // Keeping it "for reference" behind a (STALE) label was the wrong compromise.
                // A permanent false failure does not teach the reader that one line is stale;
                // it teaches them the whole report is noise. The median is still printed below
                // as CONTEXT, with no target and no verdict, which is what it actually is.
                //
                // WASTED INCOME is the correct detector and it replaced it:
                //   high  -> income over-tuned relative to what the ceiling admits
                //   ~zero + player still bleeding -> income genuinely under-tuned
                GD.Print($"   median net/room {r.MedianNet,8:F2}   (context — no target; " +
                         $"negative in combat is by design under the Lucid Ceiling)");
                anyFail |= !Report("metric 5b — income wasted at ceiling", r.WastedFraction, 0.10f, 0.45f);
            }
        }

        GD.Print("----------------------------------------------------------------");
        GD.Print(" NOTE: a model, not a playtest. It says whether the numbers are in the");
        GD.Print("       right neighbourhood — not whether the game is fun.");
        GD.Print("================================================================");
        GD.Print(anyFail ? " ECONOMY SIM: OUT OF TARGET" : " ECONOMY SIM: IN TARGET");
        GD.Print("================================================================");

        // Advisory, not a build gate — these are tuning targets, and failing them should
        // start a conversation rather than break the build.
        GetTree().Quit(0);
    }

    private static bool Report(string label, float value, float lo, float hi)
    {
        bool ok = value >= lo && value <= hi;
        GD.Print($"   [{(ok ? "in " : "OUT")}] {label}: {value:F2}  target {lo:F2}..{hi:F2}");
        return ok;
    }

    private readonly record struct Result(
        float BelowForty, float LadderFire, float MedianNet,
        float CandlesPerRoom, float DeniedPerRun, float AscensionsPerRun,
        float WastedFraction);

    private static Result RunProfile(Profile prof)
    {
        // ONE PickupManager for the whole sweep. It is a Node2D, and constructing 5,600 of
        // them without ever adding them to the tree leaks every one — Godot reported 1,200
        // orphaned CanvasItem RIDs before this was hoisted.
        var pickups = new PickupManager();

        var nets = new List<float>(Runs * RoomsPerFloor);
        float totalTime = 0f, lowTime = 0f;
        int roomsWithLadder = 0, totalRooms = 0;
        int candles = 0, denied = 0, ascensions = 0;
        float potentialIncome = 0f, wastedIncome = 0f;

        for (int run = 0; run < Runs; run++)
        {
            var rng = Hash.Derive(0xC0FFEE, "economy", run);
            var sanity = new SanitySystem();
            var drops = new DropTable();
            drops.ResetForRun();

            for (int room = 0; room < RoomsPerFloor; room++)
            {
                // Room composition mirrors CombatArena: budget scales, >=35% fodder.
                float budget = 40f + room * 16f;
                int enemies = Mathf.Max(3, Mathf.RoundToInt(budget / 13f));
                float roomHp = enemies * 32f;
                float roomSanityValue = enemies * 7.5f;

                // Webley: 11 dmg x 4.5/s, 6-round magazine, reload weight 0.5.
                float dps = 11f * 4.5f * prof.DamageEfficiency;
                float clearSeconds = Mathf.Clamp(roomHp / dps, 6f, 90f);
                int shots = Mathf.CeilToInt(roomHp / 11f);
                int reloads = shots / 6;

                float startSanity = sanity.Current;
                float income = 0f, spend = 0f;
                bool ladderFired = false;
                sanity.InCombat = true;

                float killInterval = clearSeconds / enemies;
                float reloadInterval = reloads > 0 ? clearSeconds / reloads : float.MaxValue;
                float hitInterval = prof.HitsPerMinute > 0 ? 60f / prof.HitsPerMinute : float.MaxValue;

                float nextKill = killInterval, nextReload = reloadInterval, nextHit = hitInterval;
                float banishAt = prof.BanishesPerRoom > 0f ? clearSeconds * 0.5f : float.MaxValue;

                for (float t = 0f; t < clearSeconds; t += Dt)
                {
                    sanity.Tick(Dt);
                    totalTime += Dt;
                    if (sanity.Band >= SanityBand.Fraying)
                    {
                        lowTime += Dt;
                        ladderFired = true;
                    }

                    if (t >= nextKill)
                    {
                        nextKill += killInterval;
                        float v = roomSanityValue / enemies;
                        float before = sanity.Current;
                        sanity.GainFromKill(v);
                        float applied = sanity.Current - before;
                        income += applied;

                        // Income discarded because the player was already at the Lucid
                        // Ceiling. This is the number that actually diagnoses the economy
                        // now — see the note on metric 5 below.
                        potentialIncome += v;
                        wastedIncome += v - applied;
                    }

                    if (t >= nextReload)
                    {
                        nextReload += reloadInterval;
                        float cost = Tune.SanityReciteCostPerWeight * 0.5f;
                        if (sanity.TrySpend(cost)) spend += cost;
                        else denied++;
                    }

                    if (t >= nextHit)
                    {
                        nextHit += hitInterval;
                        sanity.Drain(Tune.SanityHitCost);
                        spend += Tune.SanityHitCost;
                    }

                    if (t >= banishAt && rng.Chance(prof.BanishesPerRoom))
                    {
                        banishAt = float.MaxValue;
                        if (sanity.TrySpend(Tune.SanityBanishCost)) spend += Tune.SanityBanishCost;
                        else denied++;
                    }

                    if (sanity.ConsumeAscensionTrigger())
                    {
                        ascensions++;
                        sanity.RestoreTo(Tune.AscensionExitSanity);
                        sanity.ReduceMax(Tune.AscensionMaxSanityPenalty, Tune.AscensionMaxSanityFloor);
                    }
                }

                // Room clear: drops first (candle roll sees in-fight headroom), then the
                // ceiling decays — matching CombatArena.EndRoom.
                float headroom = sanity.LucidCeiling - sanity.Current;
                pickups.ClearAll();
                drops.RollRoomClear(pickups, Vector2.Zero, rng, 1, 0, 0.6f, headroom);

                foreach (Pickup p in pickups.Pickups)
                {
                    if (p.Kind != PickupKind.SanityCandle) continue;
                    if (!rng.Chance(prof.CandlePickupRate)) continue;
                    sanity.GainPiercing(p.Amount);   // PIERCES the ceiling — the counter-play
                    income += p.Amount;
                    candles++;
                }

                sanity.InCombat = false;
                sanity.OnRoomCleared();

                // Corridor walk before the next room.
                for (int i = 0; i < 240; i++) sanity.Tick(Dt);

                nets.Add(income - spend);
                if (ladderFired) roomsWithLadder++;
                totalRooms++;
                _ = startSanity;
            }
        }

        nets.Sort();
        var result = new Result(
            BelowForty: totalTime > 0f ? lowTime / totalTime : 0f,
            LadderFire: totalRooms > 0 ? roomsWithLadder / (float)totalRooms : 0f,
            MedianNet: nets.Count > 0 ? nets[nets.Count / 2] : 0f,
            CandlesPerRoom: totalRooms > 0 ? candles / (float)totalRooms : 0f,
            DeniedPerRun: denied / (float)Runs,
            AscensionsPerRun: ascensions / (float)Runs,
            WastedFraction: potentialIncome > 0f ? wastedIncome / potentialIncome : 0f);

        pickups.Free();
        return result;
    }
}
