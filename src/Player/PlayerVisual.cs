using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Player;

/// <summary>
/// The player's placeholder body, owned by <see cref="PlayerController"/> rather than by
/// each scene.
///
/// It lived in the scenes before, which meant every new scene had to remember to add it —
/// and FloorRunner did not, so the player was invisible on generated floors while being
/// fully simulated. Moving it into the controller makes that class of bug impossible:
/// wherever a player exists, it can be seen.
///
/// Attached as a CHILD node rather than drawn by the scene so it inherits the player's
/// transform and Godot's physics interpolation for free — drawing it from the scene's
/// _Draw would sample the un-interpolated physics position and judder against the camera.
///
/// docs/02 §1.1: the 6px hitbox is always faintly visible and FULLY lit during i-frames.
/// The player must be able to see exactly what is invulnerable, so that rule is enforced
/// here rather than left to each scene's discretion.
/// </summary>
public sealed partial class PlayerVisual : Node2D
{
    public PlayerController Controller = null!;

    private static readonly Color Body = new("6E7686");
    private static readonly Color BodyAscended = new("B0122A");
    private static readonly Color Hitbox = new("FFB347");
    private static readonly Color HitboxInvuln = new("FFFFFF");
    private static readonly Color AimColour = new("FFD9A0");

    private const float BodyRadius = 9f;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Controller is null) return;

        bool invuln = Controller.IsInvulnerable;
        bool ascended = Controller.Ascension.IsAscended;

        // Ghost trail during the dash, so the 2x movement reads as a dash rather than a
        // teleport (docs/02 §4).
        if (Controller.Phase == BlinkPhase.Invulnerable)
        {
            Vector2 back = -Controller.Velocity.Normalized();
            for (int i = 1; i <= 3; i++)
                DrawCircle(back * (i * 7f), BodyRadius - i * 1.5f,
                           Hitbox with { A = 0.22f - i * 0.05f });
        }

        // Body.
        Color bodyColour = ascended ? BodyAscended : Body;
        if (invuln && !ascended) bodyColour = bodyColour.Lerp(Colors.White, 0.35f);
        DrawCircle(Vector2.Zero, BodyRadius, bodyColour);
        DrawArc(Vector2.Zero, BodyRadius, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.5f), 1.5f);

        DrawMeleeSwing();

        // Aim indicator — without it there is no way to tell which way you are shooting.
        Vector2 aim = Controller.AimDirection;
        DrawLine(aim * (BodyRadius - 1f), aim * (BodyRadius + 7f), AimColour, 2f);

        // The hitbox. Always visible, fully lit while invulnerable.
        DrawCircle(Vector2.Zero, Tune.PlayerHitboxRadius,
                   invuln ? HitboxInvuln : Hitbox with { A = 0.8f });

        // Damage flash — 12Hz, docs/02 §2.
        DrawDamageFlash();
    }

    /// <summary>
    /// The melee swing arc.
    ///
    /// Melee emits no projectile, so before this existed a player who pressed Q onto the
    /// Sacrificial Kris saw *nothing at all* happen when they attacked — reported, exactly
    /// as you would expect, as "I can no longer see my bullets".
    ///
    /// Drawn at the weapon's REAL reach and arc, not a decorative approximation, so the
    /// player learns the actual hitbox by looking at it. Melee is a spacing weapon
    /// (docs/03 §2 Family V) and spacing cannot be judged from a swoosh that lies.
    /// </summary>
    private void DrawMeleeSwing()
    {
        float t = Controller.MeleeSwing;
        if (t <= 0f) return;

        float reach = Controller.MeleeSwingReach;
        float half = Mathf.DegToRad(Controller.MeleeSwingArc) * 0.5f;
        float centre = Mathf.Atan2(Controller.MeleeSwingDirection.Y, Controller.MeleeSwingDirection.X);

        // Sweeps across the arc as it fades, so the swing has direction rather than
        // appearing as a symmetrical flash.
        float sweep = Mathf.Lerp(-half, half, 1f - t);
        float alpha = t * 0.9f;

        DrawArc(Vector2.Zero, reach, centre - half, centre + half, 24,
                new Color(1f, 0.85f, 0.6f, alpha * 0.45f), 3f);
        DrawArc(Vector2.Zero, reach * 0.72f, centre - half, centre + half, 24,
                new Color(1f, 0.95f, 0.8f, alpha * 0.25f), 2f);

        // The leading edge of the blade.
        var edge = new Vector2(Mathf.Cos(centre + sweep), Mathf.Sin(centre + sweep));
        DrawLine(edge * (BodyRadius * 0.5f), edge * reach, new Color(1f, 1f, 0.9f, alpha), 2.5f);
    }

    private void DrawDamageFlash()
    {
        if (Controller.DamageIFramesRemaining > 0f
            && Mathf.PosMod(Controller.DamageIFramesRemaining * 12f, 1f) > 0.5f)
        {
            DrawCircle(Vector2.Zero, BodyRadius + 2f, new Color(1, 1, 1, 0.45f));
        }
    }
}
