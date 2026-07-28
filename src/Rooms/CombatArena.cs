using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Meta;
using CultistOfCthulhu.Player;
using CultistOfCthulhu.UI;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Rooms;

/// <summary>
/// The M1 vertical slice (docs/11 M1): a chain of encounter rooms with real weapons, real
/// enemies, and the full Sanity economy running, instrumented against the M1 metrics.
///
/// This is the build a playtester sits in front of. Its job is to answer ONE question
/// (docs/11, revised post-F4): *does the Sanity economy still bind when dodging is free,
/// and does the ladder ever fire?*
///
/// Rooms here are procedurally placed enemies in a fixed arena rather than the authored
/// TileMap rooms of docs/06 — the generator is M2. What is real is the encounter budget,
/// the fodder floor, and the attack-token cap, because those are what the Sanity economy
/// is actually sensitive to.
///
/// Controls: WASD move · LMB fire · SPACE Blink Step · R Recite · RMB Banish
///           (hold RMB) Open the Eye · Q swap weapon · F3 overlay · F5 dump telemetry
/// </summary>
public sealed partial class CombatArena : Node2D
{
    private const int ArenaHalfWidth = 460;
    private const int ArenaHalfHeight = 260;

    private BulletManager _enemyBullets = null!;
    private BulletManager _playerBullets = null!;
    private EnemyManager _enemies = null!;
    private PlayerController _player = null!;
    private Hud _hud = null!;
    private Rng _rng = null!;
    private readonly Telemetry _telemetry = new();

    private readonly List<EnemyData> _roster = new();
    private int _roomIndex;
    private bool _roomActive;
    private float _interRoomTimer;
    private float _hitStopTimer;

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "arena");

        LoadContent();
        BuildArena();
        BuildManagers();
        BuildPlayer();
        BuildCameraAndUi();

        StartRoom();

        GD.Print("[CombatArena] M1 slice. F3 overlay · F5 telemetry dump · R recite · hold RMB to Open the Eye.");
    }

    // ---------------------------------------------------------------- Content

    private void LoadContent()
    {
        // Loaded from .tres, not constructed in code — docs/09 §5. A missing or invalid
        // resource is a hard failure here rather than a silent fallback, because a
        // playtest run on half-loaded content produces data that looks real and is not.
        foreach (string path in new[]
                 {
                     "res://data/enemies/acolyte.tres",
                     "res://data/enemies/cellar_ghoul.tres",
                     "res://data/enemies/tallow_man.tres",
                     "res://data/enemies/netcaster.tres",
                     "res://data/enemies/chanter.tres",
                 })
        {
            var data = GD.Load<EnemyData>(path);
            if (data is null) { GD.PrintErr($"[CombatArena] failed to load {path}"); continue; }

            string? err = data.Validate();
            if (err is not null) GD.PrintErr($"[CombatArena] {data.DisplayName}: {err}");

            _roster.Add(data);
        }
    }

    private void BuildArena()
    {
        AddChild(new ColorRect
        {
            Color = new Color("14161C"),
            Position = new Vector2(-ArenaHalfWidth, -ArenaHalfHeight),
            Size = new Vector2(ArenaHalfWidth * 2, ArenaHalfHeight * 2),
            ZIndex = -100,
        });

        AddWall(new Vector2(0, -ArenaHalfHeight - 8), new Vector2(ArenaHalfWidth + 16, 8));
        AddWall(new Vector2(0, ArenaHalfHeight + 8), new Vector2(ArenaHalfWidth + 16, 8));
        AddWall(new Vector2(-ArenaHalfWidth - 8, 0), new Vector2(8, ArenaHalfHeight));
        AddWall(new Vector2(ArenaHalfWidth + 8, 0), new Vector2(8, ArenaHalfHeight));
    }

    private void AddWall(Vector2 centre, Vector2 halfExtents)
    {
        var body = new StaticBody2D { Position = centre };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = halfExtents * 2f } });
        AddChild(body);
    }

    private void BuildManagers()
    {
        var bounds = new Rect2(-ArenaHalfWidth, -ArenaHalfHeight, ArenaHalfWidth * 2, ArenaHalfHeight * 2);

        _enemyBullets = new BulletManager { Name = nameof(BulletManager), Bounds = bounds };
        AddChild(_enemyBullets);

        _playerBullets = new BulletManager
        {
            Name = "PlayerBulletManager",
            Bounds = bounds,
            CollideWithEnemies = true,
        };
        AddChild(_playerBullets);

        _enemies = new EnemyManager { Name = nameof(EnemyManager) };
        AddChild(_enemies);
        _enemies.Initialise(_enemyBullets, _playerBullets, bounds, Hash.Derive(GameRoot.Instance.RunSeed, "enemies"));
        _enemies.AttackTokens = 4;   // Floor 1 (docs/05 §8)
    }

    private void BuildPlayer()
    {
        _player = new PlayerController
        {
            Name = nameof(PlayerController),
            Position = Vector2.Zero,
            EnemyBullets = _enemyBullets,
            PlayerBullets = _playerBullets,
            Enemies = _enemies,
            Telemetry = _telemetry,
        };
        _player.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 7f } });
        _player.AddChild(new ColorRect
        {
            Name = "Hitbox",
            Color = new Color("FFB347"),
            Position = new Vector2(-Tune.PlayerHitboxRadius, -Tune.PlayerHitboxRadius),
            Size = new Vector2(Tune.PlayerHitboxRadius * 2, Tune.PlayerHitboxRadius * 2),
            ZIndex = 10,
        });
        AddChild(_player);

        // Three families, including the two M1 mandates: a Grimoire and a melee weapon,
        // because both carry their own Sanity economy and are the likeliest to invalidate
        // it (docs/03 §2 Family IV and V).
        LoadWeapon("res://data/weapons/webley_mk_vi.tres");
        LoadWeapon("res://data/weapons/cantrip_withering.tres");
        LoadWeapon("res://data/weapons/sacrificial_kris.tres");
    }

    private void LoadWeapon(string path)
    {
        var data = GD.Load<WeaponData>(path);
        if (data is null) { GD.PrintErr($"[CombatArena] failed to load {path}"); return; }

        string? err = data.Validate();
        if (err is not null) GD.PrintErr($"[CombatArena] {data.DisplayName}: {err}");

        _player.GiveWeapon(data);
    }

    private void BuildCameraAndUi()
    {
        AddChild(new Camera2D
        {
            Enabled = true,
            ProcessCallback = Camera2D.Camera2DProcessCallback.Physics,
        });

        var layer = new CanvasLayer { Name = "UI" };
        _hud = new Hud { Name = nameof(Hud), Player = _player };
        layer.AddChild(_hud);
        AddChild(layer);

        AddChild(new Debug.DebugOverlay
        {
            Name = nameof(Debug.DebugOverlay),
            BulletManagerPath = _enemyBullets.GetPath(),
            PlayerPath = _player.GetPath(),
        });
    }

    // ---------------------------------------------------------------- Room flow

    /// <summary>
    /// docs/06 §6.1 Dread Budget, in its M1 form. Enemies are drawn until the budget is
    /// spent, subject to the >= 35% fodder floor — which exists because a room of pure
    /// turrets is a Sanity death spiral, and post-F4 that means a room you cannot afford
    /// to reload in.
    /// </summary>
    private void StartRoom()
    {
        _roomIndex++;
        _enemies.ClearAll();
        _enemyBullets.Clear();
        _playerBullets.Clear();

        float budget = 40f + _roomIndex * 16f;
        float fodderFloor = budget * 0.35f;
        float fodderSpent = 0f;
        float spent = 0f;
        int guard = 0;

        while (spent < budget && guard++ < 64)
        {
            bool needFodder = fodderSpent < fodderFloor;
            EnemyData pick = PickEnemy(needFodder);
            if (pick.DreadCost > budget - spent && spent > 0f) break;

            _enemies.Spawn(pick, RandomSpawnPoint());
            spent += pick.DreadCost;
            if (pick.Role == EnemyRole.Fodder) fodderSpent += pick.DreadCost;
        }

        _telemetry.BeginRoom(_roomIndex, _player.Sanity);
        _roomActive = true;

        GD.Print($"[Room {_roomIndex}] budget {budget:F0} spent {spent:F0}  " +
                 $"enemies {_enemies.Enemies.Count}  ceiling {_player.Sanity.LucidCeiling:F0}");
    }

    private EnemyData PickEnemy(bool requireFodder)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            EnemyData e = _roster[_rng.NextInt(0, _roster.Count)];
            if (!requireFodder || e.Role == EnemyRole.Fodder) return e;
        }
        return _roster[0];
    }

    private Vector2 RandomSpawnPoint()
    {
        // Never spawn on top of the player — docs/05 R4/R5 in spirit: the player must
        // always get a chance to see a threat before it can touch them.
        for (int i = 0; i < 32; i++)
        {
            var p = new Vector2(
                _rng.Range(-ArenaHalfWidth + 40, ArenaHalfWidth - 40),
                _rng.Range(-ArenaHalfHeight + 40, ArenaHalfHeight - 40));
            if (p.DistanceTo(_player.GlobalPosition) > 170f) return p;
        }
        return new Vector2(ArenaHalfWidth - 60, ArenaHalfHeight - 60);
    }

    private void EndRoom()
    {
        _roomActive = false;
        _interRoomTimer = 2.0f;

        Weapon w = _player.Weapons.Active;
        _telemetry.EndRoom(_player.Sanity, _player.Weapons.ReloadsAttempted,
                           _player.Weapons.ReloadsDenied, w.PerfectRecitations, w.FailedRecitations);

        // Room clear: +20 Sanity, and the Lucid Ceiling drops. Post-F4 this decay is the
        // primary driver of the descent (docs/02 §3.3.1).
        _player.Sanity.OnRoomCleared();

        // Ammo economy relief so the slice can run long enough to gather data.
        foreach (Weapon weapon in _player.Weapons.Weapons) weapon.AddReserve(weapon.Data.MagazineSize * 2);

        GD.Print($"[Room {_roomIndex}] cleared. sanity {_player.Sanity.Current:F0} " +
                 $"ceiling {_player.Sanity.LucidCeiling:F0} band {_player.Sanity.Band}");
    }

    // ---------------------------------------------------------------- Tick

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        HandleDebugKeys();

        _player.Sanity.InCombat = _roomActive && _enemies.AliveCount > 0;
        _telemetry.Tick(dt, _player.Sanity);

        // docs/02 §8 — hit stop. Owned here because the arena owns the time scale.
        if (_player.PendingHitStop > 0f) _hitStopTimer = _player.PendingHitStop;
        if (_hitStopTimer > 0f)
        {
            _hitStopTimer -= dt;
            Engine.TimeScale = 0.12f;
        }
        else
        {
            Engine.TimeScale = 1f;
        }

        if (_roomActive && _enemies.AliveCount == 0) EndRoom();
        else if (!_roomActive)
        {
            _interRoomTimer -= dt;
            if (_interRoomTimer <= 0f) StartRoom();
        }

        if (_player.IsDead) OnDeath();

        if (GameRoot.Instance.HeadlessTestMode || DisplayServer.GetName() == "headless") HeadlessTrace(dt);

        UpdateHitboxTint();
        QueueRedraw();
    }

    private float _traceTimer;

    /// <summary>
    /// Periodic state dump when running without a window. Exists because a headless smoke
    /// run that produces no output is indistinguishable from one that silently did nothing
    /// — which is exactly how the instant-room-clear bug hid.
    /// </summary>
    private void HeadlessTrace(float dt)
    {
        _traceTimer -= dt;
        if (_traceTimer > 0f) return;
        _traceTimer = 2f;

        int telegraphing = 0, attacking = 0, tokens = 0;
        foreach (Enemy e in _enemies.Enemies)
        {
            if (!e.Alive) continue;
            if (e.State == EnemyState.Telegraph) telegraphing++;
            if (e.State == EnemyState.Attack) attacking++;
            if (e.HoldsAttackToken) tokens++;
        }

        GD.Print($"  t+{_telemetry.SessionDuration:F0}s  room {_roomIndex}  " +
                 $"alive {_enemies.AliveCount}  tok {tokens} tel {telegraphing} atk {attacking}  " +
                 $"bullets {_enemyBullets.Count}  " +
                 $"hearts {_player.Hearts:F1}  sanity {_player.Sanity.Current:F0}/{_player.Sanity.LucidCeiling:F0} " +
                 $"({_player.Sanity.Band})  hits {_player.HitsTaken}");
    }

    private void OnDeath()
    {
        GD.Print("--------------------------------------------------------");
        GD.Print($"[CombatArena] DEAD on room {_roomIndex}.");
        GD.Print(_telemetry.Summary());
        _telemetry.WriteCsv();
        _player.ResetForTest(Vector2.Zero);
        _roomIndex = 0;
        StartRoom();
    }

    private void UpdateHitboxTint()
    {
        var hitbox = _player.GetNodeOrNull<ColorRect>("Hitbox");
        if (hitbox is null) return;
        hitbox.Color = _player.IsInvulnerable
            ? new Color("FFFFFF")
            : new Color("FFB347") with { A = 0.75f };
    }

    private void HandleDebugKeys()
    {
        if (Input.IsKeyPressed(Key.F5))
        {
            GD.Print(_telemetry.Summary());
            _telemetry.WriteCsv();
        }
        if (Input.IsKeyPressed(Key.G)) _player.Sanity.DebugSetCurrent(Tune.SanityMax);
    }

    // ---------------------------------------------------------------- Enemy rendering

    /// <summary>
    /// Placeholder enemy art, drawn immediate-mode. Deliberately crude — M1 answers a
    /// systems question, and spending art time here would be spending it on the wrong
    /// risk. The telegraph ring is NOT placeholder: docs/05 R3 requires a readable
    /// wind-up, and testing the Sanity economy against unreadable attacks would produce
    /// meaningless data.
    /// </summary>
    public override void _Draw()
    {
        foreach (Enemy e in _enemies.Enemies)
        {
            if (!e.Alive) continue;

            Color body = e.HitFlash > 0f ? Colors.White : e.Data.Tint;
            DrawCircle(e.Position, e.Data.BodyRadius, body);
            DrawArc(e.Position, e.Data.BodyRadius, 0, Mathf.Tau, 16,
                    new Color(0, 0, 0, 0.5f), 1.5f);

            // Role marker, so a tester can name what killed them.
            DrawString(ThemeDB.FallbackFont, e.Position + new Vector2(-4, -e.Data.BodyRadius - 4),
                       e.Data.Role.ToString()[..1], HorizontalAlignment.Left, -1, 8,
                       new Color(1, 1, 1, 0.65f));

            if (e.State == EnemyState.Telegraph)
            {
                float t = e.TelegraphProgress;
                DrawArc(e.Position, e.Data.BodyRadius + 6f + (1f - t) * 10f,
                        0, Mathf.Tau, 24, new Color(1f, 0.35f, 0.35f, 0.35f + t * 0.5f), 2f);
            }

            float hp = e.Health / e.Data.MaxHealth;
            if (hp < 1f)
            {
                Vector2 p = e.Position + new Vector2(-10, e.Data.BodyRadius + 4);
                DrawRect(new Rect2(p, new Vector2(20, 2)), new Color(0, 0, 0, 0.6f));
                DrawRect(new Rect2(p, new Vector2(20 * hp, 2)), new Color("C1440E"));
            }
        }
    }
}
