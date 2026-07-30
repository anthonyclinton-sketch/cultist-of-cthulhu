# HANDOVER — Session Continuation

**Repo:** `github.com/anthonyclinton-sketch/cultist-of-cthulhu` (private) · branch `main`
**Local:** `C:\Users\antho\Cultist Of Cthulu`
**As of:** commit `baceece`, 59 commits · 74 C# files / ~19,000 lines · 76 `.tres` · 17 debug scenes

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
arrive with (docs/07 §5). Not an endless descent. **Two floors of content exist**, so
`RunState.FinalFloor` defaults to 1 and `--floors=N` / `-StartFloor N` reach the rest.

---

## 2. Environment

Godot is **not** on PATH. It lives at:

```
C:\Users\antho\Downloads\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe
```

`tools/gates.ps1` finds it automatically, or set `$env:GODOT`. **Always launch through the
script** — a raw quoted path in PowerShell is a string literal, not a command, and silently
does nothing. That has now bitten three times, once in a handover written by the very session
that had just fixed it.

The script resolves the project root from its own location, so it works from **any** working
directory when given by absolute path:

```bash
pwsh "C:\Users\antho\Cultist Of Cthulu\tools\gates.ps1" -Floor
```

```bash
pwsh ./tools/gates.ps1                        # every gate (~6 min)
pwsh ./tools/gates.ps1 -Floor                 # PLAY a run
pwsh ./tools/gates.ps1 -Floor -StartFloor 2   # begin ON the Wharfs, no floor-1 boss first
pwsh ./tools/gates.ps1 -Floor -Floors 3       # a three-floor run
pwsh ./tools/gates.ps1 -Floor -FloodDemo      # flood every room, to SEE the Tide
pwsh ./tools/gates.ps1 -Floor -Corruption 3   # start Corrupted (3 awakens, 10 = Yellow Sign)
pwsh ./tools/gates.ps1 -Floor -Autorun        # WATCH the run play itself
pwsh ./tools/gates.ps1 -Arena                 # the fixed-arena combat slice
pwsh ./tools/gates.ps1 -ShowSeed 7            # render a floor as ASCII
pwsh ./tools/gates.ps1 -Floor -MeteredDodge   # Build B, the M1 control arm
```

Controls: **WASD** move · **LMB** fire · **SPACE** dash · **R** recite · **RMB** banish ·
**E** interact · **TAB** Reverie · **M** map · **F3** overlay. Debug: **K** forces
Ascension, **G** refills Sanity, **F7** cycles hit-stop weight, **F5** dumps telemetry.

**Trap that has bitten twice:** Godot loads the **Debug** assembly from
`.godot/mono/temp/bin/Debug`. Building `-c Release` produces a binary Godot silently
ignores, and the gates then measure stale code. Never "optimise" the build config.

**Second trap, new:** the bullet-performance gate is sensitive to background load. If it
fails oddly, look for stray `Godot_v4.7*` processes from an earlier windowed capture before
believing it.

### Looking at a frame

```bash
# any room ROLE or TEMPLATE ID: Reward, Shop, Boss, wharf_hydra_hall, wharf_tide_bend, ...
godot --path . res://scenes/debug/FloorRunner.tscn --seed 3 --start-floor=2 \
      --room-demo=wharf_hydra_hall --screenshot=out.png --screenshot-after=610
```

`--room-demo` accepts a template id as well as a role — by role, a floor with nine CombatMed
rooms shows whichever the generator placed first, so the room you are working on is the one
you cannot see. Given a template id it also suppresses the map, because naming a room means
wanting to look at that room. It makes the player invulnerable (a capture is a photograph of
geometry, not a survival test) and hides the F3 overlay two frames early on purpose —
`GetImage` reads the framebuffer rendered *before* the current tick.

---

## 3. What is complete

### M0 — Technical foundation ✅ *gated*
`BulletManager` at **0.13ms p99 / 4096 bullets** against a 0.40 budget, zero steady-state
allocation. Deterministic `Rng` (xoshiro256**). Frame-exact Blink Step.

### M1 — Combat slice ✅ *systems complete, NOT formally playtested*
Sanity economy, Ascension with the debt rule, Banish, 5 weapons, pickups, telemetry, and the
`--metered-dodge` control arm.

### M2 — Floor generation ✅ *gated*
Authored flows → chain expansion → injection → cycle decomposition → beam-limited
placement → corridor stitching → validation. **10,000 seeds across all six floors, 0
failures, 0.09% fallback.** 40 templates, flood-validated so no obstacle can seal a room.

### M2 — Systems slice ✅ *built, played*
Sigil Circle, the Reverie, room content, 15 inscriptions, the run loop (`RunState`),
Corruption thresholds, the Dread Budget with kill-triggered waves.

### Floor 1 — Arkham Undercroft ✅
33 room templates, 5 enemies, The Thing on the Doorstep.

### Floor 2 — The Drowned Wharfs ✅ *structurally complete, lightly played*
- **The Tide** (docs/07 §3). `TideCycle` is WHEN — one 20s clock, run-scoped, a raised cosine
  so the extremes dwell. `TideField` is WHERE — per-tile flood levels, a sibling of
  `TileMask` rather than a bit inside it. Neither knows about the other, which is what makes
  "synchronised across the floor" true by construction rather than by everyone remembering to
  use the same number.
- **Water is authored per template** as five ints (`x, y, w, h, flood level`), and is never
  solid — making it solid at high tide would seal rooms on a timer, which is the one thing
  the generator's validation exists to prevent.
- **The asymmetry:** the same water that costs the player ×0.7 buys a Deep One ×2.0. Applied
  at the single integration point in `Enemy.Move`, not at the nine sites reading `MoveSpeed`.
- **The dash is slowed too** (×0.7 velocity; frame data untouched) or the tide is optional —
  a free dodge crossed water faster than wading *and* with 14 i-frames.
- **6 Wharf combat rooms**, each a different water shape: a bend, two parallel runs, a centre
  pool, a full-width cut, a crossing, a corner. Every combat room on floor 2 has water.
- **Deep One and Brine Priest.** `Bestiary` routes enemies by floor; `RoomLibrary` and
  `FloorTag` route rooms; `BossRoster` routes bosses.
- **Mother Hydra's Brood** in `wharf_hydra_hall`: a matriarch submerged at high tide and a
  consort submerged at low, so exactly one is hittable at any moment. A submerged boss is not
  registered as a bullet target at all, so shots pass *through* it rather than stopping dead
  on something that takes no damage.

### Quality of life
- **×2 movement out of combat**, guarded by five independent measures (§7.6).
- **Nothing spawns within 90px of the player**, wave one telegraphs like the rest, and a wave
  is spread across the room rather than stacked on one anchor.

---

## 4. The gates

18 gates in `tools/gates.ps1` and `.github/workflows/gates.yml`. **All currently green.**

| Gate | Asserts |
|---|---|
| Content validation | Every `.tres` passes `Validate()`, plus sigil-pool rules |
| Ascension invariants | Cannot be farmed; spend-to-zero ≡ drain-to-zero |
| Banish | `ClearRadius` leaves no survivors; cost gates on band |
| **Autorun** | A whole run played headlessly: every room, the boss, the summary |
| **Autorun — 3 floors** | A descent does not silently reset the run |
| **Death drill** ×2 | A run that ends badly still ENDS — between rooms, and mid-boss |
| **Encounters** | Budget responds; waves never timed; nothing spawns on the player; a wave is spread |
| **Blink frame data** | 2/14/8 and a 31-frame cycle, measured from the controller |
| **Corruption** | Severity never falls as Corruption rises |
| **The Tide** | The cycle, the shoreline, the wade/swim asymmetry, the dash, the brood |
| **Boss 1** | Every phase reached and firing; the grab connects and is rate-limited |
| **Wall collision** | Nothing occupies solid ground; sealed rooms hold; enemies move |
| Floor generation | 10k seeds over 6 floors, every invariant, room-count bands, fallback ≤1% |
| Playable floor smoke | Boots and runs on several seeds |
| Engine warning budget | Zero — a per-frame warning reads as a freeze |
| Bullet performance | 4096 bullets, sim p99 ≤0.4ms, zero alloc |
| Determinism | Same seed → identical state, 1800 ticks, six seeds |
| Economy sim *(advisory)* | Metrics 1 / 9 / 5b in target |

### Know the blind spots, and know they move

**A green suite is not a played build.** This session a fully green suite shipped: enemies
spawning on top of the player, an entire wave stacked on one anchor, and 254MB of exception
log per run. All three were found by playing. Every gate passed throughout.

**The three bugs the gates could not see, and why:**

- **Enemies spawning on the player.** The minimum-distance rule existed and sat *below* an
  early return that the first wave took. The one wave that arrives while you are still walking
  through a door was the one that never checked where you were.
- **A whole wave on one anchor.** Anchor choice scanned every anchor and returned the closest
  acceptable one; its `index` argument rotated where the scan *began*, and rotating a scan
  changes tie-breaking, never a minimum. Waves 2 and 3 had done this since they were written —
  it only became visible when wave 1 joined them.
- **254MB of log per run.** `GetTree().Quit()` does not stop the world, so a capture trigger
  changed from `==` to `<` re-entered on every remaining tick and threw each time. Invisible in
  every way that matters: after the useful output, during shutdown, exit code 0, PNG already
  written. **No gate reads the log directory.**

**When you add an assertion, add its control — and prove the assertion fails.** Twice this
session an assertion was deliberately broken to watch it go red: the exploration-speed audit
(sabotaged, 1024 discrepancies) and the tide's swim multiplier. A third, *"the harness was
actually shot at"*, turned out to be **flaky** — it depended on enemy aim, failed on a healthy
build on seed 7, and had to be replaced twice before landing on spawn counts.

**Beware assertions that derive their expectation from the code.** The tide gate checks the
swim multiplier against `Tune`, so setting that constant to 1 makes it print
`x1.00 (want x1.00)` and pass. Only the absolute check beside it catches a bad number.

---

## 5. What to build next

### 5.1 Finish floor 2's content — the obvious next step

Four Wharf enemies from docs/05 §3 are unbuilt: **Hybrid Fisherman** (hooks that pull),
**Drowned Chorus** (three linked; killing one buffs the others), **Anglerhead** (douses the
room lights). The Chorus and the Anglerhead carry new behaviour; the Fisherman is data against
systems that already exist.

**Non-combat Wharf rooms do not exist**, so a Wharf floor still has a cellar for a shop, a
cellar for a shrine and a cellar for an entrance. `PickTemplate` prefers the floor's theme and
falls back, which is what makes that work — a strict filter would fail generation on every
floor whose set is incomplete, which is all of them.

**The Ferryman of the Manuxet** (docs/05 §7) — floor 2's Warden, offering passage for +2
Corruption or a fight. Not built; no Warden system exists at all.

### 5.2 The difficulty curve — half done, and the remaining half is content

The floor term now **multiplies** rather than adds (`FloorScaling.DreadMultiplier`). Measured
effect on how much of a floor runs at maximum pressure:

```
floor 1    never (280/320)  ->  never (280/320)
floor 3    5% of the floor  ->  35%
floor 4    11%              ->  50%
floor 6    NEVER (298/320)  ->  53%
```

**But the measurement reframed the item.** Five of six floors sit AT `ThreatCapacity` — the
formula asks for more Dread and the room physically cannot hold it. Floor 1 already peaks at
280 of 320 and plays well there, so **there is no headroom above a well-tuned floor 1 for five
more floors of "more enemies"**. Per-room Dread is not where the descent can live.

Past the clamp only two levers remain: **bigger rooms**, or **enemies worth more per point**.
Floors 3–6 have no authored rooms, so that is where the headroom has to come from. The
encounter gate prints where the ceiling binds per floor — read it before touching the formula
again, because tuning `baseline` now mostly moves a number that gets clamped.

### 5.3 Floors 3–6

`FloorScaling` already scales attack tokens (4→9), damage (×2 from floor 3), room counts and
Dread across all six. `Bestiary`, `RoomLibrary`, `BossRoster` and `FloorScaling.ThemeTag` all
take a floor and return content, so adding a floor is authoring plus one row in each table.

### 5.4 Save/load

On the M2 checklist and not built — a run ends when the process does. `RunState` holds no
scene references and now records `StartFloor`, so this is mostly serialisation. A run is ~12
minutes at two floors; it becomes urgent at six.

### 5.5 The M1/M2 playtest — deferred by the owner, deliberately

Design in `docs/11-roadmap.md` § "M1 TEST DESIGN": 10–12 testers, ~25 min each, **both arms**,
counterbalanced. Watch for metric 6 (does anyone Open the Eye unprompted?), metric 7 (does the
free dodge beat the metered one?), and the Pathogenic failure mode — *"being forced to stop
shooting breaks the rhythm."*

**Telemetry is finally fit for this.** It records the room a run died in and what killed it,
which it did not before: a failed run used to write every room survived and nothing at all
about the one that ended it.

---

## 6. Smaller open items

`docs/AUDIT-spec-vs-code.md` § "M2 sweep" is the authority; re-run it at the end of every
milestone. The ones that matter:

- **`Element` is authored and read by nothing.** `Element = Brine` is set on patterns, and no
  bullet carries an element — so Brine attacks do not Drench, and
  `PlayerController.IncomingLightningMultiplier` is a property with no caller. Wiring it means
  widening the bullet struct the M0 performance gate measures: a decision, not a tidy-up.
  **This is the fifth "specified, believed present, absent" in this project.** The other four
  were attack tokens, damage scaling, the enemy roster and `FloorTag` — all four found by
  someone going to look, none by a gate.
- **An unexplained illegal-instruction exit.** Seen once, on a floor-3 death, then never again
  — because the run started winning, not because anything was fixed. The death path is now
  gated both ways (16 deliberate deaths, no crash), so a regression has somewhere to land.
- **Directional sigils do nothing directionally.** docs/04 §3.2's orientation layer is
  scaffolding: the facing is stored, rotates with the tile and is drawn, and no effect reads it.
- **The Reverie's "live diff panel" is not a diff.** It shows the state after committing, not
  what a placement would gain and lose (docs/04 §7).
- **Inscription overwrite and transfer.** `Weapon.ReplaceInscription` exists and nothing calls it.
- **`Tune.cs` still holds gameplay constants**, violating docs/09 §5.
- **The F3 overlay reports ~54KB/tick allocation in `FloorRunner`** and labels it `REGRESSION`.
  Verified pre-existing; `BulletManager` itself measures zero. Nobody has chased it.
- **Unbroken Seals** — M2 checklist, not built. Gates floor 7 access (docs/07 §4).
- **Real room templates.** 40 exist and are rectangles with authored obstacle blocks. The
  hand-built TileMap pipeline docs/11 puts on the critical path does not exist — but
  `wharf_tide_bend` proved the rectangle system *can* express a channel, so the fork is now a
  choice rather than a guess.

Deferred to M3 and recorded: the **Hound of Tindalos**, **Sovereign bosses**, **Corrupted
Doors**, both **Corruption reduction sinks**, and **all audio**.

---

## 7. Working agreements that earned their place

1. **Look at a frame before believing anything visual.** The waterline drew in exactly one room
   per floor and looked correct, because `--flood-demo` makes every room identical. A stressor
   that makes everything the same is good at proving code runs and bad at proving it is right.
2. **Fix the bug class, not the bug.** `Boss.Submerged` folded into the existing `Invulnerable`
   property rather than sitting beside it, so the tide rule reached `TakeDamage`, target
   registration, contact damage and the HUD for free. A parallel flag would have needed four
   updates by hand, and the one that got missed would have been the interesting bug.
3. **Parents tick before children.** Any state a child sets during its tick and a parent reads
   is invisible unless it is a consume-once latch.
4. **A passing check is not a fair one.** "Every floor inside its docs/07 §2 band" passes on a
   generator that ignores the floor index entirely, because every band overlaps its neighbour.
   It needs the companion check that the distributions actually moved.
5. **Add the control with the assertion, and prove the assertion fails.** Sabotage it and watch
   it go red. Three assertions this session were wrong in ways only that revealed.
6. **Measure before diagnosing, and report the number.** Raising `MaxBacktracks` was the obvious
   fix for a rising fallback rate; it bought 0.2 points for 75% more sweep time. The real cause
   was that expansion spent its budget on chains inside loops — acyclic chains first took it to
   0.09% and made the sweep *faster*.
7. **When a gate breaks after a content change, the content usually drifted** — but check
   whether the *assertion* encoded a coincidence. "The grab must cost Sanity" failed Mother
   Hydra for not having a mechanic she was never given: one boss's design written as universal
   law.
8. **A singular assumption is cheap to fix before there is a second thing.** The enemy roster,
   the template pool and the boss slot were each a flat list with no floor concept, and each was
   fixed *before* the second floor's content rather than after.
9. **Commit messages carry the reasoning**, including wrong turns and corrected estimates.
10. **End every piece of work with the command to run it**, routed through `gates.ps1`. If no
    switch exists for the thing just built, add one rather than explaining a workaround.

---

## 8. Open design questions

- **Recovery-cancelling the dodge is currently disallowed**, and docs/02 §4 contains both sides.
  The cycle was protected. Note the out-of-combat ×2 walk now makes walking faster than
  dash-chaining, which removes most of the traversal argument for allowing the cancel.
- **Ascension is very hard to reach in normal play.** Press `K` to force it.
- **docs/04 §2.1 claims 41 usable cells; its own diagram has 37.** The build follows the
  diagram, so the intended oversupply pressure is slightly higher than §6 reasons about. The
  number needs correcting, not the shape.
- **Sigil effects are a fixed modifier vocabulary, not a scripting hook.** Right for 20 sigils,
  will not survive 70. Decide before the pool grows.
- **Floor 2's difficulty is unvalidated past a couple of playthroughs.** The tide, the Deep
  One's ×2 swim and the ×2 damage from floor 3 all landed this session without a tuning pass.
  If floor 2 feels punishing, the first lever is the Deep One's `TideSwimSpeedMultiplier`, not
  the wade — the wade number is what makes the tide legible, the swim is what makes it lethal.
- **Fable's review** (`HANDOVER-FOR-REVIEW.md` §9) reclassified the Sanity bet from "borrowed
  and safe" to "novel and unvalidated". Still true, and the Circle and the Tide are two more
  unvalidated bets stacked on top of it.
