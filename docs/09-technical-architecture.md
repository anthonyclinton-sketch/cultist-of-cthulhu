# 09 — Technical Architecture

**Godot 4.7-stable mono (pinned in `.godot-version`) · .NET 8 · C# 12 · Forward+ renderer (Compatibility fallback for Steam Deck if needed)**

---

## 1. The Governing Constraint

A bullet hell must sustain **600+ simultaneous enemy projectiles at a locked 144 Hz** on a Steam Deck-class GPU. This single requirement dictates most of the architecture below. If you build bullets as `Area2D` nodes, you will hit a wall at ~800 and spend a month rewriting it. **Build the bullet system correctly on day one.**

---

## 2. Project Structure

```
CultistOfCthulhu/
├── project.godot
├── CultistOfCthulhu.csproj
├── .editorconfig                         # enforce style; treat warnings as errors in CI
├── src/
│   ├── Core/
│   │   ├── GameRoot.cs                   # autoload: bootstraps services, owns the loop
│   │   ├── ServiceLocator.cs             # explicit, typed, no reflection magic
│   │   ├── Rng.cs                        # xoshiro256** — deterministic, seedable, fast
│   │   ├── Hash.cs                       # sub-seed derivation
│   │   ├── EventBus.cs                   # typed pub/sub for cross-system events
│   │   ├── ObjectPool.cs
│   │   └── FixedStep.cs                  # 60Hz sim tick + render interpolation
│   ├── Bullets/
│   │   ├── BulletManager.cs              # THE hot path — see §3
│   │   ├── BulletData.cs                 # struct, cache-friendly SoA layout
│   │   ├── PatternPlayer.cs
│   │   ├── PatternPrimitives.cs
│   │   └── SpatialHash.cs
│   ├── Player/
│   │   ├── PlayerController.cs           # CharacterBody2D, movement + input
│   │   ├── SanitySystem.cs
│   │   ├── CorruptionSystem.cs
│   │   ├── BlinkStep.cs
│   │   ├── WeaponHolder.cs
│   │   └── AscensionController.cs
│   ├── Weapons/
│   │   ├── Weapon.cs
│   │   ├── WeaponData.cs                 # [GlobalClass] Resource
│   │   ├── Inscription.cs
│   │   ├── InscriptionData.cs
│   │   └── ModifierStack.cs              # ordered, deterministic stat resolution
│   ├── Sigils/
│   │   ├── SigilCircle.cs                # the grid model
│   │   ├── SigilData.cs
│   │   ├── AdjacencyResolver.cs
│   │   ├── LeyLine.cs
│   │   └── ReverieScreen.cs
│   ├── Enemies/
│   │   ├── EnemyBrain.cs
│   │   ├── EnemyData.cs
│   │   ├── States/
│   │   ├── FlowField.cs
│   │   └── AttackTokenPool.cs
│   ├── Generation/
│   │   ├── FloorGenerator.cs             # orchestrates the 9 stages
│   │   ├── FloorFlow.cs                  # Resource: the graph
│   │   ├── FlowTransformer.cs
│   │   ├── CompositeDecomposer.cs
│   │   ├── CompositeLayout.cs
│   │   ├── CorridorStitcher.cs
│   │   ├── FloorValidator.cs
│   │   ├── Populator.cs                  # Dread Budget
│   │   └── RoomTemplate.cs
│   ├── Rooms/
│   │   ├── Room.cs
│   │   ├── DoorController.cs
│   │   ├── EncounterRunner.cs
│   │   └── Hazards/
│   ├── Economy/
│   │   ├── Shop.cs
│   │   ├── InscriptionBench.cs
│   │   ├── LootTable.cs
│   │   └── PitySystem.cs
│   ├── Meta/
│   │   ├── RunState.cs                   # everything about the current run
│   │   ├── ProfileState.cs               # persistent: unlocks, fragments, codex
│   │   ├── SaveService.cs
│   │   └── AchievementService.cs
│   ├── UI/
│   ├── Audio/
│   └── Debug/
│       ├── PatternLab.cs
│       ├── GenerationVisualiser.cs
│       └── DebugOverlay.cs
├── data/                                 # .tres resources — ALL tuning lives here
│   ├── weapons/  inscriptions/  sigils/  enemies/  patterns/  flows/  loot/
├── scenes/
│   ├── rooms/{undercroft,innsmouth,archives,mountains,leng,rlyeh}/
│   ├── bosses/  ui/  vfx/
├── art/  audio/
├── addons/
│   ├── flow_editor/                      # custom EditorPlugin
│   └── room_validator/
└── tests/                                # GdUnit4
```

---

## 3. The Bullet Manager — the most important class in the codebase

### 3.1 The key insight

**Enemy bullets only ever need to collide with two things: the player (one 6px circle) and walls.** That is not a broad-phase problem — it is a linear scan against a single circle. Godot's physics server is enormous overkill and 50× slower than doing it by hand.

### 3.2 Design

```csharp
// Structure-of-Arrays. Never an array of Node2D. Never an Area2D per bullet.
public sealed class BulletManager : Node2D
{
    private const int MaxBullets = 4096;

    // Hot arrays — contiguous, cache-friendly, no GC pressure.
    private readonly Vector2[] _pos     = new Vector2[MaxBullets];
    private readonly Vector2[] _vel     = new Vector2[MaxBullets];
    private readonly float[]   _radius  = new float[MaxBullets];
    private readonly float[]   _life    = new float[MaxBullets];
    private readonly int[]     _flags   = new int[MaxBullets];   // hallucination, pierce, elem
    private readonly short[]   _typeId  = new short[MaxBullets]; // → sprite region
    private readonly float[]   _rot     = new float[MaxBullets];
    private int _count;                                          // dense-packed; swap-remove

    private MultiMeshInstance2D _mmi;    // one draw call for every bullet on screen
    private Texture2D _atlas;            // all bullet sprites in one atlas

    public override void _PhysicsProcess(double delta) { /* fixed 60Hz */ }
}
```

**Per-tick work:**
1. **Integrate** — `_pos[i] += _vel[i] * dt`, plus behaviour modifiers (homing, wave, gravity, delay). Vectorisable; use `System.Numerics` / `Vector128` if profiling demands it.
2. **Player collision** — one loop, circle-vs-circle against the player's single hitbox. `distSq < (r + 6)²`. ~4096 iterations of trivial math, well under 0.05ms.
3. **Wall collision** — sample the room's collision bitmask (a `bool[,]` baked at floor generation, not a physics query). O(1) per bullet.
4. **Lifetime & culling** — swap-remove dead bullets from the dense arrays.
5. **Upload transforms** to the `MultiMesh` buffer in one `SetBuffer` call.

**Player bullets** run through a second, smaller manager that collides against enemies via a **uniform spatial hash** (cell size ≈ 32px). Enemy counts are ≤ 60, so this is also trivial.

### 3.3 Rendering
One `MultiMeshInstance2D` per bullet *layer* (below-player, above-player), each with a custom shader reading per-instance colour + atlas UV from `INSTANCE_CUSTOM`. **Two draw calls for the entire bullet field.**

**Plus one shadow layer (required, not optional).** A third `MultiMeshInstance2D` beneath both, drawing a soft offset quad for every projectile whose `Hallucination` flag is clear. This is the sole tell distinguishing hallucinated bullets from real ones ([02 §3.4](02-player-and-combat.md), [05 §1 R9](05-enemies-and-bosses.md)) — it is load-bearing gameplay, not a visual flourish, and it must be in the M0 stress test. Cost: one extra draw call and one extra transform buffer write per tick. **Three draw calls total for the bullet field.**

### 3.4 Why not Godot physics
| Approach | 1000 bullets |
|---|---|
| `Area2D` per bullet | ~9–14 ms/frame. Unusable. |
| `RigidBody2D` | Worse. |
| `PhysicsServer2D` direct | ~2–3 ms. Workable but complex and still overkill. |
| **SoA + manual circle tests + MultiMesh** | **~0.15 ms.** |

**Non-negotiable architectural decision. Do not compromise this for prototyping convenience** — write the simple version of the correct architecture, not the correct version of the simple architecture.

---

## 4. Fixed Timestep & Determinism

```csharp
// project.godot: physics/common/physics_ticks_per_second = 60
//                physics/common/physics_jitter_fix = 0
//                (jitter fix ON causes non-determinism — must be 0)
```

- **All gameplay in `_PhysicsProcess` at a locked 60 Hz.** Nothing gameplay-affecting in `_Process`.
- **All rendering interpolated** in `_Process` between the last two sim states, so the game looks 144 Hz while simulating 60. Godot 4.4's `physics_interpolation` handles nodes; the bullet MultiMesh needs manual lerp between two transform buffers.
- **Input sampled in `_Process`, buffered, consumed on the next sim tick.** Buffer window: 6 frames for Blink Step and reload (forgiving inputs without feeling laggy).
- **No `float` accumulation across ticks** for anything that must be reproducible.

**Determinism payoff:** seeded runs, daily challenges, replay files (input-only, ~2KB per run), and bug reports that reproduce.

---

## 5. The Data Pipeline

**Rule: no gameplay number appears in a `.cs` file.** All tuning lives in `.tres` resources so designers iterate without recompiling (Godot C# recompiles are slow enough that this genuinely matters).

```csharp
[GlobalClass]
public partial class WeaponData : Resource
{
    [Export] public string DisplayName { get; set; }
    [Export] public WeaponTier Tier { get; set; }
    [Export] public WeaponFamily Family { get; set; }
    [Export] public float Damage { get; set; }
    [Export] public int ProjectilesPerShot { get; set; }
    [Export] public float FireRate { get; set; }
    [Export] public int MagazineSize { get; set; }
    [Export] public int ReserveMagazines { get; set; }
    [Export] public float ReloadDuration { get; set; }
    [Export(PropertyHint.Range, "0.5,2.0,0.1")]
    public float ReloadWeight { get; set; } = 1.0f;   // → Sanity cost multiplier
    [Export] public PatternData FirePattern { get; set; }
    [Export] public int InscriptionSlots { get; set; } = 1;
    [Export] public int CorruptionOnPickup { get; set; }
    [Export(PropertyHint.MultilineText)] public string CodexText { get; set; }
}
```

### 5.1 The Modifier Stack
Weapon stats are computed through an **ordered, deterministic** pipeline so that Inscriptions + sigils + ley bonuses always resolve identically:

```
BASE  →  Additive flat  →  Additive percent (summed, then applied once)
      →  Multiplicative (ordered by source priority)  →  Clamp  →  FINAL
```

Never allow arbitrary mutation order. Cache the result and invalidate on any equip/unequip event — this is computed once per change, not per frame.

---

## 6. Scene & Node Architecture

**Composition over inheritance.** Godot's node tree is the composition mechanism; use it.

```
Main (GameRoot autoload sibling)
└── World
    ├── FloorRoot                          # rebuilt each floor
    │   ├── TileMapLayers (floor/walls/deco)   # merged from room templates at bake
    │   ├── Rooms/  (Room nodes; only the active + neighbours process)
    │   ├── Enemies/
    │   └── Props/
    ├── Player
    ├── EnemyBulletManager                 # MultiMeshInstance2D
    ├── PlayerBulletManager
    ├── VFXLayer
    └── LightingLayer (CanvasModulate + Light2Ds)
UILayer (CanvasLayer)
├── HUD  ├── ReverieScreen  ├── PauseMenu  └── CodexScreen
```

### 6.1 Room activation
Only the **active room and its immediate neighbours** run `_PhysicsProcess`. Everything else is `ProcessMode.Disabled`. Enemy AI, hazards, and props in inactive rooms cost zero. This is what makes 18-room floors free.

### 6.2 The EventBus
Typed signals for cross-cutting concerns, to avoid a web of direct references:
```csharp
EventBus.EnemyKilled     += (e, killer) => sanity.Gain(e.SanityValue);
EventBus.CorruptionChanged += OnCorruptionThreshold;
EventBus.RoomCleared     += loot.RollDrops;
```
Keep it to ~15 events. An EventBus with 80 events is a debugging nightmare.

---

## 7. Save System

Two files, both JSON via `System.Text.Json` (source-generated serializers — no reflection, AOT-friendly).

| File | Contents | Write cadence |
|---|---|---|
| `profile.json` | Unlocks, Fragments, Codex, achievements, stats, settings | On change, debounced 2s |
| `run.json` | Full run snapshot for suspend/resume | On floor transition + on quit |

- **Atomic writes:** write to `.tmp`, `fsync`, then rename. Never corrupt a profile on a crash.
- **Versioned schema** with explicit migration functions. Ship v1 with the migration hook already in place.
- Location: `user://` (Godot maps to `%APPDATA%\Godot\app_userdata\...` on Windows).
- **Suspend/resume only, not save-scumming:** `run.json` is deleted on load. Roguelike integrity.

---

## 8. Performance Budget (per frame @ 144 Hz = 6.9 ms)

| System | Budget | Notes |
|---|---|---|
| Bullet integration + collision | 0.4 ms | 4096 bullets worst case |
| Enemy AI (60 agents) | 0.8 ms | Flow-field pathing, staggered brain ticks (¼ of agents per tick) |
| Player + weapons | 0.2 ms | |
| Physics (walls, pickups only) | 0.5 ms | Very few real physics bodies |
| Rendering | 2.5 ms | 2 bullet draw calls, batched tilemaps, ≤ 24 Light2Ds |
| VFX/particles | 0.6 ms | GPUParticles2D only; hard cap on emitters |
| UI | 0.3 ms | HUD redraws only on value change |
| Audio | 0.2 ms | |
| **Headroom** | **1.4 ms** | |

**GC is the enemy.** Targets:
- Zero allocations in `_PhysicsProcess`, verified with a debug allocation counter in dev builds.
- Pool everything: bullets, VFX, damage popups, enemies, audio players.
- Prefer `struct` + arrays over `List<T>` of classes in hot paths.
- No LINQ in gameplay code. Ever. (Fine in generation and UI.)
- Avoid C#↔Godot marshalling in loops — batch `MultiMesh` updates, don't set node properties per bullet.

---

## 9. Testing Strategy

**GdUnit4** for unit and integration tests.

| Layer | What's tested |
|---|---|
| **Generation invariants** | Headless `--gen-only 10000` per floor type, asserting the full validation checklist from [06 §5.5](06-procedural-generation.md). Runs in CI on every push. Any assert failure prints the seed. |
| **Modifier stack** | Property-based: any permutation of the same modifier set yields identical output |
| **Determinism** | Same seed + same input replay → identical end state hash. This is the single best regression test the project can have. |
| **Bullet manager** | Fuzz spawn/despawn; assert dense-array invariants and no leaks |
| **Economy** | Simulate 1000 runs, assert gold/key/sigil totals land in the target bands from [08 §8](08-economy-and-meta.md). **Three assertions added by review — all are percentile or ordering conditions, not averages, because every defect found was invisible at the mean:**<br>• **Sigil supply fills the circle at the 10th percentile of drop luck**, not merely on average ([04 §6](04-sigil-circle.md)) — an invariant that fails on unlucky runs does not guard Pillar II.<br>• **A full 41-cell circle of D/C sigils must not produce a higher `playerPowerMult` than a half-full circle of A/S sigils** ([06 §6.1](06-procedural-generation.md)) — guards against the Dread Budget taxing engagement with the Sigil Circle.<br>• **Gold income must fund ≥3 Inscriptions at the 10th percentile including Dissolution Bowl proceeds** ([08 §1.2](08-economy-and-meta.md)). |
| **Sanity economy** *(added)* | Headless simulation of a floor at fixed skill levels, asserting: median Sanity net per room within ±15; the Lucid Ceiling produces ≥25% of late-floor combat time below 40 Sanity ([02 §3.3.1](02-player-and-combat.md)); and **no action or item combination reduces a Pillar-I cost to zero** — a direct regression test for the four sigils and the Ascension loop the review found broken. |
| **Sigil adjacency** | Exhaustive small-grid tests for synergy resolution and ley bonuses |
| **Smoke** | Headless bot plays floor 1 with random input for 5 minutes without crashing |

---

## 10. Tooling & Ops

- **Godot version pinned** in the repo (`.godot-version`); no drifting.
- **CI:** GitHub Actions — build, run tests, run 10k-seed generation sweep, export Windows + Linux builds on tag.
- **Debug overlay** (F3): FPS, frame-time graph, bullet count, entity count, GC gen0/1/2 counters, current seed, Corruption, Dread Budget of the current room.
- **Cheat console** (F1, dev builds only): `give <weapon>`, `sanity <n>`, `corruption <n>`, `warp <floor>`, `seed <n>`, `killall`. Non-negotiable for content velocity — you will otherwise waste hundreds of hours replaying floor 1.
- **Telemetry** (opt-in, post-launch): death location heatmaps per floor, room completion times, weapon/sigil pick and win rates, Corruption distribution at death. This is how balance gets fixed after launch.
- **Steam integration:** GodotSteam or Facepunch.Steamworks for achievements, cloud saves, and rich presence. Deck Verified requires ≥ 30 fps at 1280×800 and full controller support — validate on real hardware from Milestone 3.

---

## 11. Key Technical Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Bullet perf collapses under real content | **High** | Build the SoA/MultiMesh manager in M0 and stress-test at 4096 bullets *before* any content exists |
| C# GC hitches cause dropped inputs | High | Zero-alloc discipline in the tick loop; allocation counter in dev builds; `GCSettings.LatencyMode` tuned |
| Non-euclidean R'lyeh (SubViewport stitching) is too expensive or too buggy | Medium | Prototype in M4 with a hard go/no-go date; fallback is seam-teleports only |
| 600 hand-authored rooms is the schedule | **High** | Room authoring must start in M2 and run continuously; build the validator tool early; consider a room-template kit-bashing system |
| Godot C# tooling regressions between versions | Medium | Pin the version; upgrade only between milestones, never mid-milestone |
| Deterministic replay breaks on any float divergence | Medium | Determinism test in CI from M1, so divergence is caught the day it's introduced |
