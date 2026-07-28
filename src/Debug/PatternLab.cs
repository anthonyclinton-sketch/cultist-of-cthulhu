using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// Isolated bullet-pattern preview (docs/06 §10, docs/11 M1).
///
/// The roadmap says of this tool: "you will use it every single day for two years." It is
/// built in M1 rather than when it is needed because the alternative — tuning a pattern by
/// launching the whole game, walking to a room, and hoping that enemy rolls that attack —
/// is a 30-second loop instead of a 1-second one, and that difference decides whether
/// patterns get iterated on at all.
///
/// It also enforces the readability contract (docs/05 §1) at author time: every loaded
/// pattern is validated, and the telegraph window is drawn to scale so an under-telegraphed
/// attack is visible as a design error rather than felt as an unfair death.
///
/// Controls: LEFT/RIGHT switch pattern · SPACE fire once · A auto-fire
///           G ghost player orbit · H toggle hallucination preview · R reset
/// </summary>
public sealed partial class PatternLab : Node2D
{
    private readonly List<PatternData> _patterns = new();
    private readonly List<string> _names = new();
    private readonly List<string?> _errors = new();

    private BulletManager _bullets = null!;
    private PatternPlayer _player = null!;
    private Rng _rng = null!;

    private int _index;
    private bool _autoFire = true;
    private bool _ghostOrbit = true;
    private bool _showHallucinations;
    private float _autoTimer;
    private float _ghostAngle;
    private Vector2 _ghostPos;
    private Vector2 _ghostVel;

    private int _peakBullets;
    private float _peakResetTimer;

    private static readonly Vector2 Emitter = new(0, -60);

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "pattern_lab");

        AddChild(new ColorRect
        {
            Color = new Color("14161C"),
            Position = new Vector2(-320, -180),
            Size = new Vector2(640, 360),
            ZIndex = -100,
        });

        _bullets = new BulletManager
        {
            Name = nameof(BulletManager),
            Bounds = new Rect2(-400, -240, 800, 480),
            TargetRadius = Tune.PlayerHitboxRadius,
            TargetInvulnerable = true,   // the ghost observes; it does not consume bullets
        };
        AddChild(_bullets);

        AddChild(new Camera2D { Enabled = true, ProcessCallback = Camera2D.Camera2DProcessCallback.Physics });

        _player = new PatternPlayer();
        LoadAllPatterns();
        Select(0);

        GD.Print($"[PatternLab] {_patterns.Count} patterns. LEFT/RIGHT switch · SPACE fire · A auto · H hallucinations.");
    }

    private void LoadAllPatterns()
    {
        using var dir = DirAccess.Open("res://data/patterns");
        if (dir is null) { GD.PrintErr("[PatternLab] no res://data/patterns"); return; }

        foreach (string file in dir.GetFiles())
        {
            // Godot renames .tres to .tres.remap in exported builds.
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres")) continue;

            var p = GD.Load<PatternData>($"res://data/patterns/{name}");
            if (p is null) { GD.PrintErr($"[PatternLab] failed to load {name}"); continue; }

            _patterns.Add(p);
            _names.Add(name[..^5]);

            string? err = p.Validate();
            _errors.Add(err);
            if (err is not null) GD.PrintErr($"[PatternLab] {name}: {err}");
        }
    }

    private void Select(int index)
    {
        if (_patterns.Count == 0) return;
        _index = Mathf.PosMod(index, _patterns.Count);
        _bullets.Clear();
        _peakBullets = 0;
        _player.Configure(_patterns[_index], _bullets, _rng);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        HandleInput();

        // The ghost orbits at the player's real move speed, so density is judged against
        // a target that moves the way a player actually moves — a stationary reference
        // makes every pattern look survivable.
        if (_ghostOrbit)
        {
            _ghostAngle += dt * 0.8f;
            Vector2 next = new(Mathf.Cos(_ghostAngle) * 150f, Mathf.Sin(_ghostAngle * 0.7f) * 90f + 40f);
            _ghostVel = (next - _ghostPos) / dt;
            _ghostPos = next;
        }
        else
        {
            _ghostVel = Vector2.Zero;
        }

        _bullets.TargetPosition = _ghostPos;

        if (_patterns.Count > 0)
        {
            Vector2 facing = (_ghostPos - Emitter).Normalized();
            _player.Tick(dt, Emitter, facing, _ghostPos, _ghostVel);

            if (_autoFire && !_player.IsActive)
            {
                _autoTimer -= dt;
                if (_autoTimer <= 0f) { _player.Fire(); _autoTimer = 1.2f; }
            }
        }

        if (_bullets.Count > _peakBullets) _peakBullets = _bullets.Count;
        _peakResetTimer -= dt;
        if (_peakResetTimer <= 0f) { _peakResetTimer = 5f; _peakBullets = _bullets.Count; }

        QueueRedraw();
    }

    private void HandleInput()
    {
        if (Input.IsActionJustPressed("ui_right")) Select(_index + 1);
        if (Input.IsActionJustPressed("ui_left")) Select(_index - 1);
        if (Input.IsActionJustPressed("ui_accept")) _player.Fire();
        if (Input.IsKeyPressed(Key.R)) { _bullets.Clear(); _peakBullets = 0; }

        if (Input.IsKeyPressed(Key.A)) _autoFire = true;
        if (Input.IsKeyPressed(Key.S)) _autoFire = false;
        if (Input.IsKeyPressed(Key.G)) _ghostOrbit = !_ghostOrbit;
        if (Input.IsKeyPressed(Key.H)) _showHallucinations = !_showHallucinations;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;

        // Emitter
        DrawCircle(Emitter, 9f, new Color("9D4EDD"));
        if (_player.IsActive && _player.TelegraphProgress < 1f)
        {
            float t = _player.TelegraphProgress;
            DrawArc(Emitter, 12f + (1f - t) * 16f, 0, Mathf.Tau, 28,
                    new Color(1f, 0.35f, 0.35f, 0.3f + t * 0.6f), 2f);
        }

        // Ghost player, drawn at the true 6px hitbox — the whole point is judging density
        // against the real collision size, not against a sprite.
        DrawCircle(_ghostPos, Tune.PlayerHitboxRadius, new Color("FFB347"));
        DrawArc(_ghostPos, Tune.PlayerHitboxRadius + 2f, 0, Mathf.Tau, 16, new Color(1, 1, 1, 0.3f), 1f);

        if (_patterns.Count == 0) return;

        PatternData p = _patterns[_index];
        string? err = _errors[_index];

        var lines = new List<string>
        {
            $"[{_index + 1}/{_patterns.Count}]  {_names[_index]}",
            $"{p.Primitive}   count {p.Count} x burst {p.BurstCount} = {p.TotalBullets} bullets",
            $"telegraph {p.TelegraphSeconds:F2}s   speed {p.Speed:F0}   behaviour {p.Behaviour}",
            $"on screen {_bullets.Count}   peak(5s) {_peakBullets}   design cap {Tune.EnemyBulletDesignCap}",
            $"auto {(_autoFire ? "ON" : "off")}   ghost {(_ghostOrbit ? "orbit" : "still")}",
        };

        var pos = new Vector2(-310, -166);
        foreach (string line in lines)
        {
            DrawString(font, pos, line, HorizontalAlignment.Left, -1, 10, new Color(0.85f, 0.85f, 0.9f));
            pos.Y += 13;
        }

        if (err is not null)
        {
            DrawString(font, pos + new Vector2(0, 4), $"INVALID: {err}",
                       HorizontalAlignment.Left, 600, 10, new Color("FF5555"));
        }

        // R7 budget warning. The design ceiling is 600 on-screen enemy bullets; exceeding
        // it means the encounter should be re-authored, not clamped at runtime.
        if (_peakBullets > Tune.EnemyBulletDesignCap)
        {
            DrawString(font, new Vector2(-310, 150),
                       $"R7: peak {_peakBullets} exceeds the {Tune.EnemyBulletDesignCap} design ceiling",
                       HorizontalAlignment.Left, 600, 11, new Color("FFAA33"));
        }
    }
}
