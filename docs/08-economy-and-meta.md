# 08 — Economy, Shops & Meta Progression

---

## 1. Currencies

Two in-run currencies, one meta currency. Deliberately minimal.

| Currency | Symbol | Use | Persists between runs? |
|---|---|---|---|
| **Innsmouth Gold** | ⬤ | Buy anything at shops; Inscriptions | No |
| **Ossuary Keys** | ✚ | Open locked chests and doors | No |
| **Yellow Fragments** | ✦ | Unlock new content into the drop pool | **Yes** |

### 1.1 Why two in-run currencies

Straight from Gungeon, and it's the right call: gold is *abundant and fungible*, keys are *scarce and specific*. Every locked chest is therefore a real decision. Keys are purchasable at shops at a **deliberately bad rate** (60 gold, rising 15 per purchase per run) so the two currencies are convertible but conversion hurts.

### 1.2 Gold income model

| Source | Gold |
|---|---|
| Fodder kill | 1–3 |
| Elite kill | 12–20 |
| Room clear bonus | 8 × floor |
| Chest (gold-type) | 40–80 |
| Boss | 60 + 20/floor |
| Selling a weapon at shop | 30% of value |
| *The Ledger of Names* sigil | 25 per no-damage room clear |

**Target totals per full run:** 620–900 gold. Enough for **~5–7 Inscriptions *if Inscriptions are the player's chosen spending identity and they buy little else*** — or 2 weapons and 3 Inscriptions, or an unhealthy number of keys. The player must choose a spending identity.

> **[REVIEW — Fable] The gold budget is over-subscribed; "5–8 Inscriptions" did not survive this document's own price scaling.**
> Inscription base costs are 45 / 90 / 130 and floor scaling is ×1.0 → ×2.0, a **mean of ×1.44**. Against the 620–900 budget:
>
> | Purchase | 5× | 8× |
> |---|---|---|
> | Lesser (45) | 324 | 519 |
> | Greater (90) | **649** | **1,038** |
> | Forbidden (130) | 937 | 1,499 |
>
> **8 Greater Inscriptions costs 1,038 gold against a 900-gold ceiling** — and that is before keys (the player needs 6–9 and holds 9–13 locked chests), shop sigils at 80–260, hearts at 140, armour at 90, ammo at 45, and rerolls at 50+25. The upper bound was only reachable by buying nothing else and buying only the cheapest tier.
> Corrected to 5–7 and scoped to a committed spender. **The design intent is unchanged and good** — "you cannot upgrade everything, so you commit" is the right pressure. The error was that the checklist asserted the outcome unconditionally, which would have driven a false-confident tuning pass.

**Anti-swing guard:** each floor has a guaranteed minimum gold payout. If the player finishes a floor below `floorMin`, the boss drop tops them up. Bad seeds should not end runs.

### 1.3 Keys

- Sources: room-clear drop (8%), guaranteed 1 from each Warden, 1 guaranteed per floor from a `connector` room chest, purchasable.
- **Expected keys per run: 6–9. Expected locked chests per run: 9–13.** The deficit is the point.
- **Pity:** if the player has 0 keys and has cleared 5 rooms since their last key, the next room-clear drop is forced to a key.

---

## 2. Shops

### 2.1 Gaunt's Stall

**Gaunt** is a ghoul merchant in a moth-eaten Victorian coat who is unfailingly courteous and eats the dead you leave behind. He appears once per floor from floor 1 onward (guaranteed from floor 2; ~70% on floor 1).

**Stock (7 slots, rerolled per floor):**

| Slot | Contents | Price band |
|---|---|---|
| 1–2 | **Sigils** (tier weighted by floor + Corruption) | 80–260 |
| 3 | **Weapon** | 100–320 |
| 4–5 | **Inscription Bench** — 3 offers, apply to any carried weapon | 45–130 |
| 6 | **Consumables** — ammo refill (45), Sanity candle (35), key (60+15/purchase), armour (90), heart (140) |
| 7 | **The Odd Item** — one high-value, high-weirdness item; sometimes cursed, always interesting | 150–400 |
| — | **The Dissolution Bowl** *(added)* — sell any Reliquary sigil for `20 × cells × tierMult` (D 1.0 / C 1.4 / B 2.0 / A 3.0 / S 4.5). Not a stock slot; always available. | *pays out* |

**Prices scale by floor:** ×1.0 / 1.15 / 1.3 / 1.5 / 1.7 / 2.0.

> **[DECISION — Opus] The Dissolution Bowl is the fix for §1.2's gold shortfall, not a gold-drop increase.** The review showed the 620–900 budget could not fund the Inscription spend the design assumed. [04 §6](04-sigil-circle.md) simultaneously showed the player is oversupplied with sigils by ~50%. Converting the surplus of one into the shortfall of the other solves both with a single affordance and without devaluing any existing gold source. Expected contribution: **120–320 gold across a full run**, concentrated in floors 4–6 where Inscription prices scale hardest. Re-run the economy simulation with this included before touching base drop rates.

### 2.2 The Inscription Bench — the featured system

This is the mechanism the brief specifically calls for: *buy upgrades for weapons in shops*.

```
┌────────────────── THE INSCRIPTION BENCH ────────────────────┐
│                                                             │
│  SELECT WEAPON:   ◄  [ Shoggoth Maw ]  ►                    │
│  Slots: ●●○  (2 of 3 filled)                                │
│    ● Keen Etching    (+15% damage)                          │
│    ● Whispering Rounds (homing)                             │
│    ○ empty                                                  │
│                                                             │
│  OFFERS:                                                    │
│   [1] Fractal Etching     Greater   90 ⬤                    │
│       Projectiles split into 2 shards on hit                │
│       ▸ PREVIEW: DPS 142 → 171  ·  Sanity/s 8.4 → 8.4       │
│                                                             │
│   [2] The Hungering Barrel Greater   90 ⬤                   │
│       Kills restore 2 reserve ammo                          │
│       ▸ PREVIEW: sustain 6 mags → effectively 9.2           │
│                                                             │
│   [3] Blood Price         Forbidden 130 ⬤  +1 CORRUPTION    │
│       Firing costs 1 HP per mag. Damage ×2.2                │
│       ▸ PREVIEW: DPS 142 → 312  ·  ⚠ 1 HP per magazine      │
│                                                             │
│  [ REROLL OFFERS — 50 ⬤ ]        Gold: 214 ⬤   Keys: 3 ✚    │
└─────────────────────────────────────────────────────────────┘
```

Design rules:
- **Live stat preview before purchase.** No blind buys. Ever.
- **Reroll** for 50 gold (+25 per reroll this floor) — a gold sink that gives agency over bad offers.
- **Overwrite** a filled slot at 1.5× cost. Mistakes are recoverable, at a price.
- Conflicting Inscriptions grey out with an explicit reason.

### 2.3 Stealing
You can shoot Gaunt. If you do:
- He drops everything in stock.
- He **never appears again this run** (all future shops are empty stalls, which is a large loss).
- You gain **+2 Corruption**.
- On the next run, one Vestibule NPC comments on it. Persistent, cosmetic, delightful.

Not a good play. Available anyway, because the option existing is the point.

### 2.4 The Caravan (Floor 5)
The Leng shop is a caravan of Men of Leng. Same stock structure, but they **barter** — they will trade a sigil for a sigil, or an Inscription for one point of Corruption, no gold involved. Different economy on the floor that breaks every other formula.

---

## 3. Item (Reward) Rooms

One guaranteed per floor, injected by the flow generator at a dead end.

**Contents:** a single pedestal with a choice of **two sigils**, take one. (A choice of two is dramatically better than a fixed one — it converts luck into a decision.)

At Corruption ≥ 3, a **third option** appears: a higher-tier sigil that costs +1 Corruption to take.

Roll table (before Corruption modifiers):

| Floor | D | C | B | A | S |
|---|---|---|---|---|---|
| 1 | 30% | 45% | 22% | 3% | — |
| 2 | 15% | 42% | 33% | 9% | 1% |
| 3 | 6% | 33% | 40% | 18% | 3% |
| 4 | — | 24% | 41% | 28% | 7% |
| 5 | — | 14% | 36% | 38% | 12% |
| 6 | — | 6% | 28% | 44% | 22% |

Corruption shifts a roll up one tier with probability `20% / 45% / 70%` at thresholds 1 / 3 / 5.

---

## 4. Chests

| Tier | Colour | Key? | Contents |
|---|---|---|---|
| **D — Rust** | brown | Free | Gold, ammo, a D sigil |
| **C — Brass** | brass | 1 ✚ | C sigil or a C/B weapon |
| **B — Silver** | silver | 1 ✚ | B sigil, weapon, or Inscription |
| **A — Gold** | gold | 1 ✚ | A sigil or weapon; 20% two items |
| **S — Obsidian** | black/violet | 2 ✚ | S sigil or Artefact weapon |
| **Blasphemous** | pulsing red | **Free** | Always B+; **+1 Corruption**; 12% chance it is a Mimic |

**Mimics** (8% of all chests, 12% of Blasphemous): a chest that is an enemy. It has a tell — a single frame of animation every ~4s, and it never has dust on it. Killing a mimic yields the loot it was impersonating plus a bonus.

**Design note on Blasphemous Chests:** free, high-tier loot that costs Corruption is the game's cleanest expression of its central trade. A player short on keys but willing to be brave has a real, distinct path.

---

## 5. Shrines

One or two per floor, injected as dead-end nodes. Each is a one-shot, clearly-labelled trade.

| Shrine | Cost | Reward |
|---|---|---|
| **Black Font** | +1 to +3 Corruption (rolled, shown before) | A random A/S sigil |
| **The Weighing Stone** | Half your current gold | Best-tier sigil the floor can offer |
| **Altar of Nodens** | 1 heart container (permanent for the run) | +40 max Sanity |
| **The Cleansing Pool** | Destroys one random equipped sigil | −2 Corruption |
| **The Bargainer's Table** | One weapon (destroyed) | Two Inscriptions applied to another weapon |
| **The Mirror of Yith** | Sanity drops to 1 immediately | An S-tier sigil |
| **The Ledger Stone** | Nothing | Reveals the full floor map + all secret rooms. Costs 15 Sanity. |

**Rule:** every shrine states its exact cost and the *tier* of its reward before commitment. Uncertainty is fine; hidden costs are not.

---

## 6. Meta Progression

Hybrid of the two reference models. From *Pathogenic*: achievement-driven, lateral unlocks so the game is fully playable at hour one. From *Gungeon*: a content-unlock currency so there's a long tail — but **breadth only, never power**.

### 6.1 Yellow Fragments (✦)

- Earned: 1 per boss killed **first time this run** (so 6 max/run), +3 for a Sovereign boss, +2 per new Codex entry milestone, +5 for a full clear.
- Spent in the Vestibule at **The Ledger** to inscribe new weapons, sigils, and Inscriptions into the drop pool.
- **They buy no stats.** Never. Not +5% damage, not +1 heart. Unlocking content changes what runs look like; it does not make the player stronger.
- A full unlock of everything takes ~35–50 runs. Long enough to matter, short enough to finish.

### 6.2 Achievement unlocks (lateral)

| Unlock | Condition |
|---|---|
| The Dreamer (character) | Reach Floor 3 |
| The Fisherman (character) | Kill Mother Hydra's Brood |
| The Deserter (character) | Earn 3 Unbroken Seals in one run |
| The Professor (character) | Fill 30 cells of the Sigil Circle |
| **The Nameless** (character) | Achieve the Yellow Ending |
| Corrupted Start mode | Reach Corruption 10 |
| Seeded runs / daily run | Clear Floor 4 once |
| Ascension Codex chapter | Ascend 10 times total |
| Boss Rush mode | Kill every boss at least once |

### 6.3 The Vestibule (hub)

Small, fast to traverse, no combat. Contains:
- **The Circle** — character select, laid out as six seats around a table
- **The Ledger** — Fragment spending; also lists every dead run by name
- **The Codex** — everything encountered, with full mechanical text
- **The Standing Stones** — achievements and statistics
- **The Door**

**Requirement: launch → in a run in under 8 seconds** with Skip Intro on, and death → new run in under 5. Hub friction is the single biggest killer of roguelike retention.

---

## 7. The Playable Cultists

Characters differentiate on **Sanity relationship, ley line layout, and Bound Arm** — not on flat stat bumps.

| Character | Hearts | Max Sanity | Bound Arm | Heart Sigil | Ley layout | Identity |
|---|---|---|---|---|---|---|
| **The Antiquarian** | 3 | 100 | Webley Mk VI | *Steady Pulse* (+10% dmg) | Blood ═ / Salt ║ | The baseline. Honest gunplay. |
| **The Dreamer** | 2 | 160 | Cantrip: Withering | *Open Eye* (+40 max Sanity) | Ash ═ / Gate ║ | Fragile. Grimoire-focused. Ascends often, and is built for it. |
| **The Fisherman** | 5 | 70 | Sacrificial Kris | *Innsmouth Blood* (melee heals Sanity ×2) | Salt ═ / Blood ╲ | Melee bruiser. Permanently Drenched. Deep Ones don't attack him on Floor 2. |
| **The Deserter** | 4 | 80 | Chicago Typewriter | *Trench Discipline* (+60% reserve ammo) | Blood ═ / Blood ║ | No madness synergies. Pure ammo economy. The "I just want to shoot" character. |
| **The Professor** | 3 | 90 | Miskatonic Service Rifle | *Marginalia* (+1 sigil from every reward room) | Gate ═ / Gate ║ | Build-focused. Weakest raw power, biggest circle payoff. |
| **The Nameless** *(unlock)* | 1 | 100 | Rite Blade | *The Yellow Sign* (starts at **Corruption 5**) | Ash ═ / Ash ║ / Ash ╲ | Starts deep in corruption. One heart. Endgame character. |

**Ley layouts are a major differentiator** and should be the last thing balanced — a character with two Blood leys plays completely differently from one with two Gate leys, and that's worth more than any stat.

---

## 8. Economy Balance Checklist

Re-verify these whenever any number in this document changes:

```
□ Expected gold/run in [620, 900]
□ Expected keys/run in [6, 9]; locked chests in [9, 13]
□ A player who spends ONLY on Inscriptions can afford 5-7 across a full run   [REVISED]
□ A player who spends on keys + consumables can still afford 3+ Inscriptions  [ADDED]
□ Sigil cell supply exceeds circle capacity (41) at the 10th PERCENTILE of    [REVISED]
  drop luck, not merely on average  -- low-roll runs must still fill the circle
□ Surplus sigils have a non-zero use (Reliquary, or dissolve-for-gold)        [ADDED]
□ No run can produce 0 Fragments and 0 Codex entries
□ Corruption 10 is reachable by floor 4 if actively pursued
□ Corruption 0 is achievable through floor 6 without feeling punished
□ Every shop slot has a meaningful buy at every floor's price scaling
□ Meta unlocks grant zero permanent stat increases
□ No single sigil or Inscription reduces a Pillar-I cost to zero              [ADDED]
□ Median Sanity spend per room is within +-15 of income at floor difficulty   [ADDED]
```

> **[REVIEW — Fable] Three checklist items were wrong as stated and are corrected above.**
> - *"5–8 Inscriptions"* was unaffordable at the top of the range (see §1.2) and unqualified as to what else the player buys.
> - *"12–18 sigils ≈ 35–47 cells exceeds capacity (41)"* was **false at the low end**: 12 × 2.6 = **31 cells**, which does not fill a 41-cell circle. An invariant that fails on unlucky runs is not an invariant — and this one guards the central tension of Pillar II ([04 §6](04-sigil-circle.md)). Restated as a percentile condition, which is how it should be verified in the 1000-run economy simulation ([09 §9](09-technical-architecture.md)).
> - The arithmetic itself was off: 12–18 × 2.6 is 31–47, not 35–47.
