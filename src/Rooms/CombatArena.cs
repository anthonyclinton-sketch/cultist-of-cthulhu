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
    private Items.PickupManager _pickups = null!;
    private readonly Items.DropTable _drops = new();
    private PlayerController _player = null!;
    private Hud _hud = null!;
    private Rng _rng = null!;
    private readonly Telemetry _telemetry = new();

    private readonly List<EnemyData> _roster = new();
    private int _roomIndex;
    private bool _roomActive;
    private float _interRoomTimer;
    private readonly HitStop _hitStop = new();

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
        //
        // Floor 1's roster, from the one list that knows which enemies belong where. This
        // used to be its own copy of the paths, which is how a roster acquires an enemy the
        // other scene has never heard of.
        foreach (EnemyData data in Bestiary.ForFloor(1))
        {
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

        _pickups = new Items.PickupManager { Name = nameof(Items.PickupManager) };
        AddChild(_pickups);

        _enemies = new EnemyManager { Name = nameof(EnemyManager) };
        AddChild(_enemies);
        _enemies.Initialise(_enemyBullets, _playerBullets, bounds, Hash.Derive(GameRoot.Instance.RunSeed, "enemies"));
        _enemies.AttackTokens = FloorScaling.AttackTokens(1);   // the arena is a floor-1 slice
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
            Pickups = _pickups,
            Telemetry = _telemetry,
        };
        // The body is created by PlayerController itself (see PlayerVisual) — scenes no
        // longer supply one.
        _player.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 7f } });
        AddChild(_player);

        // --weapons=a,b,c turns the arena into a weapon bench (docs/09 §10). Without it, the
        // default three: one per family, including the two M1 mandates — a Grimoire and a
        // melee weapon, because both carry their own Sanity economy and are the likeliest to
        // invalidate it (docs/03 §2 Family IV and V).
        string spec = "";
        foreach (string arg in OS.GetCmdlineArgs())
            if (arg.StartsWith("--weapons=")) spec = arg["--weapons=".Length..];

        List<WeaponData> loadout = WeaponPool.ResolveLoadout(spec);
        if (loadout.Count > 0)
        {
            foreach (WeaponData w in loadout) _player.GiveWeapon(w);
            return;
        }

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

    private Camera2D? _camera;

    private void BuildCameraAndUi()
    {
        _camera = new Camera2D
        {
            Enabled = true,
            ProcessCallback = Camera2D.Camera2DProcessCallback.Physics,
        };
        AddChild(_camera);

        var layer = new CanvasLayer { Name = "UI" };
        _hud = new Hud { Name = nameof(Hud), Player = _player };
        layer.AddChild(_hud);
        AddChild(layer);

        AddChild(new Debug.DebugOverlay
        {
            Name = nameof(Debug.DebugOverlay),
            BulletManagerPath = _enemyBullets.GetPath(),
            PlayerBulletManagerPath = _playerBullets.GetPath(),
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

        // Drops BEFORE the ceiling drops, so the candle roll sees the headroom the player
        // actually had during the fight rather than the post-clear number.
        float headroom = _player.Sanity.LucidCeiling - _player.Sanity.Current;
        _drops.RollRoomClear(_pickups, _player.GlobalPosition, _rng, floor: 1,
                             playerKeys: _player.Keys,
                             reserveFraction: _player.TotalReserveFraction(),
                             sanityHeadroom: headroom);

        // Room clear: +20 Sanity, and the Lucid Ceiling drops. Post-F4 this decay is the
        // primary driver of the descent (docs/02 §3.3.1).
        _player.Sanity.OnRoomCleared();

        GD.Print($"[Room {_roomIndex}] cleared. sanity {_player.Sanity.Current:F0} " +
                 $"ceiling {_player.Sanity.LucidCeiling:F0} band {_player.Sanity.Band}   " +
                 $"drops {_pickups.Count}  armour {_player.Armour}  gold {_player.Gold}  " +
                 $"candles {_player.CandlesCollected}");
    }

    // ---------------------------------------------------------------- Tick

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        HandleDebugKeys();

        _player.Sanity.InCombat = _roomActive && _enemies.AliveCount > 0;
        _enemies.PlayerAscended = _player.Ascension.IsAscended;

        // The ladder's headline effect, finally reaching the arena (docs/02 §3.4). Until
        // this line existed, metric 9 measured whether players REACH Fraying while nothing
        // observable happened when they got there.
        _enemies.HallucinationRatio = _player.Sanity.HallucinationRatio;

        _telemetry.Tick(dt, _player.Sanity);

        if (_camera is not null) _camera.Offset = _player.ShakeOffset(_rng);

        // docs/02 §8 — hit stop, counted in REAL time by the shared helper. Counting it
        // down with the scaled delta made it last 1/TimeScale too long, which read as the
        // game freezing rather than punching.
        if (_player.PendingHitStop > 0f) _hitStop.Request(_player.PendingHitStop);
        _hitStop.Apply();

        if (_roomActive && _enemies.AliveCount == 0) EndRoom();
        else if (!_roomActive)
        {
            _interRoomTimer -= dt;
            if (_interRoomTimer <= 0f) StartRoom();
        }

        if (_player.IsDead) OnDeath();

        if (GameRoot.Instance.HeadlessTestMode || DisplayServer.GetName() == "headless") HeadlessTrace(dt);

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

    /// <summary>
    /// The Banish shockwave. Expands to exactly Tune.BanishRadius so the player learns the
    /// real reach by watching it — a ring that does not match the hitbox teaches a wrong
    /// mental model, which is worse than no ring at all.
    /// </summary>
    private void DrawBanishPulse()
    {
        float p = _player.BanishPulse;
        if (p <= 0f) return;

        float t = 1f - p;                          // 0 -> 1 outward
        float radius = Tune.BanishRadius * (0.25f + 0.75f * t);
        float alpha = p * 0.9f;

        DrawArc(_player.BanishOrigin, radius, 0, Mathf.Tau, 48,
                new Color(0.55f, 0.9f, 0.85f, alpha), 3f + p * 3f);
        DrawArc(_player.BanishOrigin, radius * 0.82f, 0, Mathf.Tau, 48,
                new Color(1f, 1f, 1f, alpha * 0.5f), 1.5f);
    }

    /// <summary>
    /// docs/02 §8: "every kill spawns a small light that visibly flies into the player's
    /// sanity ring. This makes the 'kill to fund yourself' loop viscerally legible without
    /// any UI text."
    ///
    /// Empowered motes (killed during i-frames, worth double) are drawn hotter and larger,
    /// so the x2 is something the player SEES rather than something they read about.
    /// </summary>
    private void DrawSanityMotes()
    {
        for (int i = 0; i < _player.MoteCount; i++)
        {
            PlayerController.Mote m = _player.GetMote(i);
            float a = Mathf.Clamp(m.Life, 0f, 1f);
            float r = m.Empowered ? 4.5f : 3f;
            Color c = m.Empowered ? new Color(1f, 0.85f, 0.45f, a) : new Color(0.5f, 0.88f, 0.83f, a);

            DrawCircle(m.Position, r, c);
            DrawCircle(m.Position, r * 2.2f, c with { A = a * 0.22f });
        }
    }

    /// <summary>
    /// Ground pickups. The candle gets a halo and a bob the others do not — it is the
    /// only counter-play to the descent, and a player who walks past one because it
    /// looked like loose change has been failed by the presentation, not by the design.
    /// </summary>
    private void DrawPickups()
    {
        float t = _telemetry.SessionDuration;

        foreach (Items.Pickup p in _pickups.Pickups)
        {
            Color c = Items.PickupManager.ColourFor(p.Kind);
            float r = Items.PickupManager.RadiusFor(p.Kind);
            float bob = Mathf.Sin(t * 4f + p.Position.X * 0.1f) * 1.5f;
            Vector2 pos = p.Position + new Vector2(0, bob);

            if (p.Kind == Items.PickupKind.SanityCandle)
            {
                float pulse = 0.35f + 0.25f * Mathf.Sin(t * 5f);
                DrawCircle(pos, r * 3.2f, c with { A = pulse * 0.35f });
                DrawCircle(pos, r * 2f, c with { A = pulse * 0.5f });
            }

            DrawCircle(pos, r, c);
            DrawArc(pos, r + 1.5f, 0, Mathf.Tau, 12, new Color(0, 0, 0, 0.55f), 1.2f);
        }
    }

    private void HandleDebugKeys()
    {
        if (Input.IsKeyPressed(Key.F5))
        {
            GD.Print(_telemetry.Summary());
            _telemetry.WriteCsv();
        }
        if (Input.IsKeyPressed(Key.G)) _player.Sanity.DebugSetCurrent(Tune.SanityMax);

        // K forces Ascension. Without this it is nearly impossible to reach on purpose —
        // the player dies at 6 hits but Ascension needs 10, so in normal play it only
        // happens via heavy Grimoire or Banish spending. Needed to feel the state at all.
        if (Input.IsKeyPressed(Key.K) && !_player.Ascension.IsAscended) _player.Sanity.Drain(999f);
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
        DrawBanishPulse();
        DrawSanityMotes();
        DrawPickups();

        foreach (Enemy e in _enemies.Enemies)
        {
            if (!e.Alive) continue;

            Color body = e.HitFlash > 0f ? Colors.White
                       : e.IsStunned ? e.Data.Tint.Lerp(new Color("4A4A6A"), 0.6f)
                       : e.Data.Tint;
            DrawCircle(e.Position, e.Data.BodyRadius, body);

            // Stunned enemies need to be distinguishable at a glance, or Banish looks
            // like it did nothing — the bullets vanishing is obvious, the interrupted
            // wind-up is not.
            if (e.IsStunned)
            {
                DrawArc(e.Position, e.Data.BodyRadius + 3f, 0, Mathf.Tau, 12,
                        new Color(0.6f, 0.6f, 1f, 0.7f), 1.5f);
            }
            DrawArc(e.Position, e.Data.BodyRadius, 0, Mathf.Tau, 16,
                    new Color(0, 0, 0, 0.5f), 1.5f);

            // Role marker, so a tester can name what killed them.
            DrawString(ThemeDB.FallbackFont, e.Position + new Vector2(-4, -e.Data.BodyRadius - 4),
                       e.Data.Role.ToString()[..1], HorizontalAlignment.Left, -1, 8,
                       new Color(1, 1, 1, 0.65f));

            // Weak point — always live, VISIBLE only at Fraying and below (docs/02 §3.4).
            // That distinction is the ladder's payoff: the low band gives you information,
            // not damage, and the information is worth something only if you can use it.
            if (_player.Sanity.WeakPointsVisible)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(_telemetry.SessionDuration * 6f + e.Id);
                DrawCircle(e.WeakPointPosition, e.WeakPointRadius, new Color(1f, 0.9f, 0.35f, pulse));
                DrawArc(e.WeakPointPosition, e.WeakPointRadius + 1.5f, 0, Mathf.Tau, 10,
                        new Color(1f, 1f, 1f, pulse * 0.8f), 1f);
            }

            if (e.IsMarked)
            {
                DrawArc(e.Position, e.Data.BodyRadius + 5f, 0, Mathf.Tau, 14,
                        new Color(1f, 0.45f, 0.2f, 0.8f), 2f);
            }

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
