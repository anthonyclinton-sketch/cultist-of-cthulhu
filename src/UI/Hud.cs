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

        Vector2 anchor = new(70, 300);
        DrawSanityRing(anchor);
        DrawHearts(anchor);
        DrawCorruption(anchor + new Vector2(-24, 44));
        DrawWeapon(new Vector2(430, 300));
        DrawAscension();
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

    private void DrawCorruption(Vector2 origin)
    {
        // Small, permanent, ominous (docs/10 §3). Placeholder at zero until the Corruption
        // system lands at M3.
        const int pips = 0;
        for (int i = 0; i < pips; i++)
            DrawCircle(origin + new Vector2(i * 7, 0), 2.5f, CorruptionColour);
    }

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
            int mag = w.Data.MagazineSize;
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
                       $"{w.Reserve} rounds   ·   recite {w.Data.SanityCostToReload:F0} sanity",
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
