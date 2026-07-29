# HANDOVER — Session Continuation

**Repo:** `github.com/anthonyclinton-sketch/cultist-of-cthulhu` (private) · branch `main`
**Local:** `C:\Users\antho\Cultist Of Cthulu`
**As of:** commit `482d820`, 27 commits · 58 C# files / ~13,400 lines · 60 `.tres` · ~48k words of docs

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

---

## 2. Environment

Godot is **not** on PATH. It lives at:

```
C:\Users\antho\Downloads\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe
```

`tools/gates.ps1` finds it automatically, or set `$env:GODOT`.

```bash
pwsh ./tools/gates.ps1              # every gate (~4 min)
pwsh ./tools/gates.ps1 -Floor       # PLAY a generated floor
pwsh ./tools/gates.ps1 -Arena       # fixed-arena combat slice
pwsh ./tools/gates.ps1 -Lab         # Pattern Lab
pwsh ./tools/gates.ps1 -ShowSeed 7  # render a floor as ASCII
pwsh ./tools/gates.ps1 -Floor -MeteredDodge   # Build B, the M1 control arm
```

**Trap that has bitten twice:** Godot loads the **Debug** assembly from
`.godot/mono/temp/bin/Debug`. Building `-c Release` produces a binary Godot silently
ignores, and the gates then measure stale code. Never "optimise" the build config.

### Looking at a frame

The screenshot harness is the only way to see anything the headless gates cannot. It has
grown; these are the modes:

```bash
# any room role: Reward, Shop, Shrine, Secret, Hub, Boss, CombatHard, ...
godot --path . res://scenes/debug/FloorRunner.tscn --seed 7 --room-demo=Shop \
      --screenshot=out.png --screenshot-after=70

godot --path . res://scenes/debug/FloorRunner.tscn --seed 7 --reverie-demo \
      --screenshot=out.png --screenshot-after=70

godot --path . res://scenes/debug/FloorRunner.tscn --seed 7 --combat-demo \
      --screenshot=out.png --screenshot-after=120
```

`--room-demo` hides the F3 overlay, snaps the camera and stands the player on the first
interactable so the prompt is in frame. Note: **not every floor has a shop** — it is a 70%
roll on floor 1 (docs/08 §2.1), so a missing-room message means try another seed.

---

## 3. What is complete

### M0 — Technical foundation ✅ *gated*
- `BulletManager`: struct-of-arrays, manual circle collision, MultiMesh rendering.
  **0.19ms p99 at 4096 bullets against a 0.40 budget, zero steady-state allocation.**
  (Was 0.06 before wall collision joined the tick.)
- Deterministic `Rng` (xoshiro256**) + sub-seed derivation. Replay test green.
- `PlayerController`: frame-exact Blink Step, dash at 2× move speed.

### M1 — Combat slice ✅ *systems complete, NOT playtested*
Sanity economy, Ascension, Banish, 5 weapons, 5 enemies, pickups, telemetry, and the
`--metered-dodge` control arm. Unchanged this session except where sigils touch it.

### M2 — Floor generation ✅ *gated*
- Authored flow graphs → chain expansion → node injection → cycle decomposition →
  beam-limited placement → corridor stitching → validation.
- **10,000-seed sweep, 0 failures, 0.07% fallback, flows used 34/34/32.**
- 32 room templates with **authored interiors** — pillars, tombs, a long table — validated
  by flood fill so no obstacle can seal a room or block a door.

### M2 — Systems slice ✅ *built this session, NOT playtested*
- **Sigil Circle** (docs/04): 7×7 corner-cut grid, locked Heart, three ley lines rolled per
  run, 7 polyomino shapes with rotation and mirroring, tag-based adjacency synergies,
  Reliquary, dissolution. 20 sigils + 2 Hearts. Balance rules §8.1–§8.7 enforced in
  `Validate()` and gated in CI.
- **The Reverie** (Tab): pauses, refuses to open while doors are sealed, states why a
  placement is illegal, auto-arranges badly on purpose.
- **Room content**: reward rooms offering a choice of two sigils (three at Corruption 3+),
  Gaunt's stall with sigils/consumables/bench/reroll/Dissolution Bowl, four shrines,
  tiered chests, the guaranteed connector key chest.
- **Inscriptions** (docs/03 §3): 15, held per weapon and projected into effective stats on
  read, with conflict groups.
- **Boss 1 — The Thing on the Doorstep**: all three phases, the Sanity-costing grab, timed
  adds, phase transitions, a HUD boss bar.

### Tooling
Screenshot harness (see §2), Pattern Lab, ASCII floor visualiser, economy simulation,
debug overlay (`F3`).

---

## 4. The gates

All in `tools/gates.ps1` and `.github/workflows/gates.yml`. **All currently green.**

| Gate | Asserts |
|---|---|
| Content validation | Every `.tres` passes its own `Validate()`, plus sigil-pool rules |
| Ascension invariants | Cannot be farmed; spend-to-zero ≡ drain-to-zero |
| Banish | `ClearRadius` leaves no survivors; cost gates on band |
| **Boss 1** | Every phase reached and firing; the grab connects and is rate-limited |
| **Wall collision** | Nothing occupies solid ground, over four real floors, incl. tunnelling |
| Floor generation | 10k seeds, every invariant, fallback ≤1% |
| Playable floor smoke | Boots and runs on several seeds |
| Engine warning budget | Zero — a per-frame warning reads as a freeze |
| Bullet performance | 4096 bullets, sim p99 ≤0.4ms, zero alloc |
| Determinism | Same seed → identical state, 1800 ticks |
| Economy sim *(advisory)* | Metric 1 / 9 / 5b in target |

**Know their blind spot, and know that it moved.** The old rule was "anything visual or
input-driven passes trivially". That is still true of *rendering*, but two of this
session's bugs were things that LOOKED visual and were not:

- Bullets and enemies walking through walls is a **positional invariant**, and is now gated.
- A boss phase change that never fired is an **ordering** bug, and is now gated.

The remaining blind spot is genuinely what is on screen. Use the harness in §2.

---

## 5. Next focus, in order

### 5.1 Run the M1/M2 playtest — deferred by the owner, deliberately
The previous handover made this the priority; the call this session was to defer it until
more of the game exists, which it now does. Everything M1 exists to answer is still
unanswered, and M2 has added a second unvalidated bet on top of it.

Design is in `docs/11-roadmap.md` § "M1 TEST DESIGN": 10–12 external testers, ~25 min each,
**both arms** (`-Floor` and `-Floor -MeteredDodge`), counterbalanced.

Watch for: metric 6 (does anyone Open the Eye unprompted?), metric 7 (does the free dodge
beat the metered one?), and the qualitative Pathogenic failure mode — *"being forced to
stop shooting breaks the rhythm."* Three testers saying that routes to fallback F1
regardless of the numbers.

**New for M2:** nobody has played a floor with a Circle in it. The whole loot loop —
pick up a sigil, open Reverie, decide what to cut — is untested, and the Circle is the
system the design calls signature.

### 5.2 Close the M2 gaps the audit names
`docs/AUDIT-spec-vs-code.md` § "M2 sweep" is current as of this session. The ❌s that
matter most, in order:

1. **Directional sigils do nothing directionally.** The facing is stored, rotates and is
   drawn, and no effect reads it. docs/04 §3.2's orientation layer is scaffolding.
2. **The Reverie's "live diff panel" is not a diff.** It shows the state after committing,
   not what a placement would gain and lose.
3. **Inscription overwrite and transfer.** `ReplaceInscription` exists and nothing calls
   it; transfer was the review's fix for the Inscriptions-vs-ammo-rotation conflict.
4. **Save/load and run state.** A run ends when the process does.
5. **Real room templates.** 32 exist and they are still rectangles with authored obstacle
   blocks. docs/11 puts hand-built rooms on the critical path at ~4/day and nothing here
   substitutes for that pipeline.

### 5.3 Known gaps worth closing early
- `Tune.cs` still holds gameplay constants, violating `docs/09 §5`. Sigils, inscriptions,
  bosses and rooms all moved to `.tres` this session; the player and Sanity numbers did not.
- **Boss numbers are a first pass.** 900 HP, phases at 62%/28%, a 4.5s grab cooldown —
  all written by someone who has not played the fight.
- The economy sim's **metric 5 is still reported and still stale**; 5b replaced it. It says
  `[OUT]` every run and should be removed rather than ignored.

---

## 6. Working agreements that earned their place

1. **Look at a frame before believing a rendering fix.** Four bugs hid behind healthy
   counters; this session added two more. The boss health bar drew fifty pixels off the top
   of the viewport and every assertion about it passed.
2. **Fix the bug class, not the bug.** Sanity zero-detection became one `SetCurrent`
   funnel; the invisible player became `PlayerVisual` owned by the controller; wall
   collision became one `TileMask` shared by every hand-simulated system.
3. **Parents tick before children.** Any state a child sets during its tick and a parent
   reads is invisible unless it is a consume-once latch. This has now caused three separate
   bugs — `AliveCount`, Ascension's trigger, and the boss phase change.
4. **Feel parameters get live knobs, not constants.** `F7` cycles hit-stop weight.
5. **When a gate breaks after a content change, the content usually drifted** — but check
   whether the *assertion* encoded a coincidence. `BanishTest` asserted "cost ≥ ceiling
   floor" because both happened to be 45; the wall gate asserted "the room centre is
   walkable" until a room was authored with a table through the middle of it.
6. **A passing check is not a fair one.** The generation sweep asserted every flow was
   *reachable* and was satisfied while one flow served 60% of floors.
7. **Commit messages carry the reasoning**, including wrong turns.

---

## 7. Open design questions

- **Ascension is very hard to reach in normal play** (die at 6 hits, need 10 to drain
  Sanity). Press `K` to force it. Is the game's signature moment too rare to ever be seen?
  The boss grab is now a second route into it, which is deliberate.
- **The player outruns every enemy projectile** after the speed rescale. The lever if it
  needs correcting is pattern *density*, not bullet speed.
- **docs/04 §2.1 claims 41 usable cells; its own diagram has 37.** The build follows the
  diagram. §6 and docs/08 §8 both reason about sigil oversupply against 41, so the intended
  "you must cut things" pressure is slightly *higher* than the docs claim. The number needs
  correcting, not the shape.
- **Sigil effects are a fixed modifier vocabulary, not a scripting hook.** That was the
  right call for 20 sigils and will not survive 70 — several docs/04 §5 entries (turrets,
  splitting projectiles, element swapping) are absent because they cannot be expressed.
  Decide before the pool grows.
- **Metric 5 was retired** as measuring the wrong thing post-Lucid-Ceiling; 5b (income
  wasted at the cap) replaced it. See `docs/11`.
- **Fable's review** (`HANDOVER-FOR-REVIEW.md` §9) reclassified the Sanity bet from
  "borrowed and safe" to "novel and unvalidated". That is still true, and the Circle is now
  a second unvalidated bet sitting on top of it.
