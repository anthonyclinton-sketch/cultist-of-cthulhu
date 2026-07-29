using CultistOfCthulhu.Sigils;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Rooms;

public enum InteractableKind
{
    /// <summary>A sigil on a pedestal. Reward rooms offer two or three (docs/08 §3).</summary>
    SigilOffer,
    /// <summary>A chest. Tiered, sometimes key-locked (docs/08 §4).</summary>
    Chest,
    /// <summary>A shrine's single clearly-labelled trade (docs/08 §5).</summary>
    Shrine,
    /// <summary>Gaunt's stock: a sigil, a consumable, or a key.</summary>
    ShopItem,
    /// <summary>An Inscription offer at the bench (docs/08 §2.2).</summary>
    Inscription,
    /// <summary>The Dissolution Bowl — sell a Reliquary sigil for gold (docs/04 §6).</summary>
    DissolutionBowl,
    /// <summary>Reroll the bench's offers for gold.</summary>
    Reroll,
}

/// <summary>
/// One thing in a room the player can walk up to and press E on.
///
/// A plain class rather than a Node, in the same spirit as <see cref="Enemies.Enemy"/> and
/// <see cref="Items.Pickup"/>: a room holds a handful of these, they need no scene
/// presence of their own, and the room draws them in one pass. A Node per pedestal buys
/// nothing and costs a layout pass.
///
/// The COST fields are all on the interactable rather than resolved at activation time,
/// because docs/08 §5's rule applies to everything in this file, not just shrines: *every*
/// one of these states its exact cost before commitment. Uncertainty is fine; hidden costs
/// are not. If a cost cannot be shown in <see cref="Prompt"/>, it does not belong here.
/// </summary>
public sealed class Interactable
{
    public InteractableKind Kind;
    public Vector2 Position;
    public float Radius = 26f;

    /// <summary>Shown above the object when in range. Must state the full cost.</summary>
    public string Title = "";
    public string Detail = "";

    /// <summary>Taking one member of a group consumes the whole group — that is what makes
    /// a reward room a CHOICE of two rather than two free sigils.</summary>
    public int GroupId = -1;
    public bool Consumed;

    public int GoldCost;
    public int KeyCost;
    public float CorruptionCost;

    // Payload. Exactly one of these is meaningful per kind.
    public SigilData? Sigil;
    public InscriptionData? Inscription;
    public WeaponData? Weapon;
    public ShrineKind Shrine;
    public ConsumableKind Consumable;
    public int Amount;

    public Color Tint = new("D8A85B");

    public bool InRange(Vector2 p) => !Consumed && p.DistanceSquaredTo(Position) <= Radius * Radius;

    /// <summary>The full prompt line, cost included. Never abbreviated — see the class note.</summary>
    public string Prompt()
    {
        string cost = "";
        if (KeyCost > 0) cost += $"  [{KeyCost} key{(KeyCost > 1 ? "s" : "")}]";
        if (GoldCost > 0) cost += $"  [{GoldCost} gold]";
        if (CorruptionCost > 0f) cost += $"  [+{CorruptionCost:0.##} Corruption]";
        return $"E — {Title}{cost}";
    }
}

/// <summary>docs/08 §5. Each is a one-shot trade whose cost is stated up front.</summary>
public enum ShrineKind
{
    /// <summary>+1 to +3 Corruption (rolled and SHOWN) for a random A/S sigil.</summary>
    BlackFont,
    /// <summary>Half your current gold for the best-tier sigil the floor can offer.</summary>
    WeighingStone,
    /// <summary>One heart container for +40 max Sanity.</summary>
    AltarOfNodens,
    /// <summary>15 Sanity: reveals the whole floor map.</summary>
    LedgerStone,
}

public enum ConsumableKind { Ammo, Candle, Key, Armour, Heart }
