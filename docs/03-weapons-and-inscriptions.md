# 03 — Weapons & Inscriptions

> **Thesis:** Gungeon has 200 weapons and no build. We have 40 weapons and 35 inscriptions, which is a larger design space at ~15% of the art cost, and it puts the player in the driver's seat because *they choose the upgrades*.

---

## 1. Weapon Framework

### 1.1 Carry limits
- The player carries **3 weapons** at once (Gungeon lets you carry everything; that removes the decision). A fourth pickup forces a **swap prompt**.
- Weapons swap instantly (0.15s). No animation lock — this is a bullet hell, not a shooter.
- One weapon slot is always the character's **Bound Arm** — their starter weapon, infinite ammo, cannot be dropped. It is the safety net that makes running dry survivable rather than fatal.

### 1.2 The ammo economy
- All non-Bound weapons have finite **reserve ammo**, expressed in magazines (e.g. "6 mags").
- Ammo pickups restore a percentage of max reserve (not flat), so heavy weapons don't feel starved.
- **Empty weapons are not deleted** — they can be refilled at shops or by pickups. This is a deviation from Gungeon and it matters: it preserves build identity across floors, which Gungeon's design actively fights.
- Ammo drops: ~22% from normal enemies, guaranteed from elites, guaranteed from room clears at low reserve (a pity system — if total reserve across all weapons is below 20%, the next room-clear drop is forced to ammo).

### 1.3 Weapon stat block

Every weapon is a `WeaponData` resource with:

```
Name, Tier (D/C/B/A/S), Family, Element
Damage per projectile
Projectiles per shot / spread pattern
Fire rate (RPS)  ·  Charge time (if applicable)
Magazine size  ·  Reserve magazines
Reload duration  ·  RELOAD WEIGHT (0.5–2.0 → Sanity cost multiplier)
Projectile speed  ·  lifetime  ·  size  ·  behaviour script
Knockback  ·  Pierce  ·  Bounce
Inscription slots (1–3, tier-dependent)
Corruption on pickup (0 for most, 1 for a few)
```

**Reload weight is the key balancing lever.** It ties every weapon back into Pillar I. A devastating weapon with weight 2.0 costs 24 Sanity per magazine — you literally cannot afford to dodge much while using it. This creates weapon *personality* on the resource axis, not just the damage axis.

---

## 2. Weapon Families

Six families, each with a distinct silhouette language and a distinct relationship to Sanity.

### Family I — **Relic Arms** (period firearms, 1890–1928)
Grounded, reliable, cheap on Sanity. The backbone of early floors.

| Weapon | Tier | Character |
|---|---|---|
| **Webley Mk VI** | Starter | 6-shot revolver, weight 0.5. Honest. |
| **Trench Sweeper** | C | Pump shotgun, 7-pellet cone, weight 1.0 |
| **Chicago Typewriter** | B | Thompson SMG, 50-round drum, huge spread growth, weight 1.4 |
| **Nitro Express** | B | Double-barrel elephant gun. 2 shots, enormous damage, weight 2.0, knocks *you* back |
| **Miskatonic Service Rifle** | C | Bolt-action. Slow, pierces 3, weight 0.8 |
| **Flare Pistol** | D | Low damage, but ignites and **lights the room** (matters on dark floors) |
| **Derringer of Last Rites** | A | 2 shots. Deals **triple damage when your Sanity is below 20**. |

### Family II — **Defiled Arms** (guns that have been *done something to*)
Relic weapons visibly fused with tissue, chitin, or brass fittings that aren't quite right.

| Weapon | Tier | Character |
|---|---|---|
| **The Weeping Colt** | C | Revolver that fires teeth. Bullets curve slightly toward enemies. |
| **Gristlebore** | B | SMG grown from bone. Fire rate *increases* the longer you hold, resets on release. |
| **Shoggoth Maw** | A | Shotgun that is a mouth. Pellets are homing globs that leave acid pools. |
| **The Congregation** | B | Fires a tight cluster of 5 screaming faces that split on impact into 3 each. |
| **Hookline** | C | Innsmouth harpoon gun. Pulls enemies toward you. Pulls *you* toward walls. |
| **Vermiculate** | A | Fires a burrowing worm that travels *under* the floor, ignoring walls, surfacing under enemies. |
| **Rotgut Repeater** | C | Every kill adds +1 to the magazine size, resets on reload. |

### Family III — **Artefacts** (things dug up that should not have been)
High tier, high weirdness, mechanically unique. These are the "run-defining find" weapons.

| Weapon | Tier | Character |
|---|---|---|
| **The Shining Trapezohedron** | S | Charge beam. Charge time scales *inversely* with missing Sanity. At Sanity < 20 it's instant. |
| **Tillinghast Resonator** | S | Fires nothing visible. Damages everything in a cone **through walls**, and reveals hidden rooms in range. |
| **Elder Sign Projector** | A | Lobs a rotating star that pins itself to a wall and beams nearby enemies for 6s. |
| **The Silver Key** | S | Not a gun. Consumable. Rewinds you to the previous room on death, once. |
| **Azathoth's Flute** | S | Fires expanding sound rings. Damage scales with *number of enemies alive*. Idiot-god energy. |
| **Dho-Hna Lens** | A | Beam that bends 90° at walls, ricocheting infinitely until it expires. |
| **Yith Exchanger** | A | Swaps your position with the nearest enemy and deals damage equal to distance travelled. |

### Family IV — **Grimoires** (spell-casting, Sanity-fuelled)
These consume **Sanity instead of ammo**. Infinite use, but they compete directly with dodging. The purest expression of Pillar I.

| Weapon | Tier | Character |
|---|---|---|
| **Cantrip: Withering** | Starter (Dreamer) | 4 Sanity/shot. A weak, reliable bolt. |
| **The Yellow Sign** | A | 20 Sanity. Marks an enemy; it takes ×2 damage and, when it dies, explodes. |
| **Call of the Deep** | B | 15 Sanity/s channel. Summons a rising column of black water at the cursor. |
| **Dreamlands Gate** | A | 30 Sanity. Opens a portal; anything that enters (including bullets) exits at your cursor. |
| **Ghoulcalling** | B | 25 Sanity. Summons 2 ghouls for 25s. |
| **Ia! Ia!** | S | 50 Sanity. Everything on screen takes damage equal to your *missing* Sanity ×3, **calculated AFTER the cost is paid, and it cannot reduce you below 1 Sanity** *(revised — see note)*. |

> **[REVIEW — Fable] Grimoires need an economy note, and *Ia! Ia!* was a self-Ascension engine.**
> **The exploit:** damage scaled with *missing* Sanity but the cost was paid from the same bar, so the optimal cast was at exactly 50 Sanity — maximum missing Sanity that can still pay the cost — which lands you at **0 and triggers Ascension**. The best use of an S-tier weapon was therefore to deliberately Ascend, in direct violation of [02 §6](02-player-and-combat.md)'s *"Ascension must never be the optimal strategy."* On **The Dreamer** (160 max Sanity, and the character most likely to hold this) the same cast dealt **330 damage** and Ascended, every time. Calculating after the cost and flooring at 1 Sanity removes the Ascension trigger while keeping the "desperation nuke" fantasy intact.
>
> **Wider point — Grimoires are a different game and the docs do not acknowledge it.** [02 §3.2](02-player-and-combat.md) states *"shooting is free; only sustaining fire costs"*, which is the mental model the whole Sanity economy is taught through. Grimoires break it: every shot is a purchase. *Cantrip: Withering* at 4 Sanity/shot means a 30-shot room costs **120 Sanity** — roughly two-thirds of a full room budget — **before any dodging**. That is a fundamentally harsher economy, and it is the **Dreamer's starting weapon**, i.e. the first experience of an unlockable character.
> This is not necessarily wrong — a fragile 160-Sanity caster who pays for every bolt is a coherent identity — but it is **untested by the current M1 plan and it must not be discovered at M4**. A Grimoire is now required in the M1 build ([11 §2](11-roadmap.md)).

### Family V — **Instruments of Devotion** (melee)
Adopted from Pathogenic's melee-viable builds. Melee weapons occupy a weapon slot, have **no ammo**, and **restore Sanity on hit — 3 per hit, capped at 12 Sanity per enemy per 3 seconds** *(cap added — see note)*. They are the answer to running dry.

> **[REVIEW — Fable] §5.3 is confirmed and it is the second-worst break in the economy. Melee was an uncapped Sanity printer, and it is worst on the character built around it.**
> *Sacrificial Kris* is a "fast 3-hit combo" — call it 3 hits/second. At 3 Sanity/hit that is **9 Sanity/s**; on **The Fisherman**, whose Heart Sigil doubles melee Sanity ([08 §7](08-economy-and-meta.md)), it is **18/s — a full Blink Step every second, indefinitely, with no ammo cost.** A gun character must kill a whole fodder enemy (4 Sanity) to fund a *quarter* of a dodge. That is not a balance gap, it is a different game: the Fisherman simply does not play inside Pillar I. The per-enemy rate cap above bounds the printer without making melee useless against crowds.
>
> **Unresolved and more fundamental — melee has no answer to contact damage.** [02 §2](02-player-and-combat.md): *"One enemy contact or one bullet = half a heart."* Melee requires closing to contact range, so the melee player pays health to use their weapon, in a bullet hell, on a 5-heart character. *Whaling Iron* ("slow, huge arc") and *The Ninth Tooth* (dashing thrust that closes distance) can outrange the contact hitbox; *Sacrificial Kris* ("tiny range") explicitly cannot — and it is the **Fisherman's starter**, so a new player's first melee experience is "hitting things damages me."
>
> **Recommendation — one of, decided before M3:**
> - **(a) Preferred: every melee weapon's effective reach must exceed the enemy contact radius**, and melee hits apply knockback that resets contact. Cheap, and it makes melee a *spacing* weapon rather than a hugging weapon.
> - **(b) Melee hits grant 0.2s of contact-damage immunity** against the target struck (a "parry" read).
> - **(c) Cut Family V from v1** and make melee the Fisherman's exclusive identity, per the brief's own suggestion — this is the honest option if (a) does not feel good by M3, and it removes an entire art/animation line.
>
> **Melee must be in the M1 test build.** It is currently scheduled implicitly and it is the family most likely to invalidate the Sanity economy.

> **[DECISION — Opus, 26 Jul 2026] Taking (a) — reach must exceed contact radius — and adding (b) as a narrow window. (c) stays on the table as the M3 fallback.**
>
> The contact-damage problem is the real finding here; the Sanity printer was arithmetic and the cap fixes it. A weapon whose use costs health, in a game where one touch is half a heart, is not a hard weapon — it is a broken one, and no amount of damage tuning repairs it.
>
> **Rule, binding on every melee weapon in the game:**
> - **Effective reach ≥ enemy contact radius + 0.5 units.** No exception. Melee is a *spacing* weapon — you stand at the edge of your arc, not inside the enemy.
> - **Every melee hit applies knockback sufficient to reset contact** with the struck target (minimum 1.2 units).
> - **Melee hits grant 0.25s of contact-damage immunity against the struck target only** *(this is option (b), scoped down)*. It does not protect against that enemy's projectiles, and it does not protect against anything else in the room. It exists so that the frame in which you connect cannot also be the frame in which you are touched — which is otherwise a coin-flip the player cannot see or control.
>
> **This forces one content change: *Sacrificial Kris* cannot be "tiny range".** It is the Fisherman's starter and therefore a new player's first melee experience; a starter weapon that damages its user is the worst possible first impression of an unlockable character. Respecified below as a short but *safe* arc — the compensation for its reach is speed and Sanity throughput, not risk.
>
> **(c) — cut Family V, make melee the Fisherman's exclusive identity — remains the M3 fallback** and is now item 6a on the cut list ([11 §5](11-roadmap.md)). The trigger is explicit: **if melee still does not feel good at M3 after (a) and (b) are implemented, cut the family rather than continuing to tune it.** It removes an entire animation line and the Fisherman keeps the fantasy.

| Weapon | Tier | Character |
|---|---|---|
| **Sacrificial Kris** | Starter (Fisherman variant) | Fast 3-hit combo. **Short but safe arc** — reach clears the contact radius; the trade is arc *width*, not distance *(revised — see note)* |
| **Whaling Iron** | C | Slow, huge arc, knocks back hard |
| **The Ninth Tooth** | B | Dashing thrust that closes 4 units — combos with Blink Step for free repositioning |
| **Censer of Nodens** | A | Swung on a chain, orbits you continuously, damages on contact. Zero input weapon. |
| **Rite Blade** | S | Damage scales with Corruption (×1 + 0.2/Corruption). The corruption-build capstone. |

### Family VI — **Aberrant** (joke/chaos tier — the Gungeon spirit)
Every game like this needs three weapons that are absurd. They also serve as the highest-variance loot and generate clips.

- **The Cat With Too Many Faces** — fires cats. Cats attack enemies. Cats also occasionally attack you.
- **Cthulhu Ftaghn (Kazoo)** — fires the sound of the word. Damage scales with how *long* you've held the trigger this room.
- **Randolph's Filing Cabinet** — fires a random weapon's projectile every shot, from the entire pool, including ones you haven't unlocked.

**Total: 40 weapons.** Distribution: 4 starter (character-bound), 8 D/C, 14 C/B, 10 A, 4 S.

---

## 3. INSCRIPTIONS — The Shop Upgrade System

> This is the system the brief specifically asked for: *"the player can buy upgrades for weapons in shops."*

### 3.1 How it works

- Every weapon has **1–3 Inscription slots** (D/C = 1, B/A = 2, S = 3).
- An Inscription is a permanent modification etched onto that weapon for the rest of the run.
- Sources:
  - **The Inscription Bench** — a fixture in every shop. Pay gold, pick from 3 offered Inscriptions, apply to a chosen weapon.
  - **Reward rooms** — ~20% chance of a free Inscription instead of a sigil.
  - **Wardens** — always drop one.
- **Overwriting** is allowed: replacing a filled slot costs 1.5× and refunds nothing. This lets a player course-correct without making mistakes permanent.
- **[REVIEW — added] Transferring:** at the Inscription Bench, a weapon's Inscriptions may be **moved wholesale to another carried weapon for 60 gold per Inscription**, destroying the source weapon. See the note below.

> **[REVIEW — Fable] §5.6 is real, but the competition is not sigils-vs-inscriptions for gold. It is Inscriptions vs. the ammo economy — one system rewards commitment, the other mandates rotation.**
> §3.4 states Inscriptions are **lost if the weapon is dropped**, and at floor-scaled prices a fully-kitted weapon represents **~250–500 gold** of a 620–900 gold run. That weapon can therefore never be dropped. But the ammo economy exists precisely to force weapon rotation ([00 §1.2](00-comparative-analysis.md): *"it forces weapon rotation… converts loot into a rotating hand"*), and §4 of this document explicitly plans for *"considering dropping the primary for an Artefact"* on Floor 5. The two systems issue opposite instructions to the player at exactly the point the run peaks.
> The transfer rule above resolves it at the cost of one bench affordance: commitment is preserved (you pay again), rotation is possible (you are not locked to a Floor-2 weapon for the rest of the run), and the S-tier Artefact you find on Floor 5 is a genuine decision rather than an automatic decline.
>
> **Sigils and Inscriptions genuinely do not compete for gold** — sigils arrive overwhelmingly from reward rooms, bosses and chests (free), while Inscriptions are the main gold sink. That half of §5.6 is overstated. What they *do* compete for is **player attention and screen time**, and that is a UX problem for M2 to measure, not an economy problem.
- Inscriptions are **weapon-agnostic** but many have conditional text that only makes sense on some weapons — the game shows a live preview of the resulting stat block *before* purchase. No blind buys.

### 3.2 Pricing

| Inscription tier | Base cost (gold) | Floor scaling |
|---|---|---|
| Lesser | 45 | ×1.0 / 1.15 / 1.3 / 1.5 / 1.7 / 2.0 by floor |
| Greater | 90 | same |
| Forbidden | 130 **+1 Corruption** | same |

A typical run generates ~620–900 gold total, so a player who commits their spending to Inscriptions affords roughly **5–7** across a full run — enough to fully kit one weapon and partially kit a second. A player who also buys keys, sigils and consumables should still reach **3+**. That's the intended pressure: **you cannot upgrade everything, so you commit.** *(Revised from "5–8" — see [08 §1.2](08-economy-and-meta.md) for the arithmetic; 8 Greater Inscriptions cost ~1,038 gold against a 900 ceiling.)*

### 3.3 The Inscription pool (~35)

**Lesser (flat improvements, always safe)**
| Name | Effect |
|---|---|
| *Keen Etching* | +15% damage |
| *Swift Etching* | +18% fire rate |
| *Deep Etching* | +40% magazine size |
| *Hoarder's Mark* | +50% reserve ammo |
| *Light Etching* | Reload weight −0.3 (cheaper Sanity reloads) |
| *Steady Hand* | −35% spread |
| *Longreach* | +50% projectile speed and range |
| *Piercing Rune* | Projectiles pierce 1 additional enemy |
| *Rebounding Rune* | Projectiles bounce once off walls |

**Greater (behaviour changes — the interesting layer)**
| Name | Effect |
|---|---|
| *Sigil of the Second Mouth* | Weapon fires a second, weaker projectile at 180° behind you |
| *Gaunt's Bargain* | +45% damage, −40% magazine size |
| *Rite of Recitation* | Perfect Recitation window is 2.5× wider and refunds **all** Sanity |
| *The Hungering Barrel* | Kills with this weapon restore 2 reserve ammo |
| *Whispering Rounds* | Projectiles home weakly (12°/s turn rate) |
| *Sanguine Etching* | +30% damage while at or below half health |
| *Mark of the Tide* | Every 6th shot is a free Banish-lite (destroys bullets in 3 units) |
| *Fractal Etching* | Projectiles split into 2 half-damage shards on enemy hit |
| *Cold of Leng* | Hits slow enemies 25% for 2s, stacking to 60% |
| *Yellow Ink* | +4 Sanity restored per kill with this weapon |
| *Chorus* | Firing continuously for 2s doubles fire rate until you stop |
| *Vessel Rune* | Weapon consumes **Sanity instead of ammo** (converts any gun into a Grimoire) |

**Forbidden (powerful, +1 Corruption each)**
| Name | Effect |
|---|---|
| *The Unblinking Eye* | +60% damage. You take +25% damage. |
| *Tindalos Angle* | Projectiles pass through walls |
| *Devourer's Etching* | Kills with this weapon heal a quarter heart, 8s internal cooldown |
| *The Screaming Sigil* | Every shot deals damage in a small AoE. Also damages you if fired at point-blank. |
| *Sovereign's Mark* | Damage scales with Corruption (+8% per point) |
| *Blood Price* | Firing costs 1 HP per magazine. Damage ×2.2. |
| *The Nameless Etching* | The weapon's identity is scrambled each floor — it randomly becomes a different weapon's firing behaviour, keeping your Inscriptions |

### 3.4 Anti-synergy guards
- A weapon may not carry two Inscriptions from the same *conflict group* (e.g. `Deep Etching` + `Gaunt's Bargain`). The UI greys these out with a reason.
- `Vessel Rune` cannot go on a Grimoire (already Sanity-fuelled) or a melee weapon.
- Inscriptions are lost if the weapon is dropped, and the game confirms this with a modal.

---

## 4. Weapon Progression Across a Run

| Floor | Expected loadout |
|---|---|
| 1 | Bound Arm + 1 found weapon, 0–1 Inscriptions |
| 2 | 2–3 weapons, 1–2 Inscriptions, first shop visit |
| 3 | Committed to a primary. 3 Inscriptions. Ammo pressure real. |
| 4 | Primary fully inscribed (2–3), secondary partially. Warden drop. |
| 5 | Chasing A/S-tier upgrades. Considering dropping the primary for an Artefact. |
| 6 | Final loadout. Ammo conservation is the dominant concern. |

---

## 5. Elements & Status Effects

Kept deliberately small — four statuses, each with a clear visual and a clear counter.

| Element | Status | Effect | Notes |
|---|---|---|---|
| **Fire** | Burning | 4 dmg/s for 4s | Spreads to adjacent enemies. Lights dark rooms. |
| **Brine** | Drenched | +40% damage taken from lightning; −20% move | Innsmouth-flavoured |
| **Void** | Unmade | −30% enemy damage output, 5s | The "defensive" element |
| **Rot** | Festering | Stacks; at 5 stacks the enemy bursts for AoE | Rewards sustained fire |

No elemental resistances on enemies. Resistances make loot feel bad and make builds fail for invisible reasons.

---

## 6. Balancing Framework

**The DPS-to-Sanity ratio is the master metric.** For each weapon, compute:

```
Effective DPS  =  (dmg × projectiles × RPS) × uptime
Sanity Cost/s  =  (reload weight × 12) / (time per magazine cycle)
Efficiency     =  Effective DPS / (1 + Sanity Cost/s)
```

All weapons of the same tier must land within **±18%** on Efficiency at the same Inscription count. Weapons differentiate on *shape* (range, pattern, uptime), never on raw efficiency.

Track this in a spreadsheet generated from the `.tres` resources by a build script — see [09 §7](09-technical-architecture.md).
