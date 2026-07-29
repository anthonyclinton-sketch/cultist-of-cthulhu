# HANDOVER — Session Continuation

**Repo:** `github.com/anthonyclinton-sketch/cultist-of-cthulhu` (private) · branch `main`
**Local:** `C:\Users\antho\Cultist Of Cthulu`
**As of:** commit `2e9ea4f`, 22 commits · 43 C# files / ~10,100 lines · 14 `.tres` · ~44k words of docs

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
pwsh ./tools/gates.ps1              # every gate (~90s)
pwsh ./tools/gates.ps1 -Floor       # PLAY a generated floor
pwsh ./tools/gates.ps1 -Arena       # fixed-arena combat slice
pwsh ./tools/gates.ps1 -Lab         # Pattern Lab
pwsh ./tools/gates.ps1 -ShowSeed 7  # render a floor as ASCII
pwsh ./tools/gates.ps1 -Floor -MeteredDodge   # Build B, the M1 control arm
```

**Trap that has bitten twice:** Godot loads the **Debug** assembly from
`.godot/mono/temp/bin/Debug`. Building `-c Release` produces a binary Godot silently
ignores, and the gates then measure stale code. Never "optimise" the build config.

---

## 3. What is complete

### M0 — Technical foundation ✅ *gated*
- `BulletManager`: struct-of-arrays, manual circle collision, MultiMesh rendering,
  drop-shadow layer. **0.06ms p99 at 4096 bullets, zero steady-state allocation.**
- Deterministic `Rng` (xoshiro256**) + sub-seed derivation. Replay test green.
- `PlayerController`: frame-exact Blink Step (2 startup / 14 i-frame / 8 recovery), dash at
  2× move speed.

### M1 — Combat slice ✅ *systems complete, NOT playtested*
- **Sanity**: costs, kill refunds, Lucid Ceiling, band hysteresis, Open the Eye, the
  no-damage ladder.
- **Ascension**: diminishing window, debt rule, fatal default. CI-gated against farming.
- **Banish**: radius clear, knockback, stun, Corruption cost.
- **Weapons**: `WeaponData` .tres, Recitation + Perfect Recitation, 5 weapons incl. a
  Grimoire and a melee.
- **Enemies**: brain FSM, flow field, attack-token pool, 5 roles, 5 enemies.
- **Pickups**: candles (pierce the ceiling), hearts, armour, ammo, gold, drop tables + pity.
- **Telemetry**: per-room CSV, build tag, M1 metrics.
- **Build B** control arm via `--metered-dodge`.

### M2 — Floor generation ✅ *gated*
- Authored flow graphs → chain expansion → node injection → cycle decomposition →
  beam-limited placement → corridor stitching → validation.
- **10,000-seed sweep, 0 failures, 0.77% fallback rate.**
- `FloorRunner`: walkable floors, room activation, door sealing, minimap.
- Rooms are screen-relative (4–8× their first-pass area).

### Tooling
Screenshot harness (`--screenshot=<path>`, `--combat-demo`, `--melee-demo`), Pattern Lab,
ASCII floor visualiser, economy simulation, debug overlay (`F3`).

---

## 4. The gates

All in `tools/gates.ps1` and `.github/workflows/gates.yml`. **All currently green.**

| Gate | Asserts |
|---|---|
| Content validation | Every `.tres` passes its own `Validate()` |
| Ascension invariants | Cannot be farmed; spend-to-zero ≡ drain-to-zero |
| Banish | `ClearRadius` leaves no survivors; cost gates on band |
| Floor generation | 10k seeds, every invariant, fallback ≤1% |
| Playable floor smoke | Boots and runs on several seeds |
| Bullet performance | 4096 bullets, sim p99 ≤0.4ms, zero alloc |
| Determinism | Same seed → identical state, 1800 ticks |
| Economy sim *(advisory)* | Metric 1 / 9 / 5b in target |

**Know their blind spot.** Four bugs in a row were invisible to every gate: invisible
player, door trap, hit-stop freeze, and bullets culled outside the spawn room. A headless
run has no framebuffer and never moves, so *anything visual or input-driven passes
trivially*. Use the screenshot harness and short manual passes after scene-level changes.

---

## 5. Next focus, in order

### 5.1 Run the M1 playtest — **this is the priority**
Everything M1 exists to answer is still unanswered. The full design is in
`docs/11-roadmap.md` § "M1 TEST DESIGN": 10–12 external testers, ~25 min each, **both
arms** (`-Floor` and `-Floor -MeteredDodge`), counterbalanced.

The economy simulation says the numbers are in the right neighbourhood (39.1% time below
40, ladder fires 77% of rooms). It **cannot** say whether any of it is fun.

Watch for: metric 6 (does anyone Open the Eye unprompted?), metric 7 (does the free dodge
beat the metered one?), and the qualitative Pathogenic failure mode — *"being forced to
stop shooting breaks the rhythm."* Three testers saying that routes to fallback F1
regardless of the numbers.

### 5.2 Finish M2 content
The generator works; the rooms it places are **placeholder rectangles**. In priority order:
1. **Reward / shop / shrine rooms contain nothing.** They announce themselves and are
   empty. The Sigil Circle, chests and the Inscription Bench are the M2 deliverable and
   none of them exist.
2. **Boss 1** — The Thing on the Doorstep (`docs/05` §7). No boss exists at all.
3. **Real room templates.** `docs/11` puts authoring on the critical path at ~4/day;
   nothing built so far substitutes for it.

### 5.3 Known gaps worth closing early
- **Bullets pass through walls.** No wall collision for projectiles — noticeable now that
  rooms are big.
- **Enemies pass through walls.** They steer by flow field with no collision.
- `Tune.cs` still holds gameplay constants, violating `docs/09 §5`. Deliberate scaffolding;
  migrate to `.tres` when touching balance.
- `docs/AUDIT-spec-vs-code.md` lists everything specified-but-unbuilt. **Re-run that sweep
  at the end of every milestone** — it exists because three systems were found inert.

---

## 6. Working agreements that earned their place

1. **Look at a frame before believing a rendering fix.** `--screenshot=<path>` exists for
   this; four bugs hid behind healthy-looking counters.
2. **Fix the bug class, not the bug.** The Sanity zero-detection bug became a single
   `SetCurrent` funnel; the invisible player became `PlayerVisual` owned by the controller.
3. **Feel parameters get live knobs, not constants.** `F7` cycles hit-stop weight. Numbers
   written by someone who has not played the build are a starting point.
4. **When a gate breaks after a content change, the content usually drifted** — but check
   whether the *assertion* encoded a coincidence. `BanishTest` asserted "cost ≥ ceiling
   floor" purely because both happened to be 45.
5. **Commit messages carry the reasoning**, including wrong turns. Several of this
   session's fixes were right for different reasons than predicted, and that is recorded.

---

## 7. Open design questions

- **Ascension is very hard to reach in normal play** (die at 6 hits, need 10 to drain
  Sanity). Press `K` to force it. Is the game's signature moment too rare to ever be seen?
- **The player now outruns every enemy projectile** after the speed rescale. The lever if
  it needs correcting is pattern *density*, not bullet speed.
- **Metric 5 was retired** as measuring the wrong thing post-Lucid-Ceiling; 5b (income
  wasted at the cap) replaced it. See `docs/11`.
- **Fable's review** (`HANDOVER-FOR-REVIEW.md` §9) reclassified the Sanity bet from
  "borrowed and safe" to "novel and unvalidated". That is still true.
