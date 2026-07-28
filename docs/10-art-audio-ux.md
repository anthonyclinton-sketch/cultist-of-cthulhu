# 10 — Art Direction, Audio & UX

---

## 1. Art Direction

### 1.1 The one-line brief
> **A 1920s pulp horror magazine cover, printed on cheap paper, that has been left in a damp cellar.**

Grounded period detail (brick, brass, wool coats, oil lamps) rendered in a limited, slightly *soured* palette — then invaded by colours that don't belong to the period or to the visible spectrum.

### 1.2 Technique
- **High-resolution pixel art.** 16px tile grid; characters 20×32; bosses up to 128×128. This is the correct choice for a solo/small team: it's readable at bullet-hell density, it animates cheaply, and it dates well.
- **Native resolution 640×360**, integer-scaled to 1080p/1440p. Enables a `viewport` scaling mode with crisp pixels. Bullets are drawn at native res so they stay sharp and legible.
- **Two-plane lighting.** A `CanvasModulate` sets the floor's base darkness; `Light2D`s carve out warm pools. Normal maps on tilesets are *optional* — evaluate cost in M2; the mood works without them.
- **Sub-pixel motion is allowed for bullets, forbidden for characters.** Characters snap to the pixel grid; bullets do not, because bullet-hell readability requires smooth trajectories.

### 1.3 The palette contract (readability first — see [05 §1](05-enemies-and-bosses.md))

```
PLAYER & PLAYER PROJECTILES      warm — amber #FFB347, ember #FF6B35, rust #C1440E
ENEMY PROJECTILES                cool — bile #7FBF3F, violet #9D4EDD, bone #E8E1D5
NEUTRAL / PICKUPS                gold #F2C14E (gold), pale cyan #7FE0D4 (sanity)
CORRUPTION UI                    the wrong red #B0122A
BACKGROUNDS                      desaturated 35% vs. entities, value-compressed
```

**This contract overrides every floor's palette.** Innsmouth is green — but its bullets are still a *different* green than the walls, at higher value and with an outline. Any floor palette that fights this is re-authored.

### 1.4 Per-floor palette identity

| Floor | Dominant | Accent | Light source |
|---|---|---|---|
| Undercroft | Oxblood brick, black | Tallow amber | Candles (flickering, small radius) |
| Innsmouth | Grey-green, rot-brown | Innsmouth gold | Overcast sky + lamp posts |
| Archives | Sepia, dark walnut | Banker's-lamp green | Desk lamps (large, soft, static) |
| Mountains | Whiteout blue, basalt black | Elder Thing crimson | Diffuse snow-glare, no shadows |
| Leng | Grey-violet | Star-white | Ambient, sourceless — deeply unsettling |
| R'lyeh | Viridian, bone | The wrong orange | Bioluminescence (moving, breathing) |
| Court of Azathoth | **No colour** — pure black and white | One yellow | None. The bullets *are* the light. |

### 1.5 The "geometry lie" — our signature visual

*Pathogenic*'s identity is soft-body squish. Ours must be equally screenshot-legible and equally cheap. It is **distortion**:

| Effect | Implementation | Trigger |
|---|---|---|
| **Chromatic separation** at screen edges | Full-screen shader, radial offset | Sanity < 40, scaling |
| **Breathing walls** | Vertex displacement on tilemap material, low freq | Sanity < 20, R'lyeh always |
| **Impossible angles** | Slight per-room shear on the tilemap transform (0.5–2°) | R'lyeh, Leng |
| **Seam warp** | Screen-space ripple at teleport seams | R'lyeh |
| **Palette inversion pulse** | Screen shader, 0.4s | Ascension, boss phase change |
| **The Colour** | Desaturate everything *including the UI* to greyscale, then tint one entity a colour with no name | Floor S only |

All are post-process shaders on a single full-screen pass. Cost: one fullscreen quad. All must be **individually disableable** in accessibility options.

### 1.6 Animation budget
| Asset | Frames |
|---|---|
| Player idle / run / roll / hit / death | 4 / 8 / 6 / 2 / 12 |
| Fodder enemy | 4 idle, 6 walk, 4 attack, 6 death |
| Elite | +8 for a signature attack |
| Boss | 60–120 across all phases |
| Weapon | 1 sprite + 2-frame muzzle flash (weapons don't animate; the flash sells it) |

**Rule:** telegraph frames get more animation budget than anything else. A boss's wind-up is worth more than its idle.

---

## 2. Audio Direction

### 2.1 Music
- **Instrumentation:** period-appropriate but wrong — 1920s parlour piano, wax-cylinder crackle, a lone clarinet, church organ, and beneath it all a sub-bass drone that isn't a musical instrument. Bowed metal, prepared piano, contrabass clarinet.
- **Adaptive layering** (Godot's `AudioStreamInteractive` or manual bus crossfades):
  - `Explore` — sparse, ambient, almost silent
  - `Combat` — percussion and drone enter on door seal
  - `Combat_Intense` — full layer stack when the room is above 60% of its Dread budget
  - `Low_Sanity` — a **pitch-shifted, time-stretched version of the current track** crossfades in below Sanity 40 and fully replaces it below 20. Same melody, gone wrong.
  - `Ascension` — everything cuts to a single sustained tone plus your own heartbeat
- **Boss themes:** one per boss, no exceptions. Bosses are the memory anchors.

### 2.2 SFX
- **Every enemy shot type has a distinct spawn sound.** Voice-limited to 6 concurrent per type, with a 0.02s spawn window merge so a 40-bullet radial is one sound, not forty.
- ~~**Hallucinated bullets are silent.** This is the KBM tell for low-sanity states and it must be reliable.~~ **CUT — the audio tell is unimplementable.** Voice-limiting (6 concurrent per type) and the 0.02s spawn-merge above mean real bullets are routinely silent too, so silence carries no information. The hallucination tell is now **visual and universal**: real bullets cast a soft offset drop-shadow on the floor plane, hallucinations do not. See [02 §3.4](02-player-and-combat.md) and [05 §1 R9](05-enemies-and-bosses.md). Hallucinations may otherwise sound exactly like real bullets.
- **Sanity motes** have a soft glass chime on absorption — the audio half of the "kills fund your dodges" feedback loop.
- **Weapon audio is the game's texture.** Period firearms should sound heavy, echoing, and mechanically specific. Grimoires should sound like a voice.
- **The Sanity ring** has an audio state: candles gutter audibly as segments deplete.
- Mixing: separate buses for Music / SFX / UI / Ambience / **Voice-of-the-Deep** (the whisper layer), each independently sliderable.

### 2.3 Whispers
Below Sanity 40, a whisper layer fades in — real, intelligible fragments, mixed low, in the player's own voice. Content is drawn from the Codex entries you've unlocked. It says true things.

---

## 3. HUD

Minimal, bottom-left and bottom-right, nothing in the play area.

```
┌────────────────────────────────────────────────────────────────────┐
│  ⌗ Floor 3 — Restricted Archives            [ ▣ Minimap  ]         │
│                                              [           ]         │
│                                                                    │
│                                                                    │
│                        ( P L A Y   A R E A )                       │
│                                                                    │
│                                                                    │
│    ╭─ SANITY RING ─╮                              214 ⬤   3 ✚      │
│   ( ♥♥♥ ♡  ▓▓▓▓░ )                        ┌──────────────────┐     │
│    ╰───────────────╯                      │ SHOGGOTH MAW     │     │
│    CORRUPTION ✱✱✱                         │ ▮▮▮▮▮▮░░  4 mags │     │
└────────────────────────────────────────────────────────────────────┘
```

| Element | Position | Rules |
|---|---|---|
| **Hearts + Sanity ring** | Bottom-left, combined | The ring physically surrounds the hearts. One glance = full defensive state. |
| **Corruption pips** | Below hearts | Small, permanent, ominous. Turns gold at 10. |
| Weapon + ammo | Bottom-right | Magazine as discrete pips (never a bar — pips are countable at a glance) |
| Gold / keys | Above weapon | |
| Minimap | Top-right, 140×140 | Toggleable to fullscreen with M. **Lies in R'lyeh.** |
| Floor name | Top-left, fades after 4s | |
| Boss health | Top-centre, segmented by phase | Only during boss fights |
| Damage numbers | — | **Off by default** |

**Absolute rule:** nothing may occlude the play area during combat. No pickup toasts in the centre, no full-screen synergy popups, no tutorial banners. Synergy activation is signalled by a brief ring flash and a sound, with the detail available in Reverie.

---

## 4. The Reverie Screen

See [04 §7](04-sigil-circle.md) for the full spec. UX essentials:

- Opens in < 0.15s. Full pause.
- **Live diff panel** — every stat delta and every gained/lost synergy updates as you drag.
- **Controller-first design**: a grid cursor with snapping, not a simulated mouse pointer. Shoulder buttons rotate. This screen will be used constantly on Deck.
- **Auto-arrange** as an accessibility affordance, deliberately suboptimal.
- Invalid placements state a *reason*, never just turn red.

---

## 5. The Codex

Modelled on Gungeon's Ammonomicon, which is one of the best pieces of UX in the genre.

- Entry unlocked on first encounter with anything: weapon, sigil, inscription, enemy, boss, room hazard, NPC, floor.
- Each entry: sprite, name, **full mechanical text** (real numbers, not vague adjectives), flavour text, and unlock condition if not yet acquired.
- Sigil entries list every synergy tag they carry and want, with undiscovered synergies shown as `??? [TIDE]` — enough to theorycraft toward.
- Accessible from the pause menu **during a run**. Players will want to check what a sigil does mid-run; making them quit to find out is hostile.

---

## 6. Onboarding

No tutorial level. Teaching happens through room design (Pillar IV) and contextual prompts.

| Moment | Teaching |
|---|---|
| First room, floor 1 | Two Acolytes with 3s attack gaps. Movement alone suffices. |
| Second room | A prompt appears: `SPACE — Blink Step` when the first unavoidable volley telegraphs |
| First empty magazine | `R — Recite`, with the Sanity cost highlighted on the ring |
| First time Sanity < 40 | A single line: *"You are seeing more clearly."* Nothing else. Let them notice the damage buff themselves. |
| First locked chest | Key icon pulses |
| First sigil pickup | Reverie opens automatically, once, ever |
| First Corruption gain | A one-time modal explaining the stat, with the threshold table |
| First Ascension | No explanation. It explains itself. |

**Rule:** no prompt appears twice. The profile records every taught moment permanently.

---

## 7. Accessibility

Treated as a first-class feature set, not a difficulty selector. **None of these disable achievements.**

| Option | Default |
|---|---|
| **Bullet speed** 60% / 80% / 100% | 100% |
| **Blink Step i-frame extension** +0 / +4 / +8 frames | +0 |
| **Telegraph duration** ×1.0 / ×1.5 / ×2.0 | ×1.0 |
| **High-contrast bullets** (max-saturation outlines, flat backgrounds) | Off |
| **Hallucination contrast** — renders the real-bullet drop-shadow at full opacity with a hard edge | Off |
| **Hitbox always fully visible** | On (small) |
| Screen shake intensity slider | 60% |
| **Disable all distortion shaders** | Off |
| Photosensitivity mode (no flashes, no palette inversion) | Off |
| Colourblind palettes (deuter/prot/trit) | Off |
| Hold-to-fire ↔ toggle-to-fire | Hold |
| **Auto-Recite** (reload without input) | On |
| UI scale 100–200% | 100% |
| Subtitles for all significant audio cues, incl. whispers | On |
| Full input remapping, KBM + pad | — |
| Aim assist cone 0–8° | 0° |
| **Assist Mode**: 2× hearts, no Ascension max-Sanity penalty | Off |

**Deliberate stance:** we ship Assist Mode and keep achievements enabled. A player who needs it is a player, and the alternative is that they refund the game.

---

## 8. Localisation

- All strings in CSV → Godot `.translation` from day one. Retrofitting is expensive; the discipline is nearly free.
- Launch: EN. Post-launch priority by wishlist geography — likely ZH-Hans, RU, DE, ES-419, PT-BR, JA, FR, KO.
- **Watch the Codex** — it's the bulk of the word count (~25,000 words at 1.0) and the most flavour-dense, hardest-to-translate text in the game. Budget accordingly.
- Font: must support Cyrillic and CJK. A pixel font that only covers Latin will block localisation entirely — pick a font with wide coverage *before* authoring UI, or plan a separate CJK font path.
