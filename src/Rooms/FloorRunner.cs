using System.Collections.Generic;
using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Generation;
using CultistOfCthulhu.Meta;
using CultistOfCthulhu.Player;
using CultistOfCthulhu.UI;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Rooms;

/// <summary>
/// A walkable, procedurally generated floor. The first scene where the generator and the
/// combat slice are the same game.
///
///   pwsh ./tools/gates.ps1 -Floor
///
/// Everything before this ran a fixed arena and spawned waves into it, which tested the
/// Sanity economy but not the thing docs/06 exists for — moving through an authored
/// topology, choosing routes, and finding the reward room. The generator had been built,
/// validated across 10,000 seeds, and connected to nothing.
///
/// Room activation follows docs/09 §6.1: only the room the player occupies runs its
/// encounter. Doors seal on entry to an uncleared combat room and open on clear, which is
/// what makes a room a room rather than a region of an open map.
/// </summary>
public sealed partial class FloorRunner : Node2D
{
    private BulletManager _enemyBullets = null!;
    private BulletManager _playerBullets = null!;
    private EnemyManager _enemies = null!;
    private Items.PickupManager _pickups = null!;
    private readonly Items.DropTable _drops = new();
    private PlayerController _player = null!;
    private Hud _hud = null!;
    private Camera2D _camera = null!;
    private Rng _rng = null!;
    private readonly Telemetry _telemetry = new();

    private GeneratedFloor _floor = null!;
    private FloorGeometry _geometry = null!;
    private readonly List<EnemyData> _roster = new();

    private readonly HashSet<int> _clearedRooms = new();
    private readonly Dictionary<int, StaticBody2D> _doorSeals = new();
    private int _currentRoom = -1;
    private int _pendingSealRoom = -1;
    private bool _encounterActive;
    private readonly HitStop _hitStop = new();
    private bool _f7Held;
    private int _roomsCleared;

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "floor_runner");

        LoadContent();
        GenerateFloor();
        BuildGeometry();
        BuildManagers();
        BuildPlayer();
        BuildCameraAndUi();

        ParseScreenshotArgs();
        EnterRoom(_floor.FindRole(RoomRole.Entrance)!.NodeId);

        GD.Print($"[FloorRunner] {_floor.Rooms.Count} rooms, flow '{_floor.FlowId}'. " +
                 "WASD move · LMB fire · SPACE dash · R recite · RMB banish · TAB map · F3 overlay");
    }

    // ---------------------------------------------------------------- Setup

    private void LoadContent()
    {
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
            if (data is not null) _roster.Add(data);
            else GD.PrintErr($"[FloorRunner] failed to load {path}");
        }
    }

    private void GenerateFloor()
    {
        var gen = new FloorGenerator(UndercroftContent.Flows(), UndercroftContent.Rooms());
        GeneratedFloor? floor = gen.Generate(
            Hash.Combine(GameRoot.Instance.RunSeed, "floor1"), floorIndex: 1, out string failure);

        if (floor is null)
        {
            GD.PrintErr($"[FloorRunner] generation failed: {failure}");
            GetTree().Quit(1);
            return;
        }
        _floor = floor;
    }

    private void BuildGeometry()
    {
        _geometry = new FloorGeometry(_floor);

        // Floor tiles, drawn as one batched node rather than thousands of ColorRects.
        AddChild(new FloorTiles { Geometry = _geometry, ZIndex = -100 });

        // Collision shell.
        var walls = new StaticBody2D { Name = "Walls" };
        foreach (Rect2 r in _geometry.BuildWallRects())
        {
            walls.AddChild(new CollisionShape2D
            {
                Position = r.Position + r.Size * 0.5f,
                Shape = new RectangleShape2D { Size = r.Size },
            });
        }
        AddChild(walls);
    }

    private void BuildManagers()
    {
        Rect2I b = _floor.Bounds();
        var world = new Rect2(
            (b.Position.X - 4) * FloorGeometry.Tile, (b.Position.Y - 4) * FloorGeometry.Tile,
            (b.Size.X + 8) * FloorGeometry.Tile, (b.Size.Y + 8) * FloorGeometry.Tile);

        _enemyBullets = new BulletManager { Name = nameof(BulletManager), Bounds = world };
        AddChild(_enemyBullets);

        _playerBullets = new BulletManager { Name = "PlayerBullets", Bounds = world, CollideWithEnemies = true };
        AddChild(_playerBullets);

        _pickups = new Items.PickupManager { Name = nameof(Items.PickupManager) };
        AddChild(_pickups);

        _enemies = new EnemyManager { Name = nameof(EnemyManager) };
        AddChild(_enemies);
        _enemies.Initialise(_enemyBullets, _playerBullets, world, Hash.Derive(GameRoot.Instance.RunSeed, "enemies"));
    }

    private void BuildPlayer()
    {
        PlacedRoom entrance = _floor.FindRole(RoomRole.Entrance)!;

        _player = new PlayerController
        {
            Name = nameof(PlayerController),
            Position = _geometry.RoomCentreWorld(entrance),
            EnemyBullets = _enemyBullets,
            PlayerBullets = _playerBullets,
            Enemies = _enemies,
            Pickups = _pickups,
            Telemetry = _telemetry,
        };
        _player.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 6f } });
        AddChild(_player);

        foreach (string path in new[]
                 {
                     "res://data/weapons/webley_mk_vi.tres",
                     "res://data/weapons/cantrip_withering.tres",
                     "res://data/weapons/sacrificial_kris.tres",
                 })
        {
            var data = GD.Load<WeaponData>(path);
            if (data is not null) _player.GiveWeapon(data);
        }
    }

    private void BuildCameraAndUi()
    {
        _camera = new Camera2D
        {
            Enabled = true,
            ProcessCallback = Camera2D.Camera2DProcessCallback.Physics,
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 8f,
        };
        _player.AddChild(_camera);

        var layer = new CanvasLayer { Name = "UI" };
        _hud = new Hud { Name = nameof(Hud), Player = _player };
        layer.AddChild(_hud);
        layer.AddChild(new Minimap { Name = nameof(Minimap), Floor = _floor, Player = _player, Cleared = _clearedRooms });
        AddChild(layer);

        AddChild(new Debug.DebugOverlay
        {
            Name = nameof(Debug.DebugOverlay),
            BulletManagerPath = _enemyBullets.GetPath(),
            PlayerBulletManagerPath = _playerBullets.GetPath(),
            PlayerPath = _player.GetPath(),
        });
    }

    // ---------------------------------------------------------------- Tick

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_player.PendingHitStop > 0f) _hitStop.Request(_player.PendingHitStop);
        _hitStop.Apply();

        TrackRoom();
        UpdatePendingSeal();
        SafetyUnstick();

        _enemies.PlayerPosition = _player.GlobalPosition;
        _enemies.PlayerVelocity = _player.Velocity;
        _enemies.PlayerAscended = _player.Ascension.IsAscended;
        _enemies.HallucinationRatio = _player.Sanity.HallucinationRatio;
        _player.Sanity.InCombat = _encounterActive && _enemies.AliveCount > 0;

        _telemetry.Tick(dt, _player.Sanity);
        _camera.Offset = _player.ShakeOffset(_rng);

        if (_encounterActive && _enemies.AliveCount == 0) ClearRoom();
        if (_player.IsDead) OnDeath();

        QueueRedraw();
        HandleDebugKeys();
        TickScreenshot();
    }

    /// <summary>
    /// Which room is the player standing in? Drives activation and the minimap.
    ///
    /// Tested against the room INTERIOR, not its bounds. A doorway is carved through the
    /// wall ring of both rooms, so a player standing in a door is inside both rooms'
    /// bounds — which made the room flip over the instant you touched a threshold and
    /// sealed a door on top of you.
    /// </summary>
    private void TrackRoom()
    {
        foreach (PlacedRoom r in _floor.Rooms)
        {
            if (!_geometry.RoomInteriorWorld(r).HasPoint(_player.GlobalPosition)) continue;
            if (r.NodeId != _currentRoom) EnterRoom(r.NodeId);
            return;
        }
        // Standing in a doorway or corridor: keep the last room, do not flip.
    }

    /// <summary>
    /// Doors close only once the player is CLEAR of them.
    ///
    /// Sealing on room entry spawned a StaticBody2D over a player still standing in the
    /// threshold, and a CharacterBody2D inside a static body cannot push itself out — the
    /// run was simply over. The encounter still starts on entry; only the seal waits.
    ///
    /// Leaving the room before the seal engages cancels it, which also gives a small,
    /// forgiving grace window to back out of a fight you have just seen.
    /// </summary>
    private void UpdatePendingSeal()
    {
        if (_pendingSealRoom < 0) return;

        if (_currentRoom != _pendingSealRoom) { _pendingSealRoom = -1; return; }

        PlacedRoom? room = FindRoom(_pendingSealRoom);
        if (room is null) { _pendingSealRoom = -1; return; }

        foreach (Doorway d in _geometry.Doors)
        {
            if (d.RoomA != room.NodeId && d.RoomB != room.NodeId) continue;
            // Generous clearance: the player's body radius plus a margin, so the seal never
            // materialises against them.
            if (d.WorldRect.Grow(DoorClearance).HasPoint(_player.GlobalPosition)) return;
        }

        SealDoors(room, true);
        _pendingSealRoom = -1;
    }

    /// <summary>
    /// Last-resort unstick: if the player is ever found inside an active seal, open it.
    ///
    /// The ordering fix above should make this unreachable. It exists anyway because the
    /// failure it guards is TERMINAL — a CharacterBody2D inside a StaticBody2D cannot push
    /// itself out, so the player is stuck until they quit, losing the run. A rare open door
    /// is a far cheaper failure than that, and this costs one rect test per seal per tick.
    /// </summary>
    private void SafetyUnstick()
    {
        if (_doorSeals.Count == 0) return;

        foreach (Doorway d in _geometry.Doors)
        {
            int key = d.RoomA * 10000 + d.RoomB;
            if (!_doorSeals.TryGetValue(key, out StaticBody2D? body)) continue;
            if (!d.WorldRect.Grow(4f).HasPoint(_player.GlobalPosition)) continue;

            GD.PrintErr("[FloorRunner] player found inside a sealed door — opening it. " +
                        "This should be unreachable; the seal ordering has a gap.");
            body.QueueFree();
            _doorSeals.Remove(key);
            return;
        }
    }

    private const float DoorClearance = 20f;

    private PlacedRoom? FindRoom(int nodeId)
    {
        foreach (PlacedRoom r in _floor.Rooms) if (r.NodeId == nodeId) return r;
        return null;
    }

    private void EnterRoom(int nodeId)
    {
        _currentRoom = nodeId;
        PlacedRoom? room = null;
        foreach (PlacedRoom r in _floor.Rooms) if (r.NodeId == nodeId) { room = r; break; }
        if (room is null) return;

        if (_clearedRooms.Contains(nodeId)) return;
        if (!IsCombatRole(room.Role)) { _clearedRooms.Add(nodeId); OnNonCombatRoom(room); return; }

        StartEncounter(room);
    }

    private static bool IsCombatRole(RoomRole r) =>
        r is RoomRole.CombatEasy or RoomRole.CombatMed or RoomRole.CombatHard or RoomRole.Hub;

    private void OnNonCombatRoom(PlacedRoom room)
    {
        // Reward/shop/shrine content is M2 proper; for now the room announces itself so
        // route choice is legible while walking the floor.
        if (room.Role is RoomRole.Reward or RoomRole.Shop or RoomRole.Shrine or RoomRole.Secret)
            GD.Print($"[{room.Role}] {room.Template.Id} — contents are M2.");
    }

    /// <summary>
    /// docs/06 §6.1 Dread Budget. Scales with rooms cleared and the room's authored
    /// ThreatCapacity, with the >=35% fodder floor that keeps the Sanity economy solvent.
    /// </summary>
    private void StartEncounter(PlacedRoom room)
    {
        float budget = Mathf.Min(room.Template.ThreatCapacity, 34f + _roomsCleared * 13f);
        if (room.Role == RoomRole.CombatHard) budget *= 1.25f;

        float fodderFloor = budget * 0.35f;
        float fodderSpent = 0f, spent = 0f;
        int guard = 0;

        Rect2 area = _geometry.RoomRectWorld(room).Grow(-32f);

        while (spent < budget && guard++ < 64)
        {
            EnemyData pick = PickEnemy(fodderSpent < fodderFloor);
            if (pick.DreadCost > budget - spent && spent > 0f) break;

            var at = new Vector2(
                _rng.Range(area.Position.X, area.Position.X + area.Size.X),
                _rng.Range(area.Position.Y, area.Position.Y + area.Size.Y));

            _enemies.Spawn(pick, at);
            spent += pick.DreadCost;
            if (pick.Role == EnemyRole.Fodder) fodderSpent += pick.DreadCost;
        }

        if (_enemies.AliveCount == 0) { _clearedRooms.Add(room.NodeId); return; }

        _encounterActive = true;
        // Arm the seal; UpdatePendingSeal closes it once the player is clear of the door.
        _pendingSealRoom = room.NodeId;
        _telemetry.BeginRoom(_roomsCleared + 1, _player.Sanity);

        GD.Print($"[Room {room.Template.Id}] {room.Role}  budget {budget:F0}  " +
                 $"enemies {_enemies.AliveCount}  ceiling {_player.Sanity.LucidCeiling:F0}");
    }

    private EnemyData PickEnemy(bool needFodder)
    {
        for (int i = 0; i < 24; i++)
        {
            EnemyData d = _roster[_rng.NextInt(0, _roster.Count)];
            if (needFodder && d.Role != EnemyRole.Fodder) continue;
            return d;
        }
        return _roster[0];
    }

    private void ClearRoom()
    {
        _encounterActive = false;
        _pendingSealRoom = -1;      // never let an armed seal fire after the fight is over
        _clearedRooms.Add(_currentRoom);
        _roomsCleared++;

        PlacedRoom? room = FindRoom(_currentRoom);
        if (room is not null) SealDoors(room, false);

        float headroom = _player.Sanity.LucidCeiling - _player.Sanity.Current;
        _drops.RollRoomClear(_pickups, _player.GlobalPosition, _rng, 1,
                             _player.Keys, _player.TotalReserveFraction(), headroom);

        Weapon w = _player.Weapons.Active;
        _telemetry.EndRoom(_player.Sanity, _player.Weapons.ReloadsAttempted,
                           _player.Weapons.ReloadsDenied, w.PerfectRecitations, w.FailedRecitations);

        _player.Sanity.OnRoomCleared();

        GD.Print($"[cleared] {_roomsCleared} rooms  sanity {_player.Sanity.Current:F0}/" +
                 $"{_player.Sanity.Max:F0}  ceiling {_player.Sanity.LucidCeiling:F0}  band {_player.Sanity.Band}");
    }

    /// <summary>
    /// Doors seal while a room is contested. This is what makes an encounter a ROOM rather
    /// than a region you can walk out of — without it every fight is optional and the
    /// Sanity economy never binds.
    /// </summary>
    private void SealDoors(PlacedRoom room, bool sealed_)
    {
        foreach (Doorway d in _geometry.Doors)
        {
            if (d.RoomA != room.NodeId && d.RoomB != room.NodeId) continue;
            int key = d.RoomA * 10000 + d.RoomB;

            if (sealed_)
            {
                if (_doorSeals.ContainsKey(key)) continue;
                var body = new StaticBody2D { Position = d.WorldRect.Position + d.WorldRect.Size * 0.5f };
                body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = d.WorldRect.Size } });
                AddChild(body);
                _doorSeals[key] = body;
            }
            else if (_doorSeals.TryGetValue(key, out StaticBody2D? body))
            {
                body.QueueFree();
                _doorSeals.Remove(key);
            }
        }
    }

    // ---------------------------------------------------------------- Draw

    /// <summary>
    /// Death. Its absence is what turned a lost run into an apparent freeze: with no
    /// handler the scene simply kept ticking a corpse, and nothing ever restored the
    /// time scale or gave the player a way out.
    /// </summary>
    private void OnDeath()
    {
        GD.Print("--------------------------------------------------------");
        GD.Print($"[FloorRunner] DEAD after {_roomsCleared} rooms.");
        GD.Print(_telemetry.Summary());
        _telemetry.WriteCsv();

        // Clear the time scale explicitly. Relying on the per-frame Apply() to release it
        // is what failed here in the first place.
        _hitStop.Clear();

        RestartFloor();
    }

    /// <summary>Rebuild the run on the same floor: everything back to the entrance.</summary>
    private void RestartFloor()
    {
        _enemies.ClearAll();
        _enemyBullets.Clear();
        _playerBullets.Clear();
        _pickups.ClearAll();

        foreach (StaticBody2D body in _doorSeals.Values) body.QueueFree();
        _doorSeals.Clear();

        _clearedRooms.Clear();
        _encounterActive = false;
        _pendingSealRoom = -1;
        _currentRoom = -1;
        _roomsCleared = 0;
        _drops.ResetForRun();

        PlacedRoom entrance = _floor.FindRole(RoomRole.Entrance)!;
        _player.ResetForTest(_geometry.RoomCentreWorld(entrance));
        EnterRoom(entrance.NodeId);
    }

    // ---------------------------------------------------------------- Visual capture
    //
    // --screenshot=<path> renders a frame to PNG and quits. Every rendering bug in this
    // project so far (invisible player, and now invisible bullets) was invisible to the
    // gates too, because a headless run has no framebuffer and a passing smoke test proves
    // only that nothing threw. This is the cheapest way to actually LOOK at a frame
    // without a human in the loop.
    //
    // It fires a fan of player bullets first, because the thing most often worth checking
    // is whether projectiles render at all.

    private string _screenshotPath = "";
    private int _screenshotAfter = 40;
    private bool _meleeDemo;
    private bool _combatDemo;
    private int _frameCount;

    private void ParseScreenshotArgs()
    {
        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (arg.StartsWith("--screenshot=")) _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--screenshot-after="))
                _screenshotAfter = int.TryParse(arg["--screenshot-after=".Length..], out int n) ? n : 40;
            else if (arg == "--melee-demo") _meleeDemo = true;
            else if (arg == "--combat-demo") _combatDemo = true;
        }
    }

    private void TickScreenshot()
    {
        if (_screenshotPath.Length == 0) return;
        _frameCount++;

        // Set up EARLY and hold fire for a long window. Twice now a 12-frame window has
        // produced "one shot fired" and been mistaken for a fault — at 4.5 rounds/sec that
        // is simply the correct number. The window must span several fire cycles or the
        // test cannot distinguish a broken gun from a fast one.
        if (_frameCount == 30)
        {
            // Hold FIRE and let the real weapon path run. Spawning bullets by hand only
            // proved the renderer works; it could not tell us whether the gun does.
            // Reproduce the reported case: bullets are fine in the empty spawn room and
            // vanish once you enter a room with enemies. Teleport into the first combat
            // room so the difference is the ENEMIES, not the geometry.
            if (_combatDemo)
            {
                foreach (PlacedRoom r in _floor.Rooms)
                {
                    if (!IsCombatRole(r.Role)) continue;
                    _player.GlobalPosition = _geometry.RoomCentreWorld(r);
                    EnterRoom(r.NodeId);
                    GD.Print($"[screenshot] combat demo — moved to {r.Template.Id} ({r.Role}), " +
                             $"{_enemies.AliveCount} enemies");

                    // A stationary ring right next to the player. Aim direction cannot
                    // confound this: if these do not appear on screen, the bullets are
                    // being culled rather than mis-aimed.
                    for (int k = 0; k < 20; k++)
                    {
                        float a = k / 20f * Mathf.Tau;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        _playerBullets.Spawn(_player.GlobalPosition + d * 55f, Vector2.Zero,
                                             3f, 30f, new Color("FFB347"), 8f, BulletFlags.PlayerOwned);
                    }
                    break;
                }
            }

            if (_meleeDemo)
            {
                for (int i = 0; i < _player.Weapons.Count; i++)
                    if (_player.Weapons.Weapons[i].Data.IsMelee) _player.Weapons.SetActive(i);
                GD.Print("[screenshot] melee demo — swapped to the melee weapon");
            }
            Input.ActionPress("fire");
            GD.Print("[screenshot] holding fire — exercising the real weapon path");
        }

        // Per-tick trace over the last 10 frames: are bullets being SPAWNED and then
        // destroyed, or never spawned at all? Those need completely different fixes and a
        // single end-of-run count cannot tell them apart.
        if (_combatDemo && _frameCount > _screenshotAfter - 10 && _frameCount <= _screenshotAfter)
        {
            Weapon aw = _player.Weapons.Active;
            GD.Print($"  f{_frameCount}  bullets {_playerBullets.Count,3}  " +
                     $"renderUs {_playerBullets.LastRenderMicroseconds,6:F1}  " +
                     $"shadows {_playerBullets.ShadowCount,3}  " +
                     $"visible {_playerBullets.DebugVisibleInstances,3}  " +
                     $"firstOffset {_playerBullets.DebugFirstOffsetFrom(_player.GlobalPosition)}");
        }

        if (_frameCount != _screenshotAfter) return;

        // Fire EVERY carried weapon in turn and report what each produced. "I can't see my
        // bullets" is weapon-specific far more often than it is renderer-specific, and
        // testing only the starter would have missed that entirely.
        GD.Print($"[screenshot] melee swing state at capture: {_player.MeleeSwing:F2} " +
                 $"(reach {_player.MeleeSwingReach:F0}px, arc {_player.MeleeSwingArc:F0}deg)");
        GD.Print("[screenshot] --- per-weapon fire test ---");
        for (int i = 0; i < _player.Weapons.Count; i++)
        {
            _player.Weapons.SetActive(i);
            Weapon w = _player.Weapons.Active;
            _playerBullets.Clear();
            _player.Sanity.DebugSetCurrent(Tune.SanityMax);

            for (int f = 0; f < 40; f++) _player._PhysicsProcess(1.0 / 60.0);

            GD.Print($"  [{i}] {w.Data.DisplayName,-22} family {w.Data.Family,-12} " +
                     $"melee {w.Data.IsMelee,-5} mag {w.Magazine}/{w.Data.MagazineSize}  " +
                     $"-> {_playerBullets.Count} bullets");
        }
        _player.Weapons.SetActive(0);

        Input.ActionRelease("fire");

        Image img = GetViewport().GetTexture().GetImage();
        Error err = img.SavePng(_screenshotPath);
        GD.Print($"[screenshot] {_screenshotPath} → {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    /// <summary>Engine.TimeScale is global state. Leaving a scene mid-hit-stop would
    /// otherwise strand the whole game at 0.05x.</summary>
    public override void _ExitTree() => _hitStop.Clear();

    public override void _Draw()
    {
        // Sealed doorways, so the player can see why they cannot leave.
        foreach (Doorway d in _geometry.Doors)
        {
            int key = d.RoomA * 10000 + d.RoomB;
            bool isSealed = _doorSeals.ContainsKey(key);
            DrawRect(d.WorldRect, isSealed ? new Color("B0122A") : new Color("2A3038"));
        }

        foreach (Items.Pickup p in _pickups.Pickups)
        {
            Color c = Items.PickupManager.ColourFor(p.Kind);
            float r = Items.PickupManager.RadiusFor(p.Kind);
            if (p.Kind == Items.PickupKind.SanityCandle)
                DrawCircle(p.Position, r * 3f, c with { A = 0.25f });
            DrawCircle(p.Position, r, c);
        }

        for (int i = 0; i < _player.MoteCount; i++)
        {
            PlayerController.Mote m = _player.GetMote(i);
            Color c = m.Empowered ? new Color(1f, 0.85f, 0.45f) : new Color(0.5f, 0.88f, 0.83f);
            DrawCircle(m.Position, m.Empowered ? 4.5f : 3f, c with { A = Mathf.Clamp(m.Life, 0f, 1f) });
        }

        foreach (Enemy e in _enemies.Enemies)
        {
            if (!e.Alive) continue;

            Color body = e.HitFlash > 0f ? Colors.White
                       : e.IsStunned ? e.Data.Tint.Lerp(new Color("4A4A6A"), 0.6f)
                       : e.Data.Tint;
            DrawCircle(e.Position, e.Data.BodyRadius, body);

            if (_player.Sanity.WeakPointsVisible)
                DrawCircle(e.WeakPointPosition, e.WeakPointRadius, new Color(1f, 0.9f, 0.35f, 0.85f));

            if (e.State == EnemyState.Telegraph)
                DrawArc(e.Position, e.Data.BodyRadius + 4f + e.TelegraphProgress * 8f,
                        0, Mathf.Tau * e.TelegraphProgress, 20, new Color("FF5555"), 2f);

            float hp = e.Health / e.Data.MaxHealth;
            if (hp < 1f)
                DrawRect(new Rect2(e.Position.X - 10, e.Position.Y - e.Data.BodyRadius - 7, 20 * hp, 2),
                         new Color("D64545"));
        }

        if (_player.BanishPulse > 0f)
        {
            float t = 1f - _player.BanishPulse;
            DrawArc(_player.BanishOrigin, Tune.BanishRadius * (0.25f + 0.75f * t), 0, Mathf.Tau, 48,
                    new Color(0.55f, 0.9f, 0.85f, _player.BanishPulse * 0.9f), 3f);
        }
    }

    private void HandleDebugKeys()
    {
        if (Input.IsKeyPressed(Key.F5))
        {
            GD.Print(_telemetry.Summary());
            _telemetry.WriteCsv();
        }
        // F7 cycles hit-stop weight. It is a taste parameter, so it gets a live knob rather
        // than a constant someone has to guess at from a description.
        if (Input.IsKeyPressed(Key.F7) && !_f7Held) GD.Print($"[feel] {HitStop.CyclePreset()}");
        _f7Held = Input.IsKeyPressed(Key.F7);

        if (Input.IsKeyPressed(Key.G)) _player.Sanity.DebugSetCurrent(Tune.SanityMax);
        if (Input.IsKeyPressed(Key.K) && !_player.Ascension.IsAscended) _player.Sanity.Drain(999f);
    }
}

/// <summary>Batched floor rendering — one _Draw over merged tile runs, not a node per tile.</summary>
public sealed partial class FloorTiles : Node2D
{
    public FloorGeometry Geometry = null!;
    private static readonly Color FloorColour = new("1A1D24");
    private static readonly Color GridColour = new("22262F");

    public override void _Ready()
    {
        foreach (Rect2 r in Geometry.BuildFloorRects()) _rects.Add(r);
        QueueRedraw();
    }

    private readonly List<Rect2> _rects = new();

    public override void _Draw()
    {
        foreach (Rect2 r in _rects)
        {
            DrawRect(r, FloorColour);
            DrawRect(r, GridColour, filled: false, width: 1f);
        }
    }
}
