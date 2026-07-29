# HANDOVER — Session Continuation

**Repo:** `github.com/anthonyclinton-sketch/cultist-of-cthulhu` (private) · branch `main`
**Local:** `C:\Users\antho\Cultist Of Cthulu`
**As of:** commit `832f35f`, 40 commits · 66 C# files / ~16,100 lines · 65 `.tres` · 16 debug scenes

---

## 1. What this is

*Cultist of Cthulhu* — a top-down twin-stick action roguelike with bullet-hell set pieces,
in **Godot 4.7-stable mono + C# (.NET 8)**. Lovecraftian; procedurally assembled floors;
shops that sell weapon upgrades. Full design in `docs/` (start at `README.md`).

**The one thing to understand before changing anything:** the design's central bet is that
**Sanity is the stamina bar** — it pays for reloading and Banish, kills refund it, and
running low changes what you can perceive rather than how hard you hit. Everything in
`docs/02-player-and-combat.md` serves that. Read `docs/00-comparative-analysis.md` §2.2 for
why it is a *risk* rather than a proven mechanic.

**The run:** six floors, Cthulhu at the bottom, five endings keyed to the Corruption you
arrive with (docs/07 §5). Not an endless descent. **One floor of content exists**, so
`RunState.FinalFloor` is 1 and beating Boss 1 wins the run.

---

## 2. Environment

Godot is **not** on PATH. It lives at:

```
C:\Users\antho\Downloads\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe
```

`tools/gates.ps1` finds it automatically, or set `$env:GODOT`. **Always launch through the
script** — a raw quoted path in PowerShell is a string literal, not a command, and silently
does nothing.

```bash
pwsh ./tools/gates.ps1                       # every gate (~5 min)
pwsh ./tools/gates.ps1 -Floor                # PLAY a run
pwsh ./tools/gates.ps1 -Floor -Seed 23       # a seed that has a shop (70% roll on floor 1)
pwsh ./tools/gates.ps1 -Floor -Floors 3      # a three-floor run
pwsh ./tools/gates.ps1 -Floor -Corruption 3  # start Corrupted (3 awakens, 10 = Yellow Sign)
pwsh ./tools/gates.ps1 -Floor -Autorun       # WATCH the run play itself
pwsh ./tools/gates.ps1 -Arena                # the fixed-arena combat slice
pwsh ./tools/gates.ps1 -ShowSeed 7           # render a floor as ASCII
pwsh ./tools/gates.ps1 -Floor -MeteredDodge  # Build B, the M1 control arm
```

Controls: **WASD** move · **LMB** fire · **SPACE** dash · **R** recite · **RMB** banish ·
**E** interact · **TAB** Reverie · **M** map · **F3** overlay. Debug: **K** forces
Ascension, **G** refills Sanity, **F7** cycles hit-stop weight, **F5** dumps telemetry.

**Trap that has bitten twice:** Godot loads the **Debug** assembly from
`.godot/mono/temp/bin/Debug`. Building `-c Release` produces a binary Godot silently
ignores, and the gates then measure stale code. Never "optimise" the build config.

### Looking at a frame

```bash
# any room role: Reward, Shop, Shrine, Secret, Hub, Boss, CombatHard, ...
godot --path . res://scenes/debug/FloorRunner.tscn --seed 7 --room-demo=Shop \
      --screenshot=out.png --screenshot-after=70

godot --path . res://scenes/debug/FloorRunner.tscn --seed 7 --reverie-demo \
      --screenshot=out.png --screenshot-after=70

# mid-run: waves, telegraphs, Corruption effects
godot --path . res://scenes/debug/FloorRunner.tscn --seed 42 --autorun --corruption=6 \
      --screenshot=out.png --screenshot-after=344
```

`--room-demo` hides the F3 overlay, snaps the camera, stands the player on the first
interactable and holds the map open for combat rooms. The overlay is hidden two frames
early on purpose — `GetImage` reads the framebuffer rendered *before* the current tick.

---

## 3. What is complete

### M0 — Technical foundation ✅ *gated*
`BulletManager` at **0.13ms p99 / 4096 bullets** against a 0.40 budget, zero steady-state
allocation. Deterministic `Rng` (xoshiro256**). Frame-exact Blink Step.

### M1 — Combat slice ✅ *systems complete, NOT playtested*
Sanity economy, Ascension with the debt rule, Banish, 5 weapons, 5 enemies, pickups,
telemetry, and the `--metered-dodge` control arm.

### M2 — Floor generation ✅ *gated*
Authored flows → chain expansion → injection → cycle decomposition → beam-limited
placement → corridor stitching → validation. **10,000 seeds, 0 failures, 0.07% fallback,
flows used 34/34/32.** 32 templates with authored interiors, flood-validated so no obstacle
can seal a room or block a door.

### M2 — Systems slice ✅ *built, NOT playtested beyond floor 1*
- **Sigil Circle** (docs/04): 7×7 corner-cut grid, locked Heart, three ley lines rolled per
  run, 7 polyomino shapes with rotation and mirroring, tag-based synergies, Reliquary,
  dissolution. 20 sigils + 2 Hearts. Balance rules §8.1–§8.7 enforced in `Validate()`.
- **The Reverie** (Tab): pauses, refuses to open while doors are sealed.
- **Room content**: reward rooms with a choice of two (three at Corruption 3+), Gaunt's
  stall with sigils/consumables/bench/reroll/Dissolution Bowl, four shrines, tiered chests.
- **Inscriptions** (docs/03 §3): 15, projected into effective stats on read.
- **Boss 1**: three phases, the Sanity-costing grab, timed adds, HUD boss bar.
- **The run loop**: `RunState` on `GameRoot` carrying hearts, max Sanity, the Circle,
  weapons and inscriptions, gold, keys, Corruption, telemetry, drop pity across floors.
  Floor completion, a winnable Floor 1, a run summary.
- **Corruption** (docs/02 §7): thresholds with real consequences — Awakened enemies at 3+,
  a fourth bench offer, +1 enemy at 7+, the Yellow Sign at 10.
- **Encounters** (docs/06 §6): the full Dread Budget formula including the Corruption and
  player-power terms, and **waves** — 2–3 per big room, triggered on kills, never a timer.

---

## 4. The gates

All in `tools/gates.ps1` and `.github/workflows/gates.yml`. **All currently green.**

| Gate | Asserts |
|---|---|
| Content validation | Every `.tres` passes `Validate()`, plus sigil-pool rules |
| Ascension invariants | Cannot be farmed; spend-to-zero ≡ drain-to-zero |
| Banish | `ClearRadius` leaves no survivors; cost gates on band |
| **Autorun** | A whole run played headlessly: every room, the boss, the summary |
| **Autorun — 3 floors** | A descent does not silently reset the run |
| **Encounters** | Budget responds; tier-weighting beats cell count; waves never timed |
| **Blink frame data** | 2/14/8 and a 31-frame cycle, measured from the controller |
| **Corruption** | Severity never falls as Corruption rises |
| **Boss 1** | Every phase reached and firing; the grab connects and is rate-limited |
| **Wall collision** | Nothing occupies solid ground; sealed rooms hold; enemies move |
| Floor generation | 10k seeds, every invariant, fallback ≤1% |
| Playable floor smoke | Boots and runs on several seeds |
| Engine warning budget | Zero — a per-frame warning reads as a freeze |
| Bullet performance | 4096 bullets, sim p99 ≤0.4ms, zero alloc |
| Determinism | Same seed → identical state, 1800 ticks, six seeds |
| Economy sim *(advisory)* | Metric 1 / 9 / 5b in target |

### Know the blind spots, and know they move

**A green suite is not a played build.** Every bug found in the last play session survived a
fully green suite: rooms emptying on re-entry, no gold in the HUD, the dodge cancelling
itself into 82% invulnerability, enemies walking through locked doors, Corruption invisible
below its first threshold. About thirty minutes of play beat fourteen gates.

**A test can lie more comfortably than the game can.** Twice this session:

- The enemy wall-collision assertion — *"no enemy body entered a wall over 300 ticks of
  pushing"* — held because **nothing was pushing**. It aimed the flow field at a point
  outside the floor; `FlowField` clamps the target into its grid, the clamped cell is solid,
  and a BFS from a blocked cell cannot spread, so every enemy got a zero direction and stood
  still. Both affected tests now assert that their subjects actually move.
- The Blink single-dodge measurement read 3/49/3 frames because synthetic `Input` actions
  report "just pressed" on every manually-driven tick — Godot only clears that when its own
  input frame advances. `TryBeginBlink()` is now callable directly.

**When you add an assertion, add its control.** The seal test proves it can detect an escape
by running the doors open first. Without that it would have reported success while measuring
motionless enemies.

---

## 5. What to build next

### 5.1 Fix the floor-scaling phantoms — small, do these first

Three things are specified, believed present, and absent. All three only bite once floors
2–6 exist, which is exactly why they will be missed.

1. **Attack tokens never scale.** `EnemyManager.AttackTokens = 4`, `CombatArena` sets it to
   4 explicitly, and **`FloorRunner` never sets it at all** — so it is 4 on every floor
   forever. docs/05 §8 says 4 on floor 1 rising to 9 on floor 6 and calls it *"the single
   most important knob for making a room fair"*. It is also how R7's 600-bullet ceiling is
   meant to be honoured by design rather than clamped at runtime.
2. **Damage does not scale by floor.** `PlayerController.TakeHit(0.5f)` is hardcoded.
   docs/02 §2: half a heart on floors 1–2, **a full heart on floors 3+**, bosses a full
   heart from phase 2. That is a straight doubling of lethality the pacing assumes.
3. **Room counts do not vary by floor.** The generator produces 8–19 from flow expansion,
   unrelated to floor index. docs/07 §2 wants 11–14 rising to 14–18.

### 5.2 Rebalance the difficulty curve — *with* floor 2, not before

`EncounterTest` prints the curve every run. Today, on a medium room at Corruption 0:

```
floor 1   first room  47   tenth room  226
floor 6   first room 102   tenth room  281
```

It escalates, but **floor 1's tenth room outweighs floor 6's first by more than two to
one** — the within-floor ramp is 13/room against a floor lift of only 8/floor, so the run
reads as six separate ramps rather than one descent, and floor 6's peak is just 24% above
floor 1's. Compounding it: `playerPowerMult` is clamped at ×1.35 (docs/06 §6.1) while the
player's real power over six floors grows far more than that, so late floors may well play
*easier* than floor 1.

The levers are `DreadBudget`'s `baseline` terms. **Do this alongside floor 2** — it is
tuning against a felt experience, and tuning a curve nobody can play is guesswork. The gate
prints before/after directly.

### 5.3 Floor 2 — The Drowned Wharfs, and Mother Hydra's Brood

The biggest structural step, and the one that makes everything above meaningful. docs/07
§3 and docs/05 §7. 13–16 room templates, the tidal cycle, ~10 new enemies, and a two-boss
fight where the tide decides which one is vulnerable.

It is also the first real test of the run loop: `RunState`, the floor scaling in the loot
tables and shop prices, and the descent are all built and have only ever run against the
Undercroft at a raised floor index.

### 5.4 Save/load

On the M2 checklist and not built — a run ends when the process does. `RunState` was
written to be the thing a save file serialises and deliberately holds no scene references,
so this is mostly a serialization job. Low urgency while a run is one 5–8 minute floor;
it becomes important the moment a run is 45 minutes.

### 5.5 The M1/M2 playtest — deferred by the owner, deliberately

Design in `docs/11-roadmap.md` § "M1 TEST DESIGN": 10–12 testers, ~25 min each, **both
arms** (`-Floor` and `-Floor -MeteredDodge`), counterbalanced. Deferred until more of the
game exists, which it now does.

Watch for metric 6 (does anyone Open the Eye unprompted?), metric 7 (does the free dodge
beat the metered one?), and the qualitative Pathogenic failure mode — *"being forced to
stop shooting breaks the rhythm."* Three testers saying that routes to fallback F1
regardless of the numbers.

**New:** nobody has played a floor with a Circle in it for more than one floor, and the
wave pacing (50/30/20 front-loaded, 30%-remaining trigger) has never been felt by anyone.

---

## 6. Smaller open items

`docs/AUDIT-spec-vs-code.md` § "M2 sweep" is the authority; re-run it at the end of every
milestone. The ones that matter:

- **Directional sigils do nothing directionally.** The facing is stored, rotates with the
  tile and is drawn, and no effect reads it. docs/04 §3.2's orientation layer is scaffolding.
- **The Reverie's "live diff panel" is not a diff.** It shows the state after committing,
  not what a placement would gain and lose (docs/04 §7).
- **Inscription overwrite and transfer.** `Weapon.ReplaceInscription` exists and nothing
  calls it; transfer was the review's fix for the Inscriptions-vs-ammo-rotation conflict.
- **`Tune.cs` still holds gameplay constants**, violating docs/09 §5. Sigils, inscriptions,
  bosses, patterns and rooms are all `.tres` now; the player and Sanity numbers are not.
- **The economy sim reports metric 5 as `[OUT]` on every run.** It was retired and replaced
  by 5b. A permanent false failure in gate output teaches people to stop reading output —
  delete it.
- **The F3 overlay reports ~54KB/tick allocation in `FloorRunner`** and labels it
  `REGRESSION`. Verified pre-existing (checked against a stashed baseline), and docs/09 §8
  claims zero. `BulletManager` itself measures zero, so it is elsewhere in the scene tick.
  Nobody has chased it.
- **Unbroken Seals** — M2 checklist, not built. Gates floor 7 access (docs/07 §4).
- **Real room templates.** 32 exist and are rectangles with authored obstacle blocks. The
  hand-built TileMap pipeline docs/11 puts on the critical path at ~4/day does not exist.

Deliberately deferred to M3 and recorded: the **Hound of Tindalos** (Corruption 5+, a new
agent that phases through walls), **Sovereign bosses** (10), **Corrupted Doors** (1+), both
**Corruption reduction sinks**, and **all audio** — docs/05 R3 telegraphs are currently
visual only, and R8's per-shot sounds do not exist.

---

## 7. Working agreements that earned their place

1. **Look at a frame before believing anything visual.** Six bugs have hidden behind healthy
   counters. This session: the boss health bar drew fifty pixels off the top of the viewport,
   and the wave telegraph was the same red ring an Awakened enemy wears.
2. **Fix the bug class, not the bug.** Sanity zero-detection became one `SetCurrent` funnel;
   wall collision became one `TileMask` shared by every hand-simulated system; door seals
   became part of that same mask rather than a second thing to remember.
3. **Parents tick before children.** Any state a child sets during its tick and a parent
   reads is invisible unless it is a consume-once latch. Four bugs so far: `AliveCount`,
   Ascension's trigger, the boss phase change, the boss death.
4. **A passing check is not a fair one.** The generation sweep asserted every flow was
   *reachable* and was satisfied while one flow served 60% of floors.
5. **Add the control with the assertion.** See §4.
6. **Measure before diagnosing, and report the number.** "Some doors stay open" was one
   corridor per floor plus a class nobody had considered; the dodge was 17 frames against a
   documented 31.
7. **When a gate breaks after a content change, the content usually drifted** — but check
   whether the *assertion* encoded a coincidence. `BanishTest` asserted "cost ≥ ceiling
   floor" because both happened to be 45; the wall gate asserted "the room centre is
   walkable" until a room was authored with a table through the middle of it.
8. **Commit messages carry the reasoning**, including wrong turns and corrected estimates.

---

## 8. Open design questions

- **Recovery-cancelling the dodge is currently disallowed**, and docs/02 §4 contains both
  sides: its Cancel row permits it "at double cost", a brake fallback F4 set to zero, while
  the same section calls the 24-frame + 0.12s cycle an invariant that "must be protected".
  I protected the cycle. If dash-cancel chaining is wanted as traversal tech, that decision
  and the §4 wording both need revisiting.
- **Ascension is very hard to reach in normal play.** Press `K` to force it. The boss grab
  is now a second route in, deliberately.
- **docs/04 §2.1 claims 41 usable cells; its own diagram has 37.** The build follows the
  diagram, so the intended oversupply pressure is slightly *higher* than §6 and docs/08 §8
  reason about. The number needs correcting, not the shape.
- **Sigil effects are a fixed modifier vocabulary, not a scripting hook.** Right for 20
  sigils, will not survive 70 — several docs/04 §5 entries (turrets, splitting projectiles,
  element swapping) are absent because they cannot be expressed. Decide before the pool grows.
- **The player outruns every enemy projectile** after the speed rescale. The lever if it
  needs correcting is pattern *density*, not bullet speed.
- **Fable's review** (`HANDOVER-FOR-REVIEW.md` §9) reclassified the Sanity bet from
  "borrowed and safe" to "novel and unvalidated". Still true, and the Circle is now a second
  unvalidated bet on top of it.
