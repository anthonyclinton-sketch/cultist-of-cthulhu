# 05 — Enemies, Bullet Patterns & Bosses

---

## 1. The Readability Contract

Bullet hell fairness is a set of hard rules, not a feeling. These are inviolable; any encounter violating one is a bug.

| # | Rule |
|---|---|
| **R1** | **Enemy projectiles are always in the cool half of the palette** — sickly green, violet, bone-white. Player projectiles are always warm — amber, ember, rust. No exceptions, ever, on any floor. |
| **R2** | Every projectile has a **1px high-contrast outline** and a bright core, so it reads against any background. Backgrounds are desaturated by 35% relative to entities. |
| **R3** | **Telegraph before volley.** Minimum 0.35s of clear wind-up animation + audio cue before any attack fires. Bosses: 0.5s minimum for new patterns. |
| **R4** | **No off-screen spawns into the play area** without a 0.6s inbound marker at the screen edge. |
| **R5** | Bullets never spawn **inside** the player. Minimum spawn distance 1.5 units, or the bullet is delayed until it can spawn legally. |
| **R6** | **Nothing is faster than a Blink Step can escape** except one deliberately named exception per floor, which is always visually distinct (a "Piercer" — bright white, thin, with a 0.8s laser telegraph line). |
| **R7** | On-screen bullet count caps at **600** for enemies. Encounter design that would exceed this is re-authored, not clamped at runtime. |
| **R8** | Audio: every enemy shot type has a distinct spawn sound, spatialised, with voice-limiting (max 6 concurrent of a type). ~~Hallucinated bullets are silent — the low-sanity tell.~~ **Silence is NOT a hallucination tell — voice-limiting and spawn-merging make real bullets routinely silent too.** See R9. |
| **R9** | **Every real projectile renders a soft offset drop-shadow on the floor plane. Hallucinated projectiles render none.** This is the single, universal hallucination tell and it is a hard requirement of the bullet shader, not a polish item. It must read on every floor including the bright ones. See [02 §3.4](02-player-and-combat.md). |

---

## 2. Enemy Taxonomy

Enemies are classified by **role**, and encounter design mixes roles rather than counts.

| Role | Function | Example count in a room |
|---|---|---|
| **Fodder** | Dies fast, refills Sanity, creates positioning pressure | 4–10 |
| **Turret** | Static or slow, fires dense patterns, must be prioritised | 1–3 |
| **Rusher** | Closes distance, punishes camping | 2–5 |
| **Zoner** | Denies areas (pools, beams, walls of bullets) | 1–2 |
| **Support** | Buffs, shields, or revives others — the priority target | 0–2 |
| **Elite** | Mini-encounter within a room; telegraphed by a purple aura | 0–1 |
| **Warden** | Floor mid-boss; gates progression | 1 per gate floor |
| **Boss** | Floor finale, multi-phase | 1 per floor |

**The Sanity feedback loop constrains this.** Rooms need enough Fodder that a competent player can fund their dodges. A room of pure Turrets is a Sanity death spiral. Rule: **every encounter must contain at least 35% Fodder by threat budget.**

---

## 3. Bestiary

### Floor 1 — Arkham Undercroft
| Enemy | Role | Behaviour |
|---|---|---|
| **Acolyte** | Fodder | Walks toward you, fires a single slow bolt every 2s |
| **Chanter** | Support | Never attacks; grants nearby enemies a shield. Visible chant beam to its target. |
| **Cellar Ghoul** | Rusher | Burrows, surfaces near you (with a 0.8s dirt-mound telegraph), lunges |
| **Tallow Man** | Turret | Immobile wax figure; fires a slow 8-way radial that drips downward |
| **Rat Swarm** | Fodder | Splits into 3 smaller swarms on death. Melee only. |
| **The Whateley Boy** | Elite | Alternates between a human form (fires a shotgun) and a partly-invisible form (charges) |

### Floor 2 — Drowned Wharfs of Innsmouth
| Enemy | Role | Behaviour |
|---|---|---|
| **Deep One** | Rusher | Swims through water tiles at 2× speed, emerges to claw |
| **Hybrid Fisherman** | Fodder | Throws hooks in a 3-shot spread; hooks pull if they hit |
| **Brine Priest** | Turret | Rotating spiral of water bolts. Rotation reverses every 4s. |
| **Netcaster** | Zoner | Throws a spreading net that slows and blocks Blink Step |
| **Drowned Chorus** | Support | Three linked enemies; killing one buffs the others. Kill order matters. |
| **Anglerhead** | Elite | Douses room lights; its lure is the only light source; fires from darkness |

### Floor 3 — Miskatonic Restricted Archives
| Enemy | Role | Behaviour |
|---|---|---|
| **Bibliovore** | Fodder | Flying book; dive-bombs in a sine wave |
| **The Indexed** | Turret | Fires *words* — projectiles that spell out a name, each letter a bullet |
| **Shelf Warden** | Zoner | Rearranges the room's bookshelves mid-fight (walls move — see §5) |
| **Censor** | Support | Erases enemy health bars and your minimap while alive |
| **Page Wraith** | Rusher | Teleports between bookshelves, always to your blind side |
| **The Redacted** | Elite | Immune to damage from the direction you're facing. Must be shot while moving away. |

### Floor 4 — The Mountains of Madness
| Enemy | Role | Behaviour |
|---|---|---|
| **Elder Thing** | Turret | 5-fold radial symmetry: fires 5 patterns simultaneously at 72° offsets |
| **Frost Larva** | Fodder | Slow; freezes the floor where it dies, creating slide zones |
| **Shoggoth Fragment** | Rusher | Splits into two smaller fragments each time it takes 40% damage |
| **Star-Spawn Cultist** | Zoner | Plants pillars that beam between each other |
| **Penguin (albino, blind, six feet tall)** | Fodder | Runs in a straight line. Harmless unless you're in it. A Lovecraft deep cut and a joke. |
| **Nameless Geometry** | Elite | A shape that is *wrong*; its hitbox does not match its sprite, and the game tells you so |

### Floor 5 — The Plateau of Leng
| Enemy | Role | Behaviour |
|---|---|---|
| **Nightgaunt** | Rusher | Silent, faceless, grabs and *carries you* to another part of the map |
| **Moon-Beast** | Turret | Fires slow, enormous, orbit-locked spheres |
| **Man of Leng** | Fodder | Numerous; only visible when you are not looking at them (outside the aim cone) |
| **Dholes** | Zoner | Enormous worms that tunnel across the open plateau, denying huge lanes |
| **Shantak** | Elite | Flying; can only be hit at the apex of its dive |

### Floor 6 — R'lyeh
| Enemy | Role | Behaviour |
|---|---|---|
| **Star-Spawn** | Elite/common | Smaller Cthulhus. Full boss-grade pattern set, deployed as regular enemies. |
| **Angle Dweller** | Rusher | Moves only along wall angles; instant traversal between any two corners |
| **The Drowned Crew** | Fodder | Endlessly respawning until their anchor-object is destroyed |
| **Non-Euclid** | Zoner | Creates a false wall that bullets pass through but you cannot |

---

## 4. Bullet Pattern Grammar

Patterns are composed, not hand-coded per enemy. A small data-driven grammar produces the whole game's bullet vocabulary.

### 4.1 Primitives

```
RADIAL(n, spread, offset)       — n bullets evenly across `spread` degrees
SPIRAL(n, rate, arc)            — a rotating emitter
AIMED(n, spread, lead)          — n bullets toward the player, with prediction lead
WALL(n, width, gapCount)        — a line with gaps
BURST(n, interval)              — n repetitions of the inner pattern
RING_IN(n, radius)              — spawns on a circle, converges inward
LASER(chargeTime, duration)     — telegraph line → beam
ARC(n, sweepDegrees, time)      — a sweeping wall
```

### 4.2 Modifiers (compose onto any primitive)

```
.Speed(curve)        — accelerate/decelerate over lifetime
.Homing(turnRate, duration)
.Split(at, into, pattern)
.Wave(amplitude, frequency)
.Delay(t)            — freeze in place then resume (the classic "pause and fire" trick)
.Reverse(at)         — travel out, then return
.Gravity(vec)
.Element(fire|brine|void|rot)
```

### 4.3 Example — the Brine Priest

```csharp
Pattern.Spiral(n: 3, rate: 55f, arc: 360f)
       .Every(0.12f)
       .Speed(Curves.EaseOutSlow)
       .Element(Element.Brine)
       .ReverseRotationEvery(4f);
```

Patterns are authored as `.tres` `PatternData` resources so designers tune them without recompiling. A **Pattern Lab** debug scene renders any pattern in isolation with a ghost player for readability checks — build this in Milestone 1, it pays for itself immediately.

---

## 5. Room Hazards

Per-floor environmental systems that make rooms distinct beyond enemy lists.

| Floor | Hazard |
|---|---|
| 1 | **Candle pits** (damage + light), **collapsing floorboards**, tables to flip for cover (destructible after 3 hits) |
| 2 | **Rising tide** — water level oscillates on a 20s cycle; deep water slows you and speeds Deep Ones |
| 3 | **Shifting shelves** — walls physically move on a timer, changing cover and lines of sight mid-fight |
| 4 | **Ice** (reduced friction, the *one* place we allow it), **blizzard zones** (vision radius shrinks to 6 units) |
| 5 | **No walls** — open plateau; instead, **gravity wells** and **dream-fog** that hides the minimap |
| 6 | **Non-euclidean seams** — walking through certain gaps teleports you to a mirrored part of the room |

---

## 6. Boss Design Template

Every boss follows this structure. Deviating requires a written reason.

```
INTRO (2.5s, skippable after first kill)
  ↓
PHASE 1 — "The Grammar Lesson"
  3 patterns, telegraphed generously. Teaches the boss's vocabulary.
  60% → 100% HP
  ↓
TRANSITION (1.2s, invulnerable, full-screen clear of bullets)
  ↓
PHASE 2 — "The Combination"
  Same 3 patterns, faster, plus 2 new ones, and patterns now overlap.
  25% → 60% HP
  ↓
TRANSITION
  ↓
PHASE 3 — "The Desperation"
  One signature attack the boss has not shown, plus everything at max speed.
  Add-spawning for Sanity refill (mandatory — the player must be able to fund dodges).
  0% → 25% HP
  ↓
[ if Corruption ≥ 7 ] PHASE 4 — "The Awakening"
  ↓
DEATH (3s, slow-mo, room lights change)
  ↓
REWARD: sigil (tier scales with Corruption) + gold + heart
  UNBROKEN SEAL if no damage taken this fight → +1 max heart, permanent for the run
```

**Mandatory properties:**
- **Every boss has a Sanity source.** Adds, breakable objects, or a phase-transition refill. A boss you cannot fund your dodges against is unbeatable, not hard.
- **Every boss has a safe lane.** For every pattern there exists a positioning solution requiring no Blink Step. **Executing it perfectly is not expected to be free** — safe lanes are narrow, require committed movement, and cost damage uptime. *(Revised — see note.)*
- **[REVIEW — added] Per-pattern, not per-phase.** The safe lane must exist for each pattern *in isolation*; where two patterns overlap (Phase 2+), the design may require a Blink Step. This is the intended place for the Sanity economy to bite in a boss fight.

> **[REVIEW — Fable] The old clause "experts should be able to clear bosses at near-full Sanity" was the single most damaging sentence in the design, and it has been cut.**
> Taken literally it says: *at the skill ceiling, the game's central resource is not a constraint.* Three consequences followed from it:
> 1. **It repeals Pillar I at exactly the skill level the game is built for.** "Every dodge is a purchase" becomes "every dodge is a purchase, until you are good, and then it is free."
> 2. **It inverts the damage curve.** An expert sitting at near-full Sanity is **Lucid** — the only band with *no* damage bonus. The better the player, the less damage they do, and the less they ever see of Pillar III. Combined with [02 §3.5](02-player-and-combat.md), expert play is penalised on both axes at once.
> 3. **It makes the boss Sanity sources pointless.** If the fight can be cleared without spending, the mandated adds and refills are dead content.
>
> The revised rule keeps the *fairness* guarantee the original was reaching for — a player is never forced to spend Sanity they cannot afford — without promising that skill makes the resource irrelevant. **The fairness floor is now the mandated Sanity source, not the absence of cost.**
- **Bosses do not heal.**
- **Boss HP bars are segmented by phase** so the player can see the fight's shape.

---

## 7. The Bosses

### F1 — **The Thing on the Doorstep** *(Undercroft)*
A cultist whose body is being worn by something else. Phase 1 is a *human* fight — pistol shots, dodges, taunts. Phase 2 the host loses control mid-sentence and the body inverts. Phase 3 it abandons the corpse and attacks as a formless possessing entity that tries to enter *you* (a grab that, if it connects, costs 30 Sanity rather than health).
*Teaches:* dodge timing, telegraph reading.

### F2 — **Mother Hydra's Brood** *(Innsmouth)*
Two bosses. A colossal Deep One matriarch fixed at the far end of a flooded hall, and her consort who circles. The tide mechanic runs throughout — at high tide the matriarch is submerged (invulnerable) and the consort is fast; at low tide the reverse. **The player must fight the right one at the right time.**
*Teaches:* target prioritisation, environmental awareness.

### F3 — **The Librarian** *(Miskatonic)*
A robed figure of stacked books. Fights by *citing* — each attack is announced by a call number that appears on screen, and repeated call numbers use the same pattern. A player who reads the citations can pre-position. Phase 3: it starts reading *your* Codex aloud and fires patterns based on weapons **you are currently carrying**.
*Teaches:* pattern memory. Also the game's best joke.

### F4 — **The Shoggoth** *(Mountains of Madness)* — **Warden gate**
A boss that is a *room*. The shoggoth fills the arena and the fightable space shrinks and grows. It has no health bar; instead it has **nine eyes** that must be destroyed, and it regrows one every 12s. Tekeli-li. The modal death of the run happens here — it's the intended skill checkpoint.
*Teaches:* damage prioritisation under spatial pressure.

### F5 — **Nyarlathotep, the Crawling Chaos** *(Leng)*
Not one entity. Three **avatars** appear across the open plateau — the Black Pharaoh, the Bloated Woman, the Haunter of the Dark — and you must find and defeat all three, in any order, while they hunt you across the open map. Whichever you leave for last has absorbed the others' patterns. **This is the boss fight for the floor that has no rooms.**
*Teaches:* everything so far, without arena boundaries to help you.

### F6 — **Cthulhu** *(R'lyeh)*
Four phases. Phase 1: you fight his *dreaming*, an intangible projection, in a room that is not obeying geometry. Phase 2: he wakes, and the arena becomes a sinking platform. Phase 3: pure bullet hell — the densest patterns in the game, no adds, one Sanity fountain in the centre that is also the most dangerous place to stand. Phase 4 (Corruption 7+, or the true ending path): he notices you *specifically*.
*Teaches:* nothing. It's the exam.

### Wardens (mid-floor gates, floors 2 and 4)
- **The Ferryman of the Manuxet** (F2) — offers passage for +2 Corruption, or fights you.
- **The Thing in the Ice** (F4) — sealed; fighting it is optional but it drops an Inscription and a key. Skipping it locks one door on the floor.

### Secret bosses
- **The Colour Out of Space** — found only via a Floor 2 secret room chain. Deals *permanent max-Sanity* damage rather than health damage. Drops the Prism sigil.
- **Azathoth** — the true final boss, on the secret Floor 7. See [07](07-floors-and-world.md).

---

## 8. Enemy AI Architecture

Behaviour is a **hierarchical state machine per enemy**, driven by data:

```
EnemyBrain
 ├── Perception (player position, LOS, distance bands, threat)
 ├── StateMachine: Idle → Approach → Attack(patternId) → Recover → Reposition → Dead
 ├── PatternPlayer (runs PatternData timelines)
 └── Steering (separation, wall avoidance, flow-field pathing)
```

- **Pathing:** a per-room **flow field** regenerated when walls move, not per-agent A*. Handles 40 concurrent enemies at negligible cost.
- **Separation steering** so groups don't stack into a single silhouette (a readability requirement, not a polish item).
- **Attack token system:** a room-level budget limits how many enemies may be in `Attack` simultaneously (e.g. 4 on floor 1, 9 on floor 6). This is the single most important knob for making a room fair, and it's how we hit R7 without runtime clamping.
- **Awakened variant** (Corruption 3+) adds one extra `Attack` state with an additional pattern; authored per enemy, not generated.
