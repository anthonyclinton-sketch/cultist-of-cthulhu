using System.Collections.Generic;
using System.Text;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Generation;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// The generation gate (docs/09 §9): 10,000 seeds, every invariant asserted, failing seed
/// printed.
///
///   godot --path . --headless res://scenes/debug/GenerationTest.tscn
///   godot --path . --headless res://scenes/debug/GenerationTest.tscn --show-seed 42
///
/// A procedural generator without this is a system whose failures reach players as "the
/// game is broken and I don't know why". With it, a failure is one command from being
/// reproduced. docs/11 makes the sweep a per-commit CI gate for exactly that reason.
///
/// It also reports the DISTRIBUTION, not just pass/fail. A generator that passes every
/// invariant while producing the same 12-room floor every time is technically correct and
/// creatively worthless, and only the distribution shows that.
/// </summary>
public sealed partial class GenerationTest : Node
{
    private const int Seeds = 10000;

    /// <summary>Room counts seen for one floor index. A struct in a flat array so the sweep
    /// stays allocation-free — it runs 10,000 times per commit.</summary>
    private struct BandTally
    {
        public int Count;
        public int Min;
        public int Max;
        private long _total;

        public void Add(int rooms)
        {
            if (Count == 0) { Min = rooms; Max = rooms; }
            else { if (rooms < Min) Min = rooms; if (rooms > Max) Max = rooms; }
            Count++;
            _total += rooms;
        }

        public float Mean => Count == 0 ? 0f : _total / (float)Count;
    }

    public override void _Ready()
    {
        var flows = UndercroftContent.Flows();
        var rooms = UndercroftContent.Rooms();

        GD.Print("================================================================");
        GD.Print(" FLOOR GENERATION SWEEP");
        GD.Print("================================================================");

        int contentErrors = ValidateContent(flows, rooms);
        if (contentErrors > 0)
        {
            GD.PrintErr($" {contentErrors} content error(s) — fix before sweeping.");
            GetTree().Quit(1);
            return;
        }

        // --show-seed N dumps one floor as ASCII instead of sweeping. This is the
        // Generation Visualiser from docs/06 §10 in its cheapest useful form — being able
        // to LOOK at a failing seed is worth more than any amount of aggregate statistics.
        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (!arg.StartsWith("--show-seed=")) continue;
            ShowSeed(flows, rooms, Hash.ParseSeed(arg["--show-seed=".Length..]));
            GetTree().Quit(0);
            return;
        }

        Sweep(flows, rooms);
    }

    private int ValidateContent(List<FloorFlow> flows, List<RoomTemplate> rooms)
    {
        int errors = 0;

        foreach (FloorFlow f in flows)
        {
            string? err = f.Validate();
            if (err is null) continue;
            GD.PrintErr($" [FLOW] {err}");
            errors++;
        }

        foreach (RoomTemplate r in rooms)
        {
            string? err = r.Validate();
            if (err is null) continue;
            GD.PrintErr($" [ROOM] {err}");
            errors++;
        }

        // Every role a flow can request must have at least one room that satisfies it,
        // or generation fails at runtime with a confusing "no template" error.
        var have = new HashSet<RoomRole>();
        foreach (RoomTemplate r in rooms) have.Add(r.Role);
        foreach (RoomRole role in new[]
                 {
                     RoomRole.Entrance, RoomRole.CombatEasy, RoomRole.CombatMed, RoomRole.CombatHard,
                     RoomRole.Hub, RoomRole.Connector, RoomRole.Reward, RoomRole.Shop,
                     RoomRole.Shrine, RoomRole.Secret, RoomRole.BossFoyer, RoomRole.Boss,
                 })
        {
            if (have.Contains(role)) continue;
            GD.PrintErr($" [CONTENT] no room template for role {role}");
            errors++;
        }

        // DEGREE SATISFIABILITY. A flow node with more neighbours than any room of its
        // role has exits is geometrically impossible — the generator cannot attach the
        // connections it is asked for, and the failure surfaces at the far end as a high
        // failure RATE rather than an error, which is very hard to attribute.
        //
        // This check exists because it happened: undercroft_figure_eight's hub needs six
        // connections and the best hub template had five, so that flow succeeded 4% of the
        // time and the sweep reported only "backtrack budget exhausted".
        var maxExitsByRole = new Dictionary<RoomRole, int>();
        foreach (RoomTemplate r in rooms)
        {
            maxExitsByRole.TryGetValue(r.Role, out int cur);
            if (r.ExitCount > cur) maxExitsByRole[r.Role] = r.ExitCount;
        }

        foreach (FloorFlow f in flows)
        {
            foreach (FlowNode n in f.Nodes)
            {
                int available = maxExitsByRole.GetValueOrDefault(n.Role);
                if (n.Neighbours.Count <= available) continue;
                GD.PrintErr($" [DEGREE] flow '{f.Id}' node {n.Id} ({n.Role}) needs " +
                            $"{n.Neighbours.Count} connections; best {n.Role} template has {available} exits");
                errors++;
            }
        }

        GD.Print($" {flows.Count} flows, {rooms.Count} room templates, {errors} content error(s)");
        GD.Print("----------------------------------------------------------------");
        return errors;
    }

    private void Sweep(List<FloorFlow> flows, List<RoomTemplate> rooms)
    {
        var gen = new FloorGenerator(flows, rooms);

        int ok = 0, failed = 0;
        int minRooms = int.MaxValue, maxRooms = 0;
        long totalRooms = 0, totalAttempts = 0;
        var failures = new Dictionary<string, int>();
        var flowUse = new Dictionary<string, int>();
        ulong firstFailingSeed = 0;
        int retried = 0;
        int fallbacks = 0;

        // Every floor, not just floor 1. The sweep swept floorIndex: 1 for a milestone while
        // the generator ignored the floor index entirely, so it was measuring the only case
        // that could not reveal that — see the band check below.
        var band = new BandTally[FloorScaling.DeepestFloor + 1];
        var fallbackByFloor = new int[FloorScaling.DeepestFloor + 1];
        var fallbackCauses = new Dictionary<string, int>();
        var seedsByFloor = new int[FloorScaling.DeepestFloor + 1];

        for (int i = 0; i < Seeds; i++)
        {
            ulong seed = Hash.Combine(0xC0FFEEUL, i);
            int floorIndex = 1 + i % FloorScaling.DeepestFloor;
            seedsByFloor[floorIndex]++;
            GeneratedFloor? floor = gen.Generate(seed, floorIndex, out string failure);

            if (floor is null)
            {
                if (failed == 0) firstFailingSeed = seed;
                failed++;
                string key = Shorten(failure);
                failures[key] = failures.GetValueOrDefault(key) + 1;
                continue;
            }

            ok++;
            if (gen.UsedFallback)
            {
                fallbacks++;
                fallbackByFloor[floorIndex]++;

                // WHY the authored flow gave up. Generate leaves its last failure in `failure`
                // even when the fallback then succeeds, and nothing was reading it — so a
                // rising fallback rate was a number with no attached cause, which is the one
                // thing a gate should never report.
                string why = $"{gen.LastAuthoredFlowId,-28} target {gen.LastRoomTarget,2}   " +
                             Shorten(gen.LastAuthoredFailure);
                fallbackCauses[why] = fallbackCauses.GetValueOrDefault(why) + 1;
            }
            totalRooms += floor.Rooms.Count;
            totalAttempts += floor.Attempts;
            if (floor.Attempts > 1) retried++;
            minRooms = Mathf.Min(minRooms, floor.Rooms.Count);
            maxRooms = Mathf.Max(maxRooms, floor.Rooms.Count);
            flowUse[floor.FlowId] = flowUse.GetValueOrDefault(floor.FlowId) + 1;

            // The fallback flow is excluded on purpose. It is authored to be trivially
            // placeable rather than well paced (docs/06 §5.5) and has no expandable chain, so
            // it cannot reach any band — holding it to one would force it to stop being
            // minimal, which is the property the whole retry escape hatch rests on. Its count
            // is reported separately so this exemption stays visible.
            if (!gen.UsedFallback) band[floorIndex].Add(floor.Rooms.Count);
        }

        GD.Print($" seeds            {Seeds}");
        GD.Print($" generated        {ok}   failed {failed}");
        GD.Print($" rooms per floor  {(ok > 0 ? $"{minRooms}..{maxRooms}" : "n/a")}   " +
                 $"mean {(ok > 0 ? totalRooms / (double)ok : 0):F1}");
        GD.Print($" attempts         mean {(ok > 0 ? totalAttempts / (double)ok : 0):F2}   " +
                 $"needed a retry: {retried * 100.0 / Mathf.Max(1, ok):F1}%");

        GD.Print(" flow usage:");
        foreach ((string id, int n) in flowUse) GD.Print($"   {id,-28} {n * 100.0 / Mathf.Max(1, ok),5:F1}%");

        // docs/07 §2's room count, floor by floor. Printed as well as asserted: a floor that
        // sits on its band's edge every time is passing while producing one length, and only
        // the mean shows that.
        GD.Print(" rooms by floor (docs/07 §2):");
        int bandFailures = 0;
        for (int f = 1; f <= FloorScaling.DeepestFloor; f++)
        {
            ref BandTally t = ref band[f];
            if (t.Count == 0) { GD.Print($"   floor {f}   (no samples)"); continue; }

            if (!FloorScaling.TryRoomCount(f, out int lo, out int hi))
            {
                GD.Print($"   floor {f}   {t.Min}..{t.Max}  mean {t.Mean:F1}   open — no band");
                continue;
            }

            bool inBand = t.Min >= lo && t.Max <= hi;
            if (!inBand) bandFailures++;
            GD.Print($"   floor {f}   {t.Min}..{t.Max}  mean {t.Mean:F1}   " +
                     $"want {lo}..{hi}   {(inBand ? "ok" : "OUT OF BAND")}   " +
                     $"fallback {fallbackByFloor[f] * 100.0 / Mathf.Max(1, seedsByFloor[f]):F2}%");
        }

        if (fallbackCauses.Count > 0)
        {
            GD.Print(" why the authored flow fell back:");
            foreach ((string reason, int n) in fallbackCauses) GD.Print($"   {n,6}x  {reason}");
        }

        if (failures.Count > 0)
        {
            GD.Print(" failure reasons:");
            foreach ((string reason, int n) in failures) GD.Print($"   {n,6}x  {reason}");
            GD.Print($" reproduce the first failure with:");
            GD.Print($"   --show-seed={Hash.FormatSeed(firstFailingSeed)}");
        }

        GD.Print("----------------------------------------------------------------");

        float fallbackRate = fallbacks / (float)Mathf.Max(1, Seeds);

        bool allGenerated = failed == 0;
        bool variety = maxRooms - minRooms >= 3;
        // Count only AUTHORED flows — the fallback appears in flowUse too, and counting it
        // made "4 of 3" a failure.
        int authoredUsed = 0;
        foreach (FloorFlow f in flows) if (flowUse.ContainsKey(f.Id)) authoredUsed++;
        bool flowsUsed = authoredUsed == flows.Count;
        // The fallback exists so no seed can end a run — but it produces a deliberately
        // plain floor, so leaning on it is a content problem. A rising rate means an
        // authored flow or a room set has drifted into being hard to place.
        bool fallbackRare = fallbackRate <= 0.01f;
        bool bandsHeld = bandFailures == 0;

        // THE CONTROL FOR THE BAND CHECK (working agreement: add the control with the
        // assertion). Every band overlaps its neighbour — floor 1 is 11–14 and floor 4 is
        // 14–18 — so a generator that ignored the floor index entirely could sit at 14 rooms
        // forever and pass every band. That is not a hypothetical: it is exactly what the
        // generator did until this commit, and the band check alone would have blessed it.
        //
        // So assert that the distributions actually MOVED: the mean rises from floor 1 to
        // floor 4, and by enough that it cannot be noise. The Corruption gate's "severity
        // never falls" is the same shape of check for the same reason.
        float firstMean = band[1].Mean, deepMean = band[4].Mean;
        bool curveRises = true;
        for (int f = 1; f < 4; f++)
            if (band[f].Count > 0 && band[f + 1].Count > 0 && band[f + 1].Mean < band[f].Mean)
                curveRises = false;
        bool curveMoved = curveRises && deepMean - firstMean >= 2f;

        GD.Print($" [{(allGenerated ? "PASS" : "FAIL")}] every seed produced a floor");
        GD.Print($" [{(variety ? "PASS" : "FAIL")}] room count varies ({minRooms}..{maxRooms}, need spread >= 3)");
        GD.Print($" [{(flowsUsed ? "PASS" : "FAIL")}] every authored flow is reachable ({authoredUsed}/{flows.Count})");
        GD.Print($" [{(fallbackRare ? "PASS" : "FAIL")}] fallback rate {fallbackRate * 100:F2}% (need <= 1%)");
        GD.Print($" [{(bandsHeld ? "PASS" : "FAIL")}] every floor inside its docs/07 §2 band " +
                 $"({bandFailures} floor(s) out)");
        GD.Print($" [{(curveMoved ? "PASS" : "FAIL")}] the count follows the floor — mean " +
                 $"{firstMean:F1} on floor 1 to {deepMean:F1} on floor 4 (need +2.0, monotone)");

        bool pass = allGenerated && variety && flowsUsed && fallbackRare && bandsHeld && curveMoved;
        GD.Print("================================================================");
        GD.Print(pass ? " GENERATION SWEEP: PASS" : " GENERATION SWEEP: FAIL");
        GD.Print("================================================================");
        GetTree().Quit(pass ? 0 : 1);
    }

    /// <summary>
    /// Collapse a failure into a category by stripping the specific ids, so the histogram
    /// groups causes rather than listing 10,000 unique strings. The first version cut the
    /// message at '(' and produced entries like "node 10", which named the symptom's
    /// location and hid the symptom.
    /// </summary>
    private static string Shorten(string failure)
    {
        var sb = new StringBuilder(failure.Length);
        bool lastWasDigit = false;
        foreach (char c in failure)
        {
            if (char.IsDigit(c)) { if (!lastWasDigit) sb.Append('#'); lastWasDigit = true; continue; }
            lastWasDigit = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- Visualiser

    /// <summary>
    /// ASCII dump of one floor (docs/06 §10, "Generation Visualiser"). Deliberately in the
    /// terminal rather than a PNG: it works headless, in CI logs, and over SSH, and being
    /// able to eyeball a failing seed in one command is the entire value.
    /// </summary>
    private static void ShowSeed(List<FloorFlow> flows, List<RoomTemplate> rooms, ulong seed)
    {
        var gen = new FloorGenerator(flows, rooms);
        GeneratedFloor? floor = gen.Generate(seed, 1, out string failure);

        if (floor is null)
        {
            GD.PrintErr($" seed {Hash.FormatSeed(seed)} FAILED: {failure}");
            return;
        }

        Rect2I b = floor.Bounds();
        GD.Print($" seed {Hash.FormatSeed(seed)}   flow {floor.FlowId}   " +
                 $"{floor.Rooms.Count} rooms   {b.Size.X}x{b.Size.Y} tiles   attempt {floor.Attempts}");
        GD.Print("");

        // Downscale so a 200-tile floor fits a terminal.
        const int scale = 4;
        int w = Mathf.CeilToInt(b.Size.X / (float)scale) + 1;
        int h = Mathf.CeilToInt(b.Size.Y / (float)scale) + 1;
        if (w > 200 || h > 100) { GD.Print(" (floor too large to draw)"); return; }

        var grid = new char[h, w];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) grid[y, x] = ' ';

        foreach (PlacedRoom r in floor.Rooms)
        {
            char glyph = Glyph(r.Role);
            int x0 = (r.Position.X - b.Position.X) / scale;
            int y0 = (r.Position.Y - b.Position.Y) / scale;
            int x1 = (r.Position.X + r.Width - b.Position.X) / scale;
            int y1 = (r.Position.Y + r.Height - b.Position.Y) / scale;

            for (int y = y0; y < y1 && y < h; y++)
                for (int x = x0; x < x1 && x < w; x++)
                    if (y >= 0 && x >= 0) grid[y, x] = glyph;
        }

        var sb = new StringBuilder();
        for (int y = 0; y < h; y++)
        {
            sb.Clear();
            for (int x = 0; x < w; x++) sb.Append(grid[y, x]);
            GD.Print(" " + sb.ToString().TrimEnd());
        }

        GD.Print("");
        GD.Print(" E entrance  . easy  o med  O hard  H hub  - connector");
        GD.Print(" R reward  $ shop  ! shrine  ? secret  F foyer  B boss");
        GD.Print($" corridors: {floor.Corridors.Count}");
    }

    private static char Glyph(RoomRole role) => role switch
    {
        RoomRole.Entrance => 'E',
        RoomRole.CombatEasy => '.',
        RoomRole.CombatMed => 'o',
        RoomRole.CombatHard => 'O',
        RoomRole.Hub => 'H',
        RoomRole.Connector => '-',
        RoomRole.Reward => 'R',
        RoomRole.Shop => '$',
        RoomRole.Shrine => '!',
        RoomRole.Secret => '?',
        RoomRole.BossFoyer => 'F',
        RoomRole.Boss => 'B',
        _ => '#',
    };
}
