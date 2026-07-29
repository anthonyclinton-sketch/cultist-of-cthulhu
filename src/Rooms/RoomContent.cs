using System.Collections.Generic;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Generation;
using CultistOfCthulhu.Items;
using CultistOfCthulhu.Player;
using CultistOfCthulhu.Sigils;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Rooms;

/// <summary>
/// What is actually IN a non-combat room.
///
/// Until this existed, reward, shop, shrine and secret rooms announced their own name to
/// the console and were otherwise empty rectangles — the generator placed them correctly
/// and there was nothing to find. That made every route choice on a floor cosmetic, which
/// is the one thing docs/06's whole architecture exists to prevent.
///
/// Populated once per room, on first entry, from a seed derived from the room's node id.
/// Deriving from the node id rather than drawing from a running stream means walking back
/// into a room cannot re-roll it and the contents of room 7 do not depend on whether the
/// player visited room 4 first (docs/06 §7).
///
/// Owns the drawing and the interaction prompt as well as the state. One node for the
/// whole layer rather than a node per pedestal: a room holds a handful of these, and they
/// need no independent scene presence.
/// </summary>
public sealed partial class RoomContent : Node2D
{
    public PlayerController Player = null!;
    public PickupManager Pickups = null!;
    public UI.ReverieScreen? Reverie;

    /// <summary>Floor index, for loot tiers and shop prices.</summary>
    public int FloorIndex = 1;

    private readonly List<Interactable> _items = new();
    private readonly HashSet<int> _populated = new();
    private int _nextGroup;

    /// <summary>The bench's reroll price rises within a floor (docs/08 §2.2).</summary>
    private int _rerollCost = 50;

    private Interactable? _focus;
    private string _flash = "";
    private float _flashAge;

    /// <summary>Where this room's furniture is laid out from — a guaranteed-standable point,
    /// not necessarily the geometric centre.</summary>
    private Vector2 _centre;

    public IReadOnlyList<Interactable> Items => _items;
    public bool HasContent => _items.Count > 0;

    // ---------------------------------------------------------------- Population

    /// <summary>
    /// Discard the previous room's furniture and lay out this one. Rooms are visited one
    /// at a time.
    ///
    /// Takes an explicit <paramref name="centre"/> rather than deriving one from the
    /// interior rect, because a room's geometric centre may be inside an authored block.
    /// Non-combat templates carry no obstacles today, so this costs nothing now and stops
    /// the first shop template that gains a counter from putting its stock inside it.
    /// </summary>
    public void EnterRoom(PlacedRoom room, Rect2 interior, Vector2 centre, Rng roomRng)
    {
        _items.Clear();
        _focus = null;
        _centre = centre;

        // Populate once per room per run. Re-rolling on re-entry would turn a reward room
        // into an infinite sigil dispenser, which is the most obvious possible exploit and
        // the one a player finds by accident within a minute of walking back through a door.
        if (!_populated.Add(room.NodeId)) return;

        switch (room.Role)
        {
            case RoomRole.Reward: PopulateReward(interior, roomRng); break;
            case RoomRole.Shop: PopulateShop(interior, roomRng); break;
            case RoomRole.Shrine: PopulateShrine(interior, roomRng); break;
            case RoomRole.Secret: PopulateSecret(interior, roomRng); break;
            case RoomRole.Connector: PopulateConnector(interior, roomRng); break;
        }
    }

    /// <summary>
    /// docs/08 §3 — one guaranteed reward room per floor, offering a CHOICE OF TWO sigils.
    ///
    /// Two rather than one is the whole design of the room: it converts luck into a
    /// decision. At Corruption 3+ a third, higher-tier option appears that costs another
    /// point to take, which is the game's central trade stated in its shortest form.
    /// </summary>
    private void PopulateReward(Rect2 interior, Rng rng)
    {
        int group = _nextGroup++;
        var taken = new List<SigilData>();
        var offers = new List<SigilData>();

        for (int i = 0; i < 2; i++)
        {
            SigilData? s = SigilPool.Draw(FloorIndex, Player.Corruption, rng, taken);
            if (s is null) continue;
            taken.Add(s);
            offers.Add(s);
        }

        bool blasphemous = Player.Corruption >= 3f;
        if (blasphemous)
        {
            // Rolled at an inflated Corruption so it genuinely reads a tier higher than the
            // two beside it; an "extra option" that draws from the same table is not an
            // option, it is a third coin flip.
            SigilData? s = SigilPool.Draw(FloorIndex, Player.Corruption + 3f, rng, taken);
            if (s is not null) { taken.Add(s); offers.Add(s); }
        }

        Vector2 centre = _centre;
        float spacing = 78f;
        float startX = centre.X - spacing * (offers.Count - 1) * 0.5f;

        for (int i = 0; i < offers.Count; i++)
        {
            bool isThird = blasphemous && i == offers.Count - 1;
            _items.Add(new Interactable
            {
                Kind = InteractableKind.SigilOffer,
                Position = new Vector2(startX + i * spacing, centre.Y),
                GroupId = group,
                Sigil = offers[i],
                Title = $"Take {offers[i].DisplayName} [{offers[i].Tier}]",
                Detail = offers[i].RulesText,
                CorruptionCost = isThird ? 1f : 0f,
                Tint = isThird ? new Color("B0122A") : TierTint(offers[i].Tier),
            });
        }
    }

    /// <summary>
    /// Gaunt's stall (docs/08 §2.1): two sigils, three Inscription offers, consumables, a
    /// reroll, and the Dissolution Bowl.
    /// </summary>
    private void PopulateShop(Rect2 interior, Rng rng)
    {
        Vector2 c = _centre;
        float priceMult = Player.Circle.Effects.ShopPriceMultiplier;
        float floorScale = InscriptionData.FloorScale(FloorIndex);

        // --- Sigils, 2 slots, 80-260 gold.
        var taken = new List<SigilData>();
        for (int i = 0; i < 2; i++)
        {
            SigilData? s = SigilPool.Draw(FloorIndex, Player.Corruption, rng, taken);
            if (s is null) continue;
            taken.Add(s);

            int price = Mathf.RoundToInt(Mathf.Lerp(80f, 260f, ((int)s.Tier) / 4f) * floorScale * priceMult);
            _items.Add(new Interactable
            {
                Kind = InteractableKind.ShopItem,
                Position = c + new Vector2(-120f + i * 70f, -70f),
                Sigil = s,
                GoldCost = price,
                Title = $"Buy {s.DisplayName} [{s.Tier}]",
                Detail = s.RulesText,
                Tint = TierTint(s.Tier),
            });
        }

        // --- The Inscription Bench, 3 offers.
        List<InscriptionData> offers = InscriptionPool.DrawOffers(FloorIndex, rng);
        for (int i = 0; i < offers.Count; i++)
        {
            _items.Add(BenchOffer(offers[i], c + new Vector2(-120f + i * 80f, 10f), priceMult));
        }

        _items.Add(new Interactable
        {
            Kind = InteractableKind.Reroll,
            Position = c + new Vector2(130f, 10f),
            GoldCost = _rerollCost,
            Title = "Reroll the bench",
            Detail = "New offers. The price rises each time.",
            Tint = new Color("8B8578"),
        });

        // --- Consumables (docs/08 §2.1 slot 6).
        AddConsumable(ConsumableKind.Ammo, "Ammunition", 45, c + new Vector2(-120f, 76f), priceMult, floorScale);
        AddConsumable(ConsumableKind.Candle, "Sanity Candle", 35, c + new Vector2(-50f, 76f), priceMult, floorScale);
        AddConsumable(ConsumableKind.Key, "Ossuary Key", 60, c + new Vector2(20f, 76f), priceMult, floorScale);
        AddConsumable(ConsumableKind.Armour, "Armour", 90, c + new Vector2(90f, 76f), priceMult, floorScale);
        AddConsumable(ConsumableKind.Heart, "Heart", 140, c + new Vector2(160f, 76f), priceMult, floorScale);

        // --- The Dissolution Bowl. Not a stock slot; always available (docs/04 §6).
        _items.Add(new Interactable
        {
            Kind = InteractableKind.DissolutionBowl,
            Position = c + new Vector2(130f, -70f),
            Title = "Dissolve a Reliquary sigil",
            Detail = "Pays 20 x cells x tier. Equipped sigils must be removed in Reverie first.",
            Tint = new Color("7FE0D4"),
        });
    }

    private Interactable BenchOffer(InscriptionData ins, Vector2 at, float priceMult) => new()
    {
        Kind = InteractableKind.Inscription,
        Position = at,
        Inscription = ins,
        GoldCost = ins.CostAt(FloorIndex, priceMult),
        CorruptionCost = ins.CorruptionOnApply,
        Title = $"Etch {ins.DisplayName}",
        Detail = ins.RulesText,
        Tint = ins.Tier switch
        {
            InscriptionTier.Lesser => new Color("5A7FB0"),
            InscriptionTier.Greater => new Color("B08A3E"),
            _ => new Color("B0122A"),
        },
    };

    private void AddConsumable(ConsumableKind kind, string name, int baseCost, Vector2 at,
                               float priceMult, float floorScale)
    {
        _items.Add(new Interactable
        {
            Kind = InteractableKind.ShopItem,
            Position = at,
            Consumable = kind,
            Amount = 1,
            GoldCost = Mathf.RoundToInt(baseCost * floorScale * priceMult),
            Title = $"Buy {name}",
            Tint = PickupManager.ColourFor(kind switch
            {
                ConsumableKind.Ammo => PickupKind.Ammo,
                ConsumableKind.Candle => PickupKind.SanityCandle,
                ConsumableKind.Key => PickupKind.Key,
                ConsumableKind.Armour => PickupKind.Armour,
                _ => PickupKind.Heart,
            }),
        });
    }

    /// <summary>
    /// docs/08 §5. One clearly-labelled trade, cost stated before commitment.
    ///
    /// The four implemented here are the ones whose cost and reward the M2 build can
    /// actually express. The Cleansing Pool, Bargainer's Table and Mirror of Yith all
    /// depend on systems scheduled later; offering them as no-ops would be worse than not
    /// offering them, because a shrine that lies about its trade poisons the one rule the
    /// whole section rests on.
    /// </summary>
    private void PopulateShrine(Rect2 interior, Rng rng)
    {
        Vector2 c = _centre;
        var kind = (ShrineKind)rng.NextInt(0, 4);

        var it = new Interactable
        {
            Kind = InteractableKind.Shrine,
            Position = c,
            Shrine = kind,
            Radius = 30f,
            Tint = new Color("9B5DB0"),
        };

        switch (kind)
        {
            case ShrineKind.BlackFont:
                // Rolled NOW and shown, not rolled on activation. "+1 to +3, revealed
                // before you commit" is the version §5 asks for; rolling at the moment of
                // use would make the stated cost a lie.
                it.CorruptionCost = rng.NextInt(1, 4);
                it.Title = "Drink from the Black Font";
                it.Detail = "A random A or S sigil.";
                break;

            case ShrineKind.WeighingStone:
                it.GoldCost = Mathf.Max(1, Player.Gold / 2);
                it.Title = "Pay the Weighing Stone";
                it.Detail = "Half your gold, for the best sigil this floor can offer.";
                break;

            case ShrineKind.AltarOfNodens:
                it.Title = "Kneel at the Altar of Nodens";
                it.Detail = "One heart container, permanently this run, for +40 maximum Sanity.";
                break;

            default:
                it.Title = "Read the Ledger Stone";
                it.Detail = "15 Sanity. Reveals the whole floor.";
                break;
        }

        _items.Add(it);
    }

    /// <summary>A secret room's payoff: a free chest and a candle. No key, no cost — the
    /// finding was the cost.</summary>
    private void PopulateSecret(Rect2 interior, Rng rng)
    {
        Vector2 c = _centre;
        _items.Add(MakeChest(c, tier: 2, keyCost: 0, rng));
        Pickups.Spawn(PickupKind.SanityCandle, c + new Vector2(0f, 34f), Tune.SanityCandleValue, rng);
    }

    /// <summary>docs/08 §1.3 — one guaranteed key chest per floor, in a connector.</summary>
    private void PopulateConnector(Rect2 interior, Rng rng)
    {
        if (_keyChestPlaced) return;
        _keyChestPlaced = true;

        Vector2 c = _centre;
        _items.Add(new Interactable
        {
            Kind = InteractableKind.Chest,
            Position = c,
            Amount = 1,
            Consumable = ConsumableKind.Key,
            Title = "Open the strongbox",
            Detail = "An Ossuary Key.",
            Tint = new Color("E8E1D5"),
        });
    }

    private bool _keyChestPlaced;

    /// <summary>docs/08 §4 — chests are tiered, and everything above Rust wants a key.</summary>
    private Interactable MakeChest(Vector2 at, int tier, int keyCost, Rng rng)
    {
        var t = (SigilTier)Mathf.Clamp(tier, 0, 4);
        return new Interactable
        {
            Kind = InteractableKind.Chest,
            Position = at,
            KeyCost = keyCost,
            Amount = tier,
            Title = $"Open the {ChestName(t)} chest",
            Detail = $"A {t}-tier sigil, or its weight in gold.",
            Tint = TierTint(t),
        };
    }

    private static string ChestName(SigilTier t) => t switch
    {
        SigilTier.D => "rust",
        SigilTier.C => "brass",
        SigilTier.B => "silver",
        SigilTier.A => "gilt",
        _ => "obsidian",
    };

    private static Color TierTint(SigilTier t) => t switch
    {
        SigilTier.D => new Color("6B7280"),
        SigilTier.C => new Color("4E8C7A"),
        SigilTier.B => new Color("5A7FB0"),
        SigilTier.A => new Color("B08A3E"),
        _ => new Color("9B5DB0"),
    };

    /// <summary>Drop a chest into a cleared combat room (docs/08 §4).</summary>
    public void AddChest(Vector2 at, int tier, int keyCost, Rng rng) =>
        _items.Add(MakeChest(at, tier, keyCost, rng));

    // ---------------------------------------------------------------- Tick

    public override void _PhysicsProcess(double delta)
    {
        _flashAge += (float)delta;

        _focus = null;
        float best = float.MaxValue;
        foreach (Interactable it in _items)
        {
            if (it.Consumed) continue;
            float d = it.Position.DistanceSquaredTo(Player.GlobalPosition);
            if (d > it.Radius * it.Radius || d >= best) continue;
            best = d;
            _focus = it;
        }

        if (_focus is not null && Input.IsActionJustPressed("interact")) Activate(_focus);

        QueueRedraw();
    }

    /// <summary>
    /// Take the first thing in this room the player can pay for. Used by the autorun
    /// harness, which has no hands.
    ///
    /// It goes through <see cref="Activate"/> rather than granting anything directly, so
    /// the harness exercises the real acquisition path — payment, group consumption, the
    /// Reliquary — instead of a shortcut that would prove nothing about it.
    /// </summary>
    public bool DebugTakeSomething()
    {
        foreach (Interactable it in _items)
        {
            if (it.Consumed) continue;
            if (it.KeyCost > Player.Keys || it.GoldCost > Player.Gold) continue;

            Activate(it);
            return true;
        }
        return false;
    }

    private void Flash(string text)
    {
        _flash = text;
        _flashAge = 0f;
        GD.Print($"[room] {text}");
    }

    // ---------------------------------------------------------------- Activation

    private void Activate(Interactable it)
    {
        if (it.KeyCost > 0 && Player.Keys < it.KeyCost) { Flash($"Needs {it.KeyCost} key(s)."); return; }
        if (it.GoldCost > 0 && Player.Gold < it.GoldCost) { Flash($"Needs {it.GoldCost} gold."); return; }

        switch (it.Kind)
        {
            case InteractableKind.SigilOffer: TakeSigil(it); break;
            case InteractableKind.Chest: OpenChest(it); break;
            case InteractableKind.Shrine: UseShrine(it); break;
            case InteractableKind.ShopItem: Buy(it); break;
            case InteractableKind.Inscription: Etch(it); break;
            case InteractableKind.DissolutionBowl: Dissolve(it); break;
            case InteractableKind.Reroll: Reroll(it); break;
        }
    }

    private bool Pay(Interactable it)
    {
        if (it.KeyCost > 0 && !Player.TrySpendKeys(it.KeyCost)) return false;
        if (it.GoldCost > 0 && !Player.TrySpendGold(it.GoldCost)) return false;
        if (it.CorruptionCost > 0f) Player.AddCorruption(it.CorruptionCost);
        return true;
    }

    /// <summary>Consume this item, and everything else in its group — that is what makes a
    /// reward room a choice rather than a shelf.</summary>
    private void ConsumeGroup(Interactable it)
    {
        it.Consumed = true;
        if (it.GroupId < 0) return;
        foreach (Interactable other in _items) if (other.GroupId == it.GroupId) other.Consumed = true;
    }

    private void GrantSigil(SigilData s)
    {
        // Straight into the Reliquary and onto the Reverie cursor. It is deliberately NOT
        // auto-placed: docs/04 §1 says every new sigil is a replacement decision, and
        // auto-placing takes that decision away at the exact moment it is most interesting.
        if (!Player.Circle.AddToReliquary(s))
        {
            Flash($"Reliquary is full — {s.DisplayName} could not be carried.");
            return;
        }
        if (Reverie is not null) Reverie.PendingOffer = s;
        Flash($"{s.DisplayName} taken. TAB to inscribe it.");
    }

    private void TakeSigil(Interactable it)
    {
        if (it.Sigil is null || !Pay(it)) return;
        ConsumeGroup(it);
        GrantSigil(it.Sigil);
    }

    private void OpenChest(Interactable it)
    {
        if (!Pay(it)) return;
        it.Consumed = true;

        var rng = Hash.Derive(GameRoot.Instance.RunSeed, "chest", Mathf.RoundToInt(it.Position.X + it.Position.Y));

        if (it.Consumable == ConsumableKind.Key)
        {
            Player.AddKeys(it.Amount);
            Flash($"+{it.Amount} key.");
            return;
        }

        SigilData? s = SigilPool.Draw(FloorIndex, Player.Corruption, rng, null);
        if (s is not null) { GrantSigil(s); return; }

        int gold = rng.NextInt(40, 81);
        Player.AddGold(gold);
        Flash($"+{gold} gold.");
    }

    private void UseShrine(Interactable it)
    {
        switch (it.Shrine)
        {
            case ShrineKind.BlackFont:
            {
                if (!Pay(it)) return;
                var rng = Hash.Derive(GameRoot.Instance.RunSeed, "black_font");
                // Corruption was already charged, so draw at the POST-payment level: the
                // shrine's whole pitch is that the price buys the better roll.
                SigilData? s = SigilPool.Draw(FloorIndex, Player.Corruption + 4f, rng, null);
                it.Consumed = true;
                if (s is not null) GrantSigil(s);
                break;
            }

            case ShrineKind.WeighingStone:
            {
                int half = Mathf.Max(1, Player.Gold / 2);
                if (!Player.TrySpendGold(half)) { Flash("Nothing to weigh."); return; }
                var rng = Hash.Derive(GameRoot.Instance.RunSeed, "weighing_stone");
                SigilData? s = SigilPool.Draw(FloorIndex, Player.Corruption + 6f, rng, null);
                it.Consumed = true;
                if (s is not null) GrantSigil(s);
                break;
            }

            case ShrineKind.AltarOfNodens:
            {
                if (Player.MaxHearts <= 1f) { Flash("You have nothing left to give."); return; }
                Player.SpendHeartContainer();
                Player.GrantMaxSanity(40f);
                it.Consumed = true;
                Flash("A container for forty. +40 maximum Sanity.");
                break;
            }

            default:
                if (!Player.Sanity.TrySpend(15f)) { Flash("Not enough Sanity."); return; }
                it.Consumed = true;
                RevealFloor = true;
                Flash("The floor is revealed.");
                break;
        }
    }

    /// <summary>Set by the Ledger Stone. Read by the minimap.</summary>
    public bool RevealFloor { get; private set; }

    private void Buy(Interactable it)
    {
        if (it.Sigil is not null)
        {
            if (!Pay(it)) return;
            it.Consumed = true;
            GrantSigil(it.Sigil);
            return;
        }

        if (!Pay(it)) return;
        it.Consumed = true;

        var rng = Hash.Derive(GameRoot.Instance.RunSeed, "shop_purchase");
        switch (it.Consumable)
        {
            case ConsumableKind.Ammo:
                Pickups.Spawn(PickupKind.Ammo, Player.GlobalPosition, 1, rng);
                Flash("Ammunition.");
                break;
            case ConsumableKind.Candle:
                Pickups.Spawn(PickupKind.SanityCandle, Player.GlobalPosition, Tune.SanityCandleValue, rng);
                Flash("A candle.");
                break;
            case ConsumableKind.Key:
                Player.AddKeys(1);
                // docs/08 §1.1 — keys are purchasable at a DELIBERATELY BAD rate that gets
                // worse. The two currencies must be convertible; conversion must hurt, or
                // gold silently becomes the only currency.
                it.GoldCost += 15;
                it.Consumed = false;
                it.Title = "Buy Ossuary Key";
                Flash("+1 key. The next one costs more.");
                break;
            case ConsumableKind.Armour:
                Pickups.Spawn(PickupKind.Armour, Player.GlobalPosition, 1, rng);
                Flash("Armour.");
                break;
            default:
                Pickups.Spawn(PickupKind.Heart, Player.GlobalPosition, 1, rng);
                Flash("A heart.");
                break;
        }
    }

    private void Etch(Interactable it)
    {
        if (it.Inscription is null) return;
        if (Player.Weapons.Count == 0) { Flash("No weapon to etch."); return; }

        Weapon w = Player.Weapons.Active;

        string? reject = w.RejectReason(it.Inscription);
        if (reject is not null) { Flash(reject); return; }
        if (!w.HasFreeSlot)
        {
            Flash($"{w.Data.DisplayName} has no free slot ({w.Inscriptions.Count}/{w.InscriptionSlots}). " +
                  "Q swaps weapon.");
            return;
        }

        if (!Pay(it)) return;
        it.Consumed = true;
        w.AddInscription(it.Inscription);
        Flash($"{it.Inscription.DisplayName} etched onto {w.Data.DisplayName}.");
    }

    private void Dissolve(Interactable it)
    {
        IReadOnlyList<SigilData> reliquary = Player.Circle.Reliquary;
        if (reliquary.Count == 0) { Flash("The Reliquary is empty."); return; }

        SigilData s = reliquary[0];
        int gold = Player.Circle.Dissolve(s);
        Player.AddGold(gold);
        Flash($"{s.DisplayName} dissolved for {gold} gold.");
    }

    private void Reroll(Interactable it)
    {
        if (!Player.TrySpendGold(it.GoldCost)) return;

        _rerollCost += 25;
        var rng = Hash.Derive(GameRoot.Instance.RunSeed, "bench_reroll", _rerollCost);

        // Replace the bench offers in place, keeping their positions so the stall does not
        // visibly rearrange itself.
        var positions = new List<Vector2>();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Kind != InteractableKind.Inscription) continue;
            positions.Add(_items[i].Position);
            _items.RemoveAt(i);
        }
        positions.Reverse();

        float priceMult = Player.Circle.Effects.ShopPriceMultiplier;
        List<InscriptionData> offers = InscriptionPool.DrawOffers(FloorIndex, rng, positions.Count);
        for (int i = 0; i < offers.Count && i < positions.Count; i++)
            _items.Add(BenchOffer(offers[i], positions[i], priceMult));

        it.GoldCost = _rerollCost;
        it.Title = "Reroll the bench";
        Flash($"Rerolled. Next reroll {_rerollCost} gold.");
    }

    // ---------------------------------------------------------------- Draw

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;

        foreach (Interactable it in _items)
        {
            if (it.Consumed) continue;

            bool focused = ReferenceEquals(it, _focus);
            float r = it.Kind == InteractableKind.Chest ? 9f : 7f;

            DrawCircle(it.Position, r + 5f, it.Tint with { A = focused ? 0.35f : 0.15f });
            DrawCircle(it.Position, r, it.Tint);
            if (focused) DrawArc(it.Position, r + 7f, 0, Mathf.Tau, 20, Colors.White, 1.2f);
        }

        if (_focus is null) return;

        // The prompt. Everything about the cost is on this line, because docs/08 §5's rule
        // — state the exact cost before commitment — is only kept if the player can read it
        // without opening anything.
        Vector2 at = _focus.Position + new Vector2(0f, -26f);
        string prompt = _focus.Prompt();
        DrawString(font, at - new Vector2(prompt.Length * 2.4f, 0f), prompt,
                   HorizontalAlignment.Left, -1, 10, Colors.White);

        if (_focus.Detail.Length > 0)
        {
            DrawString(font, at + new Vector2(-_focus.Detail.Length * 1.9f, 11f), _focus.Detail,
                       HorizontalAlignment.Left, -1, 8, new Color("B8C4D0"));
        }

        if (_flash.Length > 0 && _flashAge < 3f)
        {
            DrawString(font, at + new Vector2(-_flash.Length * 2.2f, -14f), _flash,
                       HorizontalAlignment.Left, -1, 9, new Color("FFE066"));
        }
    }

    /// <summary>
    /// Forget this floor's furniture. Called on every floor transition.
    ///
    /// The reroll price and the key-chest flag reset because both are per-FLOOR promises:
    /// docs/08 §2.2 raises the reroll price "per reroll this floor", and §1.3 guarantees
    /// one connector key chest per floor. Carrying either across a descent would quietly
    /// make floor 2 stingier than floor 1 for no stated reason.
    /// </summary>
    public void ResetForFloor()
    {
        _items.Clear();
        _populated.Clear();
        _focus = null;
        _keyChestPlaced = false;
        _rerollCost = 50;
        RevealFloor = false;
    }
}
