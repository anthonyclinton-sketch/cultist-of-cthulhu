using System.Collections.Generic;
using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Sigils;

/// <summary>A sigil placed on the circle, with its fitting transform baked in.</summary>
public sealed class PlacedSigil
{
    public SigilData Data = null!;
    public Vector2I Origin;
    public int Rotation;
    public bool Mirrored;
    /// <summary>Absolute grid cells this occupies. Cached — recomputed only on placement.</summary>
    public Vector2I[] Cells = System.Array.Empty<Vector2I>();
    /// <summary>True for the Heart, which cannot be removed (docs/04 §2.3).</summary>
    public bool Locked;

    public Vector2I Facing => SigilShape.Facing(Rotation, Mirrored);
}

/// <summary>One firing adjacency, named for the tooltip and the arc drawn between tiles.</summary>
public readonly struct Synergy
{
    public readonly int FromIndex;
    public readonly int ToIndex;
    public readonly SigilTag Tag;
    public readonly string Name;

    public Synergy(int from, int to, SigilTag tag, string name)
    {
        FromIndex = from;
        ToIndex = to;
        Tag = tag;
        Name = name;
    }
}

/// <summary>
/// docs/04 — the Sigil Circle. A 7x7 grid with the corners cut, a locked Heart at the
/// centre, three ley lines, and adjacency synergies.
///
/// This is the game's signature system and the thing that makes loot a decision rather
/// than a roll: space is finite, so every new sigil is a replacement decision (§1). All
/// of the value is in the constraints, so all of them are enforced here rather than in the
/// UI — the Reverie screen asks this class whether a placement is legal and renders the
/// answer. A rule enforced only by the screen is a rule that stops existing the moment
/// anything else places a sigil, and reward rooms, chests, shops and shrines all do.
///
/// Pure logic, no Node, no rendering. It is constructed once per run and survives floors.
/// </summary>
public sealed class SigilCircle
{
    public const int Size = 7;
    public const int ReliquaryCapacity = 6;   // docs/04 §6
    private const int MaxCountedSynergies = 6;   // §8.3

    /// <summary>
    /// The playable mask: 7x7 with the corners cut, giving the rough octagon in §2.1.
    /// 41 usable cells, of which the Heart takes one — so 40 for the player.
    /// </summary>
    private static readonly bool[,] Usable = BuildUsable();

    private static bool[,] BuildUsable()
    {
        var u = new bool[Size, Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // Cut the 2x2 block at each corner down to the octagon in §2.1: the top and
                // bottom rows are 3 wide, the second and sixth are 5, the middle three are 7.
                int inset = y == 0 || y == Size - 1 ? 2 : y == 1 || y == Size - 2 ? 1 : 0;
                u[x, y] = x >= inset && x < Size - inset;
            }
        }
        return u;
    }

    public static bool IsUsable(int x, int y) =>
        x >= 0 && y >= 0 && x < Size && y < Size && Usable[x, y];

    public static readonly Vector2I HeartCell = new(3, 3);

    // ---------------------------------------------------------------- Ley lines

    /// <summary>
    /// docs/04 §2.2 — three lines: column 3, row 3, and one diagonal. Their POSITIONS are
    /// fixed per character; their TYPES are rolled per run, which is what stops an
    /// optimal layout from being copied between runs.
    /// </summary>
    public LeyType VerticalLey { get; private set; } = LeyType.Blood;
    public LeyType HorizontalLey { get; private set; } = LeyType.Salt;
    public LeyType DiagonalLey { get; private set; } = LeyType.Ash;
    /// <summary>True when the diagonal runs top-left to bottom-right.</summary>
    public bool DiagonalIsMain { get; private set; } = true;

    /// <summary>
    /// Roll the run's three ley types, WITHOUT replacement.
    ///
    /// Drawing independently gave a 1-in-16 chance of two lines matching and 1-in-64 of all
    /// three, and the first seed tried came up Blood/Blood/Blood — a run in which the whole
    /// ley layer collapses to a single flat offensive multiplier and the ley cross stops
    /// being a decision. §2.2's stated purpose for rolling them at all is variety, and
    /// three of a kind is the one outcome that has none.
    /// </summary>
    public void RollLeyLines(Rng rng)
    {
        var pool = new[] { LeyType.Blood, LeyType.Salt, LeyType.Ash, LeyType.Gate };
        rng.Shuffle(new System.Span<LeyType>(pool));

        VerticalLey = pool[0];
        HorizontalLey = pool[1];
        DiagonalLey = pool[2];
        DiagonalIsMain = rng.NextFloat() < 0.5f;
    }

    public bool OnVerticalLey(Vector2I c) => c.X == 3;
    public bool OnHorizontalLey(Vector2I c) => c.Y == 3;
    public bool OnDiagonalLey(Vector2I c) => DiagonalIsMain ? c.X == c.Y : c.X + c.Y == Size - 1;

    /// <summary>Every ley type touching a cell. A cell on the cross gets both (§2.2).</summary>
    private void LeysAt(Vector2I c, out bool blood, out bool salt, out bool ash, out bool gate)
    {
        LeyType a = OnVerticalLey(c) ? VerticalLey : LeyType.None;
        LeyType b = OnHorizontalLey(c) ? HorizontalLey : LeyType.None;
        LeyType d = OnDiagonalLey(c) ? DiagonalLey : LeyType.None;

        blood = a == LeyType.Blood || b == LeyType.Blood || d == LeyType.Blood;
        salt = a == LeyType.Salt || b == LeyType.Salt || d == LeyType.Salt;
        ash = a == LeyType.Ash || b == LeyType.Ash || d == LeyType.Ash;
        gate = a == LeyType.Gate || b == LeyType.Gate || d == LeyType.Gate;
    }

    // ---------------------------------------------------------------- Contents

    private readonly List<PlacedSigil> _placed = new();
    private readonly List<SigilData> _reliquary = new();
    private readonly List<Synergy> _synergies = new();
    private readonly SigilEffects _effects = new();

    public IReadOnlyList<PlacedSigil> Placed => _placed;
    public IReadOnlyList<SigilData> Reliquary => _reliquary;
    public IReadOnlyList<Synergy> Synergies => _synergies;
    public SigilEffects Effects => _effects;

    public int UsedCells { get; private set; }
    public static int TotalCells => CountUsable();

    private static int CountUsable()
    {
        int n = 0;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (Usable[x, y]) n++;
        return n;
    }

    /// <summary>Occupancy, indexed by cell. -1 is empty.</summary>
    private readonly int[,] _occupant = new int[Size, Size];

    public SigilCircle()
    {
        ClearOccupancy();
        Resolve();
    }

    private void ClearOccupancy()
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                _occupant[x, y] = -1;
    }

    public int OccupantAt(int x, int y) => IsUsable(x, y) ? _occupant[x, y] : -1;

    /// <summary>
    /// Install the character's Heart Sigil (docs/04 §2.3): a fixed 1x1 at the centre that
    /// cannot be removed and always sits on every ley.
    /// </summary>
    public void SetHeart(SigilData heart)
    {
        for (int i = _placed.Count - 1; i >= 0; i--)
            if (_placed[i].Locked) RemoveAt(i, toReliquary: false);

        var p = new PlacedSigil
        {
            Data = heart,
            Origin = HeartCell,
            Cells = new[] { HeartCell },
            Locked = true,
        };
        _placed.Add(p);
        _occupant[HeartCell.X, HeartCell.Y] = _placed.Count - 1;
        Reindex();
        Resolve();
    }

    // ---------------------------------------------------------------- Placement

    /// <summary>
    /// Can this sigil sit here? Returns false with a reason the UI shows verbatim —
    /// docs/04 §7 requires invalid placements to say WHY ("overlaps Bone Lattice"), and a
    /// bool with the reason reconstructed in the UI drifts out of sync with the rule.
    /// </summary>
    public bool CanPlace(SigilData data, Vector2I origin, int rotation, bool mirrored, out string reason)
    {
        Vector2I[] cells = SigilShape.Cells(data.Shape, rotation, mirrored);

        foreach (Vector2I c in cells)
        {
            Vector2I at = origin + c;

            if (!IsUsable(at.X, at.Y))
            {
                reason = at.X < 0 || at.Y < 0 || at.X >= Size || at.Y >= Size
                    ? "outside the circle"
                    : "outside the circle (corner)";
                return false;
            }

            int occ = _occupant[at.X, at.Y];
            if (occ >= 0)
            {
                reason = $"overlaps {_placed[occ].Data.DisplayName}";
                return false;
            }
        }

        reason = "";
        return true;
    }

    public bool Place(SigilData data, Vector2I origin, int rotation, bool mirrored, out string reason)
    {
        if (!CanPlace(data, origin, rotation, mirrored, out reason)) return false;

        var p = new PlacedSigil
        {
            Data = data,
            Origin = origin,
            Rotation = rotation,
            Mirrored = mirrored,
            Cells = System.Array.ConvertAll(
                SigilShape.Cells(data.Shape, rotation, mirrored), c => origin + c),
        };

        _placed.Add(p);
        int index = _placed.Count - 1;
        foreach (Vector2I c in p.Cells) _occupant[c.X, c.Y] = index;

        _reliquary.Remove(data);
        Resolve();
        return true;
    }

    /// <summary>
    /// Find somewhere this will fit, scanning left-to-right and trying every rotation.
    ///
    /// Deliberately mediocre: it takes the first legal spot and never considers adjacency
    /// at all. docs/04 §7 asks for exactly that — an auto-arrange affordance for players
    /// who do not want the puzzle, which "never finds the best adjacency layout" so that
    /// engaging with the puzzle stays worth doing.
    /// </summary>
    public bool AutoPlace(SigilData data)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                for (int rot = 0; rot < 4; rot++)
                {
                    if (Place(data, new Vector2I(x, y), rot, false, out _)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Remove a placed sigil. It goes to the Reliquary so nothing is ever permanently lost
    /// (docs/04 §6) — that is what makes rearranging between floors free and what makes
    /// "loot is never dead" true.
    /// </summary>
    public bool RemoveAt(int index, bool toReliquary = true)
    {
        if (index < 0 || index >= _placed.Count) return false;
        if (_placed[index].Locked) return false;

        PlacedSigil p = _placed[index];
        foreach (Vector2I c in p.Cells) _occupant[c.X, c.Y] = -1;
        _placed.RemoveAt(index);

        if (toReliquary) AddToReliquary(p.Data);

        Reindex();
        Resolve();
        return true;
    }

    /// <summary>Occupancy stores list indices, so removing anything shifts the ones after
    /// it. Rebuilding the map is O(cells) and cannot go stale; patching it by hand is how
    /// an inventory grid ends up pointing at the wrong tile.</summary>
    private void Reindex()
    {
        ClearOccupancy();
        for (int i = 0; i < _placed.Count; i++)
            foreach (Vector2I c in _placed[i].Cells)
                _occupant[c.X, c.Y] = i;
    }

    /// <summary>Wipe the build. A new run gets a new Circle — carrying the previous run's
    /// layout over would make the first floor of run two silently easier than run one.</summary>
    public void ResetForRun()
    {
        _placed.Clear();
        _reliquary.Clear();
        ClearOccupancy();
        Resolve();
    }

    public bool AddToReliquary(SigilData data)
    {
        if (_reliquary.Count >= ReliquaryCapacity) return false;
        _reliquary.Add(data);
        return true;
    }

    /// <summary>docs/04 §6 — dissolution is Reliquary-only. An equipped sigil must be
    /// removed first, which keeps the decision inside Reverie where the diff is visible.</summary>
    public int Dissolve(SigilData data)
    {
        if (!_reliquary.Remove(data)) return 0;
        return data.DissolveValue;
    }

    // ---------------------------------------------------------------- Resolution

    /// <summary>
    /// Recompute synergies and the effect block. Called on every mutation and never per
    /// tick — the whole design of <see cref="SigilEffects"/> is that gameplay reads a flat
    /// block rather than walking this grid.
    /// </summary>
    public void Resolve()
    {
        _synergies.Clear();
        _effects.Reset();

        BuildSynergies();

        foreach (PlacedSigil p in _placed)
        {
            LeyMultipliers(p, out float off, out float def, out float trig);
            _effects.Add(p.Data, off, def, trig);
        }

        ApplySynergyBonuses();
        _effects.Finalise();

        UsedCells = 0;
        foreach (PlacedSigil p in _placed) UsedCells += p.Cells.Length;
        _effects.CellsUsed = UsedCells;
    }

    /// <summary>
    /// The ley multipliers for one tile. A sigil covering several cells of a ley gets the
    /// bonus ONCE (§2.2) — hence the flags rather than a running product — but a sigil on
    /// two crossing leys gets both.
    /// </summary>
    private void LeyMultipliers(PlacedSigil p, out float offensive, out float defensive, out float trigger)
    {
        bool blood = false, salt = false, ash = false;

        foreach (Vector2I c in p.Cells)
        {
            LeysAt(c, out bool b, out bool s, out bool a, out _);
            blood |= b;
            salt |= s;
            ash |= a;
        }

        // The Heart always sits on all leys (§2.3), and it sits at the crossing anyway, so
        // this is belt and braces rather than a special case.
        offensive = blood ? 1.5f : 1f;
        defensive = salt ? 1.5f : 1f;
        trigger = ash ? 2f : 1f;
    }

    /// <summary>
    /// Two sigils are adjacent when any of their cells share an EDGE. Diagonal contact does
    /// not count — the arcs drawn between tiles have to be readable, and a diagonal rule
    /// makes a dense corner light up with connections nobody planned.
    ///
    /// The Ley of the Gate is the exception: a sigil on a Gate ley counts as adjacent to
    /// every other sigil on that same ley regardless of distance (§2.2), which is what
    /// makes Gate the "build around it" ley rather than another multiplier.
    /// </summary>
    private void BuildSynergies()
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            for (int j = 0; j < _placed.Count; j++)
            {
                if (i == j) continue;
                if (!AreAdjacent(_placed[i], _placed[j])) continue;

                // i OFFERS, j WANTS.
                int shared = _placed[i].Data.Offers & _placed[j].Data.Wants;
                if (shared == 0) continue;

                for (int bit = 0; bit < 8; bit++)
                {
                    var tag = (SigilTag)(1 << bit);
                    if ((shared & (int)tag) == 0) continue;
                    _synergies.Add(new Synergy(i, j, tag, SynergyName(tag)));
                }
            }
        }
    }

    private bool AreAdjacent(PlacedSigil a, PlacedSigil b)
    {
        foreach (Vector2I ca in a.Cells)
        {
            foreach (Vector2I cb in b.Cells)
            {
                int dx = Mathf.Abs(ca.X - cb.X);
                int dy = Mathf.Abs(ca.Y - cb.Y);
                if (dx + dy == 1) return true;
            }
        }
        return SharesGateLey(a, b);
    }

    private bool SharesGateLey(PlacedSigil a, PlacedSigil b)
    {
        if (VerticalLey == LeyType.Gate && Touches(a, OnVerticalLey) && Touches(b, OnVerticalLey)) return true;
        if (HorizontalLey == LeyType.Gate && Touches(a, OnHorizontalLey) && Touches(b, OnHorizontalLey)) return true;
        if (DiagonalLey == LeyType.Gate && Touches(a, OnDiagonalLey) && Touches(b, OnDiagonalLey)) return true;
        return false;
    }

    private static bool Touches(PlacedSigil p, System.Func<Vector2I, bool> onLey)
    {
        foreach (Vector2I c in p.Cells) if (onLey(c)) return true;
        return false;
    }

    /// <summary>
    /// docs/04 §4.1 — synergies are named, and named per TAG rather than per item pair.
    /// That is the whole argument against Gungeon's model in §4.3: 8 tags generalise, 350
    /// hand-authored pairs do not, and a player who learns what TIDE does can predict a
    /// synergy they have never seen.
    /// </summary>
    public static string SynergyName(SigilTag tag) => tag switch
    {
        SigilTag.Flesh => "The Thousand Young",
        SigilTag.Tide => "The Tide Rises",
        SigilTag.Star => "Under Strange Stars",
        SigilTag.Void => "The Unmade Ground",
        SigilTag.Madness => "Chorus of the Mad",
        SigilTag.Iron => "Brine and Ember",
        SigilTag.Dream => "Devoted Flame",
        _ => "Blood Calls to Blood",
    };

    /// <summary>
    /// docs/04 §8.3 — the first six synergies pay their named bonus; every one beyond that
    /// pays a flat +3% damage.
    ///
    /// Both halves of that rule matter. The cap stops a dense late-run layout blowing up
    /// combinatorially, and the flat fallback stops the seventh synergy being worth
    /// literally nothing, which would quietly make half the tag vocabulary dead weight on
    /// exactly the builds that engaged hardest with the puzzle.
    /// </summary>
    private void ApplySynergyBonuses()
    {
        _effects.ActiveSynergies = _synergies.Count;

        for (int i = 0; i < _synergies.Count; i++)
        {
            if (i >= MaxCountedSynergies)
            {
                _effects.DamageMultiplier += 0.03f;
                continue;
            }

            switch (_synergies[i].Tag)
            {
                case SigilTag.Flesh: _effects.ArmourPerFloor += 1; break;
                case SigilTag.Tide: _effects.MoveSpeedMultiplier += 0.05f; break;
                case SigilTag.Star: _effects.MaxSanityBonus += 6f; break;
                case SigilTag.Void: _effects.HitSanityCostMultiplier *= 0.92f; break;
                case SigilTag.Madness: _effects.KillSanityBonus += 2f; break;
                case SigilTag.Iron: _effects.FireRateMultiplier += 0.05f; break;
                case SigilTag.Dream: _effects.PerfectRefundBonus += 0.15f; break;
                default: _effects.DamageMultiplier += 0.05f; break;
            }
        }
    }

    // ---------------------------------------------------------------- Diagnostics

    public string Summary()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"circle {UsedCells}/{TotalCells} cells, {_placed.Count} sigils, ");
        sb.Append($"{_synergies.Count} synergies, corruption {_effects.CorruptionFromSigils}");
        sb.Append($"\n  leys: vertical {VerticalLey}, horizontal {HorizontalLey}, " +
                  $"diagonal {DiagonalLey} ({(DiagonalIsMain ? "\\" : "/")})");
        foreach (string line in _effects.Describe()) sb.Append($"\n  {line}");
        return sb.ToString();
    }

    /// <summary>ASCII render, for the gate and for reading a build in a log.</summary>
    public string Render()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!Usable[x, y]) { sb.Append("  "); continue; }
                int occ = _occupant[x, y];
                char ch = occ < 0
                    ? (OnVerticalLey(new Vector2I(x, y)) || OnHorizontalLey(new Vector2I(x, y))
                       || OnDiagonalLey(new Vector2I(x, y)) ? '+' : '.')
                    : _placed[occ].Locked ? '@' : (char)('A' + (occ % 26));
                sb.Append(ch).Append(' ');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
