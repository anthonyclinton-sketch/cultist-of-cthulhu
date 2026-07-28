# 11 — Production Roadmap

---

## 1. Scope Reality Check

Before any schedule, the honest numbers.

| Line item | Quantity | Notes |
|---|---|---|
| Hand-authored rooms | ~600 | **The critical path.** ~100 per floor set. |
| Weapons | 40 | Sprite + fire pattern + Codex text each |
| Inscriptions | 35 | Mostly logic, minimal art |
| Sigils | 70 | Icon + effect + Codex text each |
| Enemies | 30 | ~5 per floor, each with animations + patterns |
| Bosses | 8 | 60–120 frames each. Each boss ≈ 3 weeks. |
| Bullet patterns | ~120 | Data-authored on the primitive grammar |
| Music tracks | ~18 | 6 floor themes × layers + 8 boss themes |
| Codex words | ~25,000 | |

**Realistic timelines:**
- **Solo developer:** 3–4 years to 1.0. Feasible, but only with the cut list in §5 applied aggressively.
- **Team of 3** (1 gameplay/tools programmer, 1 designer/level author, 1 artist, contracted audio): **20–26 months** to 1.0. This is the model the roadmap below assumes.
- **Team of 5–6:** 14–18 months.

The **room count is the schedule**. At a sustained 4 finished, playtested rooms per day, 600 rooms is 150 working days of one person doing nothing else. Plan for it from Milestone 2 onward, in parallel with everything.

---

## 2. Milestones

### M0 — Technical Foundation *(6 weeks)*
**Goal: prove the engine can carry the game before designing content for it.**

- [ ] Godot 4.4 + .NET 8 project scaffold, CI, pinned versions, `.editorconfig`
- [ ] `Rng` (xoshiro256**) + sub-seed derivation + fixed 60Hz `FixedStep` with render interpolation
- [ ] **`BulletManager`** — SoA arrays, MultiMesh rendering, manual circle collision
- [ ] **Bullet drop-shadow pass** — a second offset quad per real projectile, in the same MultiMesh. This is the *only* hallucination tell ([02 §3.4](02-player-and-combat.md), [05 §1 R9](05-enemies-and-bosses.md)) and is therefore a shader requirement in M0, not polish in M6. Include it in the 4096-bullet stress test budget.
- [ ] **Stress test: 4096 bullets @ 144 Hz with zero allocations in the tick loop.** Hard gate.
- [ ] `PlayerController` — movement, aiming, Blink Step with exact frame data from [02 §4](02-player-and-combat.md)
- [ ] Debug overlay + cheat console
- [ ] Determinism test in CI

**Exit criteria:** a grey box room where a placeholder player dodges 4000 bullets at a locked framerate, and the same seed + input replay produces a byte-identical end state. **If the bullet stress test fails, stop and fix it. Everything else is built on this.**

---

### M1 — Combat Vertical Slice *(8 weeks)*
**Goal: is the core loop fun for 90 seconds?**

- [ ] Sanity system: costs, gains, the low-sanity ladder, the ring HUD
- [ ] Recitation + Perfect Recitation
- [ ] Banish
- [ ] Pattern grammar (primitives + modifiers) + `PatternData` resources
- [ ] **Pattern Lab** debug scene
- [ ] 5 enemies covering all roles (Fodder/Turret/Rusher/Zoner/Support)
- [ ] `EnemyBrain` FSM + flow-field pathing + attack token pool
- [ ] 6 weapons across 3 families — **must include one Grimoire and one melee weapon** (both carry their own Sanity economy and are the likeliest to invalidate it)
- [ ] 8 hand-authored Undercroft rooms, manually chained
- [ ] Hit stop, screen shake, Sanity motes, damage feel pass
- [ ] **Build B — the control arm** (see test design below): identical build, free Gungeon-style dodge on a 0.6s cooldown, no Sanity cost on dodge or reload
- [ ] **Telemetry:** per-room Sanity income/spend, time-in-band, denied-action events, deaths by cause

**Exit criteria:** see the test design below. **This is the highest-risk design gate in the project.**

---

### M1 TEST DESIGN — "Is the Sanity economy fun?"

> **[REVIEW — Fable] Written to replace "playtesters play it voluntarily", which is a real signal but not a measurement, and cannot tell you *which part* is wrong. The brief's fallback — "fall back to a conventional stamina bar" — is a direction, not a plan; a pre-committed ladder is below.**

**The one thing that matters: you cannot evaluate a constraint without a control.** A tester who enjoys Build A tells you the game is fun, not that the Sanity economy made it fun. **Build B (free dodge) is a one-line change and it is the single highest-value item in M1.** Run both, counterbalanced order, same testers, ~25 min each.

**Sample:** 10–12 external testers, none who have seen the game. Mixed skill; record at least 4 who play twin-stick/roguelikes regularly and 4 who do not. Screen-record everything, plus a 10-minute structured interview.

**Measured — quantitative pass criteria:**

| # | Question | Metric | **Pass** | **Fail** |
|---|---|---|---|---|
| 1 | Is the bar actually binding? | % of combat time below 40 Sanity | **25–45%** | <15% (bar is decoration) or >60% (bar is a leash) |
| 2 | Does the intended failure state occur? | "Empty gun, no dodge, in a bullet wall" events per 8-room run | **1–3** | 0 (never bites) or >6 (misery) |
| 3 | Is the cost legible? | Denied-action events (pressed dodge, couldn't pay) per run | **1–4** | >8 = players don't model the bar |
| 4 | Can they explain a death? | % of deaths the tester attributes to a specific decision, unprompted | **≥70%** | <50% violates [01 §6.1](01-pillars-and-loop.md) |
| 5 | Is the economy near break-even? | Median Sanity net per room | **−15 to +15** | outside ±30 = income/cost mis-tuned |
| 6 | **Is the low band chosen or suffered?** | Testers who *deliberately* spend to descend, unprompted | **≥3 of 12** | 0 confirms the [02 §3.5](02-player-and-combat.md) rubber-band finding |
| 7 | **Does the constraint add value?** | A-vs-B preference for "which was more interesting to play?" | **A ≥ 60%** | B > A = the bet fails as designed |
| 8 | Voluntary replay | Testers who choose a 4th consecutive run when told they may stop | **≥60%** | <40% |

**Qualitative — listen for the Pathogenic failure mode.** [00 §2.2](00-comparative-analysis.md) documents real players of the source mechanic complaining that shared stamina *"screws up the rhythm"* and *"takes away control over damage windows."* If ≥3 testers say a version of this unprompted, it is the same failure reproducing in our game — go to Fallback F1 regardless of the other numbers.

**Pre-committed fallback ladder.** Decide by end of M1, not later. Take the *first* rule that triggers:

| Trigger | Fallback | Change |
|---|---|---|
| Metric 3 fails, or "reloading feels like a punishment" is the dominant complaint | **F1 — Decouple** | Reload no longer costs Sanity (ammo economy carries it alone). Sanity = dodge + Banish + ladder. Preserves Pillar I; removes the flow complaint Pathogenic's players report. |
| Metric 2 >6, or testers report helplessness/death-spiral | **F2 — Floor it** | Add in-combat regen of 2/s (a trickle, not a refill) and reduce the hit cost 10→5. |
| Metric 6 = 0 **and** metric 1 <15% | **F3 — Split the bar** | Sanity becomes pure stamina; the power ladder moves to a separate slow stat that changes only at room boundaries. This is §5.4's option and it costs ~2 weeks. |
| Metric 7 fails (B beats A) | **F4 — Invert the bet** | Free dodge on a cooldown; Sanity becomes the *reload + Banish + ladder* resource only. The game becomes a twin-stick with a madness economy rather than a stamina economy. **This is a real outcome and it is survivable — do not treat it as project failure.** |

**What M1 must NOT try to answer:** whether the Sigil Circle works (M2), whether the ladder is balanced (M6 telemetry), or whether hallucinations are fair — hallucinations need the *visual* tell finalised first ([02 §3.4](02-player-and-combat.md)) and testing them at M1 will produce a false negative on the whole system.

---

### M2 — Systems Slice *(10 weeks)*
**Goal: the game's shape, at one floor's depth.**

- [ ] **Full floor generator**: flows, transform, decompose, layout, stitch, validate
- [ ] **Flow Graph editor** plugin + **Room Template validator** + **Generation Visualiser**
- [ ] 30 Undercroft rooms across all roles
- [ ] Dread Budget populator + wave system
- [ ] **Sigil Circle + Reverie screen**, 20 sigils, adjacency + ley lines
- [ ] Gold, keys, chests, loot tables, pity system
- [ ] Shop + **Inscription Bench**, 12 Inscriptions
- [ ] Boss 1: The Thing on the Doorstep, all 3 phases
- [ ] Unbroken Seals
- [ ] Save/load, run state, basic Vestibule
- [ ] 10k-seed generation sweep green in CI

**Exit criteria:** a complete, replayable, winnable Floor 1 with real loot, a real shop, a real build system, and a boss. **This is the demo you show publishers and put on Steam Next Fest.**

---

### M3 — Content Expansion I *(14 weeks)*
- [ ] Floors 2 (Innsmouth) and 3 (Archives): 100 rooms each, 10 enemies, 2 bosses
- [ ] Tidal system; Shifting Shelves
- [ ] **Corruption system** — all thresholds, Awakened variants, the Hound of Tindalos
- [ ] Blasphemous chests, corrupted doors, shrines
- [ ] Secret rooms + Banish-the-wall discovery
- [ ] Wardens
- [ ] Weapons to 25, sigils to 45, Inscriptions to 25
- [ ] Characters 2 and 3 (Dreamer, Fisherman)
- [ ] Codex, meta progression, Yellow Fragments, The Ledger
- [ ] Adaptive music system + 3 floor themes + 3 boss themes
- [ ] **Steam Deck validation on real hardware**

**Exit criteria:** 20-minute runs through three distinct floors with the full economy and meta loop. Ship the demo.

---

### M4 — Content Expansion II & the Weird Floors *(14 weeks)*
- [ ] Floor 4 (Mountains of Madness) + The Shoggoth
- [ ] **Floor 5 (Leng): the open generator**, landmark scattering, roaming packs, the Caravan
- [ ] **Nyarlathotep** — 3 avatars, open-world boss fight
- [ ] **R'lyeh prototype: seams + volume violation.** *Hard go/no-go at week 8* — if `SubViewport` stitching isn't stable, cut volume violation and ship seams only.
- [ ] Ascension — full implementation, per-character forms
- [ ] Characters 4 and 5
- [ ] Weapons to 40, sigils to 70, Inscriptions to 35

**Exit criteria:** floors 1–5 playable end to end.

---

### M5 — Endgame *(10 weeks)*
- [ ] Floor 6 (R'lyeh) — 100 rooms with seam support
- [ ] **Cthulhu**, 4 phases
- [ ] Secret Floor 7 (Court of Azathoth) + Azathoth
- [ ] Secret Floor S (The Colour's Well) + the Colour + the access chain
- [ ] All 5 endings + persistent Vestibule world-state
- [ ] The Nameless (unlockable character)
- [ ] Sovereign boss variants (Corruption 10)
- [ ] All 8 boss themes, full audio pass

**Exit criteria:** the game is completable, all endings reachable.

---

### M6 — Balance, Polish & Ship *(12 weeks)*
- [ ] **Closed beta**, 200+ players, opt-in telemetry
- [ ] Balance passes driven by data: weapon efficiency ratios, sigil pick/win rates, death heatmaps, Corruption distribution
- [ ] Full accessibility option set
- [ ] Localisation pipeline + first languages
- [ ] Steam achievements, cloud saves, rich presence
- [ ] Performance pass on minimum spec + Deck Verified submission
- [ ] Trailer, store page, press kit
- [ ] **Two-week content freeze before launch.** Bugs only.

**Total: ~74 weeks ≈ 17 months of milestone work.** Add 25–30% for the things that always happen: **21–22 months realistic to 1.0.**

---

## 3. Parallel Tracks (running from M2 onward)

| Track | Cadence |
|---|---|
| **Room authoring** | Continuous. ~4 rooms/day sustained. Non-negotiable — it is the critical path. |
| **Codex writing** | 500 words/week, alongside content |
| **Audio** | Contracted per-milestone deliveries |
| **Playtesting** | Weekly external session from M1. Recorded. Non-negotiable. |
| **Marketing** | Steam page live at M2, wishlist campaign from M3, Next Fest at M3/M4 |

---

## 4. Risk Register

| Risk | P | Impact | Mitigation |
|---|---|---|---|
| **Sanity economy isn't fun** | **Med→High** | **Fatal** | M1 exists solely to test this, **with the Build B control arm**. Failure routes to the pre-committed **F1–F4 fallback ladder** in §M1 Test Design — *not* to the old "fall back to a conventional stamina bar", which was a direction rather than a plan. Decide by end of M1, not later. **Probability raised from Med:** [00 §2.2](00-comparative-analysis.md) establishes the source mechanic is Pathogenic's *most criticised* system and that its original justification (throttling 10 simultaneous weapons) does not apply to us. This is an unvalidated novel bet, not a borrowed safe one. |
| Bullet performance collapse | Low | Fatal | M0 hard gate at 4096 bullets. Architecture chosen specifically to avoid this. |
| **Room authoring underestimated** | **High** | Severe | Cut room counts to 60/floor (360 total) before cutting features. Build the validator early. Consider a kit-bash system for `connector` and `combat_easy` rooms. |
| R'lyeh non-euclidean too expensive | Med | Moderate | Explicit go/no-go in M4. Fallback already specified. |
| Sigil Circle is too fiddly / players ignore it | Med | Severe | Test in M2 with fresh players. If ignored, add stronger auto-arrange and make adjacency bonuses louder. Do not cut the system — it's Pillar II. |
| Godot C# tooling regression | Med | Moderate | Pin version; upgrade only between milestones. |
| 40 weapons × 3 Inscriptions creates degenerate combos | High | Minor | Expected and fine. Balance in M6 with telemetry. Conflict groups are the safety valve. |
| Scope creep from "one more mechanic" | **High** | Severe | The Pillars in [01](01-pillars-and-loop.md) are the veto. Anything serving none is cut without discussion. |
| Lovecraft IP concerns | Low | Moderate | Public-domain sourcing only; avoid Derleth-era and Chaosium-specific material. See [07 §6](07-floors-and-world.md). |
| Solo/small team burnout | High | Severe | Milestones end with a slack week. Ship the M2 demo publicly — external validation is the fuel that gets a project through M4. |

---

## 5. The Cut List

Ordered. Cut from the top when the schedule slips. **Decided in advance, in calm, so it isn't decided in panic.**

> **[REVIEW — Fable] The old ordering was inverted against its own stated critical path, and item 8 has been promoted to 1.**
> This document says twice that **"the room count is the schedule"** (§1, §4). Yet room count sat at **8 of 9** — nearly last — while items 1–5 were content that costs comparatively little. Ranking by person-days saved:
>
> | Cut | ≈ person-days saved | Player-visible loss |
> |---|---|---|
> | **Rooms 100 → 60/floor** | **~96** | Some repetition across runs — invisible in the first 10 hours |
> | Floor 5 open generator | ~25 | A structural surprise |
> | Secret Floor 7 | ~20 | A hidden ending |
> | Secret Floor S | ~10 | A hidden miniboss |
> | Volume violation | ~8 | One "wrong" effect among several |
>
> **Cutting rooms first saves more than items 1–5 combined**, and it is the *only* cut that relieves the critical path rather than a specialist's queue. It is also the cut you can make *incrementally and reversibly* — author 60, and add more per floor if the schedule holds. **60 rooms per floor still gives 4× the 14 rooms a single run consumes**, which is ample variety; Gungeon shipped roughly 300 rooms in total across more floors with a larger team.
> **Do this pre-emptively at M2, not reactively at M4.** A team that plans for 600 and authors 360 has failed; a team that plans for 360 and adds 60 has shipped early.

1. **Room counts 100 → 60 per floor (360 total).** *Promoted from 8. Do this at M2, pre-emptively.*
2. Secret Floor S (The Colour's Well) — self-contained, cleanly removable
3. Volume violation in R'lyeh (keep seams)
4. Characters 5 and 6 → post-launch free update
5. Sovereign boss variants → post-launch
6. Weapons 30→40 → post-launch
6a. **Melee (Family V) → cut to the Fisherman's exclusive identity.** *Trigger is a design gate, not a schedule slip:* if melee does not feel good at M3 after the reach/knockback/immunity rules in [03 §2](03-weapons-and-inscriptions.md) are implemented, cut the family rather than continuing to tune it. Removes an entire animation line; the Fisherman keeps the fantasy.
7. Floor 5 (Leng) open generator → replace with a standard flow-based floor, keep Nyarlathotep as a 3-phase arena boss
8. Secret Floor 7 (Azathoth) → post-launch "true ending" update
9. Adaptive music layering → static tracks with a low-sanity variant only

> **[REVIEW] On §5.8's question — should Floor 5's open generator be cut pre-emptively? No.** It is item 7, and that is the right place. It is the game's structural surprise ([00 §2.5](00-comparative-analysis.md)) and one of two things that stop the middle of the run feeling like more of floors 1–3. **Cut rooms instead** — that buys ~4× the schedule relief for a fraction of the identity loss. The genuine Floor 5 risk is not the generator; it is that **Nyarlathotep's three-avatar open-world fight is a bespoke boss on a bespoke generator** — schedule that fight as its own line item, and if it slips, ship Floor 5 open with a *conventional arena* Nyarlathotep rather than cutting the floor.

**Never cut, at any cost:** the Sanity system, the Sigil Circle, the Inscription Bench, flow-based generation, Unbroken Seals, the Codex, accessibility options. These are the game.

---

## 6. Definition of Done — 1.0

```
□ 6 floors + 1 secret floor, all completable
□ 8 bosses, all with Unbroken Seal support
□ 5+ playable characters
□ 40 weapons · 35 inscriptions · 70 sigils · 30 enemies
□ 5 endings, persistent hub world-state
□ Locked 144 Hz on recommended spec; 60 Hz on minimum; 40+ on Steam Deck
□ 10,000-seed generation sweep passes for every floor, every build
□ Determinism replay test green
□ Zero allocations in the physics tick, verified
□ Full accessibility set shipped, achievements unaffected
□ Codex complete — every entity has flavour + full mechanical text
□ Median new-player session > 45 minutes in beta telemetry
□ Steam Deck Verified
```

---

## 7. Immediate Next Steps

1. **Scaffold the Godot project.** `godot --headless --path . --build-solutions` should build clean before anything else exists.
2. **Write `BulletManager` first.** Not the player, not the rooms. The bullet manager. Then stress-test it. Everything else is negotiable; this is not.
3. **Build the Pattern Lab in week 2.** You will use it every single day for two years.
4. **Author 8 Undercroft rooms by hand, on paper, before writing the generator.** You cannot design a room-assembly algorithm until you know what a room is.
5. **Book the first external playtest for the end of M1** and put it in a calendar now. The Sanity economy is the project's central bet, and the only thing that resolves it is watching a stranger play.
