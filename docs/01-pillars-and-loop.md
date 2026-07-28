# 01 — Design Pillars & Core Loop

## 1. Vision Statement

*Cultist of Cthulhu* is a 15–40 minute-per-run **top-down twin-stick action roguelike with bullet-hell set pieces**, in which the player descends through six procedurally assembled Lovecraftian strata. The fantasy is not survival horror — it is **complicity**. You are not fleeing the dark; you are spending yourself into it on purpose, trading pieces of your mind for the power to go one floor deeper.

**Genre, stated once and bindingly: this is a twin-stick roguelike whose dodge is metered, and which escalates into true bullet hell on specific authored encounters.** It is not Touhou with tentacles. The default encounter is Gungeon-class density solved by reading and positioning; bullet hell is a deliberate spike, deployed where the Sanity economy has been explicitly compensated for it.

The game must be **legible at a glance and unfathomable at depth**: a new player understands "shoot, dodge, don't die" in ten seconds; a hundred-hour player is optimizing sigil adjacency against corruption thresholds to reach a floor most players never learn exists.

> **[REVIEW — Fable] Genre commitment (§5.7 of the brief) — the plan IS incoherent about this, and it must commit. Recommendation: a twin-stick shooter with bullet-hell set pieces. Say so here, and stop using "bullet hell" as the unqualified genre label.**
>
> The two vocabularies are already fighting in the docs: a **600-bullet cap** and a mandate that **every pattern have a no-dodge positioning solution** ([05 §1 R7, §6](05-enemies-and-bosses.md)) are twin-stick values; the **Court of Azathoth** is a true bullet hell.
>
> **The Sanity economy settles the argument.** A player's budget is roughly **9 dodges per room** ([02 §3.3](02-player-and-combat.md)). True bullet-hell density assumes near-continuous dodging — dozens of evasions per encounter. **You cannot have a metered dodge and genuine bullet-hell density in the same room; one of them has to be decoration.** Since the metered dodge is Bet 1 and the reason the game is not a Gungeon clone, density is what gives way.
>
> **What this means concretely:**
> - Default encounter design targets **readable, positional patterns** — Gungeon-class density, not Touhou-class. The 600 cap is the *design* ceiling and should rarely be approached.
> - **Bullet hell is a deliberate spike, not the baseline**: the Court of Azathoth, Cthulhu Phase 3, and Sovereign variants. On those encounters the Sanity economy must be explicitly compensated — a guaranteed Sanity fountain, elevated fodder density, or a temporary cost reduction — and that compensation is a *design requirement*, not a balance tweak.
> - **Marketing language should follow.** "Bullet hell" sets an expectation the moment-to-moment will not meet, and mis-set genre expectations are a review-score problem. *"A twin-stick roguelike where dodging costs your mind"* describes the actual game.
> - The client brief specifies "bullet hell" and that constraint is honoured — the game contains real bullet hell, on the floors built to carry it. This is a statement about the **default**, not a removal.

---

## 2. The Four Pillars

Every feature must serve at least one. Features serving none are cut without discussion.

### Pillar I — *You cannot stop going down*

> **[DECISION — 26 Jul 2026] This pillar replaces "Every dodge is a purchase". Blink Step is now FREE.** This is fallback **F4**, pre-committed in [11 §M1](11-roadmap.md) and taken early rather than after a failed playtest. Rationale in [00 §2.2](00-comparative-analysis.md): the mechanic was Pathogenic's most criticised system and its original justification — throttling ten auto-firing weapons — does not apply to a game carrying three weapons and firing one.

**Defence is free; sustain, panic and perception are not; and the descent happens to you regardless.**

- **Blink Step costs nothing.** Its limiter is the cooldown and the 8-frame vulnerable recovery tail, so the skill is *timing*, not budgeting.
- **Sanity still buys everything else**: Recitation (reload), Banish, and Open the Eye. Reload is now the primary sink, which means **weapon choice is the main Sanity decision** — a Nitro Express at reload weight 2.0 costs 24 Sanity a magazine and genuinely changes how much of the ladder you see.
- **The Lucid Ceiling drives the descent.** Sanity falls as the floor progresses whatever you do. You do not choose *whether* to go down, only how fast and what you buy on the way.

*Serves:* theme–mechanic unity, weapon identity, an honest skill curve, the inevitability the fiction is about.
*Kills:* in-combat regeneration, effects that raise the ceiling for free, "get out of jail" consumables that cost nothing.

**What this pillar gave up, stated plainly:** the game no longer differentiates from *Enter the Gungeon* on the dodge. It differentiates on the Sigil Circle, on Corruption, and on a descent the player cannot opt out of. That is a narrower claim than the original design made, and an honest one.

### Pillar II — *The build is a thing you can see and touch*
No hidden stats. No invisible synergies. The player's power is a physical arrangement on a summoning circle that they authored deliberately and can re-author at any time.

*Serves:* build identity, theorycraft, between-run motivation, readability.
*Kills:* undiscoverable synergies, unbounded item accumulation, stat soup, hidden luck stats.

### Pillar III — *Madness is a mechanic, not a mood*
Sanity loss produces concrete, systemic changes — new abilities, revealed secrets, hallucinated projectiles, altered enemy behaviour. Horror atmosphere is a *consequence* of the mechanics firing, never decoration bolted on top.

*Serves:* theme-mechanic unity, tension curve, memorability.
*Kills:* screen-shake-as-horror, jump scares, purely cosmetic madness effects.

### Pillar IV — *The dungeon is authored; the descent is not*
Every room is hand-designed and playtested. Every *floor* is assembled fresh. Pacing is guaranteed; topology is not.

*Serves:* replayability without slop, encounter quality, moment-to-moment fairness.
*Kills:* fully random room interiors, cave-generation algorithms, procedural enemy stat rolls.

---

## 3. Target Experience Metrics

| Metric | Target | Rationale |
|---|---|---|
| Time to first input | < 8s from launch (with Skip Intro) | Roguelikes live and die on restart friction |
| Death → next run start | < 5s | Must be faster than the impulse to quit |
| Full successful run | 32–45 min | One evening session; two runs per sitting |
| Floor 1 clear (new player) | 6–9 min | Long enough to learn, short enough to retry |
| Floor 1 clear (expert) | 2.5–4 min | Skilled play must be *fast*, not just safe |
| Rooms per floor | 11–18 (scales by floor) | Gungeon-adjacent; avoids fatigue |
| Deaths before first floor-2 clear | ~3 | New player should feel progress immediately |
| Deaths before first full clear | 25–60 | The mountain, but a climbable one |
| Sanity full-drain events per run | 2–5 | Ascension must be an event, not a state |
| Frame time budget | 6.9ms @ 1080p (144fps headroom) | Bullet hell demands frame-perfect input |

---

## 4. The Core Loop, at Three Time-Scales

### 4.1 Second-to-second — The Combat Rhythm

```
          ┌──────────────────────────────────────────┐
          │              THREAT ARRIVES              │
          └────────────────────┬─────────────────────┘
                               ▼
                  ┌────────────────────────┐
                  │  Read the pattern      │
                  │  (telegraph → volley)  │
                  └───────────┬────────────┘
                              ▼
        ┌─────────────────────┴─────────────────────┐
        ▼                                           ▼
 ┌─────────────┐                            ┌──────────────┐
 │ WALK IT OFF │  (free, slow, requires     │  BLINK STEP  │ (costs Sanity,
 │ positioning │   good spacing)            │  i-frames    │  instant, safe)
 └──────┬──────┘                            └───────┬──────┘
        └────────────────────┬──────────────────────┘
                             ▼
                  ┌──────────────────────┐
                  │   PUNISH WINDOW      │
                  │  Fire → mag empties  │
                  └──────────┬───────────┘
                             ▼
                  ┌──────────────────────┐
                  │  RECITE (reload)     │◄── also costs Sanity
                  └──────────┬───────────┘
                             ▼
                  ┌──────────────────────┐
                  │  KILL → Sanity back  │──► loop tightens
                  └──────────────────────┘
```

**The intended failure mode:** a player who dodges everything runs dry, cannot reload, and is standing in a bullet wall with an empty gun. A player who never dodges takes chip damage. The correct play is to *kill fast enough to fund your own defence.*

### 4.2 Minute-to-minute — The Room Loop

```
ENTER ROOM → doors seal → encounter waves resolve → doors open
     │                                                   │
     │                                                   ▼
     │                                         ┌──────────────────┐
     │                                         │ Drops: gold,     │
     │                                         │ ammo, sanity,    │
     │                                         │ occasional sigil │
     │                                         └────────┬─────────┘
     ▼                                                  ▼
 Read minimap → choose next node ◄──────────── Manage: enter REVERIE
 (combat / shop / reward / shrine /             (pause; rearrange the
  corrupted door / secret)                       Sigil Circle freely)
```

Key property: **the player always has a choice of at least two unexplored nodes** after the second room. Flow graphs are authored to guarantee this (see [06](06-procedural-generation.md)).

### 4.3 Run-to-run — The Meta Loop

```
      THE VESTIBULE (hub)
   ┌──────────────────────────┐
   │ • pick Cultist (6)       │
   │ • spend Yellow Fragments │──► inscribe new items into the drop pool
   │ • review Codex entries   │──► lore + mechanical detail for anything seen
   │ • achievement unlocks    │──► new characters, modes, starting sigils
   └────────────┬─────────────┘
                ▼
        DESCEND (floors 1→6)
                │
        ┌───────┴────────┐
        ▼                ▼
      DEATH          VICTORY
        │                │
        └───────┬────────┘
                ▼
   Run summary: floors, kills, corruption peak, seals earned,
   Fragments gained, new Codex entries unlocked
                │
                ▼
        THE VESTIBULE  (loop)
```

**Non-negotiable:** no run-ending outcome may produce zero Fragments and zero new Codex entries. Every run must advance *something*, or the loop leaks players.

---

## 5. Session Shape

| Phase | Duration | Player state |
|---|---|---|
| Floors 1–2 | 10–14 min | Learning the seed. Low commitment. Weapons are disposable. |
| **Reverie decision point** | ~30s | First real build commitment. Circle is ~40% full. |
| Floors 3–4 | 12–16 min | Build online. Corruption decisions bite. Difficulty step. |
| **Warden gate (F4)** | 2–3 min | Skill checkpoint. Failing here is the modal death. |
| Floor 5 (Leng) | 6–9 min | Structural pattern break. Open navigation, no room graph. |
| Floor 6 (R'lyeh) | 8–11 min | Non-euclidean. Maximum density. Everything you built, tested. |
| Cthulhu | 4–6 min | Four-phase boss. |

---

## 6. Difficulty Philosophy

1. **Fair, not easy.** Every death must be attributable to a specific readable mistake. If a playtester cannot say *why* they died, the encounter is broken.
2. **No difficulty selector at the top level.** Difficulty is chosen *inside* the run via Corruption, blasphemous chests, and optional Wardens. This is Gungeon's Curse done deliberately.
3. **Damage is chunky.** The player has 3–6 hearts, not 200 HP. Hits are events. This makes Unbroken Seals (no-damage boss clears) meaningful.
4. **The floor scales to the player, not the clock.** Encounter budget reads player power (weapon tier, sigil count, corruption) — not time elapsed. Never punish careful play with a timer.
5. **Accessibility ≠ difficulty selector.** Separate assist toggles (slower bullets, larger telegraph windows, i-frame extension) are exposed and do not disable achievements. See [10 §7](10-art-audio-ux.md).

---

## 7. Explicit Non-Goals

- **No multiplayer.** Not co-op, not versus, not asynchronous. It doubles the cost of every combat system.
- **No procedurally generated room interiors.** Pillar IV.
- **No permanent damage/health meta upgrades.** They make hour one a bad demo of hour twenty.
- **No crafting system.** Inscriptions are the upgrade layer; a second one is redundant.
- **No narrative cutscenes longer than 15 seconds.** Lore lives in the Codex and in room dressing.
- **No 3D.** Not even for effects.
- **No mobile/console port before 1.0.** Steam Deck compatibility is a constraint we honour from day one; ports are a post-launch conversation.
