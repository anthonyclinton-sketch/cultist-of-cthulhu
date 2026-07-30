using CultistOfCthulhu.Core;
using CultistOfCthulhu.Player;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.UI;

/// <summary>
/// docs/10 §3. Bottom-left defensive state, bottom-right offensive state, and NOTHING in
/// the play area — that rule is absolute during combat.
///
/// The sanity ring physically surrounds the hearts so one glance reads the player's whole
/// defensive position. Drawn with _Draw rather than assembled from Control nodes because
/// it is an arc gauge, and because a redraw-on-change custom draw is cheaper than a
/// dozen nodes fighting the layout engine every frame.
/// </summary>
public sealed partial class Hud : Node2D
{
    public PlayerController? Player { get; set; }

    private static readonly Color HeartFull = new("C1440E");
    private static readonly Color HeartEmpty = new("2A1E1E");
    private static readonly Color SanityColour = new("7FE0D4");
    private static readonly Color SanityCeiling = new("3A5F5A");
    private static readonly Color CorruptionColour = new("B0122A");
    private static readonly Color PerfectWindow = new("FFE066");
    private static readonly Color ArmourColour = new("B8C4D0");

    private const float RingRadius = 34f;
    private const float RingWidth = 5f;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Player is null) return;

        // The whole defensive cluster sits 14px higher than it did, because the Corruption
        // readout below it needs two lines and was being clipped by the bottom of a 360px
        // viewport. Moving the anchor is the fix; squeezing the readout was not.
        Vector2 anchor = new(70, 286);
        DrawSanityRing(anchor);
        DrawHearts(anchor);
        DrawCorruption(new Vector2(14, 330));
        DrawCurrency(new Vector2(430, 274));
        DrawWeapon(new Vector2(430, 300));
        DrawBoss();
        DrawAscension();
    }

    /// <summary>Set by the room owner while a boss fight is running. More than one on floor 2
    /// — Mother Hydra's Brood is a matriarch and a consort, fought at once (docs/05 §7).</summary>
    public System.Collections.Generic.IReadOnlyList<Enemies.Boss>? Bosses { get; set; }

    /// <summary>
    /// The boss bar. Pinned to the top of the SCREEN, not drawn above the boss.
    ///
    /// The first version drew it in world space, and the very first capture of the fight
    /// showed why that fails: the boss opens the fight above the player, so its bar sat
    /// fifty pixels off the top of a 360-pixel viewport and was simply not there. A bar
    /// that vanishes whenever the boss is near an edge is worse than no bar, because the
    /// player learns to stop looking for it.
    ///
    /// docs/10 §3 keeps UI out of the play area during combat, and this is the exception
    /// the rule is for: a boss bar is the fight's clock, and phases the player cannot see
    /// coming cannot be paced against. The phase thresholds are ticked onto it for the
    /// same reason.
    /// </summary>
    private void DrawBoss()
    {
        if (Bosses is null) return;

        // Bars stack downward, one per LIVING boss. A dead one's bar is removed rather than
        // left empty: on a two-boss fight an empty bar and a full one side by side reads as
        // "half done", when what it means is "that one is finished, this one is the fight".
        int row = 0;
        for (int i = 0; i < Bosses.Count; i++)
        {
            Enemies.Boss b = Bosses[i];
            if (!b.Alive) continue;
            DrawBossBar(b, row++);
        }
    }

    private void DrawBossBar(Enemies.Boss b, int row)
    {
        var font = ThemeDB.FallbackFont;
        // 340 wide rather than centred-and-as-wide-as-it-fits: the minimap occupies the
        // top-right from x=500, and a bar that runs under it hides its own last 10% —
        // which is precisely the part the player is watching at the end of a phase.
        const float w = 340f, h = 7f;
        var at = new Vector2(130f, 22f + row * 22f);

        DrawRect(new Rect2(at - new Vector2(1, 1), new Vector2(w + 2, h + 2)), new Color(0, 0, 0, 0.72f));

        // A submerged boss's bar DIMS rather than disappearing. The fight is "hit the right
        // one at the right time" (docs/05 §7), so which one is currently hittable has to be
        // readable from the bars alone — the bodies are across the room and one of them is
        // under water.
        Color fill = b.Submerged ? b.PhaseTint.Darkened(0.55f) : b.PhaseTint;
        DrawRect(new Rect2(at, new Vector2(w * b.HealthFraction, h)), fill);

        foreach (float threshold in new[] { b.Data.Phase2At, b.Data.Phase3At })
        {
            DrawRect(new Rect2(at + new Vector2(w * threshold, -2f), new Vector2(1f, h + 4f)),
                     new Color(1f, 1f, 1f, 0.5f));
        }

        DrawString(font, at + new Vector2(0f, -5f), b.Data.DisplayName,
                   HorizontalAlignment.Left, -1, 10, new Color("E8E1D5"));
        DrawString(font, at + new Vector2(w - 54f, -5f), $"phase {b.Phase}",
                   HorizontalAlignment.Left, -1, 9, new Color("8B8578"));

        // The transition is invulnerable, and a player who does not know that reads it as
        // their damage having stopped working. Same for the tide holding one under.
        if (b.Invulnerable)
        {
            DrawString(font, at + new Vector2(w * 0.5f - 30f, h + 6f),
                       b.Submerged ? "SUBMERGED" : "UNTOUCHABLE",
                       HorizontalAlignment.Left, -1, 9, new Color("FFE066"));
        }
    }

    /// <summary>
    /// docs/02 §6. The white-out marks the transformation; the countdown bar is the only
    /// piece of UI allowed into the play area, and only because the entire point of the
    /// state is that it is ending.
    /// </summary>
    private void DrawAscension()
    {
        var a = Player!.Ascension;
        var font = ThemeDB.FallbackFont;

        if (a.WhiteoutRemaining > 0f)
        {
            float alpha = a.WhiteoutRemaining / AscensionController.WhiteoutDuration;
            DrawRect(new Rect2(0, 0, 640, 360), new Color(1, 1, 1, alpha));
        }

        if (!a.IsAscended) return;

        // Countdown, centred and unmissable — the bill is coming and the player should
        // feel it approaching.
        const float w = 220f, h = 4f;
        var p = new Vector2(320 - w * 0.5f, 40);
        DrawRect(new Rect2(p, new Vector2(w, h)), new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect2(p, new Vector2(w * (1f - a.Progress), h)), new Color("B0122A"));

        DrawString(font, new Vector2(320 - 60, 32), "ASCENDED", HorizontalAlignment.Center, 120, 12,
                   new Color("B0122A"));
        DrawString(font, new Vector2(320 - 90, 60),
                   $"exit costs {a.HeartCostForNext():F1} hearts", HorizontalAlignment.Center, 180, 9,
                   new Color(0.8f, 0.5f, 0.5f));
    }

    private void DrawSanityRing(Vector2 c)
    {
        SanitySystem s = Player!.Sanity;

        // The Lucid Ceiling is drawn as a dimmer arc BEHIND the current value, so the
        // player can see the descent happening — the ceiling falling is the mechanic
        // (docs/02 §3.3.1) and it must be legible without a tutorial.
        float ceilingFrac = s.Max <= 0f ? 0f : Mathf.Clamp(s.LucidCeiling / s.Max, 0f, 1f);
        float valueFrac = s.Fraction;

        const float start = -Mathf.Pi * 0.75f;
        const float sweep = Mathf.Pi * 1.5f;

        DrawArc(c, RingRadius, start, start + sweep, 48, new Color(0.10f, 0.12f, 0.14f), RingWidth + 2f);
        if (ceilingFrac > 0f)
            DrawArc(c, RingRadius, start, start + sweep * ceilingFrac, 48, SanityCeiling, RingWidth);

        Color ringColour = s.Band switch
        {
            SanityBand.Unsettled => SanityColour.Lerp(new Color("9D4EDD"), 0.25f),
            SanityBand.Fraying => SanityColour.Lerp(new Color("9D4EDD"), 0.55f),
            SanityBand.Unravelled => new Color("B0122A"),
            _ => SanityColour,
        };

        if (valueFrac > 0f)
            DrawArc(c, RingRadius, start, start + sweep * valueFrac, 48, ringColour, RingWidth);

        // Band boundary ticks, so the ladder is a visible structure and not a surprise.
        DrawBandTick(c, start, sweep, Tune.BandUnsettled / s.Max);
        DrawBandTick(c, start, sweep, Tune.BandFraying / s.Max);
        DrawBandTick(c, start, sweep, Tune.BandUnravelled / s.Max);
    }

    private void DrawBandTick(Vector2 c, float start, float sweep, float frac)
    {
        if (frac is <= 0f or >= 1f) return;
        float a = start + sweep * frac;
        Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
        DrawLine(c + dir * (RingRadius - RingWidth), c + dir * (RingRadius + RingWidth),
                 new Color(0f, 0f, 0f, 0.65f), 1.5f);
    }

    private void DrawHearts(Vector2 c)
    {
        float p = Player!.Hearts;
        int max = Mathf.CeilToInt(Player.MaxHearts);
        const float size = 9f;
        const float gap = 4f;
        float totalW = max * size + (max - 1) * gap;
        Vector2 origin = c - new Vector2(totalW * 0.5f, size * 0.5f);

        for (int i = 0; i < max; i++)
        {
            var r = new Rect2(origin + new Vector2(i * (size + gap), 0), new Vector2(size, size));
            DrawRect(r, HeartEmpty);

            float fill = Mathf.Clamp(p - i, 0f, 1f);
            if (fill > 0f)
                DrawRect(new Rect2(r.Position, new Vector2(size * fill, size)), HeartFull);
        }

        // Armour stacks to the LEFT of the hearts (docs/02 §2) — it is consumed first, so
        // reading right-to-left gives the order in which the player will lose things.
        for (int i = 0; i < Player.Armour; i++)
        {
            var r = new Rect2(origin + new Vector2(-(i + 1) * (size * 0.7f + 3f), 1f),
                              new Vector2(size * 0.7f, size - 2f));
            DrawRect(r, ArmourColour);
            DrawRect(r, new Color(1, 1, 1, 0.35f), filled: false, width: 1f);
        }
    }

    /// <summary>
    /// Corruption pips: small, permanent, ominous, and gold at 10 (docs/10 §3).
    ///
    /// This was a placeholder that drew ZERO pips, with a comment deferring it to M3 — while
    /// Corruption was accruing from Banish, Ascension, the reward room's third option and
    /// every Forbidden inscription. The player was making one-way, permanent, irreversible
    /// decisions against a number they could not see.
    ///
    /// Partial pips are drawn, because Banish grants +0.25 and a stat that visibly does
    /// nothing three times out of four teaches the player it is not moving.
    /// </summary>
    private void DrawCorruption(Vector2 origin)
    {
        var font = ThemeDB.FallbackFont;
        float c = Player!.Corruption;

        bool yellow = Core.CorruptionTiers.YellowSign(c);
        Color pip = yellow ? new Color("F2C14E") : CorruptionColour;

        // CAPTIONED. Without this the row was ten small dots in the corner of a dark screen,
        // and a player who noticed them at all had no way to know what they counted.
        DrawString(font, origin, "CORRUPTION", HorizontalAlignment.Left, -1, 8,
                   yellow ? pip : new Color(0.5f, 0.42f, 0.44f));

        int full = Mathf.FloorToInt(c);
        float part = c - full;
        var pips = origin + new Vector2(58f, -3f);

        // Ten slots, because 10 is the cap and a fixed row shows how far there is left to go.
        // Empty slots are drawn far brighter than they were: at 0.22 grey on a dark HUD the
        // row was invisible, so an unfilled track read as no track at all.
        for (int i = 0; i < 10; i++)
        {
            Vector2 at = pips + new Vector2(i * 7f, 0f);
            if (i < full) DrawCircle(at, 2.6f, pip);
            else if (i == full && part > 0f)
            {
                // A partial pip gets a visible ring as well as its fill, because Banish
                // grants 0.25 and a quarter-radius dot is not a change anyone can see.
                DrawArc(at, 2.6f, 0, Mathf.Tau, 10, pip with { A = 0.4f }, 1f);
                DrawCircle(at, 2.6f * Mathf.Max(0.45f, part), pip);
            }
            else
            {
                DrawArc(at, 2.2f, 0, Mathf.Tau, 8, new Color(0.34f, 0.30f, 0.32f), 1f);
            }
        }

        // The NUMBER, because the pips cannot express 0.25 granularity and Banish moves it a
        // quarter at a time.
        DrawString(font, origin + new Vector2(132f, 0f), $"{c:0.##}",
                   HorizontalAlignment.Left, -1, 9, yellow ? pip : new Color(0.72f, 0.6f, 0.62f));

        // And what happens NEXT. This is the line that makes the stat a decision rather than
        // a mystery: the previous version printed the tier already reached, which said
        // "unmarked" for everything below 1 — actively telling the player nothing had
        // happened while they were spending Corruption to make it happen.
        float next = Core.CorruptionTiers.NextThreshold(c);
        string line = next > 0f
            ? $"{Core.CorruptionTiers.NextEffect(c)} at {next:0.#}"
            : "nothing left to lose";

        if (Core.CorruptionTiers.TierFor(c) > 0)
            line = $"{Core.CorruptionTiers.Describe(c)}  ·  {line}";

        DrawString(font, origin + new Vector2(0f, 12f), line, HorizontalAlignment.Left, -1, 8,
                   yellow ? pip : new Color(0.5f, 0.42f, 0.44f));
    }

    /// <summary>
    /// Gold and keys, above the weapon (docs/10 §3).
    ///
    /// Absent until now, which made the entire economy invisible: the player could not see
    /// what they had, so they could not tell whether walking back to the shop was worth the
    /// trip, and a price in a prompt had nothing to be compared against. Two currencies with
    /// no readout is not a minimal HUD, it is a missing one.
    ///
    /// Both are always shown, including at zero. Hiding a currency at zero is the version
    /// where a player never learns keys exist until the first locked chest tells them off.
    /// </summary>
    private void DrawCurrency(Vector2 origin)
    {
        var font = ThemeDB.FallbackFont;
        const float width = 180f;

        // Two right-aligned columns rather than one padded string. Padding with spaces
        // lines up only for the font it was eyeballed in.
        DrawString(font, origin, $"{Player!.Keys} keys",
                   HorizontalAlignment.Right, width, 10, KeyColour);
        DrawString(font, origin, $"{Player.Gold} gold",
                   HorizontalAlignment.Right, width - 54f, 10, GoldColour);
    }

    private static readonly Color GoldColour = new("F2C14E");
    private static readonly Color KeyColour = new("E8E1D5");

    private void DrawWeapon(Vector2 origin)
    {
        if (Player!.Weapons.Count == 0) return;
        Weapon w = Player.Weapons.Active;

        var font = ThemeDB.FallbackFont;
        DrawString(font, origin, w.Data.DisplayName, HorizontalAlignment.Right, 180, 11,
                   new Color(0.9f, 0.87f, 0.82f));

        // Say what KIND of weapon this is, prominently.
        //
        // Melee and Grimoires behave nothing like a gun — one fires no projectile at all,
        // the other spends Sanity per shot — and with only a name shown, swapping onto the
        // melee weapon reads as the gun having broken. It was reported exactly that way.
        if (w.Data.IsMelee)
        {
            DrawString(font, origin + new Vector2(0, 42), "MELEE  ·  no projectiles",
                       HorizontalAlignment.Right, 180, 9, new Color("FFB347"));
        }
        else if (w.Data.SanityPerShot > 0f)
        {
            DrawString(font, origin + new Vector2(0, 42),
                       $"GRIMOIRE  ·  {w.Data.SanityPerShot:F0} sanity/shot",
                       HorizontalAlignment.Right, 180, 9, new Color("7FE0D4"));
        }

        // Slot indicator, so it is obvious that Q swapped something.
        int slots = Player.Weapons.Count;
        for (int i = 0; i < slots; i++)
        {
            var r = new Rect2(origin + new Vector2(180 - (slots - i) * 9f, -12f), new Vector2(6f, 3f));
            DrawRect(r, ReferenceEquals(Player.Weapons.Weapons[i], w)
                ? new Color("FFB347") : new Color(0.3f, 0.29f, 0.28f));
        }

        // Magazine as discrete pips, never a bar — pips are countable at a glance
        // (docs/10 §3).
        if (!w.Data.IsMelee && w.Data.SanityPerShot <= 0f)
        {
            // EFFECTIVE, not authored. Deep Etching adds 40% magazine and Gaunt's Bargain
            // removes 40%; reading Data here would draw the pips the weapon had before it
            // was etched, so the player could not see what they had just paid for.
            int mag = w.EffectiveMagazineSize;
            float pipW = mag > 24 ? 3f : 5f;
            float gap = 2f;
            float totalW = mag * pipW + (mag - 1) * gap;
            Vector2 p = origin + new Vector2(180 - totalW, 8);

            for (int i = 0; i < mag; i++)
            {
                var r = new Rect2(p + new Vector2(i * (pipW + gap), 0), new Vector2(pipW, 7));
                DrawRect(r, i < w.Magazine ? new Color("FFB347") : new Color(0.16f, 0.14f, 0.13f));
            }

            DrawString(font, origin + new Vector2(0, 30),
                       $"{w.Reserve} rounds   ·   recite {w.EffectiveReloadCost:F0} sanity",
                       HorizontalAlignment.Right, 180, 9, new Color(0.6f, 0.58f, 0.55f));
        }
        else if (w.Data.SanityPerShot > 0f)
        {
            DrawString(font, origin + new Vector2(0, 18),
                       $"{w.Data.SanityPerShot:F0} sanity / shot", HorizontalAlignment.Right, 180, 9,
                       new Color(1f, 0.85f, 0.31f));
        }
        else
        {
            DrawString(font, origin + new Vector2(0, 18),
                       $"melee · +{w.Data.MeleeSanityPerHit:F0} sanity / hit",
                       HorizontalAlignment.Right, 180, 9, new Color(0.6f, 0.58f, 0.55f));
        }

        // Perfect Recitation: a shrinking ring the player times a keypress against.
        if (w.IsReloading)
        {
            Vector2 rc = origin + new Vector2(150, -18);
            DrawArc(rc, 12f, 0, Mathf.Tau, 24, new Color(0.25f, 0.25f, 0.25f), 2f);
            DrawArc(rc, 12f * (1f - w.ReloadProgress), 0, Mathf.Tau, 24,
                    w.PerfectWindowOpen ? PerfectWindow : new Color(0.55f, 0.55f, 0.6f), 2.5f);
            if (w.PerfectWindowOpen) DrawArc(rc, 12f, 0, Mathf.Tau, 24, PerfectWindow, 1.5f);
        }
    }
}
