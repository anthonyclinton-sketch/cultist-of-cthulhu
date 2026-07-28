# CULTIST OF CTHULHU

> *A twin-stick roguelike where the deeper you go, the less of your mind you keep. You are not the hero. You are the one who read the book and kept reading.*

**Engine:** Godot 4.4+ · **Language:** C# (.NET 8) · **Platform:** PC (Windows / Linux / Steam Deck) · **Perspective:** Top-down twin-stick

---

## The One-Paragraph Pitch

Beneath Arkham, a door has been open since 1692. You are a cultist who has chosen to walk through it. *Cultist of Cthulhu* is a top-down twin-stick action roguelike — with bullet-hell set pieces on the floors built to carry them — where you fight down through six procedurally assembled Lovecraftian strata: drowned Innsmouth wharfs, the restricted stacks of Miskatonic, the Antarctic ruins, sunken R'lyeh. You are armed with 1920s firearms you have defiled into something worse. Your **Sanity** pays to keep your guns loaded and to unmake what's coming at you, and it falls a little further with every room you clear — losing it entirely does not kill you, it *changes* you. Your build is not a list of passive items but a physical **summoning circle** you arrange by hand, where the position and facing of every sigil you place changes what it does.

---

> **Reviewed and revised, 26 July 2026.** Fable's design review is complete; findings and their resolutions are recorded in [HANDOVER-FOR-REVIEW.md](HANDOVER-FOR-REVIEW.md) §9. Review notes are inline throughout the docs as `[REVIEW — Fable]`; decisions taken in response are marked `[DECISION — Opus]`.

## Document Index

| # | Document | Contents |
|---|---|---|
| 00 | [Comparative Analysis](docs/00-comparative-analysis.md) | Deep teardown of *Enter the Gungeon* and *Pathogenic*; what we take, what we reject, and why |
| 01 | [Design Pillars & Core Loop](docs/01-pillars-and-loop.md) | Vision, four pillars, loop diagrams at three time-scales, target run length |
| 02 | [Player & Combat](docs/02-player-and-combat.md) | Controller spec, Sanity system, Blink Step, Banish, Ascension, Corruption, feel budget |
| 03 | [Weapons & Inscriptions](docs/03-weapons-and-inscriptions.md) | Weapon taxonomy, 40-weapon plan, the shop upgrade system, ammo economy |
| 04 | [The Sigil Circle](docs/04-sigil-circle.md) | Spatial build system — grid, shapes, ley lines, adjacency synergies, edit mode |
| 05 | [Enemies & Bosses](docs/05-enemies-and-bosses.md) | Bestiary, bullet-pattern grammar, readability rules, boss design template, 8 bosses |
| 06 | [Procedural Generation](docs/06-procedural-generation.md) | Flow graphs, composite decomposition, room templates, encounter budget, non-euclidean floors |
| 07 | [Floors & World](docs/07-floors-and-world.md) | Six floors + two secret floors, biome mechanics, lore spine, endings |
| 08 | [Economy, Shops & Meta](docs/08-economy-and-meta.md) | Gold/keys, shop layouts, item rooms, shrines, chest tiers, unlock ledger, characters |
| 09 | [Technical Architecture](docs/09-technical-architecture.md) | Godot/C# project structure, bullet manager, data pipeline, determinism, save, perf budgets |
| 10 | [Art, Audio & UX](docs/10-art-audio-ux.md) | Art direction, palettes, lighting, animation, audio design, HUD, accessibility |
| 11 | [Production Roadmap](docs/11-roadmap.md) | Milestones, vertical slice definition, scope guardrails, risk register, cut list |

---

## The Three Ideas That Make This Not A Clone

1. **You cannot stop going down.** Dodging is free — the skill is timing, not budgeting. But *reloading* costs sanity, panic-clearing the screen costs a lot of it, and killing things gives it back only up to a **ceiling that falls every room you clear**. So the descent happens to you regardless of how well you play; what you control is how fast, and what you buy on the way. Low sanity doesn't make you hit harder — it makes you *see*: enemy weak points, secret rooms, attacks a few frames before they land. And about a quarter of the bullets on screen aren't there.

2. **Your build is a physical object.** Items are tetromino-shaped sigils you place on a summoning circle. Adjacency creates synergies. Ley lines multiply what sits on them. Rotation changes firing direction. *Enter the Gungeon*'s hidden synergies become a spatial puzzle the player authors deliberately.

3. **The dungeon is aware of you.** Corruption is a one-way run-long stat that improves your loot and opens doors nothing else opens — and hands the floor a hunter that walks through walls looking for you.

---

## Quick Start

Requires the **Godot 4.7-stable mono** build (pinned in `.godot-version`) and the **.NET 8 SDK**.
If Godot isn't on `PATH`, set `$env:GODOT` to its console executable.

Run content validation and both M0 gates:

```bash
pwsh ./tools/gates.ps1
```

Play the M1 combat slice — WASD move, `LMB` fire, `SPACE` Blink Step, `R` Recite, `RMB` Banish (hold for Open the Eye), `Q` swap weapon, `F3` overlay, `F5` dump telemetry:

```bash
pwsh ./tools/gates.ps1 -Arena
```

Open the Pattern Lab — `←`/`→` switch pattern, `SPACE` fire, `A` auto-fire, `G` ghost orbit:

```bash
pwsh ./tools/gates.ps1 -Lab
```

## Status

**M0 — Technical Foundation: gates passing.** Measured at the full 4096-bullet array capacity,
6.8× the 600-bullet design ceiling, on an unoptimised Debug build:

| M0 exit criterion | Budget | Measured | |
|---|---|---|---|
| `BulletManager._PhysicsProcess` | 0.40 ms | **0.059 ms** p99 | ✅ |
| `BulletManager._Process` (buffer build + upload) | 0.60 ms | **0.245 ms** p99 | ✅ |
| Allocations per physics tick | 0 B | **0 B**, 0 allocating ticks in 540 | ✅ |
| Same seed → identical state, 1800 ticks | exact | **identical** | ✅ |
| Different seed → different state | must differ | **differs** | ✅ |

**M1 — Combat Vertical Slice: systems core landed, playtest pending.**

| M1 deliverable | State |
|---|---|
| Sanity system — costs, gains, ladder, Lucid Ceiling, Open the Eye, hysteresis | ✅ |
| Recitation + Perfect Recitation | ✅ |
| Banish | ✅ |
| Pattern grammar + `PatternData` resources | ✅ 6 primitives, 4 behaviours |
| Pattern Lab | ✅ |
| 5 enemies covering all roles | ✅ |
| `EnemyBrain` FSM + flow field + attack token pool | ✅ |
| 6 weapons across 3 families (incl. Grimoire + melee) | ⚠️ 5 authored |
| Dread Budget encounters with the 35% fodder floor | ✅ |
| HUD — sanity ring, hearts, magazine pips, Perfect ring | ✅ |
| Telemetry — all M1 metrics incl. metric 9 | ✅ |
| Build B control arm | ✅ one-constant flip |
| Hit stop / screen shake / Sanity motes | ⚠️ hit stop only |
| 8 hand-authored Undercroft rooms | ❌ M2 — needs the TileMap room pipeline |
| Enemy art beyond placeholder circles | ❌ deliberate — M1 answers a systems question |

Gameplay numbers now load from `.tres` under `data/`, validated in CI against the
readability contract. `src/Core/Tune.cs` still holds player-side constants and is the
next thing to migrate.

**The M1 question is not yet answered.** The build runs; nobody has played it. See
[docs/11 §M1 Test Design](docs/11-roadmap.md) — the gate is metric 9, *does the ladder
fire without the player deliberately descending?*

Pre-production. No code written. Start at [docs/11-roadmap.md](docs/11-roadmap.md) → Milestone 0.
