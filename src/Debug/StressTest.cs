using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Player;
using Godot;

namespace CultistOfCthulhu.Debug;

/// <summary>
/// M0 HARD GATE (docs/11 §2): 4096 bullets at a locked framerate with zero allocations in
/// the physics tick. If this fails, everything else stops until it passes.
///
/// Also the first playable thing in the project: a grey-box arena where a placeholder
/// player dodges a genuine bullet field. That makes it the earliest possible read on
/// whether Blink Step feels right — which is Bet 1, and the reason M1 exists.
///
/// The scene is built in code rather than authored as a .tscn. Debug scaffolding should
/// not be hand-maintained scene data; this way the whole harness is reviewable in one file.
///
/// Controls:  WASD move · SPACE Blink Step · RMB Banish (hold: Open the Eye)
///            F3 overlay · [ / ] emitter count · R reset · TAB toggle hallucinations
/// </summary>
public sealed partial class StressTest : Node2D
{
    private const int ArenaHalfWidth = 560;
    private const int ArenaHalfHeight = 320;

    private BulletManager _bullets = null!;
    private PlayerController _player = null!;
    private Rng _rng = null!;

    private int _emitterCount = 8;
    private const int MaxEmitters = 48;
    private readonly float[] _emitterAngle = new float[MaxEmitters];
    private readonly float[] _emitterSpin = new float[MaxEmitters];
    private readonly Vector2[] _emitterPos = new Vector2[MaxEmitters];

    private float _fireTimer;
    private bool _hallucinationsForced;

    // Enemy bullets are ALWAYS cool-half palette (docs/05 R1). This is inviolable.
    private static readonly Color BileGreen = new("7FBF3F");
    private static readonly Color Violet = new("9D4EDD");
    private static readonly Color Bone = new("E8E1D5");

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "stress_test");

        BuildArena();
        BuildBullets();
        BuildPlayer();
        BuildCamera();
        BuildOverlay();
        ResetEmitters();

        GD.Print("[StressTest] M0 gate scene. F3 = overlay. [ and ] change emitter count.");
    }

    // ---------------------------------------------------------------- Scene construction

    private void BuildArena()
    {
        var bg = new ColorRect
        {
            Color = new Color("14161C"),
            Position = new Vector2(-ArenaHalfWidth, -ArenaHalfHeight),
            Size = new Vector2(ArenaHalfWidth * 2, ArenaHalfHeight * 2),
            ZIndex = -100,
        };
        AddChild(bg);

        // Grey-box walls so the CharacterBody2D has something to collide with.
        AddWall(new Vector2(0, -ArenaHalfHeight - 8), new Vector2(ArenaHalfWidth, 8));
        AddWall(new Vector2(0, ArenaHalfHeight + 8), new Vector2(ArenaHalfWidth, 8));
        AddWall(new Vector2(-ArenaHalfWidth - 8, 0), new Vector2(8, ArenaHalfHeight));
        AddWall(new Vector2(ArenaHalfWidth + 8, 0), new Vector2(8, ArenaHalfHeight));
    }

    private void AddWall(Vector2 centre, Vector2 halfExtents)
    {
        var body = new StaticBody2D { Position = centre };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = halfExtents * 2f } });
        AddChild(body);
    }

    private void BuildBullets()
    {
        _bullets = new BulletManager
        {
            Name = nameof(BulletManager),
            Bounds = new Rect2(-ArenaHalfWidth, -ArenaHalfHeight, ArenaHalfWidth * 2, ArenaHalfHeight * 2),
        };
        AddChild(_bullets);
    }

    private void BuildPlayer()
    {
        _player = new PlayerController { Name = nameof(PlayerController), Position = Vector2.Zero };
        _player.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 7f } });

        // The 6px hitbox is always faintly visible and fully lit during i-frames
        // (docs/02 §1.1). Placeholder art, real rule.
        var hitbox = new ColorRect
        {
            Name = "Hitbox",
            Color = new Color("FFB347"),
            Position = new Vector2(-Tune.PlayerHitboxRadius, -Tune.PlayerHitboxRadius),
            Size = new Vector2(Tune.PlayerHitboxRadius * 2, Tune.PlayerHitboxRadius * 2),
            ZIndex = 10,
        };
        _player.AddChild(hitbox);
        AddChild(_player);
    }

    private void BuildCamera()
    {
        // ProcessCallback set explicitly: with physics interpolation on, Godot forces
        // cameras to physics mode anyway and warns about it. Stating it is correct and
        // keeps the log free of a warning that would otherwise be trained out of.
        AddChild(new Camera2D
        {
            Position = Vector2.Zero,
            Enabled = true,
            ProcessCallback = Camera2D.Camera2DProcessCallback.Physics,
        });
    }

    private void BuildOverlay()
    {
        AddChild(new DebugOverlay
        {
            Name = nameof(DebugOverlay),
            BulletManagerPath = _bullets.GetPath(),
            PlayerPath = _player.GetPath(),
        });
    }

    private void ResetEmitters()
    {
        for (int i = 0; i < MaxEmitters; i++)
        {
            float t = i / (float)MaxEmitters * Mathf.Tau;
            float r = 220f + _rng.Range(0f, 90f);
            _emitterPos[i] = new Vector2(Mathf.Cos(t) * r * 1.5f, Mathf.Sin(t) * r);
            _emitterAngle[i] = _rng.NextAngle();
            _emitterSpin[i] = _rng.Range(0.6f, 2.2f) * (_rng.Chance(0.5f) ? 1f : -1f);
        }
    }

    // ---------------------------------------------------------------- Tick

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        HandleDebugInput();

        // Emitters run spirals — the densest realistic pattern shape, so the gate is
        // measured against something the game will actually do (docs/05 §4.1 SPIRAL).
        _fireTimer -= dt;
        bool fire = _fireTimer <= 0f;
        if (fire) _fireTimer += 0.05f;

        float hallucinationRatio = _hallucinationsForced ? 0.25f : _player.Sanity.HallucinationRatio;

        for (int e = 0; e < _emitterCount; e++)
        {
            _emitterAngle[e] += _emitterSpin[e] * dt;
            if (!fire) continue;

            const int Arms = 3;
            for (int a = 0; a < Arms; a++)
            {
                float ang = _emitterAngle[e] + a * (Mathf.Tau / Arms);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                var flags = BulletFlags.None;
                Color col = a switch { 0 => BileGreen, 1 => Violet, _ => Bone };

                if (hallucinationRatio > 0f && _rng.NextFloat() < hallucinationRatio)
                {
                    // Visually identical — the ONLY tell is the missing drop-shadow
                    // (docs/02 §3.4, docs/05 R9).
                    flags |= BulletFlags.Hallucination;
                }

                _bullets.Spawn(
                    position: _emitterPos[e],
                    velocity: dir * 95f,
                    radius: 4f,
                    lifetime: 9f,
                    color: col,
                    renderSize: 9f,
                    flags: flags);
            }
        }

        UpdateHitboxTint();
    }

    private void UpdateHitboxTint()
    {
        var hitbox = _player.GetNodeOrNull<ColorRect>("Hitbox");
        if (hitbox is null) return;

        // Fully opaque during i-frames: the player must always be able to see exactly
        // what is invulnerable (docs/02 §4).
        hitbox.Color = _player.IsInvulnerable
            ? new Color("FFFFFF")
            : new Color("FFB347") with { A = 0.75f };
    }

    private void HandleDebugInput()
    {
        if (Input.IsKeyPressed(Key.Bracketright) && _emitterCount < MaxEmitters) _emitterCount++;
        if (Input.IsKeyPressed(Key.Bracketleft) && _emitterCount > 1) _emitterCount--;

        if (Input.IsKeyPressed(Key.R))
        {
            _bullets.Clear();
            _player.ResetForTest(Vector2.Zero);
        }
        if (Input.IsActionJustPressed("reverie")) _hallucinationsForced = !_hallucinationsForced;

        // Held: infinite Sanity, so the gate can be measured without the run ending.
        if (Input.IsKeyPressed(Key.G)) _player.Sanity.DebugSetCurrent(Tune.SanityMax);
    }
}
