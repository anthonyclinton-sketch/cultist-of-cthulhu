# 00 — Comparative Analysis

A teardown of the two reference games, isolating the *mechanism* behind each design choice rather than the surface feature — because copying a feature without its supporting systems is how clones die.

---

## Part 1 — Enter the Gungeon (Dodge Roll, 2016)

### 1.1 The dodge roll is the entire game

Gungeon's design is downstream of one decision: **the dodge roll grants invulnerability frames and is available roughly every 0.6s with no resource cost.** Everything else is a consequence.

- Because i-frames are cheap and frequent, bullet patterns can be *dense* — the game can throw walls of projectiles that would be unfair in a game where you only have movement.
- Because the roll has a fixed distance and a recovery tail, the skill ceiling is **timing**, not positioning. Novices roll early and eat the bullet on recovery; experts roll late.
- Because the roll also vaults tables and dodges through enemies, it doubles as a traversal verb, so it never feels like a purely defensive button.

**The trap:** a free, spammable i-frame dodge makes the *rest* of your defensive toolkit meaningless. Gungeon accepts this. Positioning matters far less than in Nuclear Throne or Hades.

**Our decision:** dodge costs a resource (Sanity). This restores positioning as a real skill and creates a decision at every bullet: *do I pay to solve this, or do I move?* See [02](02-player-and-combat.md).

### 1.2 Ammo scarcity as a forced-variety engine

Every gun except the starter pistol has finite ammo. This is the least-discussed and most important system in the game.

- It forces weapon rotation. You cannot find one good gun on floor 1 and coast.
- It converts loot into a *rotating hand* rather than a *build*. Gungeon is a game about resource management disguised as a game about guns.
- It makes ammo boxes a meaningful pickup and makes the "which gun do I burn on this boss?" question real.

**The cost:** it actively fights build identity. Players who want to specialize are punished. Gungeon compensates with 200+ guns so the churn is entertaining, which requires an enormous art and design budget.

**Our decision:** keep finite ammo, **shrink the arsenal to ~40 weapons**, and add depth through per-weapon **Inscriptions** (up to 3 upgrade slots, bought in shops) instead of breadth. 40 weapons × 3 slots from a pool of ~35 inscriptions is a far larger design space than 200 flat guns, at a fraction of the art cost. See [03](03-weapons-and-inscriptions.md).

### 1.3 The two-currency economy

Shells buy things; **Keys open things**. Keys are scarcer than money, which is the whole point:

- Every locked chest is a decision, not a formality.
- Keys are also sold in the shop, so gold and keys are convertible at a bad rate — creating a genuine economic tension.
- Chest **quality tiers (D/C/B/A/S)** let the game telegraph value before you spend, so the decision is informed.
- **Mimics** poison the pool just enough that opening a chest is never fully automatic.

This is close to perfect and we adopt it nearly wholesale, reskinned.

### 1.4 Curse — a risk stat, and its failure

Curse raises loot quality and reveals hidden content. At Curse ≥ 5 enemies become jammed (faster, tougher, black-and-red). At Curse 10 the **Lord of the Jammed** hunts you and one-shots you.

**Why it's brilliant:** it's a player-authored difficulty slider that pays out in power, and it's *incremental* — you creep into it, item by item, and can misjudge.

**Why it underperforms in practice:** curse is mostly an accident. Most players acquire it passively from items they wanted for other reasons and never engage with it as a strategy. There is no "curse build."

**Our decision:** rebuild it as **Corruption** — deliberately farmable, with explicit content gated behind thresholds (Corruption-locked doors, blasphemous chests that are *free* but costly), so choosing to go deep is a real strategy rather than an accident. See [02 §5](02-player-and-combat.md).

### 1.5 Coolness, Master Rounds, and rewarding execution

- **Master Round**: clear a boss without taking damage → +1 permanent heart container for that run. This is the single best mechanic in the game. It rewards mastery with *the resource that lets you attempt harder mastery*, and it makes every boss a self-imposed challenge run.
- **Coolness** (a hidden luck stat) reduces active item cooldowns and improves drops.

**Our decision:** Master Rounds are non-negotiable — we take this directly (as **Unbroken Seals**). Coolness we make *visible*, because a hidden stat players don't understand generates no decisions.

### 1.6 Procedural generation — the actual algorithm

This is the most technically transferable part of Gungeon and worth stating precisely, per Boris the Brave's reverse-engineering and the developers' talks:

1. **Rooms are hand-authored.** Every room in Gungeon was designed and playtested by hand. Nothing about the interior of a room is random except enemy selection.
2. **Floors are assembled from pre-authored "flows"** — graph data structures with no spatial information. The Hollow has 4 flows; Gungeon Proper has 8. The stated goal was "approximate a Zelda dungeon with each generation."
3. A flow is a **tree with a root, plus extra edges added to create loops** — so every loop has a well-defined entrance and exit.
4. **Flow transformation** before layout: some nodes expand into random-length chains of rooms; alternate branches are chosen; **special nodes are injected** conditionally (dead-end preference, probability rolls, game-state triggers like NPC rescues).
5. **Composite decomposition**: repeatedly find the smallest loop and cut it out as a composite, until only trees remain.
6. **Layout**: loop composites are built by adding rooms alternately at either end of a line, preferring exit pairs that close the gap; tree composites use depth-first placement preferring exits far from used ones, with backtracking.
7. **Crucially — hardest first.** They lay out the most connected, most important parts of the level first and fit everything else around them. Corridors (4–30 tiles, up to 50 in the Mines) stitch the rest.

**Why this beats naive BSP or random-walk generation:** it guarantees *pacing*. A random dungeon has random pacing. A flow-based dungeon has authored pacing with random topology. This is the correct architecture and we adopt it directly. See [06](06-procedural-generation.md).

### 1.7 What Gungeon gets wrong

| Problem | Consequence | Our answer |
|---|---|---|
| Synergies are hidden and undiscoverable without a wiki | Most players never experience the best content in the game | Synergies are **spatial and visible** — you build them yourself on the circle |
| No build identity; the run is a hand of cards, not a character | Runs blur together; low motivation to theorycraft between runs | Sigil circle persists all run; weapons rotate around it |
| The final unlock grind (Bullet That Can Kill The Past) is punishing | Large fraction of players never see the real ending | Endings are gated on *skill demonstrations*, not repetition |
| Shop prices and floor economy are swingy | Some runs are simply poor | Guaranteed floor income floor/ceiling; see [08](08-economy-and-meta.md) |
| Passive items pile into an unreadable soup | Late-run you cannot tell what is doing what | Circle has finite slots — every pickup is a *replacement decision* |

---

## Part 2 — Pathogenic (2026)

A twin-stick roguelite in which you are a pathogen invading a human host, released 16 July 2026, ~94% positive on Steam. It is a much smaller and more recent game than Gungeon, and its innovations are concentrated in three places.

### 2.1 The modular body — upgrades as physical objects

You loot organelles from enemy cells and **graft them onto your own body**. Flagella for movement, mitochondria for power, secretors for ranged attack, spikes for melee. Reviewers note that **"the orientation of your guns and doodads can drastically change how the game is played"** — a gun mounted rearward is a rear-firing gun.

There are a **limited number of hardpoints**, and you can enter an **edit mode at any time** to re-arrange your build ("transmogrification").

> **[REVIEW — Fable] Verified and slightly expanded from live sources.** Confirmed: limited hardpoints, free edit mode at any time, **120+ organelles**, 7 playable pathogens, released 16 July 2026 (Aberrant Labs / Slug Disco). Two details that *support* the Sigil Circle direction and were not in the original analysis:
> - **Positional constraints already exist in the source game** — *"certain mitochondria have to be placed on specific locations of your virus to work at all"*, and *"not everything can be snapped together like a simple jigsaw puzzle."* So the spatial-constraint layer is not purely our invention.
> - **Orientation is used as per-character differentiation** — the Fungal Spore pathogen *"uses a fixed orientation and special Organelle slots as an interesting layout constraint."* This is close to our per-character **ley line layouts** ([08 §7](08-economy-and-meta.md)) and is good evidence that differentiating characters by *grid shape* rather than stats works in practice.
>
> Net: **Bet 2's evidence base is stronger than Bet 1's.** The extrapolation flagged in the brief's §2 is largely borne out.

**Why this is the most important idea in the game:**
- It makes the build *legible*. You can see your build. It's your body.
- It converts loot from "number goes up" into "spatial puzzle."
- Limited hardpoints mean every pickup is a **trade-off**, not an accumulation. This is the single biggest structural improvement over Gungeon's item soup.
- Free re-arrangement means the player is never punished for experimenting — the friction is in *choosing*, not in *committing*.

**Our decision:** this is the system we build the game around, reinterpreted as an occult summoning circle. We add two things Pathogenic does not have: **adjacency synergies** and **ley lines**, converting it from a slot-assignment problem into a genuine spatial optimization puzzle. See [04](04-sigil-circle.md).

### 2.2 Stamina shared between dodge and reload

Per the Try Hard Guides review: **"stamina is used both for dodging and as your reload or magazine size mechanic."**

This is a candidate fix to Gungeon's free-dodge problem. It means:
- Every dodge is ammunition you didn't fire.
- Every reload is a dodge you can't make.
- You cannot turtle *or* mindlessly spray. The optimal line is a rhythm.

> **[REVIEW — Fable, 26 Jul 2026] CORRECTION — this mechanic is Pathogenic's most criticised system, not its most praised. Verified against live sources; the previous framing ("the elegant fix", "adopt wholesale") was not supportable.**
>
> Player and reviewer reception of the shared stamina bar is **actively contested**:
> - *"The stamina system really screws up the rhythm of this fast paced dodging style… you should not be punished for trying to deal any damage and missing that 0.5 second window."* — Steam Gameplay Discussions
> - *"Being forced to stop shooting and wait can interrupt the flow and make combat feel less responsive… the stamina system feels like it takes away control over damage windows."*
> - The most-linked thread on the mechanic is titled **"dude just pick one, not both"**, arguing the dual role is *"a sloppy mess."*
>
> **Most important: the developer's own stated rationale does not transfer to our game.** Pathogenic's developer defends the shared bar as a throttle on **10 simultaneously-equipped weapons**, introduced to prevent *"very chaotic gameplay"*, and rejected magazine-based reloading as impractical to synchronise across that many weapons. It is a **fix for a problem created by auto-firing a hydra of ten guns.** *Cultist of Cthulhu* carries **three weapons and fires one at a time** ([03 §1.1](03-weapons-and-inscriptions.md)) — we do not have the problem the mechanism was built to solve.
>
> **What this does and does not mean:**
> - It does **not** invalidate Bet 1. The mechanism can still be good; Pathogenic is ~94% positive *with* it, and the complaints are about feel and flow, not about the idea being incoherent.
> - It **does** remove the "proven elsewhere" argument. We are not adopting a validated mechanic — **we are prototyping a contested one, in a game whose weapon count removes its original justification.** That reclassifies Bet 1 from "borrowed and safe" to "genuinely novel and unvalidated," which is precisely why [11 §2](11-roadmap.md)'s M1 gate exists and why it needs a control arm (see M1 test design).
> - The specific complaint — *being forced to stop shooting* — is the one to design against. Our Sanity bar never gates **firing** (except Grimoires), only dodge and reload, which is a materially softer constraint than Pathogenic's. Preserve that distinction deliberately; it is our main protection against the failure mode their players report.

**Our decision:** adopt the *shape* of the mechanism — shared cost across defence and sustain — bound to **Sanity** so it carries thematic and narrative weight, with regeneration-on-kill so the rhythm pushes toward aggression rather than retreat. **Firing itself stays free**, which is where we deliberately diverge from Pathogenic. Treat this as an unvalidated bet requiring a controlled playtest, not an adoption.

### 2.3 Meta progression via achievements, not currency

"Permanent upgrades are rather light, with the limited options available earned through achievements rather than grinding for currency."

**Why this is better than the Gungeon/Hades model for a small game:** currency meta-progression means early runs are *supposed* to fail, which is a bad first impression and a bad review. Achievement unlocks mean the game is fully playable at hour one and unlocks are *lateral* (new characters, new starting configurations) rather than *vertical* (more damage).

**The reviewer's counterpoint is important:** the same review calls the game "rather light on the traditional formula," suggesting the thin meta layer costs long-tail retention.

**Our decision:** a hybrid. Achievement-driven **character and mode unlocks** (lateral, immediate) plus a light **content-unlock currency** that adds items to the drop pool (breadth, not power). No permanent damage upgrades. See [08 §6](08-economy-and-meta.md).

### 2.4 Soft-body physics as a feel multiplier

Pathogenic runs a dedicated soft-body simulation — "every movement, collision, and bullet-hell projectile feels squishy, responsive, and deeply satisfying." Reviews consistently lead with feel, not systems.

**The lesson is not "use soft bodies."** It's that a *single distinctive physical signature* applied to everything — enemies, projectiles, terrain — does more for identity than any amount of content. It's what makes a screenshot recognizable.

**Our decision:** we cannot afford a soft-body engine, and it's wrong for the theme anyway. Our equivalent signature is **the geometry lie**: screen-space distortion, non-euclidean room stitching, and sanity-driven visual corruption. Cheap in Godot (shaders + viewport tricks), thematically perfect, and equally screenshot-legible. See [10](10-art-audio-ux.md).

### 2.5 Breaking your own formula

"The intestine level once again totally does away with the 'room-to-room' formula and allows you to swim directly to the final boss if you please."

A structural pattern break late in the run is disproportionately memorable. It costs one bespoke level's worth of work and buys the whole game a sense of escalation.

**Our decision:** Floor 5, **the Plateau of Leng**, abandons the room graph entirely — an open, sightless dreamscape you navigate by landmark. See [07](07-floors-and-world.md).

### 2.6 Region-gated minibosses

Minibosses guard each body region, acting as progression gates rather than optional content. Cheap pacing structure — we adopt it as **Wardens**, which also serve as our key-source guarantee.

---

## Part 3 — Synthesis Table

| System | Enter the Gungeon | Pathogenic | Cultist of Cthulhu |
|---|---|---|---|
| **Dodge** | Free i-frame roll, ~0.6s cd | Stamina-costed | Sanity-costed **Blink Step**, refunded on kill |
| **Reload** | Free, manual | Costs the same stamina as dodge | Costs Sanity — **Recitation** |
| **Panic button** | Blanks (finite items) | — | **Banish** — huge Sanity cost, always available |
| **Build vessel** | Invisible list of passives | Hardpoints on your body | **Sigil Circle** — spatial grid, adjacency + ley lines |
| **Build permanence** | Permanent, unmanageable | Freely re-arrangeable | Freely re-arrangeable in **Reverie** (pause-menu edit) |
| **Weapon count** | 200+, thin | Few, modular | **~40 deep**, 3 Inscription slots each |
| **Weapon upgrades** | None | Implicit via grafting | **Inscriptions**, bought at shop benches — *player-directed* |
| **Risk stat** | Curse (passive, accidental) | — | **Corruption** (active, farmable, content-gated) |
| **Second bar** | — | Stamina | **Sanity** (tactical) + **Corruption** (strategic) |
| **Death state** | Die | Die | Sanity 0 → **Ascension**, not death |
| **Floor gen** | Authored flows + composite layout | Handcrafted + procedural mix | Authored flows + composite layout (Gungeon model) |
| **Formula break** | Bullet Hell secret floor | Intestine open level | **Plateau of Leng** (open) + **R'lyeh** (non-euclidean) |
| **Meta progression** | Currency grind (heavy) | Achievements (light) | Achievements (lateral) + content-unlock ledger |
| **Mastery reward** | Master Rounds | — | **Unbroken Seals** (adopted) |
| **Signature feel** | Chunky recoil, table flips | Soft-body squish | Geometry distortion, sanity corruption |

---

## Part 4 — The Design Thesis

> Gungeon is a game about **managing a rotating hand of weapons**. Pathogenic is a game about **assembling a body**. Cultist of Cthulhu is a game about **spending your mind**.

Every core system must answer to that sentence. Sanity is the currency of the moment-to-moment. Corruption is the currency of the run. The Sigil Circle is where you spend what you bought. If a proposed feature does not feed one of those three, it is cut.

---

## Sources

- [Dungeon Generation in Enter The Gungeon — BorisTheBrave.Com](https://www.boristhebrave.com/2019/07/28/dungeon-generation-in-enter-the-gungeon/)
- [Studying Dungeon Generation in Enter The Gungeon — 80.lv](https://80.lv/articles/studying-dungeon-generation-in-enter-the-gungeon)
- [Pathogenic on Steam](https://store.steampowered.com/app/3808690/Pathogenic/)
- [Pathogenic Review — Biological Warfare — Try Hard Guides](https://tryhardguides.com/pathogenic-review/)
- [Pathogenic is a lovely top-down shooter that splices Spore, Plague Inc. and Innerspace — Rogueliker](https://rogueliker.com/pathogenic-pc-shooter/)
- [Pathogenic: An evolutionary bullet-hell — Adventure Gamers](https://adventuregamers.com/article/pathogenic-explained)
- [Enter the Gungeon — Wikipedia](https://en.wikipedia.org/wiki/Enter_the_Gungeon)
