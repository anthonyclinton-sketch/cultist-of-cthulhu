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
    private PlayerController _player = null!;
    private Hud _hud = null!;
    private Camera2D _camera = null!;
    private Rng _rng = null!;

    private GeneratedFloor _floor = null!;
    private FloorGeometry _geometry = null!;

    /// <summary>The floor's solid mask. Held so door seals can be written into it — the
    /// hand-simulated systems only respect geometry that lives here.</summary>
    private Core.TileMask _walls = null!;
    private readonly List<EnemyData> _roster = new();

    private readonly HashSet<int> _clearedRooms = new();
    private readonly Dictionary<int, StaticBody2D> _doorSeals = new();
    private int _currentRoom = -1;
    private int _pendingSealRoom = -1;
    private bool _encounterActive;
    private readonly HitStop _hitStop = new();
    private bool _f7Held;
    private int _roomsCleared;

    private RoomContent _content = null!;
    private UI.ReverieScreen _reverie = null!;
    private Minimap _minimap = null!;
    private EncounterDirector _director = null!;
    private int _hitsAtRoomStart;

    /// <summary>
    /// Split in two, and the split is the point of this class now.
    ///
    /// RUN-SCOPED things — the player, the camera, the HUD, the Reverie — are built once
    /// and survive every floor transition, because they carry the build the player has
    /// spent the run assembling. FLOOR-SCOPED things — geometry, the bullet managers, the
    /// enemy manager, the room content — are torn down and rebuilt per floor, because
    /// their bounds, their walls and their contents all change.
    ///
    /// Before this, everything was built in _Ready and reset on death, which is why killing
    /// the boss cleared a room and then nothing happened: there was nowhere for a floor to
    /// end TO.
    /// </summary>
    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "floor_runner");
        _run = GameRoot.Instance.Run;

        LoadContent();
        ParseScreenshotArgs();

        BuildRunScopedNodes();
        if (_startingCorruption > 0f) _run.Corruption = _startingCorruption;
        BeginFloor();
    }

    private Meta.RunState _run = null!;

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

        _bossData = GD.Load<BossData>("res://data/bosses/thing_on_the_doorstep.tres");
        if (_bossData is null) GD.PrintErr("[FloorRunner] failed to load the boss.");
    }

    private void GenerateFloor()
    {
        var gen = new FloorGenerator(UndercroftContent.Flows(), UndercroftContent.Rooms());

        // The floor seed derives from the RUN seed and the floor index, so floor 2 of a
        // given run is always the same floor 2 (docs/06 §7) and a re-entered floor is not a
        // new one. It used to be hardcoded to "floor1".
        GeneratedFloor? floor = gen.Generate(_run.FloorSeed, _run.FloorIndex, out string failure);

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
        _floorTiles = new FloorTiles { Name = "FloorTiles", Geometry = _geometry, ZIndex = -100 };
        AddChild(_floorTiles);

        // Collision shell.
        _wallBody = new StaticBody2D { Name = "Walls" };
        foreach (Rect2 r in _geometry.BuildWallRects())
        {
            _wallBody.AddChild(new CollisionShape2D
            {
                Position = r.Position + r.Size * 0.5f,
                Shape = new RectangleShape2D { Size = r.Size },
            });
        }
        AddChild(_wallBody);
    }

    private FloorTiles _floorTiles = null!;
    private StaticBody2D _wallBody = null!;

    private void BuildManagers()
    {
        Rect2I b = _floor.Bounds();
        var world = new Rect2(
            (b.Position.X - 4) * FloorGeometry.Tile, (b.Position.Y - 4) * FloorGeometry.Tile,
            (b.Size.X + 8) * FloorGeometry.Tile, (b.Size.Y + 8) * FloorGeometry.Tile);

        // One mask, shared by both bullet managers and the enemies. Built from the same
        // walkable grid the collision shell comes from, so the hand-simulated systems and
        // Godot's physics agree about where the walls are.
        Core.TileMask walls = _geometry.BuildSolidMask();
        _walls = walls;

        _enemyBullets = new BulletManager { Name = nameof(BulletManager), Bounds = world, Walls = walls };
        AddChild(_enemyBullets);

        _playerBullets = new BulletManager
        {
            Name = "PlayerBullets", Bounds = world, CollideWithEnemies = true, Walls = walls,
        };
        AddChild(_playerBullets);

        _pickups = new Items.PickupManager { Name = nameof(Items.PickupManager) };
        AddChild(_pickups);

        _enemies = new EnemyManager { Name = nameof(EnemyManager) };
        AddChild(_enemies);
        _enemies.Initialise(_enemyBullets, _playerBullets, world, Hash.Derive(GameRoot.Instance.RunSeed, "enemies"));
        _enemies.SetWalls(walls);

        _director = new EncounterDirector(_roster, _enemies, _geometry,
                                          Hash.Derive(_run.FloorSeed, "encounters"));
    }

    /// <summary>
    /// Everything that survives a floor transition. Built once.
    ///
    /// The player is the important one: it carries the Circle, the inscriptions and the
    /// Ascension debt, so rebuilding it per floor would silently reset the run. Its manager
    /// references are rewired per floor by <see cref="RewirePlayer"/> instead.
    /// </summary>
    private void BuildRunScopedNodes()
    {
        _player = new PlayerController
        {
            Name = nameof(PlayerController),
            Telemetry = _run.Telemetry,
        };
        _player.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 6f } });
        AddChild(_player);

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
        _minimap = new Minimap { Name = nameof(Minimap), Player = _player, Cleared = _clearedRooms };
        layer.AddChild(_minimap);

        _reverie = new UI.ReverieScreen { Name = nameof(UI.ReverieScreen), Player = _player };
        layer.AddChild(_reverie);

        _summary = new UI.RunSummaryScreen { Name = nameof(UI.RunSummaryScreen) };
        _summary.RestartRequested += StartNewRun;
        layer.AddChild(_summary);

        AddChild(layer);

        _content = new RoomContent
        {
            Name = nameof(RoomContent),
            Player = _player,
            Reverie = _reverie,
            ZIndex = 5,
        };
        AddChild(_content);

        StartNewRunState();
    }

    /// <summary>
    /// Seed the run's starting loadout. Only on a genuinely new run — a floor transition
    /// restores what the player is already carrying instead.
    /// </summary>
    private void StartNewRunState()
    {
        if (_run.Weapons.Count > 0) return;

        foreach (string path in new[]
                 {
                     "res://data/weapons/webley_mk_vi.tres",
                     "res://data/weapons/cantrip_withering.tres",
                     "res://data/weapons/sacrificial_kris.tres",
                 })
        {
            var data = GD.Load<WeaponData>(path);
            if (data is null) continue;
            var cw = new Meta.CarriedWeapon { Data = data, Reserve = data.TotalReserveRounds };
            _run.Weapons.Add(cw);
        }

        // docs/04 §2.2, §2.3 — a fixed Heart, and three ley lines whose TYPES are rolled
        // per RUN. Rolling them per run is what stops an optimal layout being copied
        // between runs: the same set of sigils wants a different arrangement when the cross
        // is Blood/Salt than when it is Ash/Gate. Rolling them per FLOOR would invalidate
        // the build the player just spent a floor assembling.
        _run.Circle.RollLeyLines(Hash.Derive(_run.Seed, "ley_lines"));

        var heart = GD.Load<Sigils.SigilData>("res://data/sigils/heart_steady_pulse.tres");
        if (heart is not null) _run.Circle.SetHeart(heart);
    }

    // ================================================================ The run loop

    /// <summary>
    /// Build a floor and put the player at its entrance. Idempotent — it tears down any
    /// previous floor first, so it serves the first floor, every descent, and a restart.
    /// </summary>
    private void BeginFloor()
    {
        TearDownFloor();

        GenerateFloor();
        BuildGeometry();
        BuildManagers();
        RewirePlayer();

        _content.FloorIndex = _run.FloorIndex;
        _content.Pickups = _pickups;
        _minimap.Floor = _floor;
        _minimap.Enemies = _enemies;

        PlacedRoom entrance = _floor.FindRole(RoomRole.Entrance)!;
        _player.GlobalPosition = _geometry.RoomAnchorWorld(entrance);
        _player.RestoreFrom(_run);
        if (_autorun) AssertRestored();
        _player.OnFloorBegan();
        _camera.ResetSmoothing();

        EnterRoom(entrance.NodeId);

        GD.Print($"[FloorRunner] floor {_run.FloorIndex}/{_run.FinalFloor} — " +
                 $"{_floor.Rooms.Count} rooms, flow '{_floor.FlowId}', " +
                 $"{_geometry.PunchedDoors} flush doors + {_geometry.Corridors} corridors, " +
                 $"{_geometry.Doors.Count} sealable openings.\n" +
                 "WASD move · LMB fire · SPACE dash · R recite · RMB banish · " +
                 "E interact · TAB Reverie · M map · F3 overlay");
    }

    /// <summary>
    /// Free everything floor-scoped.
    ///
    /// Explicit rather than "free all children": the player, the camera, the HUD, the
    /// Reverie and the summary screen all have to survive, and a blanket sweep would take
    /// the player's Circle with it. Nulling the manager references matters too — a freed
    /// node in C# is still a live object reference, and calling into one is a hard crash
    /// rather than a null check.
    /// </summary>
    private void TearDownFloor()
    {
        _enemies?.ClearAll();

        Retire(_enemies);
        Retire(_enemyBullets);
        Retire(_playerBullets);
        Retire(_pickups);
        Retire(_floorTiles);
        Retire(_wallBody);
        Retire(_overlay);

        foreach (StaticBody2D body in _doorSeals.Values) Retire(body);
        _doorSeals.Clear();

        _enemies = null!;
        _enemyBullets = null!;
        _playerBullets = null!;
        _pickups = null!;

        _clearedRooms.Clear();
        _content.ResetForFloor();

        _encounterActive = false;
        _pendingSealRoom = -1;
        _currentRoom = -1;
        _roomsCleared = 0;
        _boss = null;
        _bossRoom = -1;
        _hud.Boss = null;
        if (_reverie.IsOpen) _reverie.Close();
    }

    /// <summary>
    /// Take a node out of the tree AND queue it for deletion.
    ///
    /// `QueueFree` alone defers until the end of the frame, so the old floor's nodes are
    /// still children while the new floor's are being added — and Godot silently renames a
    /// colliding sibling. That is survivable for the nodes held by reference and not for
    /// the ones looked up by name: two DebugOverlays would draw over each other for a
    /// frame, and `GetNodeOrNull("DebugOverlay")` would then find whichever won the race.
    /// Removing first frees the name immediately.
    /// </summary>
    private void Retire(Node? node)
    {
        if (node is null || !IsInstanceValid(node)) return;
        if (node.GetParent() is not null) node.GetParent().RemoveChild(node);
        node.QueueFree();
    }

    /// <summary>Point the surviving player at this floor's freshly built managers.</summary>
    private void RewirePlayer()
    {
        _player.EnemyBullets = _enemyBullets;
        _player.PlayerBullets = _playerBullets;
        _player.Enemies = _enemies;
        _player.Pickups = _pickups;
        _player.Telemetry = _run.Telemetry;

        _overlay = new Debug.DebugOverlay
        {
            Name = nameof(Debug.DebugOverlay),
            BulletManagerPath = _enemyBullets.GetPath(),
            PlayerBulletManagerPath = _playerBullets.GetPath(),
            PlayerPath = _player.GetPath(),
        };
        AddChild(_overlay);
    }

    private Debug.DebugOverlay? _overlay;
    private UI.RunSummaryScreen _summary = null!;

    /// <summary>
    /// The boss is dead and the floor is over.
    ///
    /// This is the path that did not exist. Killing the boss dropped its loot, cleared the
    /// room, and left the player standing in an empty arena with nothing to do and no way
    /// to finish — so docs/11's "a complete, replayable, winnable Floor 1" was true of
    /// every word except the last two.
    /// </summary>
    private void CompleteFloor()
    {
        _player.SaveTo(_run);
        _run.RoomsCleared += _roomsCleared;
        _run.Duration = _run.Telemetry.SessionDuration;
        _hitStop.Clear();

        if (_run.IsFinalFloor)
        {
            _run.FloorsCleared++;
            _run.Outcome = RunOutcome.Won;
            EndRun();
            return;
        }

        // Snapshot what SHOULD survive the stair, so the autorun can assert it did.
        _carriedGold = _run.Gold;
        _carriedCells = _run.Circle.UsedCells;
        _carriedAscensions = _run.AscensionCount;

        _run.AdvanceFloor();
        GD.Print($"[FloorRunner] the stair goes down. Floor {_run.FloorIndex}. " +
                 $"Carrying {_run.Gold} gold, {_run.Keys} keys, " +
                 $"{_run.Circle.UsedCells} cells of Circle, {_run.Corruption:0.##} Corruption.");
        BeginFloor();
    }

    /// <summary>
    /// Death. Its absence is what turned a lost run into an apparent freeze: with no
    /// handler the scene simply kept ticking a corpse, and nothing ever restored the
    /// time scale or gave the player a way out.
    /// </summary>
    private void OnDeath()
    {
        _player.SaveTo(_run);
        _run.RoomsCleared += _roomsCleared;
        _run.Duration = _run.Telemetry.SessionDuration;
        _run.Outcome = RunOutcome.Dead;

        // Clear the time scale explicitly. Relying on the per-frame Apply() to release it
        // is what failed here in the first place.
        _hitStop.Clear();

        EndRun();
    }

    /// <summary>
    /// End the run, either way, and show the summary.
    ///
    /// Telemetry is written on BOTH outcomes, and that is a correctness fix rather than a
    /// convenience. It used to be written only from the death handler, so every M1 metric
    /// the project has ever recorded was conditioned on the run having failed — a tester
    /// who finished the floor contributed nothing at all, and the sample was silently
    /// biased toward exactly the runs where the Sanity economy went worst.
    /// </summary>
    private void EndRun()
    {
        GD.Print("--------------------------------------------------------");
        GD.Print(_run.Outcome == RunOutcome.Won
            ? $"[FloorRunner] RUN COMPLETE — {_run.FloorsCleared} floor(s), {_run.RoomsCleared} rooms."
            : $"[FloorRunner] DEAD on floor {_run.FloorIndex} after {_run.RoomsCleared} rooms.");
        // The autorun does not dodge, aim, reload or Banish, so every Sanity metric below
        // reads zero. Said out loud, because a [FAIL] in a passing gate's output is how
        // people learn to stop reading output.
        if (_autorun)
            GD.Print("[autorun] the metrics below are MEANINGLESS in this mode — nothing " +
                     "spent Sanity. Structure only.");

        GD.Print(_run.Telemetry.Summary());
        _run.Telemetry.WriteCsv(outcome: _run.Outcome.ToString());

        // Stop the floor simulating behind the summary. The player is dead or the boss is,
        // and either way a room that keeps ticking can still spawn, shoot and kill.
        _enemies?.ClearAll();
        _enemyBullets?.Clear();
        _playerBullets?.Clear();

        _summary.Show(_run);
        if (_autorun) FinishAutorun();
    }

    /// <summary>
    /// The autorun's verdict. Asserts the properties a run must have and exits non-zero if
    /// any of them fail, so this is CI-consumable exactly like the other gates.
    /// </summary>
    private void FinishAutorun()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            if (ok) GD.Print($" [ok]   {what}");
            else { GD.PrintErr($" [FAIL] {what}"); failures++; }
        }

        GD.Print("================================================================");
        GD.Print(" AUTORUN");
        GD.Print("================================================================");

        Check(_run.Outcome == RunOutcome.Won,
              $"the run was won by killing the boss (outcome {_run.Outcome})");
        Check(_run.FloorsCleared == _run.FinalFloor,
              $"every floor was cleared ({_run.FloorsCleared}/{_run.FinalFloor})");
        Check(_run.RoomsCleared > 5, $"rooms were actually fought ({_run.RoomsCleared})");
        Check(_run.Telemetry.TotalRooms > 0,
              $"telemetry recorded the run ({_run.Telemetry.TotalRooms} room records)");
        Check(_restoreFailures == 0,
              $"the run survived every floor transition and room re-entry " +
              $"({_restoreFailures} discrepancies)");

        // The whole reason for a RunState. If any of this resets at a floor boundary the
        // player silently loses their run, and nothing else in the project would notice.
        if (_run.FinalFloor > 1)
        {
            Check(_carriedGold >= 0 && _run.Gold >= _carriedGold,
                  $"gold carried down the stair ({_carriedGold} -> {_run.Gold})");
            Check(_run.Circle.UsedCells >= _carriedCells,
                  $"the Circle carried down the stair ({_carriedCells} -> {_run.Circle.UsedCells} cells)");
            Check(_run.AscensionCount >= _carriedAscensions,
                  $"the Ascension count carried ({_carriedAscensions} -> {_run.AscensionCount})");
        }

        GD.Print("================================================================");
        GD.Print(failures == 0 ? " AUTORUN: PASS" : $" AUTORUN: FAIL ({failures})");
        GD.Print("================================================================");

        // `--autorun --screenshot=...` captures the summary screen instead of quitting.
        // The summary pauses the tree, which stops this node ticking and therefore stops
        // the capture ever arriving — same trap as the Reverie demo. Release the pause and
        // schedule the capture; the summary is a Control and keeps drawing regardless.
        if (_screenshotPath.Length > 0)
        {
            GetTree().Paused = false;
            _roomDemo = "captured";      // take the plain capture branch, not the fire test
            _screenshotAfter = _frameCount + 4;
            HideOverlayForCapture();
            return;
        }

        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private int _carriedGold = -1;
    private int _carriedCells;
    private int _carriedAscensions;

    /// <summary>
    /// Check the restore the moment it happens, rather than only at the end of the run.
    ///
    /// These are the parts of <see cref="PlayerController.RestoreFrom"/> that fail silently
    /// and cost the player a run: hearts and inscriptions simply not arriving, and — the
    /// subtle one — Sanity being clamped against a stale maximum. Deriving Max from the
    /// Circle AFTER placing Current inside it deletes Sanity at every floor boundary, by an
    /// amount that looks exactly like ordinary spending.
    /// </summary>
    private void AssertRestored()
    {
        void Expect(bool ok, string what)
        {
            if (!ok) { GD.PrintErr($" [FAIL] floor {_run.FloorIndex} restore: {what}"); _restoreFailures++; }
        }

        Expect(Mathf.IsEqualApprox(_player.Hearts, Mathf.Min(_run.Hearts, _run.MaxHearts)),
               $"hearts {_player.Hearts} != carried {_run.Hearts}");
        Expect(Mathf.IsEqualApprox(_player.MaxHearts, _run.MaxHearts),
               $"max hearts {_player.MaxHearts} != carried {_run.MaxHearts}");
        Expect(_player.Gold == _run.Gold, $"gold {_player.Gold} != carried {_run.Gold}");
        Expect(_player.Keys == _run.Keys, $"keys {_player.Keys} != carried {_run.Keys}");
        Expect(_player.Weapons.Count == _run.Weapons.Count,
               $"{_player.Weapons.Count} weapons != {_run.Weapons.Count} carried");
        Expect(ReferenceEquals(_player.Circle, _run.Circle), "the Circle was rebuilt rather than adopted");

        int carriedInscriptions = 0;
        foreach (Meta.CarriedWeapon cw in _run.Weapons) carriedInscriptions += cw.Inscriptions.Count;
        int liveInscriptions = 0;
        foreach (Weapon w in _player.Weapons.Weapons) liveInscriptions += w.Inscriptions.Count;
        Expect(liveInscriptions == carriedInscriptions,
               $"{liveInscriptions} inscriptions != {carriedInscriptions} carried");

        // The clamp. Sanity may be reduced by a smaller Max, but must never exceed it and
        // must never be silently zeroed.
        Expect(_player.Sanity.Current <= _player.Sanity.Max + 0.01f,
               $"Sanity {_player.Sanity.Current} exceeds Max {_player.Sanity.Max}");
        Expect(_player.Sanity.Current > 0f, "Sanity restored to zero");
    }

    private int _restoreFailures;

    /// <summary>
    /// While a room is sealed, nothing that belongs to the fight may be outside it.
    ///
    /// This is the invariant behind "a room is a room". Door seals are StaticBody2D nodes, so
    /// they only ever stopped the PLAYER — bullets and enemies both simulate their own
    /// movement and could not see them, and enemies walked out of contested rooms through
    /// doors the player could not follow them through. Reported from play, and nothing here
    /// was watching for it: the autorun kills a room in a couple of seconds, which is rarely
    /// long enough for anything to wander out.
    ///
    /// Checked as a POSITION, in the same spirit as the wall-collision gate — cheap, exact,
    /// and it cannot be satisfied by the door logic merely being self-consistent.
    /// </summary>
    private void AuditSealedRoom()
    {
        if (!_encounterActive || _doorSeals.Count == 0) return;

        PlacedRoom? room = FindRoom(_currentRoom);
        if (room is null) return;

        // Generous: the wall ring plus a tile, so a body resting against the inside of a
        // wall is not reported as having escaped through it.
        Rect2 bounds = _geometry.RoomRectWorld(room).Grow(FloorGeometry.Tile);

        foreach (Enemy e in _enemies.Enemies)
        {
            if (!e.Alive || bounds.HasPoint(e.Position)) continue;

            GD.PrintErr($" [FAIL] {e.Data.DisplayName} left sealed room {room.Template.Id} " +
                        $"(at {e.Position}, room {bounds})");
            _restoreFailures++;
            return;   // one report per run is enough to fail the gate
        }
    }

    /// <summary>Discard the run and start another from the same seed.</summary>
    private void StartNewRun()
    {
        _run = GameRoot.Instance.StartNewRun();
        _player.Telemetry = _run.Telemetry;
        StartNewRunState();
        BeginFloor();
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
        if (_autorun) AuditSealedRoom();

        _enemies.PlayerPosition = _player.GlobalPosition;
        _enemies.PlayerVelocity = _player.Velocity;
        _enemies.PlayerAscended = _player.Ascension.IsAscended;
        _enemies.HallucinationRatio = _player.Sanity.HallucinationRatio;
        _player.Sanity.InCombat = _encounterActive && _enemies.AliveCount > 0;

        TickBoss(dt);
        if (_encounterActive) _director.Tick(dt, _player.GlobalPosition);

        _run.Telemetry.Tick(dt, _player.Sanity);
        _camera.Offset = _player.ShakeOffset(_rng);

        // A room is cleared when every WAVE has spawned and nothing is left — not the first
        // time the floor happens to be empty. Checking AliveCount alone would clear the room
        // in the gap between wave one dying and wave two's telegraph resolving.
        //
        // A boss room is cleared when the BOSS dies, for the same class of reason: AliveCount
        // counts enemies and the boss is not one, so the fight would end the moment its
        // phase-2 adds were killed.
        bool enemiesDone = _director.Active ? _director.Finished : _enemies.AliveCount == 0;
        if (_encounterActive && enemiesDone && _boss is null) ClearRoom();
        if (_player.IsDead) OnDeath();

        HandleReverie();
        TickAutorun();
        _minimap.Revealed = _content.RevealFloor;
        _minimap.ShowEnemies = _encounterActive;

        // Corruption 10. Polled rather than set on a threshold crossing, because Corruption
        // can be reduced later (docs/02 §7.3) and a one-way latch would leave the floor gold
        // after the player had paid to come back from it.
        bool yellow = CorruptionTiers.YellowSign(_player.Corruption);
        if (yellow != _floorTiles.YellowSign)
        {
            _floorTiles.YellowSign = yellow;
            _floorTiles.QueueRedraw();
            if (yellow) GD.Print("[Corruption] THE YELLOW SIGN. The floor has noticed.");
        }

        QueueRedraw();
        HandleDebugKeys();
        TickScreenshot();
    }

    /// <summary>
    /// docs/04 §7 — Reverie opens on Tab, pauses the game, and is unavailable while doors
    /// are sealed.
    ///
    /// The combat gate is the load-bearing half. A player who can rearrange their Circle
    /// mid-fight is making the build decision with the enemy composition and their current
    /// Sanity already on screen, which is a strictly better decision than the one the
    /// system is balanced around — and it would also let them use the pause to read a
    /// bullet pattern.
    /// </summary>
    private void HandleReverie()
    {
        _reverie.CanOpen = !_encounterActive && _doorSeals.Count == 0;

        if (!Input.IsActionJustPressed("reverie")) return;

        if (!_reverie.IsOpen && !_reverie.CanOpen)
        {
            GD.Print("[Reverie] not while the doors are sealed.");
            return;
        }
        _reverie.Toggle();
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

        // Only seal while the player is genuinely INSIDE.
        //
        // TrackRoom keeps the last room while the player stands in a corridor, so a player
        // who steps into the room and then backs out down the corridor still counts as
        // being in it — and the seal would then close behind them, locking them OUT of a
        // room whose encounter is running. That is a softlock, not an inconvenience: the
        // fight cannot progress because nothing can reach anything, and the doors only open
        // when the fight ends.
        if (!_geometry.RoomInteriorWorld(room).HasPoint(_player.GlobalPosition)) return;

        foreach (Doorway d in _geometry.Doors)
        {
            if (d.Room != room.NodeId) continue;
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
            if (!_doorSeals.TryGetValue(d.Index, out StaticBody2D? body)) continue;
            if (!d.WorldRect.Grow(4f).HasPoint(_player.GlobalPosition)) continue;

            GD.PrintErr("[FloorRunner] player found inside a sealed door — opening it. " +
                        "This should be unreachable; the seal ordering has a gap.");
            body.QueueFree();
            _doorSeals.Remove(d.Index);

            // The mask has to be opened too, or the door is visually and physically open for
            // the player and still a wall for everything else. This path bypasses SealDoors,
            // which is exactly the kind of second exit that leaves state half-updated.
            _walls.SetSolidWorldRect(d.WorldRect, false);
            _enemies.RefreshPathing();
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

        _player.OnRoomBegan();
        _hitsAtRoomStart = _player.HitsTaken;

        // Furniture is placed on entry, cleared and re-placed on every room change. The
        // room's own seed makes that idempotent: walking back into a shop finds the same
        // stock, at the same prices, with whatever was bought still gone.
        _content.EnterRoom(room, _geometry.RoomRectWorld(room).Grow(-40f),
                           _geometry.RoomAnchorWorld(room),
                           Hash.Derive(GameRoot.Instance.RunSeed, "room_content", nodeId));

        if (_clearedRooms.Contains(nodeId)) return;
        if (!IsCombatRole(room.Role)) { _clearedRooms.Add(nodeId); OnNonCombatRoom(room); return; }

        StartEncounter(room);
    }

    private static bool IsCombatRole(RoomRole r) =>
        r is RoomRole.CombatEasy or RoomRole.CombatMed or RoomRole.CombatHard or RoomRole.Hub
          or RoomRole.Boss;

    private void OnNonCombatRoom(PlacedRoom room)
    {
        if (room.Role is RoomRole.Reward or RoomRole.Shop or RoomRole.Shrine or RoomRole.Secret)
            GD.Print($"[{room.Role}] {room.Template.Id} — {_content.Items.Count} things to interact with.");
    }

    /// <summary>
    /// docs/06 §6.1 Dread Budget. Scales with rooms cleared and the room's authored
    /// ThreatCapacity, with the >=35% fodder floor that keeps the Sanity economy solvent.
    /// </summary>
    /// <summary>
    /// docs/05 §7 — The Thing on the Doorstep.
    ///
    /// The boss room does not take a Dread budget. Everything about the encounter is
    /// authored in the boss's own data, and letting the room budget also spawn a fistful of
    /// acolytes on top would make the phase-2 adds — the fight's only add pressure, timed
    /// deliberately — indistinguishable from background noise.
    /// </summary>
    private void StartBossFight(PlacedRoom room)
    {
        if (_bossData is null)
        {
            GD.PrintErr("[FloorRunner] boss room reached with no boss data; treating it as cleared.");
            _clearedRooms.Add(room.NodeId);
            return;
        }

        Vector2 centre = _geometry.RoomAnchorWorld(room);
        _boss = new Boss(_bossData, centre + new Vector2(0f, -140f), _enemyBullets,
                         Hash.Derive(GameRoot.Instance.RunSeed, "boss", room.NodeId));
        _boss.SetWalls(_enemies.Walls);
        _enemies.Boss = _boss;

        // The boss's own adds obey the Corruption thresholds like anything else that spawns.
        _enemies.SpawnAwakened = CorruptionTiers.EnemiesAwakened(_player.Corruption);

        _bossRoom = room.NodeId;
        _hud.Boss = _boss;
        _encounterActive = true;
        _pendingSealRoom = room.NodeId;
        _run.Telemetry.BeginRoom(_roomsCleared + 1, _player.Sanity);

        GD.Print($"[BOSS] {_bossData.DisplayName} — {_bossData.MaxHealth:F0} HP, " +
                 $"phases at {_bossData.Phase2At:P0} / {_bossData.Phase3At:P0}");
    }

    private BossData? _bossData;
    private Boss? _boss;
    private int _bossRoom = -1;

    /// <summary>
    /// Advance the boss and settle everything it owes: adds, the grab, and its death.
    ///
    /// Kept out of <see cref="EnemyManager"/> deliberately. The manager knows about bodies
    /// and bullets; the adds need the floor's enemy roster and its geometry, the grab needs
    /// the player's Sanity, and the death needs the room's drop tables — all of which are
    /// this class's business and none of which belong in the thing that has to stay cheap
    /// enough to tick sixty enemies.
    /// </summary>
    private void TickBoss(float dt)
    {
        if (_boss is null) return;

        _boss.HallucinationRatio = _player.Sanity.HallucinationRatio;
        _boss.Tick(dt, _player.GlobalPosition, _player.Velocity);

        int phase = _boss.ConsumePhaseChange();
        if (phase > 0) OnBossPhase(phase);
        if (_boss.PendingAdds > 0) SpawnBossAdds(_boss.PendingAdds);

        // The grab (docs/05 §7). It costs SANITY, not health — which at low Sanity means it
        // does not hurt, it disarms: no reload, no Banish, and one more hit from Ascension.
        if (_boss.GrabConnectedThisTick && !_player.IsInvulnerable)
        {
            _player.SufferGrab(_bossData!.GrabSanityCost);
            GD.Print($"[BOSS] the passenger got a hold of you — −{_bossData.GrabSanityCost:F0} Sanity.");
        }

        // Either the manager reported the kill, or the boss is simply dead.
        //
        // The latch alone was not enough: it is only set by player-bullet hit resolution,
        // so a boss killed by anything else — melee is routed separately, and later a sigil
        // proc or a damage-over-time will be too — died with its state set to Dead and the
        // floor simply never ended. The autorun harness found this on its first run by
        // killing the boss directly and then standing in the arena forever.
        if (_enemies.ConsumeBossKilled() || !_boss.Alive) OnBossDefeated();
    }

    private void OnBossPhase(int phase)
    {
        // Clear the screen on a phase change. Not a mercy: the transition is invulnerable
        // and stationary, so bullets left over from the previous phase would be hitting a
        // player who has no target to punish and no reason to be there.
        _enemyBullets.Clear();
        _player.AddTrauma(0.55f);

        string line = phase switch
        {
            2 => "\"—and I told her, I told her the WELL was—\" The sentence does not finish. "
                 + "The body finds a new arrangement.",
            _ => "It leaves the body where it falls and comes for the only other one in the room.",
        };
        GD.Print($"[BOSS] phase {phase}. {line}");
    }

    private void SpawnBossAdds(int count)
    {
        if (_roster.Count == 0 || _boss is null) return;

        for (int i = 0; i < count; i++)
        {
            EnemyData pick = PickEnemy(needFodder: true);
            float a = _rng.NextAngle();
            Vector2 at = _boss.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 110f;
            _enemies.Spawn(pick, at);
        }
    }

    private void OnBossDefeated()
    {
        BossData data = _bossData!;
        GD.Print($"[BOSS] {data.DisplayName} is dead. What is left is a man, and he is grateful.");

        _enemies.Boss = null;
        _hud.Boss = null;
        _enemyBullets.Clear();
        _player.AddTrauma(0.8f);

        Vector2 at = _boss?.Position ?? _player.GlobalPosition;
        _boss = null;

        // Guaranteed drop (docs/04 §6): a boss always yields a sigil, plus gold and a key.
        var rng = Hash.Derive(GameRoot.Instance.RunSeed, "boss_drop", _bossRoom);
        Sigils.SigilData? s = Sigils.SigilPool.Draw(1, _player.Corruption, rng, null);
        if (s is not null && _player.Circle.AddToReliquary(s))
        {
            if (_reverie is not null) _reverie.PendingOffer = s;
            GD.Print($"[BOSS] dropped {s.DisplayName} [{s.Tier}]. TAB to inscribe it.");
        }

        _player.AddGold(data.GoldReward);
        _player.AddKeys(data.KeyReward);
        _pickups.Spawn(Items.PickupKind.Heart, at, 1f, rng);
    }

    /// <summary>
    /// docs/06 §6 — hand the room to the <see cref="EncounterDirector"/>.
    ///
    /// The budget is now the formula the spec actually states, including the two terms that
    /// were missing entirely: Corruption scales it continuously at 6% per point, and
    /// playerPowerMult answers the build the player has assembled. Before this a player who
    /// filled their Circle, kitted a weapon and bought hearts met exactly the same rooms as
    /// one who had done none of it.
    /// </summary>
    private void StartEncounter(PlacedRoom room)
    {
        if (room.Role == RoomRole.Boss) { StartBossFight(room); return; }

        // Read once, before anything spawns, so a Banish mid-room cannot change the
        // composition of the fight the player is already in.
        _enemies.SpawnAwakened = CorruptionTiers.EnemiesAwakened(_player.Corruption);

        float power = DreadBudget.PlayerPower(
            DreadBudget.TierWeightedCells(_player.Circle),
            DreadBudget.BestWeaponTier(_player.Weapons),
            DreadBudget.TotalInscriptions(_player.Weapons),
            _player.MaxHearts);

        float budget = DreadBudget.For(_run.FloorIndex, _roomsCleared, room.Template, room.Role,
                                       _player.Corruption, power);

        if (!_director.Begin(room, budget, _enemies.Walls))
        {
            _clearedRooms.Add(room.NodeId);
            return;
        }

        _encounterActive = true;
        // Arm the seal; UpdatePendingSeal closes it once the player is clear of the door.
        _pendingSealRoom = room.NodeId;
        _run.Telemetry.BeginRoom(_roomsCleared + 1, _player.Sanity);

        GD.Print($"[Room {room.Template.Id}] {room.Role}  budget {budget:F0}  " +
                 $"power x{power:F2}  {_director.WaveCount} wave(s)  " +
                 $"first {_enemies.AliveCount}  ceiling {_player.Sanity.LucidCeiling:F0}" +
                 (_enemies.SpawnAwakened ? "  AWAKENED" : ""));
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
        _director.Reset();
        _pendingSealRoom = -1;      // never let an armed seal fire after the fight is over
        _clearedRooms.Add(_currentRoom);
        _roomsCleared++;

        PlacedRoom? room = FindRoom(_currentRoom);
        if (room is not null) SealDoors(room, false);

        float headroom = _player.Sanity.LucidCeiling - _player.Sanity.Current;
        _run.Drops.RollRoomClear(_pickups, _player.GlobalPosition, _rng, _run.FloorIndex,
                             _player.Keys, _player.TotalReserveFraction(), headroom);

        // The Ledger of Names (docs/04 §5.5) — gold for a room cleared without being hit.
        // Measured against the count at room entry rather than a flag, so the Elder Sign's
        // negated hit correctly still counts as a clean room and armour absorbing one does not.
        int clean = _player.Circle.Effects.GoldPerCleanRoom;
        if (clean > 0 && _player.HitsTaken == _hitsAtRoomStart)
        {
            _player.AddGold(clean);
            GD.Print($"[Ledger of Names] clean clear — +{clean} gold.");
        }

        // A chest for a hard-won room. docs/08 §4 puts chests behind combat; a hard room
        // that pays the same as an easy one makes the route choice a pure risk with no
        // corresponding reward.
        if (room is not null && room.Role is RoomRole.CombatHard or RoomRole.CombatMed)
        {
            bool locked = room.Role == RoomRole.CombatHard;
            _content.AddChest(_player.GlobalPosition + new Vector2(0f, -40f),
                              tier: locked ? 3 : 1, keyCost: locked ? 1 : 0, _rng);
        }

        Weapon w = _player.Weapons.Active;
        _run.Telemetry.EndRoom(_player.Sanity, _player.Weapons.ReloadsAttempted,
                           _player.Weapons.ReloadsDenied, w.PerfectRecitations, w.FailedRecitations);

        _player.Sanity.OnRoomCleared();

        GD.Print($"[cleared] {_roomsCleared} rooms  sanity {_player.Sanity.Current:F0}/" +
                 $"{_player.Sanity.Max:F0}  ceiling {_player.Sanity.LucidCeiling:F0}  band {_player.Sanity.Band}");

        // Clearing the boss room ends the FLOOR, not just the room. Hooked here rather than
        // in OnBossDefeated so the boss's own room still gets its drops, its telemetry
        // record and its doors unsealed first — a floor that ends on the death frame skips
        // all three, and the missing telemetry record would be the boss fight itself.
        if (room is not null && room.Role == RoomRole.Boss) CompleteFloor();
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
            if (d.Room != room.NodeId) continue;

            if (sealed_)
            {
                if (_doorSeals.ContainsKey(d.Index)) continue;
                var body = new StaticBody2D { Position = d.WorldRect.Position + d.WorldRect.Size * 0.5f };
                body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = d.WorldRect.Size } });
                AddChild(body);
                _doorSeals[d.Index] = body;

                // The StaticBody2D stops the PLAYER, because the player is the only thing in
                // the game that uses Godot's physics. Writing the seal into the mask is what
                // stops everything else — bullets and enemies both simulate their own
                // movement and see only what is in here.
                _walls.SetSolidWorldRect(d.WorldRect, true);
            }
            else if (_doorSeals.TryGetValue(d.Index, out StaticBody2D? body))
            {
                body.QueueFree();
                _doorSeals.Remove(d.Index);
                _walls.SetSolidWorldRect(d.WorldRect, false);
            }
        }

        // The flow field caches which cells are blocked, so it has to be told. Cheap enough
        // to redo wholesale — this runs twice per room, not per tick.
        _enemies.RefreshPathing();

        // And free anything the closing door just entombed. A body that starts a tick
        // overlapping solid ground can never move out of it.
        if (sealed_) _enemies.EvictFromSolid(_geometry.RoomAnchorWorld(room));
    }

    // ---------------------------------------------------------------- Draw

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
    private string _roomDemo = "";
    private bool _reverieDemo;
    private float _startingCorruption;
    private int _frameCount;

    private void ParseScreenshotArgs()
    {
        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (arg.StartsWith("--screenshot=")) _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--screenshot-after="))
            {
                _screenshotAfter = int.TryParse(arg["--screenshot-after=".Length..], out int n) ? n : 40;
                _screenshotAfterExplicit = true;
            }
            else if (arg == "--melee-demo") _meleeDemo = true;
            else if (arg == "--combat-demo") _combatDemo = true;
            else if (arg.StartsWith("--room-demo=")) _roomDemo = arg["--room-demo=".Length..];
            else if (arg == "--reverie-demo") _reverieDemo = true;
            else if (arg == "--autorun") _autorun = true;
            // Start the run already Corrupted, so the thresholds can be seen without
            // Banishing forty times to reach them.
            else if (arg.StartsWith("--corruption=") &&
                     float.TryParse(arg["--corruption=".Length..], out float c))
                _startingCorruption = c;
        }

        // An autorun capture is of the SUMMARY, which arrives whenever the run happens to
        // finish. The default 40-frame trigger would fire in the middle of the first room
        // and quit, which is exactly what it did the first time — the file was written, the
        // harness reported success, and the picture was of the wrong thing entirely.
        // ...unless a frame was named explicitly, which is how a mid-run moment gets captured
        // at all — a wave telegraph only exists while the autorun is fighting.
        if (_autorun && _screenshotPath.Length > 0 && !_screenshotAfterExplicit)
            _screenshotAfter = int.MaxValue;
    }

    private bool _screenshotAfterExplicit;

    private void TickScreenshot()
    {
        if (_screenshotPath.Length == 0) return;
        _frameCount++;

        // Set up EARLY and hold fire for a long window. Twice now a 12-frame window has
        // produced "one shot fired" and been mistaken for a fault — at 4.5 rounds/sec that
        // is simply the correct number. The window must span several fire cycles or the
        // test cannot distinguish a broken gun from a fast one.
        // Room-content and Reverie captures. Both are cases the headless gates cannot see
        // at all: a shop with no stock drawn and an inventory screen that renders nothing
        // both pass every assertion in the project while being completely broken on screen.
        if (_frameCount == 20 && _roomDemo.Length > 0) TeleportToRole(_roomDemo);
        if (_frameCount == 24 && _reverieDemo) OpenReverieWithSample();

        if (_frameCount == 30 && _roomDemo.Length == 0 && !_reverieDemo)
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
                    _player.GlobalPosition = _geometry.RoomAnchorWorld(r);
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

        // Hide the overlay a couple of frames EARLY. GetImage reads the framebuffer that was
        // rendered before this tick ran, so hiding it on the capture frame itself only takes
        // effect on the frame after — the first attempt at this captured the overlay covering
        // two thirds of the shot.
        if (_frameCount == _screenshotAfter - 2) HideOverlayForCapture();

        if (_frameCount != _screenshotAfter) return;

        if (_roomDemo.Length > 0 || _reverieDemo || _autorun)
        {
            Image shot = GetViewport().GetTexture().GetImage();
            Error e = shot.SavePng(_screenshotPath);
            GD.Print($"[screenshot] {_screenshotPath} → {e}");
            GetTree().Quit(e == Error.Ok ? 0 : 1);
            return;
        }

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

    /// <summary>Drop the player into the first room of a given role, for a capture.</summary>
    private void TeleportToRole(string roleName)
    {
        if (!System.Enum.TryParse(roleName, ignoreCase: true, out RoomRole role))
        {
            GD.PrintErr($"[screenshot] unknown room role '{roleName}'");
            return;
        }

        PlacedRoom? room = _floor.FindRole(role);
        if (room is null) { GD.PrintErr($"[screenshot] this floor has no {role} room"); return; }

        // Enough gold and keys to see the prices being met rather than refused — a shop
        // rendered entirely in "cannot afford" is not a useful picture of a shop.
        _player.AddGold(600);
        _player.AddKeys(3);

        _player.GlobalPosition = _geometry.RoomAnchorWorld(room);
        EnterRoom(room.NodeId);

        // Stand ON something, so the capture shows the prompt as well as the furniture —
        // an untriggered prompt is precisely the part most likely to be silently broken.
        if (_content.Items.Count > 0) _player.GlobalPosition = _content.Items[0].Position + new Vector2(0f, 14f);

        // Hold the map open for combat captures. At 130px the minimap's marks are two
        // pixels across and a capture of them is unreadable, which defeats the point of
        // taking one.
        if (IsCombatRole(role)) Input.ActionPress("map");

        // The camera smooths toward the player, so a teleport leaves it a room behind for
        // most of a capture window. Snap it.
        _camera.ResetSmoothing();
        HideOverlayForCapture();

        GD.Print($"[screenshot] {role} — {room.Template.Id}, {_content.Items.Count} interactables");
        foreach (Interactable it in _content.Items) GD.Print($"    {it.Prompt()}");
    }

    /// <summary>The F3 overlay covers most of a 640x360 frame, which is fine when the thing
    /// being checked is a counter and useless when it is a room.</summary>
    private void HideOverlayForCapture()
    {
        // A CanvasLayer, NOT a CanvasItem — the first version of this tested for CanvasItem
        // and silently did nothing, which is exactly the sort of no-op the capture harness
        // exists to make visible.
        if (GetNodeOrNull(nameof(Debug.DebugOverlay)) is CanvasLayer overlay) overlay.Visible = false;
    }

    /// <summary>Fill a Circle and open Reverie, so a capture shows a populated grid rather
    /// than an empty one.</summary>
    private void OpenReverieWithSample()
    {
        foreach (string id in new[] { "bloodletters_nail", "salt_ward", "brine_knot", "elder_sign" })
        {
            Sigils.SigilData? s = Sigils.SigilPool.ById(id);
            if (s is not null) _player.Circle.AddToReliquary(s);
        }

        // Place two by hand next to the Heart so the capture shows synergy arcs and ley
        // occupancy, and leave two in the Reliquary so the tray is not empty either.
        Sigils.SigilData? nail = Sigils.SigilPool.ById("bloodletters_nail");
        Sigils.SigilData? ward = Sigils.SigilPool.ById("salt_ward");
        if (nail is not null) _player.Circle.Place(nail, new Vector2I(2, 3), 0, false, out _);
        if (ward is not null) _player.Circle.Place(ward, new Vector2I(3, 2), 1, false, out _);

        _player.OnSigilsChanged();
        HideOverlayForCapture();
        _reverie.CanOpen = true;
        _reverie.Open();

        // Opening Reverie pauses the tree, which stops THIS node ticking — so the capture
        // frame never arrives and the harness hangs until --quit-after. Release the pause
        // for the capture only. The screen still renders exactly as it does in play; it is
        // the surrounding simulation that is allowed to keep running for a few frames.
        GetTree().Paused = false;

        GD.Print($"[screenshot] reverie — {_player.Circle.Summary()}");
    }

    /// <summary>Engine.TimeScale is global state. Leaving a scene mid-hit-stop would
    /// otherwise strand the whole game at 0.05x.</summary>
    public override void _ExitTree() => _hitStop.Clear();

    public override void _Draw()
    {
        // Sealed doorways, so the player can see why they cannot leave.
        // Only the CURRENT room's doorways are drawn. Every opening now belongs to exactly
        // one room, so drawing all of them would paint both sides of every shared threshold
        // and every corridor mouth on the floor at once.
        foreach (Doorway d in _geometry.Doors)
        {
            if (d.Room != _currentRoom) continue;
            bool isSealed = _doorSeals.ContainsKey(d.Index);
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

            // AWAKENED (docs/02 §7.2). A ring, because the readability contract in docs/05
            // §1 means an enemy that behaves differently must LOOK different — an Awakened
            // acolyte has a second attack and more health, and a player who cannot tell it
            // apart from a normal one is being asked to learn a rhythm they cannot see the
            // cause of. Not a tint: tint is already the enemy's identity.
            if (e.Awakened)
            {
                DrawArc(e.Position, e.Data.BodyRadius + 3f, 0, Mathf.Tau, 20,
                        new Color("B0122A") with { A = 0.85f }, 1.5f);
            }

            if (_player.Sanity.WeakPointsVisible)
                DrawCircle(e.WeakPointPosition, e.WeakPointRadius, new Color(1f, 0.9f, 0.35f, 0.85f));

            if (e.State == EnemyState.Telegraph)
                DrawArc(e.Position, e.Data.BodyRadius + 4f + e.TelegraphProgress * 8f,
                        0, Mathf.Tau * e.TelegraphProgress, 20, new Color("FF5555"), 2f);

            float hp = e.Health / e.MaxHealth;
            if (hp < 1f)
                DrawRect(new Rect2(e.Position.X - 10, e.Position.Y - e.Data.BodyRadius - 7, 20 * hp, 2),
                         new Color("D64545"));
        }

        DrawWaveTelegraph();
        DrawBoss();

        if (_player.BanishPulse > 0f)
        {
            float t = 1f - _player.BanishPulse;
            DrawArc(_player.BanishOrigin, Tune.BanishRadius * (0.25f + 0.75f * t), 0, Mathf.Tau, 48,
                    new Color(0.55f, 0.9f, 0.85f, _player.BanishPulse * 0.9f), 3f);
        }
    }

    /// <summary>
    /// Incoming wave markers (docs/05 R4, docs/06 §6.2).
    ///
    /// R4 forbids a spawn into the play area without 0.6s of warning, and §6.2 wants the
    /// spawn points themselves visible. A marker is drawn at each pending point; one that
    /// falls outside the viewport is CLAMPED to the screen edge, which is R4's inbound-marker
    /// clause — a reinforcement the player cannot see coming is an ambush they had no way to
    /// read, and the trigger is their own kills, so it is always their doing.
    ///
    /// The ring closes as the telegraph runs, so the warning carries WHEN as well as where.
    /// </summary>
    private void DrawWaveTelegraph()
    {
        if (_director.PendingSpawns.Count == 0) return;

        float t = _director.TelegraphProgress;
        Vector2 view = _player.GlobalPosition;
        var viewHalf = new Vector2(300f, 166f);

        foreach (Vector2 at in _director.PendingSpawns)
        {
            Vector2 p = at;
            bool offScreen = Mathf.Abs(at.X - view.X) > viewHalf.X
                             || Mathf.Abs(at.Y - view.Y) > viewHalf.Y;
            if (offScreen)
            {
                p = new Vector2(
                    Mathf.Clamp(at.X, view.X - viewHalf.X, view.X + viewHalf.X),
                    Mathf.Clamp(at.Y, view.Y - viewHalf.Y, view.Y + viewHalf.Y));
            }

            var warn = new Color("FFE066");

            // A CLOSING SQUARE AND A CROSS, not a ring.
            //
            // The first version drew a red ring, which is what an Awakened enemy already
            // wears — a capture showed the two side by side and they were the same mark. So
            // the shapes are now disjoint: circles are bodies, an angular mark is a place
            // something is about to be. Warm yellow rather than red for the same reason, and
            // it does not collide with R1 either: that rule governs projectiles, and this is
            // ground marking.
            float half = 20f - 12f * t;
            var box = new Rect2(p - new Vector2(half, half), new Vector2(half * 2f, half * 2f));
            DrawRect(box, warn with { A = 0.25f + 0.45f * t }, filled: false, width: 1.5f);

            float arm = 5f + 3f * t;
            Color solid = warn with { A = 0.55f + 0.45f * t };
            DrawLine(p - new Vector2(arm, arm), p + new Vector2(arm, arm), solid, 1.5f);
            DrawLine(p - new Vector2(arm, -arm), p + new Vector2(arm, -arm), solid, 1.5f);

            // An off-screen marker gets a tick pointing the way, so it reads as "from there"
            // rather than "here".
            if (offScreen)
            {
                Vector2 dir = (at - p).Normalized();
                DrawLine(p, p + dir * 14f, solid, 2f);
            }
        }
    }

    /// <summary>
    /// The boss, and the two things the player has to be able to read at a glance: which
    /// phase it is in, and whether a grab is winding up.
    ///
    /// The grab telegraph is drawn as a filled cone rather than a ring. docs/05 R3 requires
    /// a readable wind-up, and a ring says "something is coming" while a cone says "it is
    /// coming THERE" — for an attack whose whole counter is a sideways step, the direction
    /// is the information.
    /// </summary>
    private void DrawBoss()
    {
        if (_boss is null || !_boss.Alive) return;

        Color body = _boss.HitFlash > 0f ? Colors.White : _boss.PhaseTint;
        float r = _boss.BodyRadius;

        // Phase 3 has no body: a shimmer with a hole in it, so it reads as a presence
        // rather than a creature.
        if (_boss.Phase == 3)
        {
            DrawCircle(_boss.Position, r * 1.9f, body with { A = 0.18f });
            DrawArc(_boss.Position, r, 0, Mathf.Tau, 28, body, 2.5f);
        }
        else
        {
            DrawCircle(_boss.Position, r, body);
        }

        if (_boss.Invulnerable)
        {
            float t = _boss.TransitionProgress;
            DrawArc(_boss.Position, r + 6f + t * 26f, 0, Mathf.Tau, 40,
                    new Color(1f, 1f, 1f, 1f - t), 2f);
        }

        if (_boss.State == BossState.Telegraph)
        {
            DrawArc(_boss.Position, r + 5f + _boss.TelegraphProgress * 10f,
                    0, Mathf.Tau * _boss.TelegraphProgress, 28, new Color("FF5555"), 2.5f);
        }

        if (_boss.State == BossState.GrabWindup)
        {
            Vector2 toPlayer = (_player.GlobalPosition - _boss.Position).Normalized();
            float half = Mathf.DegToRad(16f);
            float a = Mathf.Atan2(toPlayer.Y, toPlayer.X);
            float reach = _bossData!.GrabLungeSpeed * _bossData.GrabLungeSeconds;

            var pts = new Vector2[3];
            pts[0] = _boss.Position;
            pts[1] = _boss.Position + new Vector2(Mathf.Cos(a - half), Mathf.Sin(a - half)) * reach;
            pts[2] = _boss.Position + new Vector2(Mathf.Cos(a + half), Mathf.Sin(a + half)) * reach;
            DrawColoredPolygon(pts, new Color(0.85f, 0.25f, 0.35f, 0.22f));
        }

    }

    // ================================================================ Autorun
    //
    // A headless player. It walks the floor room by room, clearing each one instantly, and
    // finishes with the boss.
    //
    // It exists because the run loop cannot be tested any other way. Every part of it —
    // floor completion, carrying a Circle down a stair, the summary, starting again — only
    // happens after a boss dies, and no gate in this project has ever been able to reach a
    // boss. The floor smoke test runs 600 frames of a player standing still in the entrance.
    //
    // Deliberately NOT a simulation of play: it does not dodge, aim or spend Sanity, so it
    // proves nothing about balance and is not a substitute for a playtest. It proves the
    // STRUCTURE holds.

    private bool _autorun;
    private int _autorunTimer;
    private int _autorunRoomIndex;

    private void TickAutorun()
    {
        if (!_autorun) return;

        // A few frames between steps. Room entry arms a door seal, drops spawn and get
        // magnetised, and the boss needs a tick to register as a target — stepping every
        // frame would race all three.
        if (++_autorunTimer < 8) return;
        _autorunTimer = 0;

        // Kill whatever is in the room. The boss goes through TakeDamage rather than being
        // deleted, so the phase transitions, the drop and the floor completion all run
        // exactly as they would in play.
        if (_encounterActive)
        {
            foreach (Enemy e in _enemies.Enemies) if (e.Alive) e.TakeDamage(99999f);

            // The boss takes a CHUNK rather than a lethal hit, so the phase thresholds are
            // actually crossed and the invulnerable transitions actually run. One-shotting
            // it would test the death path and nothing else, and the phase machine is the
            // part with moving pieces.
            if (_boss is not null && !_boss.Invulnerable)
                _boss.TakeDamage(_boss.Data.MaxHealth * 0.11f);
            return;
        }

        // Then move on. Non-boss rooms first, so the floor is actually walked rather than
        // skipped straight to the end — the point is to exercise room content, drops and
        // telemetry on the way.
        PlacedRoom? next = null;
        foreach (PlacedRoom r in _floor.Rooms)
        {
            if (_clearedRooms.Contains(r.NodeId) || r.Role == RoomRole.Boss) continue;
            next = r;
            break;
        }
        next ??= _floor.FindRole(RoomRole.Boss);

        if (next is null) return;

        _player.GlobalPosition = _geometry.RoomAnchorWorld(next);
        EnterRoom(next.NodeId);
        _autorunRoomIndex++;

        // LEAVE AND COME BACK, and check the room still has its things.
        //
        // This is the shape of the bug it was written for: walk into a shop, see something
        // you cannot afford, go and earn the gold, come back to an empty stall. Re-entry
        // cleared the item list and then took the "already populated, nothing to do" branch,
        // so every revisited room in the game was empty — and no gate noticed, because the
        // autorun visits each room exactly once, which is the one access pattern that works.
        int stock = _content.Items.Count;
        if (stock > 0)
        {
            EnterRoom(next.NodeId);
            if (_content.Items.Count != stock)
            {
                GD.PrintErr($" [FAIL] {next.Template.Id}: re-entering the room changed its " +
                            $"contents ({stock} -> {_content.Items.Count})");
                _restoreFailures++;
            }
        }

        // Take whatever the room offers, and inscribe it. Without this the harness finishes
        // every run with an empty Circle, and "the Circle carried down the stair" is an
        // assertion about one locked Heart — which is exactly the kind of test that passes
        // whatever happens.
        if (_content.DebugTakeSomething())
        {
            while (_player.Circle.Reliquary.Count > 0)
                if (!_player.Circle.AutoPlace(_player.Circle.Reliquary[0])) break;
            _player.OnSigilsChanged();
        }
    }

    private void HandleDebugKeys()
    {
        if (Input.IsKeyPressed(Key.F5))
        {
            GD.Print(_run.Telemetry.Summary());
            _run.Telemetry.WriteCsv();
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

    /// <summary>
    /// Walls are drawn, not merely absent.
    ///
    /// Before authored interiors it did not matter: the only non-floor was outside the
    /// room, and unlit void reads correctly as "not here". A pillar in the middle of a
    /// room does not — it read as a hole in the floor rather than something to stand
    /// behind, and cover the player cannot identify at a glance is cover they will not use.
    /// Solid mass with a lit top edge, so a block reads as an object above the floor.
    /// </summary>
    private static readonly Color WallColour = new("2E333D");
    private static readonly Color WallEdge = new("3D4450");

    /// <summary>
    /// docs/02 §7.2 — the Yellow Sign at Corruption 10. The floor's palette turns sickly gold.
    ///
    /// The FLOOR only. Enemy projectiles stay in the cool half of the palette no matter what,
    /// because docs/10 §1.3 R1 is not negotiable: a warm bullet reads as the player's and
    /// will get walked into. Corruption is allowed to change how the world looks; it is not
    /// allowed to make incoming fire ambiguous.
    /// </summary>
    public bool YellowSign;

    private static readonly Color YellowFloor = new("2A2517");
    private static readonly Color YellowGrid = new("3A3320");
    private static readonly Color YellowWall = new("4A3F1E");
    private static readonly Color YellowEdge = new("6B5A28");

    public override void _Ready()
    {
        foreach (Rect2 r in Geometry.BuildFloorRects()) _rects.Add(r);
        foreach (Rect2 r in Geometry.BuildWallRects()) _walls.Add(r);
        QueueRedraw();
    }

    private readonly List<Rect2> _rects = new();
    private readonly List<Rect2> _walls = new();

    public override void _Draw()
    {
        Color floor = YellowSign ? YellowFloor : FloorColour;
        Color grid = YellowSign ? YellowGrid : GridColour;
        Color wall = YellowSign ? YellowWall : WallColour;
        Color edge = YellowSign ? YellowEdge : WallEdge;

        foreach (Rect2 r in _rects)
        {
            DrawRect(r, floor);
            DrawRect(r, grid, filled: false, width: 1f);
        }

        foreach (Rect2 r in _walls)
        {
            DrawRect(r, wall);
            DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 2f)), edge);
        }
    }
}
