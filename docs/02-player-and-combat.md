# 02 — Player & Combat

---

## 1. The Player Character

### 1.1 Physical spec

| Property | Value | Notes |
|---|---|---|
| Sprite footprint | 20 × 32 px | High-res pixel art, 1 unit = 16px |
| **Hitbox** | 6 px radius circle, at the sternum | Deliberately far smaller than the sprite — bullet hell convention |
| Hitbox visibility | Always faintly visible; **fully lit while Blink Stepping** | Non-negotiable readability rule |
| Base move speed | 5.6 units/s (≈90 px/s) | |
| Move speed while firing | ×0.82 | Small penalty; keeps kiting viable but not free |
| Acceleration | 0 → max in 0.06s | Near-instant. Bullet hell demands 1:1 input. |
| Deceleration | max → 0 in 0.05s | No ice. Ever. |
| Collision layer | `Player` (1) | Separate from `PlayerHurtbox` (2) |

**Rule:** the movement controller is *not* physics-driven. No `RigidBody2D`. `CharacterBody2D` with directly assigned velocity in `_PhysicsProcess`. Any "weight" the character has is animation, not simulation.

### 1.2 Aiming

- **Mouse:** cursor is the aim point. Weapon rotates to face it, 1:1, no lerp.
- **Gamepad:** right stick sets aim direction. Deadzone 0.22. Optional aim assist: 4° magnetism cone toward nearest enemy, off by default, exposed in options.
- Sprite flips on the X axis based on aim direction, with an 8-directional torso overlay.

---

## 2. Health

- **Hearts.** Start with 3 containers (character-dependent, 2–5). Half-heart granularity.
- One enemy contact or one bullet = **half a heart** on floors 1–2, **one heart** on floors 3+. Bosses hit for one full heart from phase 2.
- **Armour** (temporary): absorbs one hit of any size, consumed entirely. Stacks visually to the left of hearts. Cannot be regenerated in-combat.
- **Invulnerability after damage:** 1.0s, with the standard 12Hz flash.
- **Healing is scarce.** Hearts do not drop from normal enemies. Sources: shop (expensive), reward rooms (~15%), Unbroken Seals (+1 container), a small number of sigils, and Warden kills.
- **Death is death.** No revives except a single rare sigil (*The Silver Key*, consumed on use, spawns you in the previous room at half a heart).

---

## 3. SANITY — The Core Resource

> Sanity is stamina, magazine, panic button, and difficulty modifier in a single bar. It is the game's central mechanic.

### 3.1 The bar

- Displayed as a **ring of guttering candles** around the health hearts — a segmented gauge, 100 points, 5 visible segments of 20.
- Default max: **100**. Modified by character, sigils, and permanent floor events.
- The bar visually *inverts* below 25 — candles snuff, the ring becomes an eye.

### 3.2 Costs

| Action | Sanity cost | Notes |
|---|---|---|
| **Blink Step** (dodge) | **0 — FREE** | Limited by cooldown and the vulnerable recovery tail, not by price. See §4 and the note below. |
| **Recitation** (reload) | **12 × weapon reload weight** (0.5–2.0) | **The primary sink.** A heavy weapon is expensive to keep firing. |
| **Banish** (screen clear) | **45** | Always available if you can pay; see §5 |
| **Open the Eye** (deliberate descent) | **25+** | See §3.5.1 |
| Firing | 0 | Shooting is free. Only *sustaining* fire costs. **Exception: Grimoires ([03](03-weapons-and-inscriptions.md) Family IV) and any weapon carrying *Vessel Rune* fire directly from Sanity.** |
| Taking a hit | **10** | Damage compounds — you get hit, then you can't reload |
| Witnessing a Revelation | 15–30 | Room-entry events, boss phase transitions, opening blasphemous chests |
| Reading a Tome | 25 | Voluntary; grants Corruption + a sigil |

> **[DECISION — 26 Jul 2026] Blink Step is free. This is fallback F4** ([11 §M1](11-roadmap.md)), taken early rather than after a failed playtest, on the evidence in [00 §2.2](00-comparative-analysis.md).
>
> **The consequence this document must own: the Sanity drain collapses.** Dodge at 18 was the dominant sink. Forward-modelling the same Floor 1 room used in §3.3's review note (6 fodder + 1 turret ⇒ ~62 Sanity of income, +20 room clear):
>
> | | Metered dodge (old) | Free dodge (F4) |
> |---|---|---|
> | Typical room spend | ~162 (5 dodges + 3 reloads) | **~40–90** (3–5 reloads, 0–2 hits, 0–1 Banish) |
> | Typical room income | ~82 | ~82 |
> | Net | strongly negative | **roughly break-even** |
>
> Left alone, Sanity would sit near the ceiling, the ladder would never fire, and **Pillar III would become decoration** — the exact failure the low-Sanity system exists to avoid.
>
> **The fix is structural, not a number tweak: the descent moves from the player's actions to the world.** The Lucid Ceiling (§3.3.1) is now the *primary* driver, steepened to −7 per room with a floor of 45. Late-floor rooms therefore begin in **Unsettled** and end in **Fraying** whatever the player does.
>
> This is thematically stronger than what it replaces. The game is about complicity and inevitability; a descent you cannot opt out of by playing well says that better than a stamina bar did. **What it costs:** the dodge no longer differentiates us from *Enter the Gungeon*. We differentiate on the Sigil Circle, on Corruption, and on the descent. Narrower, and honest.
>
> **Reload becomes the interesting decision.** With dodge free, reload weight is the main Sanity lever, which promotes weapon choice from a damage decision to a *resource* decision — a Nitro Express at weight 2.0 costs 24 Sanity a magazine and materially changes how much of the ladder you see. That was always the intent of reload weight ([03 §1.3](03-weapons-and-inscriptions.md)); it now carries the system alone.

### 3.3 Gains

| Source | Sanity gained | Notes |
|---|---|---|
| **Enemy kill** | 6 base, scaled by threat tier (4 / 8 / 14 / 25) | **The primary source.** Aggression is funded. |
| Kill within 1.5s of another (chain) | +2 per chain step, caps at +10 | Rewards momentum |
| Kill during Blink Step i-frames | **×2** | The high-skill line: dodge *through* the enemy and kill it |
| Out-of-combat regen | 8/s after 2.5s with no enemies alive, **up to the Lucid Ceiling** (§3.3.1) | Refills between rooms, but not to full for long |
| In-combat regen | **0** | Deliberate. There is no waiting it out. |
| Sanity pickups (candles) | 25 | Drop from elites and Wardens; buyable. **May exceed the Lucid Ceiling** — the counter-play to the descent |
| Clearing a room | +20 flat | Guaranteed floor on room-to-room fatigue |

**Design intent:** in a well-played fight, Sanity oscillates in the 30–70 band. A player who over-dodges bottoms out. A player who never dodges takes damage, which *also* costs Sanity. The bar punishes both extremes.

> **[REVIEW — Fable, 26 Jul 2026] The bar is a per-room allowance, not a run-long resource, and this is currently unintentional.**
> Out-of-combat regen at 8/s refills 0→100 in **12.5 seconds** of walking. Every room therefore starts at or near full, and any deficit is laundered by the corridor. Forward-modelling a typical Floor 1 room (6 fodder + 1 turret ⇒ **62 Sanity of income** including chain and room-clear) against expenditure:
>
> | Spend | Net for the room | End state (start 100) |
> |---|---|---|
> | 5 dodges + 3 pistol reloads (108) | −46 | 54 |
> | 8 dodges + 3 pistol reloads (162) | −100 | 0 → Ascension |
> | 5 dodges + 3 elephant-gun reloads (162) | −100 | 0 → Ascension |
>
> So the real budget is **~162 Sanity per room ≈ 9 dodges**, and it resets every room. Two consequences the design should own explicitly:
> 1. **There is no descent arc.** The theme is "spending yourself into the dark across a run"; the mechanic returns you to Lucid every ~12 seconds. Sanity never trends downward across a floor.
> 2. **The low-Sanity ladder is backloaded.** You only reach Fraying/Unravelled *late in a fight*, when most enemies are already dead — so the damage bonus arrives when it is least needed, and the hallucination penalty applies to the fewest bullets. Both halves of the ladder are weakest exactly where they fire.
>
> **Decision needed (do not leave implicit):** either (a) accept Sanity as an explicitly per-room resource and stop describing it as a descent, or (b) make some portion of Sanity loss persist across rooms — e.g. out-of-combat regen refills only to a **per-floor ceiling** that itself drops as the floor progresses. Option (b) restores the descent arc and makes the ladder reachable while it still matters. Recommend (b), tested at M1.

> **[DECISION — Opus, 26 Jul 2026] Adopting (b). Specified below as the Lucid Ceiling.** The review is right that the corridor was laundering the entire mechanic, and right that a theme built on "spending yourself into the dark across a run" cannot have a bar that resets to full every twelve seconds. (a) was the cheaper option and it is the one I reject: it would leave the game honest but thematically inert, and it would leave the ladder permanently backloaded.

### 3.3.1 The Lucid Ceiling *(new — resolves the review above)*

Out-of-combat regeneration refills Sanity **only up to a ceiling that falls as the floor progresses.**

| Property | Value |
|---|---|
| Ceiling on floor entry | **100** (or the character's max) |
| Decay | **−7 per room cleared** on this floor *(steepened from −5 under F4)* |
| Ceiling floor | **45** — never drops below *(lowered from 50 under F4)* |
| Reset to 100 | On entering a new floor, and in the **boss foyer** |
| Can be exceeded by | Sanity candles, shop purchases, Unbroken Seals, certain sigils |

So a 14-room floor runs 100 → 45 over its first eight rooms, and the back half of every floor is played at or near the **Unsettled** band with a genuine likelihood of ending fights in **Fraying**.

> **[DECISION — 26 Jul 2026] Under F4 this section is promoted from a supporting mechanic to the load-bearing one.** With Blink Step free, player spending can no longer carry anyone down the ladder — the ceiling is now the only reliable source of descent, which is why it was steepened. If M1 telemetry shows time-below-40-Sanity under the 25% target ([11](11-roadmap.md) metric 1), **steepen the decay before touching any other number** — this is the lever, and reload costs are the second lever, not the first.

Three properties this buys:

1. **The descent is real.** Sanity trends down across a floor. The theme is now the mechanic.
2. **The ladder fires when it matters.** You enter late-floor rooms already low, so hallucinations apply to a full room of bullets rather than the last two enemies, and the low-band information payoff (§3.4) is available during the fight rather than after it.
3. **Candles become a strategic purchase, not a top-up.** Because they pierce the ceiling, buying one on floor 4 is a real decision about how the back half of the floor will play.

**Interaction with Ascension:** the ceiling does *not* reduce max Sanity, so Ascension's −10 max penalty (§6) stacks on top of it. A player who has Ascended twice on floor 5 is operating from a ceiling of 50 against a max of 80. That compounding is intended and is the run's late-game pressure.

**Tuning note:** −5/room and a floor of 50 are first-pass. The M1 telemetry metric that governs them is *time-in-band* ([11](11-roadmap.md), metric 1) — if testers spend under 25% of combat below 40 Sanity, steepen the decay before touching any other number.

### 3.4 Low-Sanity states — the ladder

This is where Pillar III lives. Effects are **cumulative** as you descend.

| Threshold | State | Mechanical effects |
|---|---|---|
| 100–61 | **Lucid** | Baseline. |
| 60–41 | **Unsettled** | Faint whispering audio layer. **Hidden wall cracks shimmer.** Enemy health bars become visible. |
| 40–21 | **Fraying** | **Enemy weak points become visible** (glowing; **+50% damage on hit** — aim-gated, not automatic). Screen edges begin chromatic separation. First **hallucinated projectiles** appear — visually identical to real bullets but pass through you harmlessly. ~1 in 8 bullets on screen. |
| 20–1 | **Unravelled** | Hallucination ratio 1 in 4. **Secret rooms outlined on the minimap.** **Movement +10%.** Enemy attack telegraphs extend by 3 frames — *you read the room faster than it moves*. Audio pitches down. Some enemies show a second, true form. |
| 0 | **ASCENSION** | See §6. |

> **[DECISION — Opus, 26 Jul 2026] Adopted §3.5 option C: the ladder no longer grants a flat damage multiplier at any band.** The ×1.08 / ×1.18 / ×1.30 figures are gone. Every band payoff is now **information, mobility, or perception** — things that reward a player who can *use* them rather than paying out automatically for having been hit.
>
> This is the change that removes the reward-for-failure inversion at its root rather than capping it. The review's §3.5 table showed a player who takes hits being paid +30% damage while a player who never gets hit is paid nothing; with no flat multiplier anywhere on the ladder, that inversion cannot occur at all, and the guard rail invented in §3.4's earlier note ("at most one multiplicative damage source") becomes unnecessary for the ladder itself.
>
> **Damage from low Sanity is now opt-in, via build.** *Derringer of Last Rites* (×3 below Sanity 20) and *The Shining Trapezohedron* (instant charge below 20) are unchanged, and are now the **only** way the low band pays damage. That is strictly better design: descending becomes a *build axis a player commits to* rather than a bonus the game hands them for bleeding. A player who wants the low band to hurt must spend a weapon slot on it.
>
> **Revised rule, replacing the earlier one:** *the ladder grants no damage. Weapons and sigils may key off low Sanity, and at most one such multiplicative source may apply at a time.*
>
> **Watch at M1:** metric 6 (does anyone descend deliberately?). If C makes the low band feel unrewarding rather than merely non-paying, the fix is to strengthen the *information* payoff — more telegraph extension, more revealed enemy state — before reintroducing any damage.

> **[REVIEW — Fable] Two numbers changed above; both were relationship errors, not tuning.** *(Point (1) is **superseded** by the decision above — the band damage figures no longer exist. Point (2), the weak-point reduction to +50%, **stands and is retained in the table**; it is now the ladder's only damage-adjacent effect, which is precisely why it must stay bounded. The note is kept because it records why the multiplicative stack was dangerous — that reasoning is what makes the "at most one source" rule binding on future weapon and sigil design.)*
> **(1) The damage figures are now ABSOLUTE per band, not cumulative.** "Cumulative" + "+8/+18/+30%" is ambiguous and reads as summing to ×1.56. The band value is now the total multiplier at that band (×1.30 at Unravelled). *Cumulative* still applies to the non-damage effects (weak points, hallucinations, minimap, move speed persist as you descend).
> **(2) Weak points reduced from ×2 to +50%.** At ×2 the Unravelled stack was ×1.30 × 2 = **×2.6 base**, and the design already contains weapons that multiply the same axis again — *Derringer of Last Rites* (×3 below Sanity 20) and *The Shining Trapezohedron* (instant charge below 20). That produced a **×7.8 damage window** at the bottom of the bar. Three multiplicative bonuses keyed to one condition is the mechanism behind §5.1; capping the stack is the cheapest fix that keeps the ladder intact.
> **Rule going forward: the low-Sanity band may carry at most ONE multiplicative damage source.** Everything else it grants must be information (weak points visible, secrets on map), mobility, or utility.

**Critical balance rule:** hallucinated bullets must be *indistinguishable* from real ones by appearance, but there is a tell — **they cast no contact shadow.** ~~they cast no light and produce no audio on spawn~~ **The audio tell has been removed as unimplementable, and the light tell replaced — see the two notes below.**

> **[REVIEW — Fable] The audio tell cannot work, and this is provable from our own audio spec.**
> [05 §1 R8](05-enemies-and-bosses.md) voice-limits each shot type to **6 concurrent**, and [10 §2.2](10-art-audio-ux.md) merges spawns inside a 0.02s window so that *"a 40-bullet radial is one sound, not forty."* Under both rules, **real bullets are routinely silent too** — silence carries no information. Worse, when a volley collapses to one merged sound there is no per-bullet audio to attribute at all. §5.5's worry is confirmed with a mechanism, not a hunch.
>
> **Replacement tell (single, visual, reliable): hallucinated bullets cast no light.** This is already specified, is unambiguous on dark floors, and survives density. It must therefore be made *load-bearing*: every bullet type gets a small `Light2D`-equivalent contribution (cheap — additive blob in the bullet shader, not a real light), and floors must retain enough contrast for its absence to read. **Floor 4 (whiteout blue) and Floor 5 (ambient, sourceless) currently defeat this** — either give hallucinations a second visual channel on bright floors (recommend: no impact-anticipation shadow beneath the bullet) or suppress hallucinations on those floors.
> This also resolves the controller-rumble asymmetry in §10.3: with the audio tell gone, the light tell is available to KBM and pad equally, and rumble may stay on for real bullets only.

> **[DECISION — Opus, 26 Jul 2026] Taking the shadow, not the light — as the *only* tell, on every floor.**
> The review is right that light fails on Floors 4 and 5, but its proposed fix (light on dark floors, shadow on bright ones) means **the tell changes identity depending on where you are standing.** A perceptual cue the player must re-learn per floor is worse than a weaker cue that is always the same thing — and hallucinations are exactly the mechanic that cannot afford an ambiguous rule.
>
> **Specification: every real bullet renders a small, soft, offset drop-shadow on the floor plane. Hallucinated bullets render none.**
> - **Works on every floor**, because it depends on the floor being *drawn*, not on the floor being *dark*. Whiteout blue and sourceless ambient both take a shadow fine.
> - **Cheap.** A second quad per bullet in the same MultiMesh, offset by a fixed vector, drawn beneath the bullet layer — one extra draw call for the entire bullet field ([09 §3.3](09-technical-architecture.md)).
> - **Survives density**, unlike audio: shadows do not voice-limit or merge.
> - **Reads at the edge of vision**, which is where bullets are read in practice.
> - It is also just good for the game — grounded projectiles help every floor's readability whether or not the player is hallucinating.
>
> **Consequences to carry:** the light contribution stays for *mood and muzzle flashes* but is no longer load-bearing for hallucinations; the shadow becomes a hard requirement of the bullet shader in M0, not a polish item in M6. **Add to the accessibility set** ([10 §7](10-art-audio-ux.md)): a *Hallucination Contrast* toggle that renders the shadow at full opacity with a hard edge, for players who cannot read a soft one.
> Rumble stays on for real bullets only, per the review.

### 3.5 The low-Sanity inversion — analysis and options *(open decision)*

> **[REVIEW — Fable] The §5.1 worry is real, but it is mis-diagnosed. The problem is not that players will camp the low band; it is that the band is not a choice at all.**

**Why camping is not the failure mode.** Three mechanisms already fight it, and they were not credited in the brief:
- **Kills push you *up*.** The main thing a player does (killing) refunds Sanity, ejecting them from the low band. Being good at the game removes the bonus.
- **The corridor resets you.** Out-of-combat regen returns you to Lucid before the next room (§3.3 note).
- **Low Sanity means no dodges.** At 20 Sanity you hold exactly one Blink Step. That is a real, permanent cost that no amount of skill removes.

**What is actually broken.** The player has almost no *deliberate* control over which band they occupy. Sanity is driven by kills (up) and by dodges and hits (down) — all things the fight dictates. So the ladder is not a risk/reward axis the player steers; **it is a readout of how the fight is going, with a damage bonus stapled to it.** And the sign of that bonus is backwards:

| Player | Sanity trend | Ladder effect |
|---|---|---|
| Takes hits, over-dodges (playing badly) | Descends | **+30% damage, weak points, speed** |
| Never hit, positions instead of dodging (playing well) | Stays high | **No bonus at all** |

Taking a hit costs 10 Sanity, which *moves you toward the reward*. The ladder is therefore an **involuntary rubber-band that pays out for failure** — a comeback mechanic, not a strategic choice. That is a defensible design, but it is not the design the docs describe, and it is in direct tension with rewarding mastery ([01 §6](01-pillars-and-loop.md)) and with "Ascension must never be optimal" (§6).

**This is made worse by [05 §6](05-enemies-and-bosses.md)'s mandate** that every boss pattern have a no-dodge positioning solution and that *"experts should be able to clear bosses at near-full Sanity."* If both hold, the expert line is: never spend, never descend, never touch Pillar III — while doing the *least* damage in the game. See the review note there.

**Options.** Ordered cheapest-first; these are not mutually exclusive.

| # | Option | Effect | Cost | Risk |
|---|---|---|---|---|
| **A** | **Give descent a deliberate verb.** Add a voluntary action ("Open the Eye") that spends a fixed Sanity chunk to drop a band on purpose, with a short lockout before you can climb back. | Converts the ladder from a readout into a *choice*. Player authors the risk. | Small — one input, one timer | Adds a button to an already busy scheme |
| **B** | **Band hysteresis.** Once entered, a band persists until Sanity crosses ~15 points *past* the boundary, and kills refund at 50% while below 40. | Stops the oscillation; lets a player *hold* a band they chose. | Trivial — two constants | Slightly muddies the bar reading |
| **C** | **Move the payout off damage.** Low bands grant information and mobility (weak points, secrets, +move) but **no damage multiplier**; damage stays flat across the bar. | Removes the reward-for-failure inversion entirely. | Trivial | Low band may feel unrewarding; Pillar III gets quieter |
| **D** | **Per-floor Sanity ceiling** (from §3.3): the corridor refills only to a ceiling that falls as the floor progresses. | Restores the descent arc; makes bands reachable while they still matter. | Moderate | Can feel like an unearned difficulty ramp |
| **E** | *(Brief's option)* **Invert the ladder** — power at high Sanity, low Sanity purely desperate. | Safest possible economy. | Large rewrite | Thematically inert. Pillar III becomes decoration. **Not recommended.** |

**Recommendation: A + B + C together, and test D at M1.**
A and B give the player authorship of the band (fixing the actual defect). C removes the perverse incentive without touching the theme — the low band still *transforms the game* (you see weak points, you see secrets, you move faster, the world distorts), it just stops paying you for getting hit. Keeping the ×1.30 damage is what forces every other guard rail; dropping it is the single cheapest way to make the whole ladder safe to tune. **Do not take E** — it trades the game's identity for a balance problem that A–C already solve.

---

> **[DECISION — Opus, 26 Jul 2026] Adopting A + B + C + D.** C is applied in §3.4 above; D is applied as the Lucid Ceiling in §3.3.1. A and B are specified here. The review's diagnosis — that the band was a *readout* rather than a *choice* — is the correct one and is more damaging than the camping worry it replaced. All four together cost roughly one input, two constants and one per-floor counter.

### 3.5.1 Open the Eye *(option A — the deliberate descent verb)*

**Input: hold Banish (RMB / LB) for 0.4s.** No new button — Banish is already the "spend Sanity dramatically" action, and hold-versus-tap is a clean disambiguation the player learns once.

| Property | Value |
|---|---|
| Cost | **25 Sanity**, or enough to cross the next band boundary — whichever is greater |
| Effect | Drops you immediately into the next band down |
| Below 20 Sanity | Unavailable. **You cannot Open the Eye into Ascension** — that closes the deliberate-Ascension loop from a third direction |
| Cooldown | 8s |
| Corruption | **+0.25**, same as Banish. You are doing the same kind of thing. |

This is the whole fix in one verb: the player who wants weak points visible for a boss phase, or secret rooms on the minimap before sweeping a floor, can now *buy* that state instead of waiting for the fight to inflict it. The band becomes a resource you spend into, which is what the design claimed it was all along.

### 3.5.2 Band hysteresis *(option B)*

- Once entered, a band persists until Sanity crosses **8 points past** its upper boundary. Entering Fraying at 40 means you do not return to Unsettled until 48.
- **Kill refunds are halved while below 40 Sanity.** Descending is therefore *sticky* — a player who chose the low band is not immediately ejected from it by playing well.

Together these mean a player can *hold* a chosen band through a fight rather than oscillating across boundaries every few kills. Without hysteresis, option A would be nearly pointless: you would Open the Eye and three kills later be back where you started, 25 Sanity poorer.

**Cost of the whole package:** one hold-input, two constants, one per-floor counter, and the deletion of three damage multipliers. This is the cheapest set of changes in the review and it addresses the single largest structural defect found in it.

---

## 4. Blink Step (the dodge)

The single most-pressed button. Its spec is the spec of the game.

| Property | Value |
|---|---|
| Sanity cost | **0 — free** (fallback F4; see §3.2) |
| **Startup** | 2 frames (0.033s) |
| **Invulnerable window** | frames 3–16 (0.233s) |
| **Recovery** | frames 17–24 (0.133s) — vulnerable, movement locked to 40% |
| Total duration | 0.40s |
| Distance | 3.2 units (~51px) |
| Cooldown | 0.12s after recovery (prevents input-buffer chaining) |
| Direction | Movement input at press; if none, aim direction |
| Cancel | Recovery cancellable into another Blink Step (double-cost) or into firing at frame 20 |

**Feel requirements:**
- 3-frame ghost trail in the character's sigil colour.
- A brief (0.05s) **hit-stop-free** time dilation to 0.85× *only if a bullet was actually avoided* — the game rewards you for the near-miss you earned, not for spamming.
- The hitbox becomes fully opaque during i-frames. The player must always be able to see exactly what is invulnerable.
- Passing through an enemy during i-frames is legal and applies a 0.3s "Marked" debuff (+25% damage taken).

**Why the recovery tail matters — and it now matters far more.** With the dodge free, the 8-frame vulnerable tail plus the 0.12s cooldown are the *only* thing standing between the player and dodge-spam. The full cycle is **24 frames + 0.12s ≈ 0.52s**, close to Gungeon's ~0.6s, and it must be protected: any sigil or inscription that shortens the recovery tail is now a balance risk of the first order, where previously the Sanity price provided a second brake.

**The skill expression that survives F4 unchanged:**
- Late dodges into a second volley are punished by the tail. Timing is still the whole game.
- **Kill during i-frames → ×2 Sanity** (§3.3). Dodging *through* an enemy to kill it is still the high-skill line, and it is now the main way an aggressive player funds their reloads.
- Passing through an enemy still applies **Marked** (+25% damage taken for 0.3s).

So the dodge is free, but dodging *well* still pays — it simply pays in reload uptime rather than in more dodges.

---

## 5. Recitation (reload) & Banish (panic)

### 5.1 Recitation
- Manual reload, bound to R / X. Automatic when the magazine empties (with a 0.25s delay to allow a manual pre-empt).
- Costs Sanity proportional to weapon **reload weight** — a pistol is 6, an elephant gun is 24.
- Duration 0.5–1.6s by weapon. **Movement unimpeded**, firing locked.
- **Perfect Recitation:** a shrinking ring appears; hitting the button inside the 0.16s window refunds **half** the Sanity cost and grants +15% damage for the next magazine. This is the skill expression layer on the reload, and it is deliberately generous to learn and hard to master under pressure.

### 5.2 Banish
The blank equivalent, but **not an item** — an always-available action gated purely on Sanity.

- Costs 45 Sanity. Cannot be used below 45.
- Destroys all enemy projectiles in a 9-unit radius, pushes enemies back 2 units, and applies a 0.6s stun.
- 1.2s internal cooldown.
- **Secondary use:** Banishing next to a cracked wall breaks it open, revealing secret rooms (direct lift of Gungeon's blank-a-wall mechanic — it is a genuinely great secret-discovery verb). **Out of combat, the wall-break use costs 15 Sanity instead of 45 and grants no Corruption** — see the review note.

> **[REVIEW — Fable] Fixed a hard conflict: secrets were visible only below 20 Sanity but openable only above 45.**
> §3.4 outlines secret rooms on the minimap at **Sanity ≤ 20** (and [06 §6.4](06-procedural-generation.md) calls this *"a major reason to run low"*), while Banish — the only way to open them — **cannot be used below 45**. The player could see the secret and was mechanically barred from opening it, then had to leave, regenerate, and walk back. The reduced out-of-combat wall-break cost above resolves it. It also stops secret-hunting from being an involuntary Corruption tax, which was punishing exploration for no design reason.
- Banishing has a *cost beyond Sanity*: each Banish adds **+0.25 Corruption**. You are, after all, unmaking part of reality.

---

## 6. ASCENSION — Sanity Zero

**Sanity reaching zero does not kill you.** This is the game's signature moment.

**On hitting 0:**
1. 0.8s of full-screen white-out, audio cuts to a single sustained tone.
2. The player transforms — character-specific monstrous form, silhouette clearly altered.
3. **Ascended state, 20 seconds:**
   - Invulnerable to damage.
   - Weapons replaced by a form-specific attack (tentacle sweep / spore burst / gaze beam) with infinite ammo.
   - Move speed ×1.35.
   - Enemies flee or become erratic.
4. **On exit:** Sanity resets to 50, you take **1 full heart of damage** (unavoidable, cannot kill you — floors at half a heart), and you gain **+1 Corruption permanently for the run**.
5. **Escalating cost:** each Ascension in a run reduces max Sanity by 10 (floor: 40) and increases the exit heart cost by half a heart.
6. **Debt rule (added — see review note):** if the exit heart cost cannot be paid in full because it would reduce you below half a heart, the **unpaid remainder is taken as permanent max-heart reduction for the run instead** (minimum 1 container). Ascension is never free.
7. **Diminishing duration (added):** the Ascended window is **20s for the first Ascension of a run, then 14 / 10 / 7 / 5s**, floored at 5s. Repetition is allowed; farming is not.

> **[REVIEW — Fable] Ascension was farmable to infinity with no sigil involved. This is the most serious economy break I found, and §5.2's suspicion that Ballast is "a symptom" is correct.**
> Two clauses combined to zero out the cost:
> - *"cannot kill you — floors at half a heart"* means that **at ≤1 heart the heart cost is absorbed by the floor** and Ascension costs no health at all.
> - *max Sanity floors at 40* means the max-Sanity penalty **stops escalating** after six Ascensions.
>
> At that point the loop is: drain 40 Sanity (**2.2 Blink Steps**) → **20 seconds of invulnerability, infinite ammo, ×1.35 speed** → repeat forever. Low health became *safer* than high health, which inverts the entire damage model. The debt rule and the diminishing duration above close it: the cost can always be paid (out of max hearts if not current hearts), and the payout shrinks toward a genuine emergency button rather than a rotation.
> With these in place, **[04 §5.2](04-sigil-circle.md)'s *Dreamer's Ballast* is still too strong and has been changed there** — but it is no longer load-bearing, because the base state is now self-limiting.

**Why this design:**
- It converts the "I'm about to die" moment into a *power fantasy with a bill attached* — the emotional peak of every run.
- It removes the frustration of a resource-drain death while making resource management still matter, because the cost is permanent and compounding.
- Thematically it is the entire premise of the game, expressed mechanically.

**Balance guard:** Ascension must never be the optimal strategy. Deliberately draining to zero should be a *desperation* line, not a rotation. If playtesting shows players farming Ascensions, increase the max-Sanity penalty first, not the duration.

---

## 7. CORRUPTION — The Run-Long Risk Stat

Where Sanity is measured in seconds, Corruption is measured in runs. It only goes up.

### 7.1 Acquisition (all voluntary or clearly telegraphed)

| Source | Corruption |
|---|---|
| Reading a Tome (item room) | +1, grants a guaranteed sigil |
| Opening a **Blasphemous Chest** | +1, free (no key required), always B-tier or better |
| Drinking from a **Black Font** shrine | +1 to +3, random large boon |
| Each Banish | +0.25 |
| Each Ascension | +1 |
| Accepting a Warden's bargain | +2, skips the fight and grants its reward |
| Certain powerful sigils | +1 on equip (refunded if removed) |

### 7.2 Thresholds

| Corruption | Effect |
|---|---|
| **1+** | Loot quality roll +1 tier at 20% chance. **Corrupted Doors** become interactable (locked black doors leading to high-value side rooms). |
| **3+** | Enemies become **Awakened**: +15% HP, one additional attack pattern each. Loot tier bump chance → 45%. Shops stock one extra Inscription. |
| **5+** | **The Hound of Tindalos** begins hunting. It enters rooms through *corners*, phases through walls, moves at 0.7× player speed, and deals 1 full heart. It can be killed for a large reward but respawns 90s later. Loot tier bump → 70%. |
| **7+** | All rooms spawn +1 enemy. Elites can appear in normal rooms. Boss gains an extra phase. |
| **10 (max)** | **The Yellow Sign** — the floor's colour palette shifts to sickly gold, all enemies gain the Awakened tier, and the run's boss is replaced by its **Sovereign** variant (much harder, drops a guaranteed S-tier sigil and a Yellow Fragment ×3). |

### 7.3 Reduction
Corruption is *almost* one-way, on purpose. Only two sinks:
- **Cleansing Pool** (rare shrine): −2 Corruption, but destroys one random equipped sigil.
- A Warden defeated without taking damage: −1.

**Design intent:** Corruption is the game's real difficulty selector. It's opt-in, incremental, and rewards system mastery. Unlike Gungeon's Curse, there is a legitimate **corruption build** — sigils that scale off Corruption exist ([04](04-sigil-circle.md)), and the Corruption-10 Sovereign bosses are the main source of top-tier loot.

---

## 8. Damage, Knockback & Game Feel Budget

| Element | Spec |
|---|---|
| **Hit stop** (player hits enemy) | 40ms at 0.05× time scale, scaled by damage tier |
| **Hit stop** (player takes damage) | 90ms + 0.25s slow-mo ramp back |
| Screen shake | Trauma-based (`trauma²` decay). Cap 6px. Fully disableable. |
| Damage numbers | **Off by default.** Optional. Bullet hell readability beats feedback text. |
| Enemy hit flash | 2 frames pure white, then 4 frames additive tint |
| Enemy death | Gib burst + a 0.15s freeze at the death frame + Sanity mote flies to the player |
| Muzzle flash | Light2D pulse, 0.06s, colour = weapon element |
| Bullet impact | Decal on tilemap, pooled, 200 max, fades over 4s |
| Controller rumble | Subtle, on-hit and on-Banish only. Off by default. |

**The Sanity mote is important:** every kill spawns a small light that visibly *flies into the player's sanity ring*. This makes the "kill to fund yourself" loop viscerally legible without any UI text.

---

## 9. Input Map (default)

| Action | KBM | Gamepad |
|---|---|---|
| Move | WASD | Left stick |
| Aim | Mouse | Right stick |
| Fire | LMB | RT |
| Blink Step | Space | A / Cross |
| Recite (reload) | R | X / Square |
| Banish | RMB | LB |
| Swap weapon | Q / scroll | Y / Triangle |
| Interact | E | B / Circle |
| **Reverie** (circle edit) | Tab | Back / Share |
| Map | M | RB (hold) |
| Pause | Esc | Start |

Full remapping required at 1.0. Both schemes must be first-class — Steam Deck Verified is a launch target.

---

## 10. Open Design Questions

1. **Should Sanity cost of Blink Step scale with floor?** Leaning no — the constant is the anchor players learn. Instead scale *max Sanity* via characters/sigils.
2. **Should Ascension be usable as a deliberate boss-phase skip?** Currently yes. **[REVIEW] Now safe to allow**, given the §6 debt rule and diminishing duration — a phase skip costs real hearts and the window shrinks each time. Re-check at M1 whether players still rotate it on the Floor 4 Warden gate.
3. ~~**Hallucinated bullets and controller rumble.**~~ **[REVIEW — RESOLVED]** The audio tell is unimplementable under our own voice-limiting and spawn-merge rules (§3.4 note), so it has been removed outright. The **no-light** tell is now the single tell and is platform-neutral, which dissolves the KBM/pad asymmetry. Rumble stays on for real bullets. Remaining work: hallucinations need a second visual channel on Floors 4 and 5, where ambient lighting defeats the light tell.
4. **[REVIEW — NEW] Is Sanity a per-room allowance or a run-long descent?** See §3.3. The current numbers make it per-room and the corridor launders every deficit. This must be decided before M1, because it determines whether the low-Sanity ladder is reachable during the part of a fight that matters.
5. **[REVIEW — NEW] Does the player get a deliberate verb for descending?** See §3.5 options A/B. Without one, Pillar III is something that happens *to* the player rather than something they choose.
