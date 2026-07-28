using System;
using System.Collections.Generic;
using System.Text;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Meta;

/// <summary>
/// M1 instrumentation (docs/11 M1 test design).
///
/// This exists NOW, before there is anything to measure, on purpose. The M1 gate is a
/// question about player behaviour — "does the Sanity economy bind, and does the ladder
/// ever fire?" — and you cannot retrofit that measurement after the playtest has already
/// happened. Every metric below maps to a numbered pass/fail criterion in the roadmap.
///
/// Writes CSV to user:// so a tester's session can be collected without a build.
/// </summary>
public sealed class Telemetry
{
    public sealed class RoomRecord
    {
        public int RoomIndex;
        public float Duration;
        public float SanityStart, SanityEnd, SanityMin;
        public float SanityIncome, SanitySpend;
        public float LucidCeiling;
        public int Kills;
        public int HitsTaken;
        public int DeniedSustain;
        public int ReloadsAttempted, ReloadsDenied;
        public int PerfectRecitations, FailedRecitations;
        public float TimeLucid, TimeUnsettled, TimeFraying, TimeUnravelled;
        public bool ReachedFrayingUnaided;
        public int Ascensions;
    }

    private readonly List<RoomRecord> _rooms = new();
    private RoomRecord _current = new();
    private float _roomTimer;
    private bool _openEyeUsedThisRoom;

    // --- Session totals -------------------------------------------------------------
    public float SessionDuration { get; private set; }
    public int TotalRooms => _rooms.Count;

    public void BeginRoom(int index, SanitySystem sanity)
    {
        _current = new RoomRecord
        {
            RoomIndex = index,
            SanityStart = sanity.Current,
            SanityMin = sanity.Current,
            LucidCeiling = sanity.LucidCeiling,
        };
        _roomTimer = 0f;
        _openEyeUsedThisRoom = false;
    }

    public void NoteOpenEye() => _openEyeUsedThisRoom = true;
    public void NoteKill() => _current.Kills++;
    public void NoteHitTaken() => _current.HitsTaken++;
    public void NoteDeniedSustain() => _current.DeniedSustain++;
    public void NoteAscension() => _current.Ascensions++;
    public void NoteSanityIncome(float v) => _current.SanityIncome += v;
    public void NoteSanitySpend(float v) => _current.SanitySpend += v;

    public void Tick(float dt, SanitySystem sanity)
    {
        _roomTimer += dt;
        SessionDuration += dt;

        if (sanity.Current < _current.SanityMin) _current.SanityMin = sanity.Current;

        switch (sanity.Band)
        {
            case SanityBand.Lucid: _current.TimeLucid += dt; break;
            case SanityBand.Unsettled: _current.TimeUnsettled += dt; break;
            case SanityBand.Fraying: _current.TimeFraying += dt; break;
            case SanityBand.Unravelled: _current.TimeUnravelled += dt; break;
        }

        // Metric 9, the one that matters most post-F4: does the ladder fire WITHOUT the
        // player deliberately spending to descend? If this stays false across a floor,
        // the Lucid Ceiling is not carrying the descent and its decay must steepen.
        if (!_openEyeUsedThisRoom && sanity.Band >= SanityBand.Fraying)
            _current.ReachedFrayingUnaided = true;
    }

    public void EndRoom(SanitySystem sanity,
                        int reloadsAttempted, int reloadsDenied,
                        int perfect, int failed)
    {
        _current.Duration = _roomTimer;
        _current.SanityEnd = sanity.Current;
        _current.ReloadsAttempted = reloadsAttempted;
        _current.ReloadsDenied = reloadsDenied;
        _current.PerfectRecitations = perfect;
        _current.FailedRecitations = failed;
        _rooms.Add(_current);
    }

    // ---------------------------------------------------------------- Derived metrics

    /// <summary>Metric 1: % of combat time below 40 Sanity. Pass 25-45%.</summary>
    public float TimeBelowFrayingFraction()
    {
        float total = 0f, low = 0f;
        foreach (RoomRecord r in _rooms)
        {
            total += r.TimeLucid + r.TimeUnsettled + r.TimeFraying + r.TimeUnravelled;
            low += r.TimeFraying + r.TimeUnravelled;
        }
        return total <= 0f ? 0f : low / total;
    }

    /// <summary>Metric 5: median Sanity net per room. Pass -15..+15.</summary>
    public float MedianNetPerRoom()
    {
        if (_rooms.Count == 0) return 0f;
        var nets = new List<float>(_rooms.Count);
        foreach (RoomRecord r in _rooms) nets.Add(r.SanityIncome - r.SanitySpend);
        nets.Sort();
        return nets[nets.Count / 2];
    }

    /// <summary>Metric 9: fraction of rooms where the ladder fired unaided. Pass >= 0.70.</summary>
    public float LadderFireRate()
    {
        if (_rooms.Count == 0) return 0f;
        int fired = 0;
        foreach (RoomRecord r in _rooms) if (r.ReachedFrayingUnaided) fired++;
        return fired / (float)_rooms.Count;
    }

    public int TotalDeniedSustain()
    {
        int n = 0;
        foreach (RoomRecord r in _rooms) n += r.DeniedSustain;
        return n;
    }

    // ---------------------------------------------------------------- Output

    public string Summary()
    {
        float below = TimeBelowFrayingFraction();
        float ladder = LadderFireRate();
        float net = MedianNetPerRoom();

        string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        var sb = new StringBuilder();
        sb.AppendLine("--- M1 METRICS (docs/11 M1 test design) ---");
        sb.AppendLine($" rooms                {_rooms.Count}   session {SessionDuration:F0}s");
        sb.AppendLine($" 1. time below 40     {below * 100:F1}%   target 25-45%      [{Verdict(below is >= 0.25f and <= 0.45f)}]");
        sb.AppendLine($" 5. median net/room   {net:+0.0;-0.0}      target -15..+15    [{Verdict(net is >= -15f and <= 15f)}]");
        sb.AppendLine($" 9. ladder fires      {ladder * 100:F0}%     target >= 70%      [{Verdict(ladder >= 0.70f)}]");
        sb.AppendLine($" 3. denied sustain    {TotalDeniedSustain()}        target 1-4/run");
        return sb.ToString();
    }

    public void WriteCsv(string path = "user://m1_telemetry.csv")
    {
        var sb = new StringBuilder();
        sb.AppendLine("room,duration,sanity_start,sanity_end,sanity_min,income,spend,ceiling," +
                      "kills,hits,denied_sustain,reloads,reloads_denied,perfect,failed," +
                      "t_lucid,t_unsettled,t_fraying,t_unravelled,ladder_fired,ascensions");

        foreach (RoomRecord r in _rooms)
        {
            sb.AppendLine($"{r.RoomIndex},{r.Duration:F2},{r.SanityStart:F1},{r.SanityEnd:F1}," +
                          $"{r.SanityMin:F1},{r.SanityIncome:F1},{r.SanitySpend:F1},{r.LucidCeiling:F1}," +
                          $"{r.Kills},{r.HitsTaken},{r.DeniedSustain},{r.ReloadsAttempted}," +
                          $"{r.ReloadsDenied},{r.PerfectRecitations},{r.FailedRecitations}," +
                          $"{r.TimeLucid:F2},{r.TimeUnsettled:F2},{r.TimeFraying:F2},{r.TimeUnravelled:F2}," +
                          $"{(r.ReachedFrayingUnaided ? 1 : 0)},{r.Ascensions}");
        }

        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f is null)
        {
            GD.PrintErr($"[Telemetry] could not write {path}: {FileAccess.GetOpenError()}");
            return;
        }
        f.StoreString(sb.ToString());
        GD.Print($"[Telemetry] wrote {_rooms.Count} room records to {ProjectSettings.GlobalizePath(path)}");
    }
}
