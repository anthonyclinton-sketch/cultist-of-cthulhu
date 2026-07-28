using System;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// F3 overlay (docs/09 §10). Exists to make the two M0 gate criteria continuously visible
/// rather than something you go and measure occasionally.
///
/// The allocation counter is the important part. docs/09 §8 requires ZERO allocations in
/// the physics tick, and the only way that survives two years of development is if a
/// regression is visible the moment it is introduced.
/// </summary>
public sealed partial class DebugOverlay : CanvasLayer
{
    [Export] public NodePath BulletManagerPath { get; set; } = default!;
    [Export] public NodePath PlayerPath { get; set; } = default!;

    private BulletManager? _bullets;
    private PlayerController? _player;
    private Label _label = null!;
    private bool _visible = true;

    // Frame-time ring buffer. Preallocated — an overlay that allocates would corrupt the
    // very measurement it exists to report.
    private const int SampleCount = 120;
    private readonly double[] _frameTimes = new double[SampleCount];
    private int _sampleIndex;

    private long _lastGcBytes;
    private long _tickAllocBytes;
    private int _gen0, _gen1, _gen2;
    private int _peakBullets;

    public override void _Ready()
    {
        Layer = 100;

        var panel = new PanelContainer
        {
            OffsetLeft = 8, OffsetTop = 8,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.72f),
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        _label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 11);
        panel.AddChild(_label);
        AddChild(panel);

        _bullets = ResolveOrFind<BulletManager>(BulletManagerPath);
        _player = ResolveOrFind<PlayerController>(PlayerPath);
        _lastGcBytes = GC.GetAllocatedBytesForCurrentThread();
    }

    private T? ResolveOrFind<T>(NodePath path) where T : Node
    {
        if (path is not null && !path.IsEmpty)
        {
            var n = GetNodeOrNull<T>(path);
            if (n is not null) return n;
        }
        return GetTree().Root.FindChild(typeof(T).Name, recursive: true, owned: false) as T;
    }

    /// <summary>
    /// Measured in _PhysicsProcess with the highest process priority so it runs LAST in
    /// the tick, capturing everything the sim allocated before it.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        long now = GC.GetAllocatedBytesForCurrentThread();
        _tickAllocBytes = now - _lastGcBytes;
        _lastGcBytes = now;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("debug_overlay"))
        {
            _visible = !_visible;
            _label.GetParent<Control>().Visible = _visible;
        }
        if (!_visible) return;

        _frameTimes[_sampleIndex] = delta;
        _sampleIndex = (_sampleIndex + 1) % SampleCount;

        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);

        double avg = 0, worst = 0;
        for (int i = 0; i < SampleCount; i++)
        {
            avg += _frameTimes[i];
            if (_frameTimes[i] > worst) worst = _frameTimes[i];
        }
        avg /= SampleCount;

        int count = _bullets?.Count ?? 0;
        if (count > _peakBullets) _peakBullets = count;

        // 6.9ms is the 144Hz budget from docs/09 §8.
        string verdict = avg * 1000.0 <= 6.9 ? "PASS (144Hz)" : avg * 1000.0 <= 16.6 ? "60Hz only" : "FAIL";
        string allocVerdict = _tickAllocBytes == 0 ? "0 B  ✓" : $"{_tickAllocBytes} B  ✗ REGRESSION";

        _label.Text =
            $"CULTIST OF CTHULHU — M0\n" +
            $"seed         {Hash.FormatSeed(GameRoot.Instance.RunSeed)}\n" +
            $"fps          {Engine.GetFramesPerSecond():F0}\n" +
            $"frame avg    {avg * 1000.0:F2} ms   worst {worst * 1000.0:F2} ms   [{verdict}]\n" +
            $"bullets      {count} / {_bullets?.Capacity ?? 0}   peak {_peakBullets}   overflow {_bullets?.OverflowCount ?? 0}\n" +
            $"tick alloc   {allocVerdict}\n" +
            $"GC           gen0 {_gen0}  gen1 {_gen1}  gen2 {_gen2}\n" +
            PlayerLine();
    }

    private string PlayerLine()
    {
        if (_player is null) return "player       (none)";
        var s = _player.Sanity;
        float dashSpeed = Tune.PlayerMoveSpeed * Tune.BlinkSpeedMultiplier * s.MoveSpeedMultiplier;
        return
            $"sanity       {s.Current:F0}/{s.Max:F0}  ceiling {s.LucidCeiling:F0}  band {s.Band}\n" +
            $"speed        walk {Tune.PlayerMoveSpeed * s.MoveSpeedMultiplier:F0} px/s   " +
            $"dash {dashSpeed:F0} px/s ({Tune.BlinkSpeedMultiplier:F1}x)   " +
            $"reach {Tune.BlinkEffectiveDistance:F0} px\n" +
            $"blink        {_player.Phase}   invuln {_player.IsInvulnerable}\n" +
            $"banish       cost {Tune.SanityBanishCost:F0}   " +
            $"{(s.CanAfford(Tune.SanityBanishCost) ? "READY" : "UNAFFORDABLE")}" +
            $"{(_player.BanishCooldownRemaining > 0f ? $"  cd {_player.BanishCooldownRemaining:F1}s" : "")}" +
            $"   last: {_player.BulletsCleared} bullets, {_player.EnemiesStunned} stunned\n" +
            $"hits taken   {_player.HitsTaken}   denied sustain {_player.DeniedSustainCount}   " +
            $"corruption {_player.Corruption:F2}";
    }
}
