using System.Collections.Generic;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Items;

public enum PickupKind
{
    /// <summary>+25 Sanity, and it PIERCES the Lucid Ceiling (docs/02 §3.3.1).
    /// The only counter-play to the descent.</summary>
    SanityCandle,
    Heart,
    Armour,
    Ammo,
    Gold,
    Key,
}

public struct Pickup
{
    public PickupKind Kind;
    public Vector2 Position;
    public Vector2 Velocity;
    public float Amount;
    public float Age;
    public bool Magnetised;
}

/// <summary>
/// Ground pickups (docs/06 §6.3).
///
/// A plain list rather than the struct-of-arrays treatment the bullets get: peak count is
/// on the order of ten, so the cache-locality argument does not apply and the simpler code
/// wins. The bullet manager's design is a response to 4096 entities, not a house style.
///
/// The important one is the SANITY CANDLE. It is the single counter-play to the Lucid
/// Ceiling — everything else in the economy pushes Sanity down across a floor, and the
/// candle is the only thing that can push it back above the ceiling (docs/02 §3.3.1).
/// Until this existed the descent was strictly one-way within a floor, which made the back
/// half harsher than designed and made the Sanity economy impossible to judge.
/// </summary>
public sealed partial class PickupManager : Node2D
{
    private readonly List<Pickup> _pickups = new(32);

    /// <summary>Pull radius. Generous, because a pickup you can see but cannot be bothered
    /// to walk into is a pickup that reads as clutter.</summary>
    private const float MagnetRadius = 72f;
    private const float CollectRadius = 13f;
    private const float MagnetAccel = 900f;
    private const float SpawnScatterSpeed = 90f;

    /// <summary>Brief delay before a pickup can be magnetised, so a burst of drops
    /// visibly scatters instead of teleporting into the player on the death frame.</summary>
    private const float MagnetDelay = 0.35f;

    public IReadOnlyList<Pickup> Pickups => _pickups;
    public int Count => _pickups.Count;

    // Collected this tick — polled by the player, so no delegate allocation in the tick.
    public float CollectedSanity { get; private set; }
    public float CollectedHearts { get; private set; }
    public int CollectedArmour { get; private set; }
    public int CollectedAmmo { get; private set; }
    public int CollectedGold { get; private set; }
    public int CollectedKeys { get; private set; }

    public Vector2 PlayerPosition;

    public void Spawn(PickupKind kind, Vector2 position, float amount, Rng rng)
    {
        _pickups.Add(new Pickup
        {
            Kind = kind,
            Position = position,
            Velocity = rng.NextUnitVector() * rng.Range(SpawnScatterSpeed * 0.4f, SpawnScatterSpeed),
            Amount = amount,
            Age = 0f,
        });
    }

    public void ClearAll() => _pickups.Clear();

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        CollectedSanity = 0f;
        CollectedHearts = 0f;
        CollectedArmour = 0;
        CollectedAmmo = 0;
        CollectedGold = 0;
        CollectedKeys = 0;

        int i = 0;
        while (i < _pickups.Count)
        {
            Pickup p = _pickups[i];
            p.Age += dt;

            float dist = p.Position.DistanceTo(PlayerPosition);

            if (p.Age >= MagnetDelay && dist <= MagnetRadius) p.Magnetised = true;

            if (p.Magnetised)
            {
                Vector2 toPlayer = (PlayerPosition - p.Position).Normalized();
                p.Velocity = p.Velocity.MoveToward(toPlayer * 460f, MagnetAccel * dt);
            }
            else
            {
                // Settle where it landed.
                p.Velocity = p.Velocity.MoveToward(Vector2.Zero, 240f * dt);
            }

            p.Position += p.Velocity * dt;

            if (dist <= CollectRadius && p.Age >= 0.12f)
            {
                Collect(p);
                _pickups.RemoveAt(i);
                continue;
            }

            _pickups[i] = p;
            i++;
        }
    }

    private void Collect(in Pickup p)
    {
        switch (p.Kind)
        {
            case PickupKind.SanityCandle: CollectedSanity += p.Amount; break;
            case PickupKind.Heart: CollectedHearts += p.Amount; break;
            case PickupKind.Armour: CollectedArmour += (int)p.Amount; break;
            case PickupKind.Ammo: CollectedAmmo += (int)p.Amount; break;
            case PickupKind.Gold: CollectedGold += (int)p.Amount; break;
            case PickupKind.Key: CollectedKeys += (int)p.Amount; break;
        }
    }

    /// <summary>Palette per docs/10 §1.3 — pickups are neutral gold and pale cyan, kept
    /// clear of both the warm player band and the cool enemy-projectile band.</summary>
    public static Color ColourFor(PickupKind kind) => kind switch
    {
        PickupKind.SanityCandle => new Color("7FE0D4"),
        PickupKind.Heart => new Color("C1440E"),
        PickupKind.Armour => new Color("B8C4D0"),
        PickupKind.Ammo => new Color("D8A85B"),
        PickupKind.Gold => new Color("F2C14E"),
        PickupKind.Key => new Color("E8E1D5"),
        _ => Colors.White,
    };

    public static float RadiusFor(PickupKind kind) => kind switch
    {
        PickupKind.SanityCandle => 6f,
        PickupKind.Heart => 6f,
        PickupKind.Armour => 5.5f,
        PickupKind.Gold => 3f,
        _ => 4.5f,
    };
}
