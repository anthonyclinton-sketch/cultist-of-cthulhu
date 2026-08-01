# Audit — Specified vs. Implemented

**Pass 1:** 26 July 2026 · docs/02 (Player & Combat)
**Pass 2:** 29 July 2026 · docs/03 §3, docs/04, docs/05 §7, docs/08 — the M2 systems.
See [§M2](#m2-sweep--29-july-2026) at the end.

---

**Date:** 26 July 2026 · **Scope:** docs/02 (Player & Combat) against `src/`

## Why this exists

Three systems in a row — Ascension's trigger, Ascension's debt rule, and Banish — were
fully specified in the docs and either inert or broken in the code. Each was found by a
different accident: writing a test, answering a question about the ladder, listing the
controls.

That is a process failure, not three coincidences. A design doc that is treated as
authoritative but never checked against the build accumulates **phantom features**: things
everyone believes are in the game because they are written down. The danger is specific —
a playtest measures the build, not the doc, and every phantom feature silently
invalidates whatever conclusion the playtest was meant to produce.

This file is the one-time sweep. It should be re-run at the end of every milestone.

---

## Legend

| | |
|---|---|
| ✅ | Implemented and wired |
| ⚠️ | Partially implemented, or implemented but not reachable in the M1 build |
| ❌ | Specified, not implemented |
| 🔜 | Correctly deferred — depends on a system scheduled later |

---

## §1–2 Player & Health

| Spec | State | Note |
|---|---|---|
| 6px hitbox, always visible, opaque during i-frames | ✅ | |
| Move speed, accel/decel, firing penalty | ✅ | |
| Mouse + right-stick aiming, 0.22 deadzone | ✅ | |
| Aim assist — 4° magnetism cone, off by default | ❌ | Accessibility option; no consumer yet |
| Hearts, half-heart granularity | ✅ | |
| Damage scaling by floor (½ heart F1–2, 1 heart F3+) | 🔜 | Needs floors |
| **Armour** — absorbs one hit of any size | ❌ | Needs the pickup/loot system |
| 1.0s post-damage i-frames | ✅ | |
| 12Hz damage flash on the player | ❌ | Player has no flash; enemies do |

## §3 Sanity

| Spec | State | Note |
|---|---|---|
| Costs — Blink (free, F4), Recite, Banish, Open the Eye, hit | ✅ | |
| Kill refund, threat-tiered | ✅ | |
| **Chain kill bonus** — +2/step within 1.5s, cap +10 | ✅ *(this pass)* | Was in Fable's income model but never coded |
| **Kill during i-frames → ×2** | ✅ *(this pass)* | "The high-skill line"; post-F4 the main aggressive income |
| Out-of-combat regen, capped by Lucid Ceiling | ✅ | |
| No in-combat regen | ✅ | |
| **Sanity candles** (+25, pierce the ceiling) | 🔜 | Needs pickups. The *only* counter-play to the descent |
| Room clear +20 | ✅ | |
| Lucid Ceiling — −7/room, floor 60 | ✅ | Floor was 45 when this row was written; the economy sim raised it to 60 ([11](11-roadmap.md) §M1) |
| Open the Eye | ✅ | |
| Band hysteresis, halved refunds below 40 | ✅ | |

## §3.4 The ladder

| Band effect | State | Note |
|---|---|---|
| **Hallucinated projectiles** (1 in 8 / 1 in 4) | ✅ *(this pass)* | Existed in `BulletManager` and `StressTest`; **never wired into the arena** |
| Real bullets cast a drop-shadow, hallucinations don't | ✅ | The sole tell |
| **Enemy weak points** — +50% on hit, visible at Fraying | ✅ *(this pass)* | The ladder's main payoff |
| +10% move at Unravelled | ✅ | |
| Enemy health bars visible at Unsettled | ⚠️ | Currently always shown once damaged |
| Wall cracks shimmer | 🔜 | Needs secret rooms (M2) |
| Secret rooms outlined on minimap | 🔜 | Needs a minimap and secrets (M2) |
| Telegraphs extend 3 frames at Unravelled | ❌ | Cheap; not yet done |
| Chromatic separation / screen distortion | 🔜 | Needs the shader pass (M4) |
| Whisper audio layer, audio pitch-down | 🔜 | Needs audio (M3) |

## §4–5 Blink Step, Recitation, Banish

| Spec | State | Note |
|---|---|---|
| Frame data — 2 startup / 14 i-frame / 8 recovery | ✅ | |
| Dash at 2× move speed | ✅ | |
| **Marked** — dash through an enemy → +25% damage taken, 0.3s | ✅ *(this pass)* | |
| Ghost trail | ❌ | Art |
| Time dilation on an earned near-miss | ❌ | Feel; needs a "bullet avoided" test |
| Recitation + Perfect Recitation | ✅ | |
| Auto-reload with a 0.25s pre-empt window | ✅ | |
| Banish — clear, shove, stun, cooldown, Corruption | ✅ | Implemented last pass |
| Banish wall-break (15 Sanity, no Corruption) | 🔜 | Needs cracked walls (M2) |

## §6–7 Ascension & Corruption

| Spec | State | Note |
|---|---|---|
| Ascension — window, invulnerability, form attack, flee | ✅ | |
| Debt rule + fatal default | ✅ | |
| Escalating max-Sanity penalty | ✅ | |
| Corruption counter | ✅ | Accrues from Banish and Ascension |
| Corruption thresholds (1/3/5/7/10) | 🔜 | M3 |
| Corruption reduction (Cleansing Pool, Warden) | 🔜 | M3 |

## §8 Game feel

| Spec | State | Note |
|---|---|---|
| Hit stop on kill and on damage | ✅ | |
| **Screen shake** — trauma², 6px cap, disableable | ✅ *(this pass)* | |
| Enemy hit flash | ✅ | |
| **Sanity motes** flying to the ring | ✅ *(this pass)* | Docs call this "important" — it is the only thing that makes the kill→Sanity loop legible without UI text |
| Enemy death gib burst | ❌ | Art |
| Muzzle flash | ❌ | Needs Light2D pass |
| Bullet impact decals | ❌ | Needs tilemaps |
| Controller rumble | ❌ | |
| Damage numbers off by default | ✅ | Never implemented, correctly |

---

## What this pass changed

Seven items, chosen by one criterion: **does its absence corrupt the M1 measurement?**

1. **Chain kill bonus** and **kill-during-i-frames ×2** — both are Sanity *income*. Fable's
   room income model (~62/room) explicitly counted the chain bonus. Without them the
   economy was running poorer than designed, which biases metric 1 and metric 5.
2. **Hallucinations wired into the arena** — the ladder's headline effect. Without it
   metric 9 measured whether players *reach* Fraying while nothing observable happened
   there.
3. **Weak points** — the ladder's main payoff. Same problem: a band with no upside.
4. **Sanity motes** — the kill→Sanity loop is the core of the economy and was invisible.
5. **Marked** and **screen shake** — small, and both were cheap enough that deferring them
   was not worth the tracking cost.

## What is deliberately still open

- **Sanity candles.** The only counter-play to the Lucid Ceiling, and it needs the pickup
  system. Until it exists the descent is strictly one-way within a floor, which makes the
  back half of a floor harsher than designed. **Worth knowing before reading metric 1.**
- **Armour.** Same dependency.
- Everything marked 🔜 depends on M2+ systems and is correctly deferred.

## Process change

Re-run this sweep at the end of every milestone, and treat any ❌ on a system the
milestone claims to deliver as a blocker. The cost of the sweep is an hour; the cost of a
phantom feature is a playtest that measures the wrong game.

---
---

# M2 sweep — 29 July 2026

**Scope:** docs/04 (Sigil Circle), docs/03 §3 (Inscriptions), docs/08 (Economy & Shops),
docs/05 §7 (Boss 1), and the docs/11 M2 checklist.

Same legend as above.

## docs/04 — The Sigil Circle

| Spec | State | Note |
|---|---|---|
| 7×7 grid, corners cut, locked Heart | ✅ | **37 usable cells, not 41 — see the discrepancy note below** |
| Three ley lines, positions fixed, types rolled per run | ✅ | Drawn without replacement, so no run gets three of a kind |
| Ley of Blood / Salt / Ash / Gate | ✅ | Gate's "adjacent to everything on the line" included |
| Bonus once per ley, both if on the cross | ✅ | |
| Seven polyomino shapes, 1–5 cells | ✅ | |
| Free rotation in 90° steps, mirroring | ✅ | |
| **Directional sigils — facing matters in play** | ⚠️ | Facing is stored, rotates with the tile and is drawn. **No sigil effect currently reads it.** Tekeli-li adds dash distance in the DASH direction, not the tile's. The layer is scaffolded, not live |
| Adjacency tags/wants, 8 tags, named synergies | ✅ | Named per tag, per §4.3's argument against Gungeon's per-pair model |
| Synergy cap 6, then flat +3% damage | ✅ | |
| Reliquary (6), removal free and instant | ✅ | |
| Dissolution at `20 × cells × tierMult`, Reliquary-only | ✅ | |
| Balance rules §8.1, §8.2, §8.3, §8.5, §8.6, §8.7 | ✅ | Enforced in `SigilData.Validate()`, gated in CI |
| Balance rule §8.4 — 6+ sigils per shape | ❌ | Impossible at 20 sigils across 7 shapes. Reported by the content gate as advisory with the count; revisit at ~70 |
| Reverie: Tab, pauses, outside combat only | ✅ | The combat gate is enforced by the room owner |
| Reverie: invalid placement states the reason | ✅ | Reason comes from `CanPlace`, so rule and message cannot drift |
| Reverie: auto-arrange, deliberately mediocre | ✅ | First-fit, ignores adjacency entirely |
| **Reverie: live diff panel while dragging** | ⚠️ | Shows the RESOLVED state and the held tile's rules text. It does not show a before/after delta of what a placement would gain or lose |
| Reverie: "?" opens a Codex entry | ❌ | Needs the Codex (M3) |
| ~70 sigils | 🔜 | 20 built, which is the M2 target |

> **DISCREPANCY — docs/04 §2.1 says the circle has 41 usable cells. Its own diagram has 37.**
> Summing the diagram's rows: 3 + 5 + 7 + 7 + 7 + 5 + 3 = **37**. The build follows the
> diagram. This matters because §6 and [08 §8](08-economy-and-meta.md) both reason about
> sigil oversupply against 41 — so the intended "you must cut things" pressure is slightly
> **higher** than the docs claim, not lower. Not silently changed: the shape is what the
> diagram draws, and the number is what needs correcting.

## docs/03 §3 — Inscriptions

| Spec | State | Note |
|---|---|---|
| 1–3 slots per weapon by tier | ✅ | From `WeaponData.InscriptionSlots` |
| Held per weapon, lost with the weapon | ✅ | Projected into effective stats on read, never applied destructively |
| Bench: pay gold, pick from 3, apply to a carried weapon | ✅ | Applies to the ACTIVE weapon; Q swaps |
| Conflict groups grey out with a reason | ✅ | `Weapon.RejectReason` returns the text the prompt shows |
| `Vessel Rune` restrictions (no Grimoire, no melee) | ✅ | Generalised as `RequiresAmmo` |
| Reroll at 50 gold, +25 each time | ✅ | |
| Prices scale by floor ×1.0 → ×2.0 | ✅ | Tier→price bands gated in `Validate()` |
| **Overwrite a filled slot at 1.5×** | ❌ | `ReplaceInscription` exists and nothing calls it. The bench refuses when slots are full |
| **Transfer inscriptions to another weapon, 60g each** | ❌ | The review added this to resolve the Inscriptions-vs-ammo-rotation conflict; not built |
| **Live stat preview before purchase** | ⚠️ | Rules text is shown. No DPS/sustain delta — the "no blind buys" promise is only half kept |
| ~35 inscriptions | 🔜 | 15 built against an M2 target of 12 |

## docs/08 — Economy, shops, rooms

| Spec | State | Note |
|---|---|---|
| Gold and Keys as in-run currencies | ✅ | |
| Yellow Fragments | 🔜 | Meta progression, M3 |
| Keys purchasable at 60 +15 per purchase | ✅ | The deliberately bad rate, and it worsens |
| Guaranteed key chest in a connector | ✅ | Once per floor |
| Reward room: choice of two sigils | ✅ | Taking one consumes the group |
| Third option at Corruption ≥3 for +1 Corruption | ✅ | Rolled at inflated Corruption so it genuinely reads a tier up |
| Floor tier table + Corruption tier shift (20/45/70%) | ✅ | |
| Shop: 2 sigils, bench, consumables, reroll, bowl | ✅ | |
| **Shop: weapon slot** | ✅ *(30 Jul)* | `WeaponPool` + `InteractableKind.WeaponOffer`. Priced 100–320 × floor scale, gated on the absolute band |
| **Weapons in chests (C and up)** | ✅ *(30 Jul)* | 33% of Brass-or-better chests. Degrades to the sigil roll when the loadout cannot receive one, so a key is never spent for a refusal |
| **A fourth weapon forces a swap** (docs/03 §1.1) | ✅ *(30 Jul)* | Replaces the ACTIVE weapon, same convention the bench uses. Bound Arms are refused |
| **The swap states what it destroys** (docs/03 §3.4) | ⚠️ | The prompt names the weapon and its Inscription count and refreshes as Q cycles. There is no modal — the prompt is the confirmation |
| **Inscription transfer at the bench, 60g each** | ❌ | Still unbuilt, and now it is load-bearing: a swap is currently a total loss of that weapon's Inscriptions |
| **Shop: The Odd Item** | ❌ | |
| **Stealing from Gaunt** | ❌ | Gaunt is not an entity — the stall is furniture |
| Chests: tiered, key-gated | ✅ | Rust free, gilt 1 key; behind Med/Hard rooms and in secrets |
| **Blasphemous chests** | 🔜 | M3 per docs/11 |
| **Mimics** | ❌ | |
| Shrines: one-shot, cost stated before commitment | ✅ | 4 of 7 |
| Black Font, Weighing Stone, Altar of Nodens, Ledger Stone | ✅ | Black Font's Corruption cost is rolled and SHOWN, not rolled on use |
| Cleansing Pool, Bargainer's Table, Mirror of Yith | ❌ | Each needs an M3 system. **Deliberately absent rather than present and lying** — §5's whole rule is that a shrine states its true cost |
| Ledger Stone reveals the floor | ✅ *(fixed in this pass)* | The sweep found it setting a flag nothing read — a shrine charging 15 Sanity for no visible effect |

## docs/05 §7 — The Thing on the Doorstep

| Spec | State | Note |
|---|---|---|
| Phase 1: a human fight, pistol shots and dodges | ✅ | Strafes at range, aimed volleys with lead |
| Phase 2: the host loses control, the body inverts | ✅ | Radial and spiral, holds ground, bigger silhouette, timed adds |
| Phase 3: abandons the corpse, formless | ✅ | Faster, smaller, no contact damage |
| **The grab — costs 30 Sanity, not health** | ✅ | Through `Drain`, so it can reach zero and latch Ascension |
| Readable telegraph on every volley (R3) | ✅ | Same `PatternPlayer` as every enemy; gated |
| Phase transitions, invulnerable, screen cleared | ✅ | |
| Boss drop: a guaranteed sigil | ✅ | Plus gold, a key and a heart |
| Taunts / dialogue | ⚠️ | Two lines, printed to the console. No presentation layer exists yet |

## docs/11 — the M2 checklist

| Item | State |
|---|---|
| Full floor generator | ✅ |
| Room Template validator | ✅ *(this pass — interiors are flood-checked)* |
| Generation Visualiser | ✅ `gates.ps1 -ShowSeed` |
| **Flow Graph editor plugin** | ❌ Flows are authored in code |
| 30 Undercroft rooms across all roles | ⚠️ **32 templates, but they are still placeholder rectangles with authored obstacle blocks, not hand-built TileMap scenes.** The level-design pipeline does not exist |
| Dread Budget populator | ✅ |
| **Wave system** | ❌ Encounters spawn once on entry |
| Sigil Circle + Reverie, 20 sigils, adjacency + leys | ✅ |
| Gold, keys, chests, loot tables, pity | ✅ |
| Shop + Inscription Bench, 12 Inscriptions | ✅ (15) |
| Boss 1, all 3 phases | ✅ |
| **Unbroken Seals** | ❌ |
| **Save/load, run state, basic Vestibule** | ❌ |
| 10k-seed sweep green in CI | ✅ |

## What this pass found that no gate was watching

1. **Flow selection was biased by the retry loop.** Re-rolling the flow inside each attempt
   meant the reported flow was always whichever succeeded, so the easiest topology won by
   attrition: 60/25/14 across three flows authored to be equally likely. The sweep's
   "every authored flow is reachable" assertion passed the whole time, because *reachable*
   is not *fair*. Fixed; now 34/34/32 with the fallback rate down from 0.77% to 0.07%.
2. **A boss phase change could never be observed.** Set inside `TakeDamage` during the
   enemy manager's tick, cleared by the room owner at the top of its own tick — and Godot
   ticks parents before children. Same bug class as Ascension's zero-detection; same fix,
   a consume-once latch.
3. **Bullets and enemies had never met a wall.** Both hand-simulate movement and never
   touch the physics server. Now gated as a positional invariant.

## Deliberately still open, and worth knowing before the M1 playtest

- **`Tune.cs` still holds gameplay constants**, violating docs/09 §5. Sigils, inscriptions,
  bosses and rooms all moved to `.tres` this pass; the player and Sanity constants did not.
- **Directional sigils do nothing directionally.** If a playtest is meant to say anything
  about docs/04 §3.2's orientation layer, it currently cannot.
- **The Reverie's diff panel is not a diff.** §7 asks for a preview of what a placement
  gains and loses; the player currently sees only the state after committing.
- **No save/load.** A run ends when the process does.

---

## Weapon acquisition — 30 July 2026

The sixth "specified, believed present, absent", and the largest: five weapons were
authored and content-validated, three were handed out by a hardcoded array at run start,
and **the other two were reachable by no means at all.** `Interactable.Weapon` was a field
with no writer, the drop tables held no weapons, and Gaunt's stall stocked every slot
docs/08 §2.1 lists except slot 3.

**The thing underneath it, which the write-up missed.** The startup array handed out the
Webley, the Cantrip and the Kris — the Antiquarian's, the Dreamer's and the Fisherman's
Bound Arms (docs/08 §7). docs/03 §1.1 gives a run **one**. Bound Arms cannot be dropped, so
three of them is not merely off-spec: it is a **full loadout with no slot a found weapon
could ever enter.** Wiring the shop slot without fixing that would have produced an offer
that always refused, and the whole feature would have read as broken rather than absent.
The run now starts with the Webley alone, which also makes the loadout agree with the
Antiquarian Heart Sigil the run was already using. The Grimoire and the melee weapon are
still carried by `gates.ps1 -Arena`, which is where docs/11's M1 mandate for them lives.

**Gated, and every assertion proven to fail.** `gates.ps1 -Weapons`. Three sabotages:
unregistering a weapon (red by name), removing the Bound Arm protection (red), and removing
the shop's weapon slot (the autorun's end-to-end assertion, red).

**The gate that would have caught the original bug is the directory scan**, not the pool
walk — `TestEveryWeaponIsReachable` iterates the pool, so a weapon that was never registered
is unreachable *and invisible to the test*. Reaching past the code to the authored content
is the only version of that check that works. Worth copying wherever else the project has a
hand-maintained list of `.tres` paths.
