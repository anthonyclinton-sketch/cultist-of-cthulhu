# Audit — Specified vs. Implemented

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
| Lucid Ceiling — −7/room, floor 45 | ✅ | |
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
