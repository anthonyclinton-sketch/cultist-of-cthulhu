using System.Collections.Generic;
using CultistOfCthulhu.Player;
using CultistOfCthulhu.Sigils;
using Godot;

namespace CultistOfCthulhu.UI;

/// <summary>
/// docs/04 §7 — the Reverie. Where the player actually plays the Sigil Circle.
///
/// Three rules from the spec are load-bearing and are enforced here rather than by
/// convention:
///
///   1. **It pauses the game entirely.** This is not a Dark Souls inventory. The whole
///      pitch of §1 is a puzzle solved in a calm screen BETWEEN fights, and a live
///      inventory would turn every placement into a time-pressured mistake.
///   2. **It only opens outside combat.** The owner gates this; §7 wants it closed while
///      doors are sealed, otherwise a player can rearrange their build mid-fight in
///      response to what is on screen, which is a strictly better decision made with
///      strictly more information than the design intends.
///   3. **Invalid placements say WHY.** <see cref="SigilCircle.CanPlace"/> returns the
///      reason and this screen prints it verbatim, so the rule and the message can never
///      drift apart.
///
/// Drawn with _Draw rather than assembled from Control nodes, matching <see cref="Hud"/>:
/// it is a grid of coloured cells with arcs between them, which the layout engine has
/// nothing useful to say about.
/// </summary>
public sealed partial class ReverieScreen : Control
{
    public PlayerController Player = null!;

    /// <summary>Set by the owner: false while any door is sealed (docs/04 §7).</summary>
    public bool CanOpen = true;

    /// <summary>
    /// A sigil the player has just been given and has not placed yet — a reward-room
    /// pickup, a chest drop. Held here so the screen opens with it already on the cursor,
    /// which is the moment the placement decision actually happens.
    /// </summary>
    public SigilData? PendingOffer;

    public bool IsOpen { get; private set; }

    private const int Cell = 22;
    private static readonly Vector2 GridOrigin = new(28, 74);

    private Vector2I _cursor = new(3, 3);
    private SigilData? _held;
    private int _rotation;
    private bool _mirrored;
    private string _message = "";
    private float _messageAge;
    private int _traySelection;

    private static readonly Color Backdrop = new(0.04f, 0.05f, 0.07f, 0.93f);
    private static readonly Color CellEmpty = new("22262F");
    private static readonly Color CellLey = new("35405A");
    private static readonly Color CellCut = new(0f, 0f, 0f, 0f);
    private static readonly Color CursorOk = new("7FE0D4");
    private static readonly Color CursorBad = new("B0122A");
    private static readonly Color HeartColour = new("C1440E");
    private static readonly Color Ink = new("E8E1D5");
    private static readonly Color InkDim = new("8B8578");

    /// <summary>Tile colours by tier, so a build reads at a glance without labels.</summary>
    private static Color TierColour(SigilTier t) => t switch
    {
        SigilTier.D => new Color("6B7280"),
        SigilTier.C => new Color("4E8C7A"),
        SigilTier.B => new Color("5A7FB0"),
        SigilTier.A => new Color("B08A3E"),
        _ => new Color("9B5DB0"),
    };

    public override void _Ready()
    {
        // The screen has to keep running while the tree is paused — it is the thing doing
        // the pausing.
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Open()
    {
        if (!CanOpen) return;

        IsOpen = true;
        Visible = true;
        GetTree().Paused = true;

        _held = PendingOffer;
        PendingOffer = null;
        _rotation = 0;
        _mirrored = false;
        Say(_held is not null ? $"Placing {_held.DisplayName}." : "Reverie.");
        QueueRedraw();
    }

    public void Close()
    {
        // Anything still on the cursor goes to the Reliquary rather than evaporating.
        // docs/04 §6 — nothing is ever permanently lost, and a sigil deleted by closing a
        // menu is the most annoying possible way to break that promise.
        if (_held is not null && !Player.Circle.AddToReliquary(_held))
            GD.Print($"[Reverie] Reliquary full — {_held.DisplayName} was left behind.");
        _held = null;

        IsOpen = false;
        Visible = false;
        GetTree().Paused = false;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void Say(string text)
    {
        _message = text;
        _messageAge = 0f;
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        _messageAge += (float)delta;
        QueueRedraw();
    }

    // ---------------------------------------------------------------- Input
    //
    // Read as raw keys rather than through the input map. Every gameplay action is bound
    // to something this screen needs for a different purpose — R is Recite, E is interact,
    // WASD is movement — and rebinding them for one screen would mean the bindings no
    // longer describe the game. A paused full-screen modal is the one place where reading
    // keys directly is the honest option.

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape or Key.Tab: Close(); break;

            case Key.W or Key.Up: MoveCursor(0, -1); break;
            case Key.S or Key.Down: MoveCursor(0, 1); break;
            case Key.A or Key.Left: MoveCursor(-1, 0); break;
            case Key.D or Key.Right: MoveCursor(1, 0); break;

            case Key.Q: _rotation = (_rotation + 3) % 4; QueueRedraw(); break;
            case Key.E: _rotation = (_rotation + 1) % 4; QueueRedraw(); break;
            case Key.F: _mirrored = !_mirrored; QueueRedraw(); break;

            case Key.Space or Key.Enter or Key.KpEnter: PlaceOrPick(); break;
            case Key.X: Lift(); break;
            case Key.Z: AutoArrange(); break;
            case Key.C: CycleTray(); break;

            default: break;
        }

        GetViewport().SetInputAsHandled();
    }

    private void MoveCursor(int dx, int dy)
    {
        _cursor = new Vector2I(
            Mathf.Clamp(_cursor.X + dx, 0, SigilCircle.Size - 1),
            Mathf.Clamp(_cursor.Y + dy, 0, SigilCircle.Size - 1));
        QueueRedraw();
    }

    private void CycleTray()
    {
        IReadOnlyList<SigilData> tray = Player.Circle.Reliquary;
        if (tray.Count == 0) { Say("Reliquary is empty."); return; }

        _traySelection = (_traySelection + 1) % tray.Count;
        Say($"Reliquary: {tray[_traySelection].DisplayName}");
    }

    /// <summary>
    /// One key does both halves of the verb. With a sigil on the cursor it places; with an
    /// empty cursor it takes the selected Reliquary tile onto the cursor. Two separate keys
    /// would leave a state where the obvious key does nothing.
    /// </summary>
    private void PlaceOrPick()
    {
        if (_held is null)
        {
            IReadOnlyList<SigilData> tray = Player.Circle.Reliquary;
            if (tray.Count == 0) { Say("Nothing to place. X lifts a sigil off the circle."); return; }

            _traySelection = Mathf.Clamp(_traySelection, 0, tray.Count - 1);
            _held = tray[_traySelection];
            _rotation = 0;
            _mirrored = false;
            Say($"Placing {_held.DisplayName}.");
            return;
        }

        if (!Player.Circle.Place(_held, _cursor, _rotation, _mirrored, out string reason))
        {
            Say($"Cannot place: {reason}.");
            return;
        }

        Say($"{_held.DisplayName} inscribed.");
        _held = null;
        Player.OnSigilsChanged();
    }

    private void Lift()
    {
        int occ = Player.Circle.OccupantAt(_cursor.X, _cursor.Y);
        if (occ < 0) { Say("Nothing here."); return; }

        PlacedSigil p = Player.Circle.Placed[occ];
        if (p.Locked) { Say($"{p.Data.DisplayName} is your Heart. It cannot be removed."); return; }

        string name = p.Data.DisplayName;
        if (!Player.Circle.RemoveAt(occ)) { Say("Could not lift that."); return; }

        Say($"{name} returned to the Reliquary.");
        Player.OnSigilsChanged();
    }

    private void AutoArrange()
    {
        // docs/04 §7 — deliberately mediocre, and it says so, so a player who uses it knows
        // they are trading the puzzle away rather than being quietly given the best layout.
        int placed = 0;
        while (Player.Circle.Reliquary.Count > 0)
        {
            SigilData next = Player.Circle.Reliquary[0];
            if (!Player.Circle.AutoPlace(next)) break;
            placed++;
        }
        Player.OnSigilsChanged();
        Say(placed == 0 ? "Nothing fits." : $"Auto-arranged {placed}. It found space, not synergy.");
    }

    // ---------------------------------------------------------------- Draw

    public override void _Draw()
    {
        if (!IsOpen || Player is null) return;

        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, 640, 360), Backdrop);

        DrawString(font, new Vector2(28, 32), "THE REVERIE", HorizontalAlignment.Left, -1, 16, Ink);
        DrawString(font, new Vector2(28, 50), LeyLine(), HorizontalAlignment.Left, -1, 9, InkDim);

        DrawGrid();
        DrawGhost();
        DrawPanel(font);
        DrawTray(font);

        DrawString(font, new Vector2(28, 348),
                   "WASD move  ·  SPACE place/take  ·  Q/E rotate  ·  F mirror  ·  X lift  ·  " +
                   "C cycle  ·  Z auto  ·  TAB close",
                   HorizontalAlignment.Left, -1, 8, InkDim);

        if (_message.Length > 0 && _messageAge < 4f)
            DrawString(font, new Vector2(28, 334), _message, HorizontalAlignment.Left, -1, 10, Ink);
    }

    private string LeyLine()
    {
        SigilCircle c = Player.Circle;
        return $"leys — vertical {c.VerticalLey}  ·  horizontal {c.HorizontalLey}  ·  diagonal {c.DiagonalLey}";
    }

    private void DrawGrid()
    {
        for (int y = 0; y < SigilCircle.Size; y++)
        {
            for (int x = 0; x < SigilCircle.Size; x++)
            {
                var at = new Vector2I(x, y);
                Rect2 r = CellRect(at);

                if (!SigilCircle.IsUsable(x, y)) { DrawRect(r, CellCut); continue; }

                int occ = Player.Circle.OccupantAt(x, y);
                Color fill;
                if (occ >= 0)
                {
                    PlacedSigil p = Player.Circle.Placed[occ];
                    fill = p.Locked ? HeartColour : TierColour(p.Data.Tier);
                }
                else
                {
                    fill = OnAnyLey(at) ? CellLey : CellEmpty;
                }

                DrawRect(r, fill);
                DrawRect(r, new Color(0, 0, 0, 0.35f), filled: false, width: 1f);

                // The facing arrow for a directional tile (docs/04 §3.2). Without it the
                // player has no way to tell which way a placed Watcher's Eye points, and the
                // whole orientation layer is invisible.
                if (occ >= 0)
                {
                    PlacedSigil p = Player.Circle.Placed[occ];
                    if (p.Data.Directional && p.Cells[0] == at)
                    {
                        Vector2 c = r.Position + r.Size * 0.5f;
                        Vector2 f = new Vector2(p.Facing.X, p.Facing.Y) * (Cell * 0.32f);
                        DrawLine(c, c + f, Ink, 1.5f);
                    }
                }
            }
        }

        // Synergy arcs. §4.1 asks for a visible line between synergising tiles, and it is
        // the only way the player can see a build working without reading a list.
        foreach (Synergy s in Player.Circle.Synergies)
        {
            Vector2 a = CentreOf(Player.Circle.Placed[s.FromIndex]);
            Vector2 b = CentreOf(Player.Circle.Placed[s.ToIndex]);
            DrawLine(a, b, new Color(1f, 0.88f, 0.4f, 0.55f), 1.5f);
        }
    }

    private bool OnAnyLey(Vector2I c) =>
        Player.Circle.OnVerticalLey(c) || Player.Circle.OnHorizontalLey(c) || Player.Circle.OnDiagonalLey(c);

    private Rect2 CellRect(Vector2I c) =>
        new(GridOrigin + new Vector2(c.X * Cell, c.Y * Cell), new Vector2(Cell - 2, Cell - 2));

    private Vector2 CentreOf(PlacedSigil p)
    {
        var sum = Vector2.Zero;
        foreach (Vector2I c in p.Cells) sum += CellRect(c).Position + CellRect(c).Size * 0.5f;
        return sum / p.Cells.Length;
    }

    /// <summary>The cursor, and the held tile previewed under it in green or red.</summary>
    private void DrawGhost()
    {
        if (_held is null)
        {
            DrawRect(CellRect(_cursor), CursorOk, filled: false, width: 2f);
            return;
        }

        bool ok = Player.Circle.CanPlace(_held, _cursor, _rotation, _mirrored, out string reason);
        Color tint = ok ? CursorOk : CursorBad;

        foreach (Vector2I c in SigilShape.Cells(_held.Shape, _rotation, _mirrored))
        {
            var at = _cursor + c;
            if (at.X < 0 || at.Y < 0 || at.X >= SigilCircle.Size || at.Y >= SigilCircle.Size) continue;
            DrawRect(CellRect(at), tint with { A = 0.35f });
            DrawRect(CellRect(at), tint, filled: false, width: 1.5f);
        }

        if (!ok && reason.Length > 0 && _held is not null)
            DrawString(ThemeDB.FallbackFont, new Vector2(28, 246), reason,
                       HorizontalAlignment.Left, -1, 9, CursorBad);
    }

    /// <summary>
    /// The live panel. docs/04 §7 calls for a diff that updates as you drag; this is the
    /// resolved state plus the held tile's own text, which is the same information for a
    /// keyboard cursor and is honest about what is currently true.
    /// </summary>
    private void DrawPanel(Font font)
    {
        const float x = 220f;
        float y = 78f;

        SigilCircle c = Player.Circle;
        SigilEffects e = c.Effects;

        DrawString(font, new Vector2(x, y), $"CELLS  {c.UsedCells} / {SigilCircle.TotalCells}",
                   HorizontalAlignment.Left, -1, 10, Ink);
        y += 14;
        DrawString(font, new Vector2(x, y), $"SYNERGIES ACTIVE ({e.ActiveSynergies})",
                   HorizontalAlignment.Left, -1, 10, Ink);
        y += 13;

        int shown = 0;
        var seen = new HashSet<string>();
        foreach (Synergy s in c.Synergies)
        {
            string label = $"{s.Name} ({s.Tag})";
            if (!seen.Add(label)) continue;
            if (shown++ >= 4) break;
            DrawString(font, new Vector2(x + 6, y), $"* {label}", HorizontalAlignment.Left, -1, 9,
                       new Color(1f, 0.88f, 0.4f));
            y += 11;
        }
        if (c.Synergies.Count == 0)
        {
            DrawString(font, new Vector2(x + 6, y), "none — sigils must share an edge",
                       HorizontalAlignment.Left, -1, 9, InkDim);
            y += 11;
        }

        y += 8;
        DrawString(font, new Vector2(x, y), "EFFECTS", HorizontalAlignment.Left, -1, 10, Ink);
        y += 13;
        foreach (string line in e.Describe())
        {
            DrawString(font, new Vector2(x + 6, y), line, HorizontalAlignment.Left, -1, 9, InkDim);
            y += 11;
            if (y > 268) break;
        }

        if (e.CorruptionFromSigils > 0)
        {
            DrawString(font, new Vector2(x, 278), $"CORRUPTION FROM SIGILS  {e.CorruptionFromSigils}",
                       HorizontalAlignment.Left, -1, 9, new Color("B0122A"));
        }

        if (_held is not null)
        {
            DrawString(font, new Vector2(28, 232), $"HOLDING  {_held.DisplayName}  [{_held.Tier}]",
                       HorizontalAlignment.Left, -1, 10, TierColour(_held.Tier));
            DrawString(font, new Vector2(28, 258), _held.RulesText, HorizontalAlignment.Left, 570, 9, InkDim);
        }
    }

    private void DrawTray(Font font)
    {
        IReadOnlyList<SigilData> tray = Player.Circle.Reliquary;
        DrawString(font, new Vector2(28, 296),
                   $"RELIQUARY  {tray.Count}/{SigilCircle.ReliquaryCapacity}",
                   HorizontalAlignment.Left, -1, 10, Ink);

        for (int i = 0; i < tray.Count; i++)
        {
            var r = new Rect2(28 + i * 96, 302, 92, 14);
            bool selected = i == _traySelection && _held is null;
            DrawRect(r, TierColour(tray[i].Tier) with { A = selected ? 0.9f : 0.4f });
            DrawString(font, r.Position + new Vector2(3, 11), tray[i].DisplayName,
                       HorizontalAlignment.Left, 88, 8, selected ? Colors.Black : Ink);
        }
    }
}
