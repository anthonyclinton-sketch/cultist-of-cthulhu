# 06 — Procedural Generation

> Architecture adopted from *Enter the Gungeon*'s flow-based generator, as reverse-engineered by Boris the Brave and described by Dodge Roll. Rooms are hand-authored; **floors** are assembled. This guarantees pacing while randomising topology.

---

## 1. Why This Architecture

| Approach | Pacing | Room quality | Loops | Verdict |
|---|---|---|---|---|
| BSP / grid partition | Random | Random | Rare | Rejected — feels like a maze, not a dungeon |
| Cellular automata / cave | None | None | Accidental | Rejected — wrong genre entirely |
| Wave Function Collapse | None | Good locally | Uncontrolled | Rejected — no global pacing control |
| Hand-crafted static levels | Perfect | Perfect | Authored | Rejected — no replayability |
| **Authored flows + composite layout** | **Authored** | **Authored** | **Authored** | **Adopted** |

The insight from Gungeon: *the thing you want to randomise is the shape of the graph's embedding in space, not the graph itself, and not the rooms.* The developers' stated goal — "approximate a Zelda dungeon with each generation" — is exactly ours.

---

## 2. The Pipeline

```
 SEED (uint64)
   │
   ├─► 1. SELECT FLOW          pick 1 of N authored flows for this floor
   │
   ├─► 2. TRANSFORM FLOW       expand chains · resolve branch choices · inject nodes
   │
   ├─► 3. ASSIGN ROOMS         choose a RoomTemplate for each node
   │
   ├─► 4. DECOMPOSE            cut smallest loops into composites until only trees remain
   │
   ├─► 5. LAYOUT COMPOSITES    loops first (hardest), then trees, DFS with backtracking
   │
   ├─► 6. STITCH               connect composites via corridors / pathfinding
   │
   ├─► 7. VALIDATE             connectivity, reachability, budget, no overlap
   │      └─ FAIL → retry with seed+1 (budget: 12 attempts, then fallback flow)
   │
   ├─► 8. POPULATE             encounters, props, loot, hazards, secrets
   │
   └─► 9. BAKE                 tilemap merge · navmesh/flow-field · occlusion · lighting
```

---

## 3. Stage 1–2 — Flows

### 3.1 What a flow is

A `FloorFlow` is a **directed graph resource** with no spatial information. Nodes are *room roles*, edges are *connections*.

```
FloorFlow: "undercroft_flow_03"
  root ── entrance
   ├─► combat_easy ──┐
   │                 ├──► hub ──► [LOOP] ──► combat_med ──► combat_med ──► back to hub
   │   combat_easy ──┘             │
   │                               ├──► reward_room  (dead end)
   │                               ├──► shop         (dead end)
   │                               └──► combat_hard ──► boss_foyer ──► BOSS
   └─► [INJECT SLOT: secret]
```

Structure rules (from Gungeon, and correct):
- A flow is a **tree with a root**, plus a small number of extra edges that **close loops**. Every loop therefore has a well-defined entrance and exit.
- Loops are the point. Loops let players choose routes and backtrack without dead-end fatigue.

### 3.2 Flow counts per floor

| Floor | Flows authored | Rooms per floor | Notes |
|---|---|---|---|
| 1 Undercroft | 6 | 11–14 | Simple, one loop, teaching pacing |
| 2 Innsmouth | 7 | 13–16 | Two loops, water splits routes |
| 3 Archives | 7 | 14–17 | Shifting walls; flows must tolerate mutation |
| 4 Mountains | 6 | 14–18 | Warden gate node is mandatory |
| 5 Leng | **1 (special)** | n/a | Open generator — see §8 |
| 6 R'lyeh | 5 | 12–15 | Non-euclidean stitcher — see §9 |

### 3.3 Flow transformation

Three operations, in order:

1. **Chain expansion.** Nodes tagged `expandable` are replaced with a run of 1–4 rooms of the same role. This is how the same flow produces a 12-room and an 18-room floor.
2. **Branch resolution.** Nodes tagged as `alternates` present 2–3 sub-graphs; one is chosen by weighted roll.
3. **Node injection.** Special rooms are inserted according to injectors:

```
Injector {
  NodeRole  role            // shop, shrine, secret, corrupted_door, tome, warden
  Placement placement       // DeadEnd | OffHub | AnyEdge | ReplaceNode
  float     chance
  Predicate condition       // "corruption >= 1", "floor >= 2", "player has no shop yet"
  int       maxPerFloor
}
```

Injection is the game's **content-pacing valve**. It's where "one shop per floor guaranteed by floor 2" and "corrupted doors only appear if Corruption ≥ 1" get enforced. Keep all such rules here, in data, not scattered through code.

---

## 4. Stage 3 — Room Templates

### 4.1 What a room is

A `RoomTemplate` is a **hand-authored Godot scene** containing:

- A `TileMapLayer` for floor/walls (authored at 16px, rooms are **24×18 to 96×66 tiles**)

> **[PLAYTEST — 26 Jul 2026] Room sizes must be authored RELATIVE TO THE SCREEN, and the first pass was far too small.**
>
> The viewport is 640×360 native = **40 × 22.5 tiles**. The original range (12×9 to 40×30) meant most combat rooms were *smaller than one screen*, which is fatal for this genre: there is nowhere to dodge *to*, radial patterns hit a wall before they finish expanding, and the camera never moves so a room reads as a box rather than a place.
>
> Rooms are now sized in screens, by role — connector ~0.9×0.7, easy ~1.2×1.1, medium ~1.6×1.4, hard ~1.9×1.9, hub ~1.8×2.3, boss ~2.4×2.9. That is **4–8× the previous area**. Floor extent rose from ~95 tiles square to ~200–450.
>
> **Three knock-on changes were required, and finding them is why the sweep exists:**
> 1. `MaxFloorExtent` 300 → 1400, or almost every layout was rejected.
> 2. `FlowField.CellSize` 24 → 40. The field covers the whole floor, so its cost is quadratic in floor size — at 24px a 450-tile floor is 90,000 cells to BFS per repath.
> 3. **Encounter budget now scales with room area** (by its *square root* — linear scaling turns a big room into a slog rather than a bigger fight). Without it, four enemies in a four-screen room is something you walk past.
>
> **And a genuine algorithmic finding.** Scaling rooms up pushed the fallback rate from 0.22% to 1.7%. Widening the search barely helped; *adding more doors made it worse*. That is the tell that the layout search was **budget-limited, not option-limited** — the shared backtrack budget was being spent exploring a very wide tree at shallow depth. Capping the branching factor to the best 10 placements per node (beam search, options already sorted best-first by compactness) took it to 0.77%. **Packing problems want depth, not breadth.**
- **Exit markers** — each with a side (N/S/E/W), a tile offset, and a width (1 or 2 tiles)
- **Spawn anchors** — tagged points where the populator may place enemies, props, chests, or hazards
- Optional **authored props** that are always present (pillars, tables, pits)
- Metadata: `role`, `floorTags[]`, `sizeClass`, `threatCapacity`, `weight`, `minFloor`

**Nothing about the interior geometry is procedural.** A designer builds and playtests every room.

### 4.2 Room roles

| Role | Purpose | Count target (per floor) |
|---|---|---|
| `entrance` | Safe, no enemies, shows the floor's visual identity | 3 |
| `combat_easy` / `_med` / `_hard` | The bulk | 25 / 22 / 18 |
| `hub` | Large, 3–4 exits, moderate encounter | 6 |
| `connector` | No enemies; a hazard or a lore beat. **Pacing rest.** | 10 |
| `reward` | The item room. One sigil pedestal. No enemies. | 3 |
| `shop` | Gaunt's stall + Inscription Bench | 2 |
| `shrine` | One interactable risk/reward | 4 |
| `secret` | Behind a cracked wall. High value. | 4 |
| `warden` | Mid-boss arena | 2 |
| `boss_foyer` | Save-point-like: heals a quarter heart, refills Sanity, last chance for Reverie | 1 |
| `boss` | Arena, authored per boss | 1 |

**≈100 room templates per floor set at 1.0, ~600 total.** This is the largest content line item in the project and the roadmap must respect it ([11](11-roadmap.md)).

### 4.3 Room selection
Weighted by `role` match, floor, `sizeClass` demand from the layout stage, and a **recency penalty** so the same room doesn't appear twice on one floor (hard rule) or in consecutive floors (soft, −70% weight).

---

## 5. Stage 4–6 — Layout

### 5.1 Decomposition
Repeatedly find the **smallest cycle** in the flow graph and extract it as a **loop composite**. Continue until the remainder is a forest; each tree is a **tree composite**.

*Why:* loops are the hardest thing to embed in 2D without overlap. Solve hard constraints first. This is Gungeon's stated principle — "generate the parts of the map that are hardest / most important first."

### 5.2 Laying out a loop composite
1. Place the first room at the origin.
2. Add rooms **alternately at either end** of the growing chain.
3. Early on, pick exit pairs randomly (to get organic shapes); as the two ends approach, **bias exit-pair selection toward closing the gap**.
4. Close the final connection with either a rectangular filler room or a corridor of length 4–30 tiles.
5. If closure fails after K attempts, backtrack two rooms and re-roll.

### 5.3 Laying out a tree composite
Depth-first from the root:
- For each child, enumerate valid (parentExit, childExit) pairs that place the child without overlapping any placed room (AABB test + a 1-tile margin).
- **Prefer exits far from already-used exits** — this spreads the layout and avoids congested knots.
- On failure, backtrack and try the next pair. Budget 200 backtracks per composite.

### 5.4 Stitching
Start from the composite containing the **most connections** (usually the main loop), place it centrally, then attach the others via corridor pathfinding (A* on a coarse tile grid, avoiding placed rooms, corridor width 2, max length 30).

### 5.5 Guaranteed invariants (Stage 7 validation)
```
✔ Every room reachable from entrance
✔ Boss reachable
✔ Exactly one reward room, one shop (floor ≥ 2), one boss
✔ No two rooms overlap (incl. 1-tile margin)
✔ All corridors ≤ 30 tiles
✔ Total floor bounds ≤ 300×300 tiles
✔ Every locked door has a reachable key source on this floor
✔ Every secret room's cracked wall is adjacent to a reachable normal room
✔ Encounter budget within [floorMin, floorMax]
✔ Player start has ≥ 2 unexplored exits after the second room
```
Any failure → retry with the next seed. After 12 failures, fall back to a **guaranteed-valid handcrafted flow** for that floor. Log the failing seed to telemetry — repeated failures indicate an authoring bug.

---

## 6. Stage 8 — Population

### 6.1 The Dread Budget

Encounters are not enemy lists; they are **budgets**.

```
DreadBudget(room) = base(floor)
                  × sizeClassMult(room)
                  × roleMult(room.role)
                  × (1 + 0.06 × Corruption)
                  × playerPowerMult
```

`playerPowerMult` reads: **cells filled weighted by sigil tier** (not raw sigil count), best weapon tier, total Inscriptions, current max hearts. It is clamped to **[0.85, 1.35]** — the floor scales *slightly* to the player, never enough to erase the reward for building well. (This deliberately avoids the Oblivion trap.)

> **[REVIEW — Fable] Changed "number of sigils equipped" → "cells filled weighted by tier", because the old metric taxed Pillar II specifically.**
> Counting *sigils* means a player who fills their circle with many small efficient tiles is rated as more powerful than one holding three large ones — so engaging deeply with the Sigil Circle raised the difficulty of every subsequent room. Worse, it uniquely punished **The Professor**, whose entire identity is *"+1 sigil from every reward room"* and whose stated design is *"weakest raw power, biggest circle payoff"* ([08 §7](08-economy-and-meta.md)) — the character built to hold the most sigils would face the hardest floors while dealing the least damage.
> Tier-weighted cell count tracks actual power rather than tile count, and does not penalise the puzzle-solving the system exists to reward. **Also add an explicit test:** the 1000-run economy simulation ([09 §9](09-technical-architecture.md)) should assert that a full 41-cell circle of D/C sigils does not produce a higher `playerPowerMult` than a half-full circle of A/S sigils.

Each enemy has a **Dread cost**. The populator picks from the floor's roster until the budget is spent, subject to:
- ≥ 35% of budget on `Fodder` (the Sanity economy constraint from [05 §2](05-enemies-and-bosses.md))
- ≤ 1 `Support` unless the room is a hub
- Attack-token cap respected
- Spawn anchors must accommodate the chosen enemies' sizes

### 6.2 Waves
Rooms with budget above a threshold split into 2–3 waves. Wave 2 spawns when wave 1 is at 30% remaining — never on a timer, so careful play is never punished. Wave spawn points are always visible on screen with a 0.6s telegraph (R4).

### 6.3 Loot placement
- Every room rolls drops on clear: gold (always), ammo (22%), Sanity candle (15%), key (8%), heart (3%).
- **Pity system:** tracked counters force a key drop if the player has 0 keys and has cleared 5 rooms; force ammo if total reserve < 20%.
- Chests are placed at anchors in `combat_hard` and `hub` rooms, tier rolled against a table modified by Corruption.

### 6.4 Secrets
- Each floor has 1–3 secret rooms, injected at flow stage as `secret` nodes attached to a normal room via a **cracked wall** rather than a door.
- The wall is subtly authored — a hairline crack decal, slightly different tile variant, and a distinct ambient audio emitter within 5 units.
- Revealed by **Banish** adjacent to it (Gungeon's mechanic, and a good one) — which is also why Banish costing Corruption is interesting: hunting secrets makes the floor angrier.
- At Sanity ≤ 20 (Unravelled), secret rooms are **outlined on the minimap**. This is a major reason to run low.

---

## 7. Determinism & Seeding

```csharp
// One master seed per run. Sub-seeds derived deterministically so that
// changing the enemy roster never changes the floor layout.
ulong runSeed         = userSeed ?? Crypto.RandomU64();
ulong floorSeed       = Hash(runSeed, floorIndex);
ulong layoutSeed      = Hash(floorSeed, "layout");
ulong populateSeed    = Hash(floorSeed, "populate");
ulong lootSeed        = Hash(floorSeed, "loot");
```

- **Never** use a global RNG. Every subsystem takes an explicit `Rng` instance.
- Seeds are displayed in the pause menu and copyable — daily runs and seed sharing come free.
- A `--seed` CLI flag plus a `--gen-only N` headless mode that generates N floors and asserts all invariants. **Run this as CI on every commit** — 10,000 floors per floor-type, asserting the invariant list.

---

## 8. Floor 5 — The Open Generator (the formula break)

Per the *Pathogenic* lesson (its intestine level abandons room-to-room structure entirely), Floor 5 uses a different generator.

- A single continuous **240×240 tile open plateau**, no walls, no doors, no room seals.
- Generated by **Poisson-disc scattering of landmarks** (monoliths, the three avatar arenas, a caravan camp that acts as the shop, ruined temples that act as reward rooms).
- Navigation is by **line-of-sight to landmarks** and a compass, not a minimap — dream-fog limits vision to ~18 units.
- Enemies roam in **packs with territories** rather than spawning in rooms.
- The three Nyarlathotep avatars are placed far apart; you can approach in any order, and you can *run past everything straight to them* if you're brave.

**This costs one bespoke generator and buys the entire game a sense of escalation.** Worth it.

---

## 9. Floor 6 — The Non-Euclidean Stitcher

R'lyeh uses the standard flow pipeline but adds a post-process that makes the *map lie*:

- **Seam pairs:** certain wall segments are linked. Walking into one exits from the other, with a shader-based screen warp. The minimap draws both locations, connected by an impossible line.
- **Volume violation:** a room's interior is authored larger than its exterior footprint. Handled by rendering the interior in a `SubViewport` and stitching at the doorway. (Technically the trickiest feature in the game — see the risk register in [11](11-roadmap.md).)
- **Map corruption:** the minimap deliberately mis-draws connections at Corruption ≥ 5, and *corrects itself* at Sanity ≤ 20. The mad player sees truly.

**Fallback if too expensive:** ship R'lyeh with seam-teleports only (cheap, one shader) and cut volume violation. The floor still reads as wrong.

---

## 10. Authoring Tools to Build

These are not optional; they determine content velocity.

| Tool | Milestone | Purpose |
|---|---|---|
| **Room Template validator** | M1 | In-editor plugin: checks exit markers align to the grid, warns on unreachable anchors, reports threat capacity |
| **Flow Graph editor** | M2 | Godot `EditorPlugin` with a `GraphEdit` UI for authoring `FloorFlow` resources visually |
| **Generation Visualiser** | M1 | Headless renderer that dumps a PNG of a generated floor for a given seed; run over 100 seeds to eyeball a floor's character |
| **Pattern Lab** | M1 | Isolated bullet-pattern preview scene with ghost player and density heatmap |
| **Balance Exporter** | M3 | Script that walks all `.tres` and emits a CSV of weapon/sigil/enemy stats for spreadsheet balancing |
