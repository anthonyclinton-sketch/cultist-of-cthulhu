# 04 — The Sigil Circle

> The signature system. Derived from *Pathogenic*'s hardpoint grafting, extended with the spatial synergy layer *Enter the Gungeon* only ever had as hidden text.

---

## 1. Concept

Your passive power is not a list. It is a **summoning circle inscribed on your own body**, viewed as an isometric-flat grid in the **Reverie** screen (Tab). Items are **Sigils** — polyomino-shaped tiles you place onto that grid by hand.

Three things make placement matter:

1. **Space is finite.** You will not fit everything. Every new sigil is a *replacement decision*.
2. **Adjacency creates synergy.** Sigils that touch edge-to-edge can trigger bonus effects. These are visible, previewed, and named.
3. **Ley lines multiply.** Fixed high-value tracks across the grid amplify anything placed on them.

The result: loot is not "did I get a good item?" but "**can I fit this, and what does it want to sit next to?**" — a puzzle the player solves in a calm, pausable screen, between fights.

---

## 2. The Grid

### 2.1 Shape

The circle is a **7 × 7 grid with the corners cut** (a rough octagon), 41 usable cells, plus a locked central **Heart** cell.

```
        . . X X X . .
        . X X X X X .
        X X X X X X X
        X X X ♥ X X X        ♥ = The Heart (locked, character core)
        X X X X X X X        ═ = Ley line (see 2.2)
        . X X X X X .
        . . X X X . .
```

### 2.2 Ley lines

Three ley lines run through the circle. Their positions are **fixed per character** (this is a major part of character differentiation) but their *type* rotates each run for variety.

```
        . . X X X . .
        . X X ║ X X .          ║  Vertical ley  — column 3
        X X X ║ X X X
        ═══════♥═══════        ═  Horizontal ley — row 3
        X X X ║ X X X
        . X X ║ X X .          ╲  Diagonal ley   — one of the two diagonals
        . . X X X . .
```

| Ley type | Effect on sigils occupying it |
|---|---|
| **Ley of Blood** | Offensive stats on that sigil ×1.5 |
| **Ley of Salt** | Defensive/utility stats ×1.5 |
| **Ley of Ash** | Sigil's on-kill and on-hit triggers fire twice |
| **Ley of the Gate** | Sigil counts as adjacent to *every* sigil on the same ley, regardless of distance |

A sigil covering multiple cells of a ley gets the bonus once, but a sigil sitting on **two crossing leys** gets both.

**Design consequence:** the ley cross (row 3 / column 3) is prime real estate. Large powerful sigils compete with small efficient ones for it. That's the central tension of the puzzle.

### 2.3 The Heart
The centre cell holds the character's **Heart Sigil** — a fixed, character-defining 1×1 that cannot be removed. It always sits on all leys. Examples: the Antiquarian's *Steady Pulse* (+10% damage), the Dreamer's *Open Eye* (+40 max Sanity).

---

## 3. Sigils

### 3.1 Shapes

Sigils are polyominoes, 1–5 cells, drawn from a fixed shape vocabulary so the puzzle stays learnable:

```
 Mote      Bar       Angle     Tee       Cross     Slab      Serpent
  █        ██        █          ███       ░█░       ██        ██░
                     ██          █        ███       ██        ░██
                                          ░█░
 1 cell   2 cells   3 cells   4 cells   5 cells   4 cells   4 cells
```

**Rule of thumb:** power scales with area, but not linearly — a 5-cell Cross is roughly 3× a 1-cell Mote, not 5×. Small sigils are efficient; large sigils are *identity-defining*. This keeps both viable.

Sigils can be **rotated freely in 90° steps** and (for asymmetric shapes) **mirrored**. Rotation is free and encouraged; it's the main fitting verb.

### 3.2 Facing

Some sigils have a **directional component** — a small arrow on the tile. This is the direct import from Pathogenic's "orientation of your guns and doodads drastically changes how the game is played."

Directional sigils include:
- **Orbiters and turrets**: fire in the direction the sigil faces, *relative to where you're aiming*. A rear-facing *Watcher's Eye* covers your back while you retreat.
- **Aegis-type**: creates a damage-blocking arc in the faced direction.
- **Thrust-type**: adds a dash/knockback impulse in the faced direction on Blink Step.

**UI requirement:** when a directional sigil is selected in Reverie, a ghost overlay on the character portrait shows exactly where it will point in-game. No guessing.

### 3.3 Sigil stat block

```
Name, Tier (D/C/B/A/S), Shape (polyomino mask), Directional (bool)
Base effects[]        — flat stat mods or triggered behaviours
Adjacency tags[]      — what this sigil "offers" to neighbours
Adjacency wants[]     — what this sigil "rewards" being next to
Ley affinity          — Blood / Salt / Ash / Gate / none
Corruption on equip   — 0 or 1
Codex text            — flavour + explicit mechanical text
```

---

## 4. Adjacency Synergies

The heart of the system. Each sigil carries **tags** and **wants**.

### 4.1 The tag vocabulary (keep it small — 8 tags)

`FLESH` · `TIDE` · `STAR` · `VOID` · `MADNESS` · `IRON` · `DREAM` · `BLOOD`

Each sigil offers 0–2 tags and wants 0–2 tags. When a wanting sigil shares an edge with an offering sigil, the **synergy fires** — a named bonus with its own line in the tooltip and a visible arc drawn between the two tiles.

### 4.2 Worked example

```
┌──────────────────────────────────────────────────────────┐
│                     THE REVERIE                          │
│                                                          │
│      . . [A][A][B] . .                                   │
│      . [C][A][A][B][B] .        A  Deep One's Gill  (Slab)│
│      [C][C]═══♥═══[B][D]        B  Ashen Censer    (Angle)│
│      [C] . . ║ . [D][D]         C  Innsmouth Blood (Serpent)│
│      . . . [E][E] . .           D  Watcher's Eye ▶ (Bar)  │
│                                 E  Salt Ward      (Bar)   │
│  SYNERGIES ACTIVE (3)                                    │
│   ⚡ A↔C  "The Tide Rises"  — Drenched enemies take +25%  │
│   ⚡ A↔B  "Brine and Ember" — Burning spreads through water│
│   ⚡ ♥↔B  "Devoted Flame"   — Censer procs on Recitation  │
│                                                          │
│  LEY: Ash (row 3) — B and ♥ trigger twice                │
│  UNUSED CELLS: 22        CORRUPTION FROM SIGILS: 1       │
└──────────────────────────────────────────────────────────┘
```

### 4.3 Why this beats Gungeon's synergies

| Gungeon | Cultist of Cthulhu |
|---|---|
| Synergy is a hidden property of an item *pair* | Synergy is a property of tags, so it generalises |
| Discovered by wiki or accident | Previewed live while dragging a sigil |
| ~350 hand-authored pairs | ~8 tags × ~20 named effects = combinatorial coverage |
| Player has no agency in triggering | Player *builds* it deliberately by placement |
| Ephemeral — you got it or you didn't | You can re-arrange at any time to chase one |

**The design win:** a player who finds a mediocre sigil can still get value by finding a *placement* for it. Loot is never dead.

---

## 5. Sigil Catalogue (design targets: ~70 sigils)

Representative sample across tiers and roles.

### 5.1 Offensive

| Sigil | Shape | Tier | Effect | Tags → Wants |
|---|---|---|---|---|
| **Bloodletter's Nail** | Mote | D | +8% damage | BLOOD → — |
| **Ashen Censer** | Angle | C | On Recitation, emit a burning ring | — → IRON |
| **Watcher's Eye** ▶ | Bar | B | A turret eye fires in the faced direction every 1.2s | STAR → MADNESS |
| **Thousand Young** | Cross | A | Every 8th shot spawns 3 seeking spawn | FLESH → BLOOD, FLESH |
| **Rite of the Open Wound** | Tee | B | Enemies below 30% HP take double damage | BLOOD → BLOOD |
| **The Crawling Chaos** | Serpent | S | Your projectiles randomly change element each shot; all elements +30% | MADNESS, VOID → MADNESS |
| **Hyperborean Edge** | Slab | A | Melee hits chain lightning to 3 nearby | IRON → TIDE |

### 5.2 Sanity & resource

| Sigil | Shape | Tier | Effect |
|---|---|---|---|
| **Candle Stub** | Mote | D | +10 max Sanity |
| **Yellow Ledger** | Bar | C | Perfect Recitation refunds +50% more |
| **Deep One's Gill** | Slab | **A** | **After 4s without dodging, reloading or Banishing, Sanity trickles 3/s until you spend again** *(revised — see note)* |
| **The Unblinking** | Cross | **S** | **Blink Step costs 6 Sanity instead of 18, and each one adds +0.25 Corruption** *(revised — see note)* |
| **Dreamer's Ballast** | Tee | **A** | **Ascension lasts 8s longer; the exit heart cost is reduced by half a heart (never to zero)** *(revised — see note)* |
| **Salt Ward** | Bar | C | Taking damage drains half Sanity (5 instead of 10) *(revised — see note)* |

> **[REVIEW — Fable] Four sigils in this table were rewritten. Three of them individually cancelled a Pillar; the fourth is the bug flagged in the brief.**
>
> **Dreamer's Ballast** *(the §5.2 bug — confirmed, and it was worse than described)*. Old text gave +12s duration **and removed the heart cost**, so a deliberate Ascension cost only −10 max Sanity — and because max Sanity floors at 40, that penalty *stops applying* after six Ascensions. Combined with [02 §6](02-player-and-combat.md)'s "cannot kill you" clause, the cost converged to **exactly zero** for 32 seconds of invulnerability, farmable forever. The base state is now self-limiting (02 §6 debt rule + diminishing duration), and Ballast now *discounts* the cost rather than deleting it. **A cost-removal effect must never exist for Ascension**; discounts only.
>
> **Deep One's Gill.** Old text granted flat in-combat regen, which [01 §2](01-pillars-and-loop.md) Pillar I explicitly lists under *Kills: passive regeneration in combat*, and [02 §3.3](02-player-and-combat.md) calls "deliberate — there is no waiting it out." A B-tier tile cannot repeal a Pillar. At 3/s over a 60s room it was ~180 Sanity — **it roughly doubled the room budget**, more than any other sigil in the game. Rewritten as a *lull* reward: it pays only while you are not spending, so it rewards clean positioning play instead of erasing the resource. Retiered to A.
>
> **The Unblinking.** Old text made dodging **free**, which is the exact mechanic Pillar I exists to remove — and its only cost (+0.1 Corruption) is something a Corruption build actively *wants*, so it was strictly upside for the archetype that most wanted it. It now reduces rather than removes the cost, and the Corruption accrues fast enough (+0.25/dodge) to be a genuine decision. Retiered to S.
>
> **Salt Ward.** Old text removed the hit→Sanity drain entirely, deleting one of the two mechanisms in §3.3 that "punish both extremes." Halved instead.
>
> **General rule added to §8:** *no sigil may reduce a Pillar-I cost to zero.*

### 5.3 Defensive & mobility

| Sigil | Shape | Tier | Effect |
|---|---|---|---|
| **Innsmouth Blood** | Serpent | C | Move ×1.12; you are permanently Drenched (thematic, mostly harmless) |
| **Aegis of Nodens** ▶ | Angle | B | 90° arc in faced direction blocks projectiles; 3s cooldown per block |
| **Elder Sign** | Cross | A | Once per room, negate a lethal hit |
| **Tekeli-li** ▶ | Bar | C | Blink Step gains +2 units of distance in the faced direction |
| **Bone Lattice** | Slab | B | +1 armour at the start of each floor |

### 5.4 Corruption-scaling (the dedicated archetype)

| Sigil | Shape | Tier | Effect |
|---|---|---|---|
| **Sovereign's Brand** | Bar | B | +6% damage per Corruption. **+1 Corruption on equip.** |
| **The Hound's Collar** | Angle | A | The Hound of Tindalos is friendly and fights for you. +1 Corruption. |
| **Ninefold Seal** | Cross | S | At Corruption 7+: all your projectiles pierce and gain +50% size |
| **Black Font Shard** | Mote | C | Corruption is capped at 12 instead of 10, and thresholds pay out 20% more |

### 5.5 Economy

| Sigil | Shape | Tier | Effect |
|---|---|---|---|
| **Gaunt's Favour** | Bar | C | Shop prices −20% |
| **Ossuary Ring** | Mote | D | +1 key per floor |
| **Antiquarian's Loupe** | Angle | B | Reveals the tier of chests and shop items before purchase; +15% gold |
| **The Ledger of Names** | Tee | A | Each room cleared without damage grants 25 gold |

---

## 6. Acquisition & Removal

| Source | Sigils per floor |
|---|---|
| Reward (item) room — guaranteed, 1 per floor | 1 |
| Chests (D/C/B/A/S tiers, key-locked) | 1–3 |
| Blasphemous chests (free, +1 Corruption) | 0–1 |
| Shop (2 sigils stocked) | 0–2 |
| Warden drop (floors 2, 4) | 1 |
| Tomes (read for +1 Corruption) | 0–1 |
| Boss drop | 1 guaranteed, tier scales with Corruption |

**Expected total across a full run: 12–18 sigils from *guaranteed* sources** (6 reward rooms + 6 boss drops + 2 Wardens), **rising to ~22–28 once chests, shops, Blasphemous chests and Tomes are included.** At an average 2.6 cells that is **57–73 cells against 41 capacity** — the player is oversupplied by roughly 50%, which is the intended "you must cut things" pressure.

> **[REVIEW — Fable] The old numbers did not reconcile with this document's own acquisition table, in two directions at once.**
> - **The stated total was too low.** Summing the table's per-floor minimums (1 reward + 1 chest + 1 boss = 3/floor) already gives **18 sigils** across six floors, and the maximums give **60** — so "12–18" counted guaranteed sources only while the table above it describes far more.
> - **The capacity invariant was false at the low end.** 12 sigils × 2.6 = **31 cells against 41 capacity** — the circle does *not* fill, so the central "every pickup is a replacement decision" tension silently disappears on low-roll runs. [08 §8](08-economy-and-meta.md)'s checklist asserted this invariant unconditionally; it has been corrected there too.
> - Arithmetic slip: 12–18 sigils × 2.6 is **31–47** cells, not the stated 35–47.
>
> **Consequence to watch:** at ~25 sigils and a 6-slot Reliquary, a large fraction of drops are pure waste, which contradicts §4.3's claim that *"loot is never dead."* Either raise the Reliquary cap or accept that late-run sigil drops are mostly gold-equivalent and say so. **Recommend: allow surplus sigils to be dissolved at any shop for gold** — it costs one UI affordance and makes the claim true.

**Removal:** free and instant, in Reverie. Removed sigils go to a **Reliquary** (a stash of up to 6) so nothing is permanently lost — you can swap builds between floors. Removing a sigil refunds any Corruption it granted.

**Dissolution *(added — resolves the review's "loot is never dead" contradiction)*:** any sigil in the Reliquary may be **dissolved at Gaunt's stall for gold**, at `20 × cells × tierMultiplier` (D 1.0 / C 1.4 / B 2.0 / A 3.0 / S 4.5). A 4-cell A-tier sigil is therefore worth 240 gold — a meaningful fraction of an Inscription.

> **[DECISION — Opus, 26 Jul 2026] Adopting the review's recommendation, and it earns its keep twice.**
> The review is right that at ~25 sigils against 41 cells and a 6-slot Reliquary, most late-run drops were literally nothing — which made §4.3's claim that *"loot is never dead"* false, and made the whole back half of a run's sigil economy feel like noise.
> Dissolution fixes that, and it also **closes the gold shortfall the review found in [08 §1.2](08-economy-and-meta.md)** — the 620–900 gold budget could not actually fund the Inscription spend the design assumed. Rather than inflating base gold drops (which would devalue every other gold source), surplus sigils become the late-run gold faucet. That is better economics: it converts the system's *own* oversupply into the currency the player is short of, and it means a lucky sigil run cashes out into a strong weapon rather than being wasted.
> **Guard:** dissolution is Reliquary-only — you cannot dissolve an equipped sigil without first removing it, which keeps the decision inside Reverie where the diff panel can show what you are giving up.

---

## 7. The Reverie Screen (UX spec)

- Opens on Tab. **Pauses the game entirely** (this is not a Dark Souls inventory).
- Only openable **outside combat** (no doors sealed). Prevents mid-fight optimization exploits.
- Drag with mouse or move a cursor with the stick; `Y`/right-click to rotate, `X` to mirror.
- **Live diff panel** on the right: shows the delta of every stat and every synergy that will gain/lose, updating as you drag.
- Invalid placements highlight red with the reason ("overlaps Bone Lattice").
- **Auto-arrange button** (fills greedily by tier) for accessibility and for players who don't want the puzzle. It is deliberately mediocre — it never finds the best adjacency layout.
- A **"?"** on any sigil opens its Codex entry, which lists every synergy it can participate in, including undiscovered ones as `???` with the tag revealed.

---

## 8. Balance Rules

1. **No sigil may exceed +25% to a single core stat** (damage, fire rate, move speed). Stacking is the source of power, not any single tile.
2. **Every tier-A and S sigil must change how you play, not just how hard you hit.** If it can be summarised as a percentage, it is not A tier.
3. **Adjacency bonuses cap at 6 active synergies.** Beyond that, additional synergies grant a flat 3% damage each. Prevents unbounded blowup and keeps the tooltip readable.
4. **Every shape must have at least 6 sigils in the pool** so no shape is a dead draw.
5. **Corruption sigils must be net-negative at Corruption 0** — they're an investment, not free power.
6. **[REVIEW — added] No sigil may reduce a Pillar-I cost to zero.** Dodge, Recitation, Banish and Ascension costs may be *discounted*, never removed, and no sigil may grant unconditional in-combat Sanity regeneration. A single tile must not be able to repeal a Pillar; if a sigil's effect can be summarised as "X no longer costs anything," it is rewritten as a discount.
7. **[REVIEW — added] Corruption-cost effects must be priced against the Corruption *build*, not against a neutral player.** An effect whose only drawback is +Corruption is free upside for the archetype most likely to take it — the drawback must bite at the rate a Corruption build accrues it.
