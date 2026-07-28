# HANDOVER — Design Review Brief

**To:** Fable
**From:** Opus (planning pass)
**Date:** 26 July 2026
**Subject:** Critical review of the *Cultist of Cthulhu* design bible
**Status of work:** Pre-production. Design complete, **no code written, nothing playtested.**

> ## ✅ REVIEW CLOSED — 26 July 2026
> Fable's review is complete and has been actioned. **Sections 0–8 below are preserved as the original brief**; the outcome is recorded in **§9**. Review findings are inline throughout the docs as `[REVIEW — Fable]`; decisions taken in response are marked `[DECISION — Opus]`.
> Six items the review left open have been resolved. Three of its changes did not propagate to files it did not edit and have been carried through. See §9.

---

## 0. What I need from you

A **critical design review**, not a copy-edit. Assume the prose is fine and the structure is fine. What I need is someone to find the places where the systems don't actually work when you run them forward in your head.

The single most valuable thing you can do is **break the Sanity economy**. It is the load-bearing wall of this entire design, it has never been tested, and if it's wrong then documents 02 through 08 are all built on sand. I have listed my own suspicions in §5 — confirm, refute, or add to them, and tell me what I'm not seeing.

Deliverable format is in §7.

---

## 1. What this project is

A top-down action roguelike / bullet hell for PC, in **Godot 4.4 + C# (.NET 8)**, taking direct inspiration from **Enter the Gungeon** (2016) and **Pathogenic** (2026). Lovecraftian setting — Arkham, Innsmouth, Miskatonic, the Mountains of Madness, Leng, R'lyeh. Procedurally assembled floors of interconnected rooms, shops selling weapon upgrades, item rooms, boss per floor.

The client brief was, verbatim: *"Perform some deep analysis on the 'Enter the Gungeon' and 'Pathogenic' games and come up with a plan and design for all elements of the game."*

Target scope is a 3-person team, ~21 months to 1.0.

---

## 2. How the plan was produced

**Research done:** web research on *Pathogenic* (released 16 July 2026 — after my training data, so everything about it in these docs comes from live sources, not memory) and on *Enter the Gungeon*'s procedural generation specifically. Sources are listed at the bottom of `docs/00-comparative-analysis.md`. Gungeon's combat, economy, and progression systems are from my own knowledge of the game.

**Where the sourcing is thin — treat these as lower-confidence:**
- *Pathogenic*'s stamina system is described in exactly one review sentence ("stamina is used both for dodging and as your reload or magazine size mechanic"). I extrapolated a great deal from that one line. If you can verify the actual numbers or find that I've misread the system, say so — I built Pillar I on it.
- *Pathogenic*'s hardpoint/orientation mechanic likewise rests on one reviewer's phrasing. The Sigil Circle is a large extrapolation from a small evidence base. It may be a better idea than what Pathogenic actually does, or it may be solving a problem that game didn't have.
- I have not played either game. Everything about *feel* is inference.

**Method:** for each reference game I tried to isolate the *mechanism* behind a feature rather than the feature itself, then asked what that mechanism would have to become in a Lovecraftian frame. The synthesis table in `00 §3` is the compressed version of that work.

---

## 3. Reading order

Read these three first — they contain every load-bearing decision:

1. `docs/00-comparative-analysis.md` — the analysis the whole plan derives from, and the synthesis table
2. `docs/02-player-and-combat.md` — **the Sanity system.** This is the one to attack.
3. `docs/04-sigil-circle.md` — the build system

Then, in rough order of how much they'd hurt if wrong:

4. `docs/09-technical-architecture.md` — bullet manager, determinism, perf budgets
5. `docs/06-procedural-generation.md` — the floor generator
6. `docs/08-economy-and-meta.md` — currencies, shops, the numbers
7. `docs/11-roadmap.md` — scope, risk register, cut list
8. `docs/03`, `05`, `07`, `10` — weapons, enemies, world, presentation
9. `docs/01-pillars-and-loop.md` — the pillars, useful as the veto criterion for anything you'd propose adding

---

## 4. The three bets

Everything else is negotiable. These three are the design.

### Bet 1 — Sanity is the stamina bar
Dodging costs 18 Sanity. Reloading costs 12 × the weapon's reload weight. The screen-clearing Banish costs 45. Taking a hit costs 10. **Kills refund it** (6 base, doubled if you kill during dodge i-frames), and there is **no in-combat regeneration**. So the game funds your defence out of your offence, and the intended failure state is standing in a bullet wall with an empty gun and no dodge.

Below 40 Sanity the player gets stronger and starts hallucinating. At 0 they don't die — they **Ascend**: 20 seconds invulnerable and monstrous, then a heart of damage and a permanent −10 max Sanity for the run.

**Why it matters:** it's the fix for Gungeon's free-i-frame problem, it's lifted from Pathogenic's shared stamina, and it makes the theme mechanical rather than decorative. If it isn't fun, there is no game — only a Gungeon clone with a horror skin.

### Bet 2 — The build is a spatial puzzle
41-cell grid, tetromino-shaped sigils placed by hand, adjacency tags creating named synergies, three ley lines that multiply what sits on them, directional sigils that fire where they point. Drop rates tuned so a full run produces 35–47 cells of sigils against 41 capacity — you *must* cut things.

**Why it matters:** it's the fix for Gungeon's unreadable item soup and its undiscoverable hidden synergies. It's also the single biggest UI/UX build in the project.

### Bet 3 — Corruption is the difficulty selector
One-way, opt-in, farmable. Better loot at every threshold, plus gated content (corrupted doors at 1, a wall-phasing hunter at 5, harder boss variants at 10). There is a deliberate corruption *build* — sigils and inscriptions that scale off it.

**Why it matters:** it replaces a difficulty menu with a strategic axis, and it's what makes Gungeon's Curse — which most players acquire by accident and never engage with — into something you'd actually pursue.

---

## 5. Where I think it's weakest

Ordered by how much damage each would do. **I would rather you tell me I'm right about these than find new ones — but find new ones too.**

### 5.1 Low Sanity may be strictly better, which inverts the whole resource
At 20–40 Sanity you get +18% damage, visible enemy weak points (2× damage on hit), and shimmering wall cracks. At 1–20 you get +30% damage, +10% move speed, and **secret rooms outlined on the minimap**.

The only downside is hallucinated bullets. A skilled player may simply learn to ignore hallucinations and then *camp the low band permanently* — at which point Sanity is not a resource with a risk curve, it's a sweet spot you park in, and the whole tension collapses.

I do not have a good answer for this. Candidate fixes: make low-Sanity downsides genuinely dangerous rather than cosmetic-adjacent; make hallucinations block your *aim* rather than just your reading; scale enemy aggression with low Sanity; or invert the ladder so power comes at *high* Sanity and low Sanity is purely desperate. That last option is thematically much worse and mechanically much safer. **I'd value your read on this above everything else in the document.**

### 5.2 The Dreamer's Ballast sigil breaks Ascension
`docs/04 §5.2` gives Ascension +12s duration and **removes the heart cost**. `docs/02 §6` says Ascension must never be optimal. With that sigil the cost of a deliberate Ascension is −10 max Sanity and nothing else, for 32 seconds of invulnerability. That is farmable, and it is my own text contradicting itself.

This is a straightforward bug in the plan, but I flag it because I suspect **it's a symptom** — Ascension may be too generous before any sigil touches it, and the whole "death is replaced by a power state" idea may need a harder cost than one heart.

### 5.3 Melee may be the dominant Sanity engine
Melee weapons have no ammo and restore 3 Sanity per hit (`docs/03 §2`, Family V). In a resource economy where Sanity is everything, a weapon that generates it for free with no ammo cost may simply be the correct answer to every situation, especially on a character built around it. Bullet hell + melee is also historically hard to make feel good. Worth checking whether melee should exist at all in v1, or should be a single character's identity rather than a whole weapon family.

### 5.4 Sanity is doing four jobs
Dodge cost, reload cost, panic button cost, and difficulty/power modifier. Elegant on paper. My worry is that the player cannot form a clear mental model of a bar that means four things, and that tuning any one job breaks the other three. There may be an argument for splitting the power-modifier role off into a separate slow stat and leaving Sanity as pure stamina.

Counter-argument, which I currently believe: splitting it makes the game ordinary. But I hold that weakly.

### 5.5 Hallucinated bullets may just feel unfair
The stated tells are that hallucinations are **silent** and **cast no light**. In a room with forty real bullets and heavy combat audio, I do not believe the audio tell is perceptible. The lighting tell may also be invisible on bright floors. If neither tell lands in practice, hallucinations are indistinguishable from real bullets, and a mechanic that makes you dodge things that aren't there — while telling you it's fair — is a mechanic players will hate.

Also note the asymmetry I flagged in `docs/02 §10`: controller rumble would give pad players a free tell that KBM players don't get.

### 5.6 Two build systems compete
The Sigil Circle (Bet 2) and weapon Inscriptions (`docs/03 §3`) are both progression systems, both consume attention, and Inscriptions consume the gold that would otherwise buy sigils. The Inscription Bench exists partly because the client explicitly asked for shop-bought weapon upgrades. Check whether these two systems are complementary or whether one is starving the other — and if so, whether Inscriptions should be folded into the Circle somehow.

### 5.7 Is this a bullet hell or a twin-stick shooter?
Gungeon is not really a bullet hell; it's a twin-stick shooter with dense patterns. The brief says bullet hell. I have written rules that pull in both directions — a 600-bullet cap and a mandate that every pattern have a no-dodge positioning solution (`docs/05 §6`) are twin-stick values; the Court of Azathoth floor is a true bullet hell. **The plan may be incoherent about which genre it's in.** Tell me if you think it needs to commit.

### 5.8 Schedule
600 hand-authored rooms is ~150 person-days of one person doing nothing else, and it is the critical path. Floors 5 and 6 each need a bespoke generator (open-world scatter; non-euclidean seam stitching) and are both scheduled late (M4/M5), which is where cost overruns are most fatal. The cut list in `docs/11 §5` is my answer, but review whether the ordering is right — in particular whether Floor 5's open generator should be cut *pre-emptively* rather than reactively.

---

## 6. Settled — please don't relitigate unless you think it's actively wrong

These were decided deliberately and have reasoning attached. Push back only with a specific argument, not a preference.

| Decision | Where | Why |
|---|---|---|
| Flow-graph procedural generation (Gungeon's model) | `06 §1` | Compared against BSP, cellular automata, WFC, static levels. It's the only one that gives authored pacing with random topology. |
| Struct-of-arrays bullet manager, not `Area2D` | `09 §3` | ~0.15ms vs ~9–14ms per frame at 1000 bullets. Not a preference; a measurement. |
| 40 weapons, not 200 | `03 §1` | Depth via Inscriptions instead of breadth. Art budget. |
| No permanent stat meta-progression | `01 §7`, `08 §6` | Currency-gated power makes hour one a bad demo of hour twenty. Unlocks are lateral only. |
| No multiplayer, no 3D, no crafting | `01 §7` | Scope. |
| Godot + C# | client-specified | Not up for review. |
| Public-domain Mythos only; no Derleth-era or Chaosium material | `07 §6` | Legal. |
| Assist Mode ships with achievements enabled | `10 §7` | Deliberate stance. |

---

## 7. What to send back

Please structure your review as:

**A. Verdict on each of the three bets** — for each: *ship it / ship it with changes / this doesn't work*. If "with changes," specify the change concretely enough to implement.

**B. Confirmed weaknesses** — which of §5.1–5.8 are real, and how bad. Say plainly if you think I've overstated one.

**C. New problems** — things I didn't see. This is the highest-value section. Systems that contradict each other, degenerate strategies, numbers that don't add up when you run them, places where two documents disagree. Cite file and section.

**D. Cheapest high-impact changes** — if you could change five things before a line of code is written, which five and why.

**E. Milestone 1 test design** — M1 exists solely to answer "is the Sanity economy fun?" (`docs/11 §2`). Tell me what the actual playtest should measure, what a pass looks like numerically, and what the fallback design is if it fails. I have written "fall back to a conventional stamina bar" as the contingency, which is honestly not a plan, just a direction.

Anything you'd cut outright is welcome. The cut list already exists (`docs/11 §5`) and adding to it is a contribution, not a criticism.

---

## 8. Constraints on the review

- **No code.** Nothing has been built, so there is nothing to run or profile. This is a paper review.
- **The client's brief is fixed**: action roguelike, bullet hell, C#/Godot, Lovecraftian, procedural floors of interconnected rooms, shops with weapon upgrades, item rooms. Don't propose a design that drops any of those.
- The numbers throughout (`18 Sanity per dodge`, `41 cells`, `620–900 gold per run`) are **first-pass estimates chosen to be internally consistent**, not tuned values. Flag them if the *relationships* are wrong; don't spend time on whether 18 should be 16.
- Where I've cited *Pathogenic*, I may have over-extrapolated from thin sources — see §2. If you can access better information about that game, correcting my reading of it is genuinely useful.

---

# 9. REVIEW OUTCOME — 26 July 2026

## 9.1 The finding that changes the project's risk profile

**Bet 1 was reclassified from "borrowed and safe" to "novel and unvalidated."** §2 of this brief asked for exactly this check and it came back worse than expected. Fable verified that Pathogenic's shared stamina bar is that game's **most criticised system**, not its most praised — the most-linked community thread on it is titled *"dude just pick one, not both."* More importantly, **the developer's own justification does not transfer**: shared stamina exists there to throttle **ten simultaneously-equipped auto-firing weapons**. We carry three and fire one at a time. We do not have the problem the mechanism was built to solve.

This does not kill Bet 1 — the complaints are about feel, not coherence, and Pathogenic is ~94% positive *with* it. But it removes the "proven elsewhere" argument entirely. Consequences carried into the docs: the M1 risk probability is raised, the M1 gate now requires a **Build B control arm** (identical build, free Gungeon-style dodge) because a constraint cannot be evaluated without one, and the specific complaint their players make — *being forced to stop shooting* — is now a named thing we design against. Our Sanity bar never gates firing, only dodge and reload. That distinction is our main protection and it is now deliberate rather than incidental.

**Bet 2 came back stronger.** Pathogenic already has positional constraints (*"certain mitochondria have to be placed on specific locations to work at all"*) and already differentiates characters by grid shape rather than stats — which is what our per-character ley layouts do. The extrapolation flagged in §2 is largely borne out.

## 9.2 Confirmed weaknesses from §5, and what happened to them

| § | Finding | Verdict | Resolution |
|---|---|---|---|
| 5.1 | Low Sanity strictly better | **Real, but mis-diagnosed** | Camping was never the failure mode — kills push you *up*, the corridor resets you, and low Sanity means no dodges. The actual defect: the band was an involuntary **readout that paid out for getting hit**, not a choice. Fixed by removing the ladder's damage entirely, adding a deliberate descent verb, and adding hysteresis. See 9.3. |
| 5.2 | Dreamer's Ballast breaks Ascension | **Confirmed, and worse** | Ascension was farmable to infinity **with no sigil involved**: "cannot kill you" meant the heart cost vanished at low health, and max Sanity floored at 40 meant the penalty stopped escalating. Loop was 2.2 dodges → 20s invulnerability, forever. Closed with a debt rule (unpaid cost becomes permanent max-heart loss) and diminishing duration (20/14/10/7/5s). Ballast now discounts rather than deletes. New binding rule: **no cost-removal effect may exist for Ascension.** |
| 5.3 | Melee is a Sanity printer | **Confirmed — second-worst break** | 9 Sanity/s, **18/s on the Fisherman** whose Heart Sigil doubles it — a full dodge every second, forever. Capped at 12 per enemy per 3s. **But the real finding was underneath it:** melee had no answer to contact damage, so the melee player paid health to use their weapon. Resolved separately — see 9.3. |
| 5.4 | Sanity does four jobs | **Not upheld as stated** | The overload is real but the fix is not splitting the bar. Removing the damage multiplier (9.3) removes one of the four jobs, which addresses most of the concern at a fraction of the cost. Option F3 (split) survives as an M1 fallback if metric 6 comes back zero. |
| 5.5 | Hallucinations feel unfair | **Confirmed, with a mechanism** | Not a hunch — provable from our own audio spec. Voice-limiting (6 concurrent) and 0.02s spawn-merge mean **real bullets are routinely silent too**, so silence carries zero information. Audio tell deleted. See 9.3 for the replacement. |
| 5.6 | Two build systems compete | **Half right** | Sigils and Inscriptions do *not* compete for gold — sigils arrive free from reward rooms, bosses and chests. The genuine conflict is **Inscriptions vs. the ammo economy**: a fully-kitted weapon is ~250–500 gold and therefore can never be dropped, while the ammo economy exists to force rotation. Resolved with Inscription transfer at the bench (60g each, destroys source weapon). |
| 5.7 | Bullet hell or twin-stick? | **Confirmed — the plan was incoherent** | Settled by arithmetic: the Sanity budget is ~9 dodges per room, and true bullet-hell density assumes dozens of evasions per encounter. **You cannot have a metered dodge and genuine bullet-hell density in the same room.** Committed: twin-stick with bullet-hell set pieces. Genre label changed in the README and doc 01. |
| 5.8 | Schedule / cut list ordering | **Confirmed — inverted against its own critical path** | Room count sat at 8 of 9 on the cut list while the doc says twice that "the room count is the schedule." Cutting rooms 100→60 saves ~96 person-days — **more than items 1–5 combined**. Promoted to item 1, to be done *pre-emptively at M2*. Floor 5's open generator stays at 7; the real risk there is Nyarlathotep being a bespoke boss on a bespoke generator, now scheduled as its own line item. |

## 9.3 The six open decisions, resolved

| # | Decision | Where |
|---|---|---|
| 1 | **Sanity is a descent, not a per-room allowance.** Out-of-combat regen now refills only to a **Lucid Ceiling** that starts at 100 and drops 5 per room cleared, floored at 50, resetting each floor and at the boss foyer. Candles pierce it. The last third of every floor is now played low, which is where the ladder was always supposed to fire. | [02 §3.3.1](docs/02-player-and-combat.md) |
| 2 | **The ladder grants no damage at any band.** ×1.08/×1.18/×1.30 deleted. Payoffs are information, mobility and perception only — weak points, secrets on the minimap, +10% move, extended telegraph reads. Damage from low Sanity is now **opt-in via build** (*Derringer of Last Rites*, *Shining Trapezohedron*), which makes descending a committed archetype rather than a bonus for bleeding. | [02 §3.4](docs/02-player-and-combat.md) |
| 3 | **Open the Eye** — hold Banish 0.4s, spend 25 Sanity, drop a band deliberately. Unavailable below 20, so you cannot Open the Eye into Ascension. Plus **band hysteresis** (8-point deadband, kill refunds halved below 40) so a chosen band can be *held*. Together these convert the ladder from readout to choice — the actual §5.1 defect. | [02 §3.5.1–2](docs/02-player-and-combat.md) |
| 4 | **Melee reach must exceed contact radius + 0.5 units**, every hit knocks back enough to reset contact, and hits grant 0.25s of contact immunity *against the struck target only*. *Sacrificial Kris* respecified from "tiny range" to short-but-safe — it is the Fisherman's starter and a starter weapon must not damage its user. **Cutting Family V is now cut-list item 6a** with an explicit M3 design trigger. | [03 §2 Family V](docs/03-weapons-and-inscriptions.md) |
| 5 | **The hallucination tell is a drop-shadow, on every floor — not light.** Fable's proposed fix (light on dark floors, shadow on bright) means the tell *changes identity depending on where you stand*, which hallucinations cannot afford. One universal rule instead: real bullets cast a soft offset floor shadow, hallucinations don't. Works everywhere because it depends on the floor being *drawn*, not *dark*; survives density because shadows don't voice-limit. Now a **M0 shader requirement** and a third bullet draw call. | [02 §3.4](docs/02-player-and-combat.md), [05 R9](docs/05-enemies-and-bosses.md), [09 §3.3](docs/09-technical-architecture.md) |
| 6 | **Surplus sigils dissolve for gold** at Gaunt's Dissolution Bowl. This closes two findings with one affordance: it makes "loot is never dead" true, and it funds the **gold shortfall** Fable found in the Inscription budget — converting the system's own ~50% sigil oversupply into the currency the player is short of, rather than inflating base drops. | [04 §6](docs/04-sigil-circle.md), [08 §2.1](docs/08-economy-and-meta.md) |

## 9.4 Propagation — changes that hadn't reached files the review didn't touch

1. **Dead audio tell** still lived in `05 §1 R8` and `10 §2.2` ("Hallucinated bullets are silent — the KBM tell") after being deleted in `02`. Both corrected; R9 added as the shadow rule.
2. **Stale M1 fallback** — the risk register still said "fall back to a conventional stamina bar," which the review had already replaced with the pre-committed F1–F4 ladder. Now points at the ladder, and the probability is raised to Med→High per 9.1.
3. **Tests referenced but never written.** The review cited assertions "in `09 §9`" that didn't exist there. Added: sigil supply at the **10th percentile** (not the mean — every defect found was invisible at the average), the `playerPowerMult` ordering test, gold sufficiency including Dissolution proceeds, and a new Sanity-economy simulation whose job is to regression-test that **no combination reduces a Pillar-I cost to zero**.

## 9.5 What is still open

- **F3 (split the bar)** remains the live fallback if M1 metric 6 returns zero deliberate descents. Decision 2 above should make this unnecessary; it is not guaranteed to.
- **Melee** has a design gate at M3, not a resolution. Cut-list item 6a is real and should be expected to fire.
- **Grimoires are an untested second economy** — every shot is a purchase, against a game taught on "shooting is free." *Cantrip: Withering* costs ~120 Sanity for a 30-shot room *before any dodging*, and it is the Dreamer's starter. Now mandatory in the M1 build so this is discovered at week 14, not month 14.
- **Nyarlathotep** — a bespoke boss on a bespoke generator. Own line item; fallback is a conventional arena fight on the open floor.
- **Everything in Bet 1** is still unvalidated and now known to be contested at the source. M1 with a control arm is the only thing that resolves it.
