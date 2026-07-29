using CultistOfCthulhu.Bullets;
using CultistOfCthulhu.Core;
using CultistOfCthulhu.Enemies;
using CultistOfCthulhu.Meta;
using CultistOfCthulhu.Weapons;
using Godot;

namespace CultistOfCthulhu.Player;

public enum BlinkPhase { None, Startup, Invulnerable, Recovery }

/// <summary>
/// docs/02 §1, §4.
///
/// Deliberately NOT physics-driven — no RigidBody2D, velocity assigned directly. Any
/// "weight" the character has is animation, never simulation (docs/02 §1.1).
///
/// Post-F4 (docs/01 Pillar I) Blink Step is FREE; the Sanity economy lives in Recitation,
/// Banish and Open the Eye. The frame data below is unchanged by that decision, because
/// the recovery tail and cooldown are now the ONLY brake on dodge spam.
/// </summary>
public sealed partial class PlayerController : CharacterBody2D
{
    public SanitySystem Sanity { get; } = new();
    public WeaponHolder Weapons { get; } = new();
    public AscensionController Ascension { get; } = new();
    public Telemetry? Telemetry { get; set; }

    /// <summary>
    /// docs/04 — the Sigil Circle. Owned by the player because it is the player's build:
    /// it survives floors, it is what the Reverie screen edits, and every one of its
    /// effects lands on something in this class or something this class publishes.
    /// </summary>
    public Sigils.SigilCircle Circle { get; } = new();

    /// <summary>docs/02 §7. The threshold EFFECTS are M3; this counter exists now so that
    /// Ascension's cost is actually recorded rather than silently dropped.</summary>
    public float Corruption { get; private set; }

    /// <summary>docs/02 §2 — each absorbs one hit of any size, including its Sanity cost.</summary>
    public int Armour { get; private set; }
    public int ArmourAbsorbed { get; private set; }

    /// <summary>No sink until the shop lands at M2; tracked so the economy simulation has
    /// real numbers to check against docs/08 §8 when it does.</summary>
    public int Gold { get; private set; }
    public int Keys { get; private set; }

    public int CandlesCollected { get; private set; }

    public Items.PickupManager? Pickups { get; set; }

    public BlinkPhase Phase { get; private set; } = BlinkPhase.None;
    public bool IsInvulnerable =>
        Ascension.IsAscended || Phase == BlinkPhase.Invulnerable || _damageIFrames > 0f;

    public Vector2 AimDirection { get; private set; } = Vector2.Right;

    /// <summary>docs/02 §2 — hearts, half-heart granularity. Hits are events, not chip damage.</summary>
    public float Hearts { get; private set; } = 3f;
    public float MaxHearts { get; private set; } = 3f;
    public bool IsDead => Hearts <= 0f;

    public int HitsTaken { get; private set; }

    /// <summary>M1 metric 3. Post-F4 this counts denied RELOADS and BANISHES — the dodge
    /// is free, so "could not afford to keep shooting" is the failure state now.</summary>
    public int DeniedSustainCount { get; private set; }

    /// <summary>Retained for Build B (the metered-dodge control arm). Zero unless
    /// Tune.SanityBlinkCost is flipped back to 18.</summary>
    public int DeniedBlinkCount { get; private set; }

    public BulletManager? EnemyBullets { get; set; }
    public BulletManager? PlayerBullets { get; set; }
    public EnemyManager? Enemies { get; set; }

    private Rng _rng = null!;
    private int _blinkFrame;
    private Vector2 _blinkVelocity;
    private float _blinkCooldown;
    private float _damageIFrames;
    private float _banishHoldTime;
    private bool _banishConsumed;
    private float _autoReloadDelay;
    private Vector2 _smoothedVelocity;
    private float _contactDamageCooldown;
    private float _banishCooldown;

    // Feel (docs/02 §8) — hit stop is applied by the arena, which owns the time scale.
    public float PendingHitStop { get; private set; }

    [Signal] public delegate void BanishedEventHandler();
    [Signal] public delegate void EyeOpenedEventHandler();
    [Signal] public delegate void AscendedEventHandler();
    [Signal] public delegate void DiedEventHandler();

    public override void _Ready()
    {
        _rng = Hash.Derive(GameRoot.Instance.RunSeed, "player");

        // The controller owns its own body. This used to be each scene's job, and
        // FloorRunner forgot — the player was fully simulated and completely invisible.
        // Owning it here makes that impossible to repeat.
        AddChild(new PlayerVisual { Name = "Visual", Controller = this, ZIndex = 20 });
    }

    /// <summary>Remaining post-damage invulnerability, for the 12Hz flash (docs/02 §2).</summary>
    public float DamageIFramesRemaining => _damageIFrames;

    // ================================================================ Sigils
    //
    // Everything below is the seam between docs/04 and the rest of the player. The rule
    // being kept: nothing in this class walks the Circle. It reads the flat effect block,
    // and it recomputes derived state exactly once, when the layout changes.

    /// <summary>Ascension's permanent max-Sanity debt, accumulated across the run.
    /// Tracked separately so that re-deriving Max from the Circle cannot refund it.</summary>
    private float _maxSanityPenalty;

    /// <summary>Max Sanity granted from outside the Circle — the Altar of Nodens, and later
    /// Unbroken Seals. Held here rather than pushed into SigilEffects because that block is
    /// rebuilt from scratch on every placement and would silently discard it.</summary>
    private float _bonusMaxSanity;

    public void GrantMaxSanity(float amount)
    {
        _bonusMaxSanity += amount;
        OnSigilsChanged();
    }

    /// <summary>Permanently give up a heart container for this run (docs/08 §5, Altar of
    /// Nodens). Floored at one, the same floor Ascension's debt rule respects.</summary>
    public void SpendHeartContainer()
    {
        MaxHearts = Mathf.Max(Tune.AscensionMinContainers, MaxHearts - 1f);
        Hearts = Mathf.Min(Hearts, MaxHearts);
    }

    /// <summary>Elder Sign — one lethal hit negated per room (docs/04 §5.3).</summary>
    private bool _lethalNegationSpent;

    /// <summary>Seconds since the player last spent Sanity, for the Deep One's Gill lull.</summary>
    private float _timeSinceSanitySpend;

    /// <summary>
    /// Re-derive everything the Circle governs. Call after any placement, removal or
    /// floor transition — never per tick.
    ///
    /// Max Sanity is recomputed from base + bonus − accumulated Ascension debt rather than
    /// adjusted incrementally. Adjusting it would mean every path that changes either
    /// input has to know about the other, and the failure mode is silent: a player who
    /// Ascends and then rearranges their Circle gets their permanent penalty refunded.
    /// </summary>
    public void OnSigilsChanged()
    {
        Sigils.SigilEffects e = Circle.Effects;

        float target = Mathf.Max(Tune.AscensionMaxSanityFloor,
                                 Tune.SanityMax + e.MaxSanityBonus + _bonusMaxSanity - _maxSanityPenalty);
        Sanity.SetMax(target);

        Ascension.DurationBonus = e.AscensionDurationBonus;
        Ascension.HeartCostMultiplier = e.AscensionHeartCostMultiplier;

        Weapons.PerfectRefundBonus = e.PerfectRefundBonus;
    }

    /// <summary>
    /// Outgoing damage right now, including the two bonuses that depend on player state
    /// and therefore cannot live on the weapon: the low-health bonus and Corruption
    /// scaling (docs/04 §5.1, §5.4).
    /// </summary>
    public float OutgoingDamageMultiplier
    {
        get
        {
            Sigils.SigilEffects e = Circle.Effects;
            bool lowHealth = Hearts <= MaxHearts * 0.5f;

            float m = e.DamageMultiplier;
            if (lowHealth) m += e.LowHealthDamageBonus;
            m += e.DamagePerCorruption * Corruption;

            // The ACTIVE weapon's inscriptions, because only the active weapon fires.
            // Sanguine Etching and Sovereign's Mark are per-weapon by design (docs/03 §3) —
            // an inscription is etched onto one gun, so it must not buff the other two.
            if (Weapons.Count > 0)
            {
                Weapon w = Weapons.Active;
                if (lowHealth) m += w.LowHealthDamageBonus;
                m += w.DamagePerCorruption * Corruption;
            }

            return m;
        }
    }

    /// <summary>Called by the room owner when a new room begins. Rearms the Elder Sign.</summary>
    public void OnRoomBegan() => _lethalNegationSpent = false;

    /// <summary>Start-of-floor sigil payouts (docs/04 §5.3, §5.5).</summary>
    public void OnFloorBegan()
    {
        Sigils.SigilEffects e = Circle.Effects;
        if (e.ArmourPerFloor > 0) Armour = Mathf.Min(Tune.MaxArmour, Armour + e.ArmourPerFloor);
        if (e.KeysPerFloor > 0) Keys += e.KeysPerFloor;
    }

    public void AddGold(int amount) => Gold += Mathf.Max(0, amount);
    public void AddKeys(int amount) => Keys += Mathf.Max(0, amount);
    public void AddCorruption(float amount) => Corruption += amount;

    /// <summary>Spend gold. Returns false and spends nothing if it cannot be paid.</summary>
    public bool TrySpendGold(int amount)
    {
        if (amount < 0 || Gold < amount) return false;
        Gold -= amount;
        return true;
    }

    public bool TrySpendKeys(int amount)
    {
        if (amount < 0 || Keys < amount) return false;
        Keys -= amount;
        return true;
    }

    public void GiveWeapon(WeaponData data) => Weapons.Add(data);

    /// <summary>Test hook: kill the player outright, with a pending hit stop set as the
    /// killing blow would leave it.</summary>
    public void DebugKill()
    {
        Hearts = 0f;
        PendingHitStop = Core.HitStop.PlayerDamaged;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Cleared BEFORE the death check, not after.
        //
        // It used to sit below the early return, so the value from the killing blow was
        // never cleared — a dead player published PendingHitStop = 0.06 forever, the scene
        // re-requested a hit stop every frame, and Engine.TimeScale locked at 0.05. The
        // game then ran at 1/20 speed indefinitely with a player who could not act, which
        // is indistinguishable from a freeze and was reported as one.
        //
        // Any state a dead player still PUBLISHES has to be reset before returning.
        PendingHitStop = 0f;

        if (IsDead) return;

        float dt = (float)delta;

        if (_blinkCooldown > 0f) _blinkCooldown -= dt;
        if (_damageIFrames > 0f) _damageIFrames -= dt;
        if (_contactDamageCooldown > 0f) _contactDamageCooldown -= dt;
        if (_banishCooldown > 0f) _banishCooldown -= dt;
        if (BanishPulse > 0f) BanishPulse = Mathf.Max(0f, BanishPulse - dt * 3.5f);
        // Decay slower than the fire rate, or the swing is invisible between attacks. At
        // 7/s the arc lasted 0.14s against the Kris's 0.33s cycle, so feedback was absent
        // more than half the time and flickered when it did appear. 3.2/s keeps the arc up
        // for most of the cycle, which is what makes melee readable as continuous action.
        if (MeleeSwing > 0f) MeleeSwing = Mathf.Max(0f, MeleeSwing - dt * 3.2f);

        _timeSinceLastKill += dt;
        if (_timeSinceLastKill > ChainWindow) _chainStep = 0;

        TickLullRegen(dt);

        // Trauma decays in REAL time. Decaying it by the scaled delta meant screen shake
        // persisted 20x longer through a hit stop, so the two effects compounded into what
        // looked like the game hanging and juddering rather than punching.
        float unscaled = dt / Mathf.Max(0.01f, (float)Engine.TimeScale);
        Trauma = Mathf.Max(0f, Trauma - unscaled * 2.6f);

        TickMotes(dt);

        UpdateAim();

        // Ascension trigger is polled, not signalled from the damage path. Sanity can
        // reach zero by being SPENT as easily as by being drained (a Banish at exactly 45
        // does it), and the two must behave identically — see SanitySystem.SetCurrent.
        if (!Ascension.IsAscended && Sanity.ConsumeAscensionTrigger()) BeginAscension();

        if (Ascension.IsAscended)
        {
            TickAscended(dt);
            MoveAndSlide();
            PublishToBulletManagers();
            CollectKillRewards();
            return;   // no Blink Step, no weapons, no incoming damage while Ascended
        }

        HandleBanishAndOpenEye(dt);
        HandleBlinkInput();
        HandleWeaponInput(dt);

        if (Phase != BlinkPhase.None) TickBlink();
        else ApplyWalkMovement(dt);

        MoveAndSlide();

        Weapons.Tick(dt);
        Sanity.Tick(dt);

        CollectKillRewards();
        CollectPickups();
        PublishToBulletManagers();
        ConsumeIncomingHits();
        ConsumeContactDamage();
    }

    /// <summary>
    /// Apply anything walked over this tick (docs/06 §6.3).
    ///
    /// The candle is the one that matters: it uses GainPiercing, so it is the ONLY source
    /// that can push Sanity back above the Lucid Ceiling (docs/02 §3.3.1). Everything else
    /// in the economy pushes down across a floor. Routing it through the normal
    /// ceiling-respecting path would silently make it a no-op late in a floor — exactly
    /// when it is supposed to matter most.
    /// </summary>
    private void CollectPickups()
    {
        if (Pickups is null) return;
        Pickups.PlayerPosition = GlobalPosition;

        if (Pickups.CollectedSanity > 0f)
        {
            Sanity.GainPiercing(Pickups.CollectedSanity);
            Telemetry?.NoteSanityIncome(Pickups.CollectedSanity);
            Telemetry?.NoteCandle();
            CandlesCollected++;
        }

        if (Pickups.CollectedHearts > 0f) Heal(Pickups.CollectedHearts);
        if (Pickups.CollectedArmour > 0) Armour = Mathf.Min(Tune.MaxArmour, Armour + Pickups.CollectedArmour);
        if (Pickups.CollectedGold > 0)
            Gold += Mathf.RoundToInt(Pickups.CollectedGold * Circle.Effects.GoldMultiplier);
        if (Pickups.CollectedKeys > 0) Keys += Pickups.CollectedKeys;

        if (Pickups.CollectedAmmo > 0)
        {
            // Percentage of max reserve rather than a flat count, so a heavy weapon is not
            // starved by the same pickup that fully refills a pistol (docs/03 §1.2).
            foreach (Weapon w in Weapons.Weapons)
                w.AddReserve(Mathf.Max(1, Mathf.RoundToInt(w.Data.TotalReserveRounds * 0.30f)));
        }
    }

    /// <summary>Total reserve across all weapons, as a fraction. Feeds the ammo pity
    /// counter in DropTable.</summary>
    public float TotalReserveFraction()
    {
        if (Weapons.Count == 0) return 1f;
        float sum = 0f;
        int counted = 0;
        foreach (Weapon w in Weapons.Weapons)
        {
            if (w.Data.IsBoundArm || w.Data.IsMelee || w.Data.SanityPerShot > 0f) continue;
            sum += w.ReserveFraction;
            counted++;
        }
        return counted == 0 ? 1f : sum / counted;
    }

    // ---------------------------------------------------------------- Feel (docs/02 §8)

    /// <summary>
    /// Sanity motes. docs/02 §8 calls these "important", and it is right: the entire
    /// economy rests on "kills fund your reloads", and without a mote flying from the
    /// corpse into the ring that relationship is invisible. The player sees a number
    /// change, not a loop.
    /// </summary>
    public struct Mote
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public bool Empowered;   // killed during i-frames — the x2
    }

    private const int MaxMotes = 64;
    private readonly Mote[] _motes = new Mote[MaxMotes];
    private int _moteCount;

    public int MoteCount => _moteCount;
    public Mote GetMote(int i) => _motes[i];

    private void SpawnSanityMote(Vector2 from, bool empowered)
    {
        if (_moteCount >= MaxMotes) return;
        _motes[_moteCount++] = new Mote
        {
            Position = from,
            Velocity = new Vector2(_rng.Range(-40f, 40f), _rng.Range(-70f, -20f)),
            Life = 1f,
            Empowered = empowered,
        };
    }

    private void TickMotes(float dt)
    {
        int i = 0;
        while (i < _moteCount)
        {
            ref Mote m = ref _motes[i];
            m.Life -= dt * 1.6f;

            // Accelerate toward the player, so it visibly homes into the sanity ring.
            Vector2 toPlayer = (GlobalPosition - m.Position).Normalized();
            m.Velocity = m.Velocity.Lerp(toPlayer * 420f, 1f - Mathf.Pow(0.02f, dt));
            m.Position += m.Velocity * dt;

            if (m.Life <= 0f || m.Position.DistanceSquaredTo(GlobalPosition) < 100f)
            {
                _motes[i] = _motes[--_moteCount];
                continue;
            }
            i++;
        }
    }

    /// <summary>Trauma-based screen shake (docs/02 §8): shake scales with trauma², so
    /// small events barely register and big ones land. Cap 6px, fully disableable.</summary>
    public float Trauma { get; private set; }
    public bool ScreenShakeEnabled { get; set; } = true;

    public void AddTrauma(float amount) => Trauma = Mathf.Min(1f, Trauma + amount);

    public Vector2 ShakeOffset(Rng rng)
    {
        if (!ScreenShakeEnabled || Trauma <= 0f) return Vector2.Zero;
        float magnitude = Trauma * Trauma * 6f;
        return new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * magnitude;
    }

    // ---------------------------------------------------------------- Ascension (docs/02 §6)

    private void BeginAscension()
    {
        Ascension.Begin(Sanity);
        Telemetry?.NoteAscension();
        EmitSignal(SignalName.Ascended);

        // The transformation clears the screen. Not a mercy — the fiction is that reality
        // briefly stops applying to you, and mechanically it marks the moment so the
        // player registers it as an event rather than a stat change.
        EnemyBullets?.Clear();

        GD.Print($"[Ascension #{Ascension.AscensionCount + 1}] {Ascension.DurationForNext():F0}s   " +
                 $"exit cost {Ascension.HeartCostForNext():F1} hearts");
    }

    private void TickAscended(float dt)
    {
        // Faster, invulnerable, and armed with a form attack instead of your weapons.
        Vector2 input = ReadMoveInput();
        Velocity = input * Tune.PlayerMoveSpeed * Tune.AscensionSpeedMultiplier;
        _smoothedVelocity = Velocity;

        if (Input.IsActionPressed("fire") && Ascension.TryConsumeAttack()) FireFormAttack();

        if (Ascension.Tick(dt)) EndAscension();
    }

    /// <summary>Infinite-ammo spread that replaces the weapon set while Ascended.</summary>
    private void FireFormAttack()
    {
        if (PlayerBullets is null) return;

        float baseAngle = Mathf.Atan2(AimDirection.Y, AimDirection.X);
        const float spread = Mathf.Pi / 5f;

        for (int i = 0; i < Tune.AscensionAttackProjectiles; i++)
        {
            float t = Tune.AscensionAttackProjectiles > 1
                ? i / (float)(Tune.AscensionAttackProjectiles - 1) - 0.5f
                : 0f;
            float angle = baseAngle + t * spread;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            PlayerBullets.Spawn(
                position: GlobalPosition,
                velocity: dir * 380f,
                radius: 5f,
                lifetime: 1.1f,
                color: new Color("B0122A"),
                renderSize: 13f,
                flags: BulletFlags.PlayerOwned | BulletFlags.Piercing,
                damage: Tune.AscensionAttackDamage);
        }
    }

    private void EndAscension()
    {
        Ascension.ResolveExit(Sanity, Hearts, MaxHearts,
                              out float heartsToDeduct, out float maxHeartDebt, out bool defaulted);

        Hearts = Mathf.Max(Tune.AscensionHeartFloor, Hearts - heartsToDeduct);

        // The debt rule. Whatever current health could not pay is taken permanently out
        // of max containers, so Ascending at low health is not cheaper — it is worse.
        if (maxHeartDebt > 0f)
        {
            MaxHearts = Mathf.Max(Tune.AscensionMinContainers, MaxHearts - maxHeartDebt);
            Hearts = Mathf.Min(Hearts, MaxHearts);
        }

        Corruption += Tune.AscensionCorruption;
        _maxSanityPenalty += Ascension.LastMaxSanityPenalty;
        _damageIFrames = 1.2f;   // brief grace so the exit is not instantly lethal

        GD.Print($"[Ascension end] −{heartsToDeduct:F1} hearts" +
                 (maxHeartDebt > 0f ? $"  −{maxHeartDebt:F1} MAX hearts (debt)" : "") +
                 $"  −{Ascension.LastMaxSanityPenalty:F0} max sanity" +
                 $"  → {Hearts:F1}/{MaxHearts:F1} hearts, sanity {Sanity.Current:F0}/{Sanity.Max:F0}" +
                 $"  corruption {Corruption:F0}" +
                 (defaulted ? "   *** DEFAULTED — the bill could not be paid ***" : ""));

        // You do not come back from defaulting.
        if (defaulted) Hearts = 0f;

        if (IsDead) EmitSignal(SignalName.Died);
    }

    // ---------------------------------------------------------------- Aiming

    private void UpdateAim()
    {
        Vector2 stick = new(
            Input.GetActionStrength("aim_right") - Input.GetActionStrength("aim_left"),
            Input.GetActionStrength("aim_down") - Input.GetActionStrength("aim_up"));

        if (stick.LengthSquared() > 0.05f)
        {
            AimDirection = stick.Normalized();
        }
        else if (!GameRoot.Instance.HeadlessTestMode)
        {
            Vector2 toMouse = GetGlobalMousePosition() - GlobalPosition;
            if (toMouse.LengthSquared() > 1f) AimDirection = toMouse.Normalized();
        }
    }

    private static Vector2 ReadMoveInput() => new Vector2(
        Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
        Input.GetActionStrength("move_down") - Input.GetActionStrength("move_up")).LimitLength(1f);

    // ---------------------------------------------------------------- Movement

    private void ApplyWalkMovement(float dt)
    {
        Vector2 input = ReadMoveInput();

        float speed = Tune.PlayerMoveSpeed * Sanity.MoveSpeedMultiplier * Circle.Effects.MoveSpeedMultiplier;
        if (Input.IsActionPressed("fire")) speed *= Tune.PlayerFiringSpeedMult;

        Vector2 target = input * speed;
        float rate = target.LengthSquared() > _smoothedVelocity.LengthSquared()
            ? Tune.PlayerAccelTime
            : Tune.PlayerDecelTime;

        _smoothedVelocity = rate <= 0f ? target
            : _smoothedVelocity.MoveToward(target, speed * dt / rate);

        Velocity = _smoothedVelocity;
    }

    // ---------------------------------------------------------------- Blink Step

    private void HandleBlinkInput()
    {
        if (!Input.IsActionJustPressed("blink_step")) return;
        if (Phase != BlinkPhase.None && Phase != BlinkPhase.Recovery) return;

        // FREE (fallback F4). The limiter is the cooldown plus the 8-frame vulnerable
        // recovery tail — spamming is punished by the tail, not by a price, which is what
        // makes timing rather than budgeting the skill.
        if (_blinkCooldown > 0f) return;

        // Cost is 0 by default; the call is kept so flipping Tune.SanityBlinkCost to 18
        // restores the metered variant (Build B) with no code change. The sigil discount
        // multiplies it and can never reach zero (docs/04 §8.6) — though with Build A's
        // free dodge it only bites in the control arm, which is the correct behaviour:
        // The Unblinking is a Build B sigil that happens to also exist in Build A.
        float blinkCost = Tune.SanityBlinkCost * Circle.Effects.BlinkCostMultiplier;
        if (blinkCost > 0f && !Sanity.TrySpend(blinkCost))
        {
            DeniedBlinkCount++;
            return;
        }
        if (blinkCost > 0f) NoteSanitySpend();

        // The Unblinking's price. Charged per dodge whether or not the dodge cost Sanity —
        // the tile buys a cheaper dodge and is paid for in Corruption, and in Build A where
        // the dodge is already free that is ALL it does.
        if (Circle.Effects.CorruptionPerBlink > 0f) Corruption += Circle.Effects.CorruptionPerBlink;

        Vector2 dir = ReadMoveInput();
        if (dir.LengthSquared() < 0.01f) dir = AimDirection;
        dir = dir.Normalized();

        Phase = BlinkPhase.Startup;
        _blinkFrame = 0;

        // A DASH, not a fixed hop: 2x the player's current move speed, so it inherits
        // move-speed modifiers (the Unravelled band's +10%, and any future mobility
        // sigil). Distance is therefore derived from the frame data rather than authored
        // — see Tune.BlinkEffectiveDistance for why the old authored value was wrong.
        float dashSpeed = Tune.PlayerMoveSpeed * Tune.BlinkSpeedMultiplier
                          * Sanity.MoveSpeedMultiplier * Circle.Effects.MoveSpeedMultiplier;

        // Tekeli-li adds DISTANCE, which at fixed frame data means adding speed. Converting
        // through the dash's own duration keeps the authored number honest — "+2 units" has
        // to mean two units on the floor, not two units of some intermediate quantity.
        float extraUnits = Circle.Effects.BlinkDistanceUnits;
        if (extraUnits > 0f)
        {
            float duration = (Tune.BlinkStartupFrames + Tune.BlinkInvulnFrames) / 60f
                             + Tune.BlinkRecoveryFrames / 60f * Tune.BlinkRecoveryMoveMult;
            dashSpeed += Tune.Units(extraUnits) / Mathf.Max(0.01f, duration);
        }

        _blinkVelocity = dir * dashSpeed;
        _smoothedVelocity = _blinkVelocity;
    }

    private void TickBlink()
    {
        _blinkFrame++;

        if (_blinkFrame <= Tune.BlinkStartupFrames)
        {
            Phase = BlinkPhase.Startup;
            Velocity = _blinkVelocity;
        }
        else if (_blinkFrame <= Tune.BlinkStartupFrames + Tune.BlinkInvulnFrames)
        {
            Phase = BlinkPhase.Invulnerable;
            Velocity = _blinkVelocity;

            // docs/02 §4 — dashing THROUGH an enemy Marks it (+25% damage taken, 0.3s).
            // Post-F4 this and the i-frame kill bonus are the only things that reward
            // dodging aggressively rather than away, so they carry the skill expression
            // the Sanity cost used to.
            Enemies?.MarkOverlapping(GlobalPosition, Tune.PlayerHitboxRadius);
        }
        else if (_blinkFrame <= Tune.BlinkTotalFrames)
        {
            Phase = BlinkPhase.Recovery;
            Velocity = _blinkVelocity * Tune.BlinkRecoveryMoveMult;
        }
        else
        {
            Phase = BlinkPhase.None;
            _blinkFrame = 0;
            _blinkCooldown = Tune.BlinkCooldown;
            _smoothedVelocity = Velocity;
        }
    }

    // ---------------------------------------------------------------- Weapons

    private void HandleWeaponInput(float dt)
    {
        if (Weapons.Count == 0 || PlayerBullets is null || Enemies is null) return;

        if (Input.IsActionJustPressed("swap_weapon")) Weapons.CycleActive();

        if (Input.IsActionPressed("fire"))
        {
            bool fired = Weapons.TryFire(GlobalPosition, AimDirection, PlayerBullets, Sanity, Enemies, _rng);

            // Melee emits no projectile, so without an explicit swing signal it produces
            // no feedback at all — swapping to it reads as the gun having broken. This is
            // the ONLY thing telling the player a melee attack happened.
            if (fired && Weapons.Active.Data.IsMelee)
            {
                MeleeSwing = 1f;
                MeleeSwingDirection = AimDirection;
                MeleeSwingReach = Weapons.Active.Data.MeleeReach;
                MeleeSwingArc = Weapons.Active.Data.MeleeArcDegrees;
            }
        }

        if (Input.IsActionJustPressed("recite"))
        {
            int deniedBefore = Weapons.ReloadsDenied;
            Weapons.TryRecite(Sanity);
            NoteSanitySpend();
            if (Weapons.ReloadsDenied > deniedBefore)
            {
                DeniedSustainCount++;
                Telemetry?.NoteDeniedSustain();
            }
        }

        int autoDeniedBefore = Weapons.ReloadsDenied;
        int autoAttemptedBefore = Weapons.ReloadsAttempted;
        Weapons.TickAutoReload(dt, Sanity, ref _autoReloadDelay);
        if (Weapons.ReloadsAttempted > autoAttemptedBefore) NoteSanitySpend();
        if (Weapons.ReloadsDenied > autoDeniedBefore)
        {
            DeniedSustainCount++;
            Telemetry?.NoteDeniedSustain();
        }

        Weapons.Active.ClearPerfectBonusIfMagazineSpent();
    }

    // ------------------------------------------------- Banish / Open the Eye

    private void HandleBanishAndOpenEye(float dt)
    {
        if (Input.IsActionPressed("banish"))
        {
            _banishHoldTime += dt;
            if (!_banishConsumed && _banishHoldTime >= Tune.OpenEyeHoldTime)
            {
                if (Sanity.TryOpenEye())
                {
                    Telemetry?.NoteOpenEye();
                    EmitSignal(SignalName.EyeOpened);
                }
                _banishConsumed = true;
            }
            return;
        }

        if (_banishHoldTime > 0f)
        {
            if (!_banishConsumed && _banishHoldTime < Tune.OpenEyeHoldTime) PerformBanish();
            _banishHoldTime = 0f;
            _banishConsumed = false;
        }
    }

    /// <summary>
    /// docs/02 §5.2. The panic button: clear the bullets, shove and stun the room.
    ///
    /// Gated purely on Sanity, which is what makes the 45 cost meaningful — dropping into
    /// Fraying (40) takes your panic button away at precisely the moment a fight is going
    /// badly enough to need it.
    /// </summary>
    private void PerformBanish()
    {
        if (_banishCooldown > 0f) return;

        if (!Sanity.TrySpend(Tune.SanityBanishCost))
        {
            DeniedSustainCount++;
            Telemetry?.NoteDeniedSustain();
            return;
        }

        _banishCooldown = Tune.BanishCooldown;
        Telemetry?.NoteSanitySpend(Tune.SanityBanishCost);
        NoteSanitySpend();

        BulletsCleared = EnemyBullets?.ClearRadius(GlobalPosition, Tune.BanishRadius) ?? 0;
        EnemiesStunned = Enemies?.ApplyBanish(GlobalPosition, Tune.BanishRadius,
                                              Tune.BanishKnockback, Tune.BanishStunSeconds) ?? 0;

        // You are unmaking part of reality, and it notices. This is also why hunting
        // secret rooms by Banishing walls makes the floor angrier (docs/02 §7.1).
        Corruption += Tune.BanishCorruption;

        BanishPulse = 1f;
        BanishOrigin = GlobalPosition;
        EmitSignal(SignalName.Banished);
    }

    /// <summary>1 → 0 over the melee swing animation. Drawn by PlayerVisual.</summary>
    public float MeleeSwing { get; private set; }
    public Vector2 MeleeSwingDirection { get; private set; } = Vector2.Right;
    public float MeleeSwingReach { get; private set; } = 40f;
    public float MeleeSwingArc { get; private set; } = 100f;

    /// <summary>1 → 0 over the shockwave animation. Drives the expanding ring.</summary>
    public float BanishPulse { get; private set; }
    public Vector2 BanishOrigin { get; private set; }
    public int BulletsCleared { get; private set; }
    public int EnemiesStunned { get; private set; }
    public float BanishCooldownRemaining => _banishCooldown;

    // ---------------------------------------------------------------- Damage & rewards

    /// <summary>
    /// Per-kill Sanity, with the two multipliers from docs/02 §3.3 that were specified
    /// and never implemented:
    ///
    ///   CHAIN — +2 per consecutive kill within 1.5s, capped at +10. Rewards momentum.
    ///   I-FRAMES — x2 for a kill landed during a Blink Step. "The high-skill line:
    ///   dodge *through* the enemy and kill it."
    ///
    /// Post-F4 these matter more than when they were written. The dodge is free, so this
    /// is the only remaining mechanical reason to dodge AGGRESSIVELY rather than away —
    /// and it is the main way an aggressive player funds reloads.
    /// </summary>
    private void CollectKillRewards()
    {
        if (Enemies is null) return;

        int kills = Enemies.KillsThisTick;
        if (kills <= 0) return;

        bool duringIFrames = Phase == BlinkPhase.Invulnerable;
        float total = 0f;

        for (int i = 0; i < kills; i++)
        {
            float value = Enemies.GetKillValue(i);

            // Chain: consecutive kills inside the window escalate.
            if (_timeSinceLastKill <= ChainWindow) _chainStep++;
            else _chainStep = 0;
            _timeSinceLastKill = 0f;

            value += Mathf.Min(_chainStep * ChainBonusPerStep, ChainBonusCap);

            // Sigil kill income is added BEFORE the i-frame doubling, so a Circle built
            // around kill Sanity rewards the high-skill line rather than being flat income
            // bolted on beside it (docs/02 §3.3).
            value += Circle.Effects.KillSanityBonus;
            if (Weapons.Count > 0) value += Weapons.Active.KillSanityBonus;   // Yellow Ink

            if (duringIFrames) value *= 2f;

            total += value;
            SpawnSanityMote(Enemies.GetKillPosition(i), duringIFrames);
            Telemetry?.NoteKill();
        }

        Sanity.GainFromKill(total);
        Telemetry?.NoteSanityIncome(total);

        LastKillWasChained = _chainStep > 0;
        LastKillDuringIFrames = duringIFrames;

        // docs/02 §8 — a freeze at the death frame. Sub-linear in kill count: summing
        // linearly turned a pack clear into a visible hang.
        PendingHitStop = HitStop.ForKills(kills);
        AddTrauma(0.16f + 0.03f * kills);
    }

    /// <summary>
    /// Deep One's Gill (docs/04 §5.2). Pays only during a LULL — several seconds without
    /// dodging, reloading or Banishing.
    ///
    /// The condition is the entire sigil. Pillar I (docs/01 §2) lists passive in-combat
    /// regeneration as one of the things the game exists to remove, and the original flat
    /// version of this tile roughly doubled a room's Sanity budget on its own. Gating it on
    /// not spending turns it into a reward for clean positioning: the moment you dodge,
    /// reload or Banish, it stops and the clock restarts.
    /// </summary>
    private void TickLullRegen(float dt)
    {
        float rate = Circle.Effects.LullRegenPerSecond;
        if (rate <= 0f) { _timeSinceSanitySpend = 0f; return; }

        _timeSinceSanitySpend += dt;
        if (_timeSinceSanitySpend >= Circle.Effects.LullDelaySeconds) Sanity.GainTrickle(rate * dt);
    }

    /// <summary>Any Pillar-I spend restarts the lull clock.</summary>
    private void NoteSanitySpend() => _timeSinceSanitySpend = 0f;

    private const float ChainWindow = 1.5f;
    private const float ChainBonusPerStep = 2f;
    private const float ChainBonusCap = 10f;

    private float _timeSinceLastKill = 99f;
    private int _chainStep;

    public int ChainStep => _chainStep;
    public bool LastKillWasChained { get; private set; }
    public bool LastKillDuringIFrames { get; private set; }

    private void PublishToBulletManagers()
    {
        if (EnemyBullets is not null)
        {
            EnemyBullets.TargetPosition = GlobalPosition;
            EnemyBullets.TargetRadius = Tune.PlayerHitboxRadius;
            EnemyBullets.TargetInvulnerable = IsInvulnerable;
        }
        if (Enemies is not null)
        {
            Enemies.PlayerPosition = GlobalPosition;
            Enemies.PlayerVelocity = Velocity;
            Enemies.ExecuteDamageBonus = Circle.Effects.ExecuteDamageBonus;
        }

        // Republished every tick rather than on placement, because two of the three inputs
        // — health and Corruption — change during a fight.
        Weapons.DamageMultiplier = OutgoingDamageMultiplier;
        Weapons.FireRateMultiplier = Circle.Effects.FireRateMultiplier;
    }

    private void ConsumeIncomingHits()
    {
        if (EnemyBullets is null || EnemyBullets.HitsThisTick <= 0) return;
        if (IsInvulnerable) return;
        TakeHit(0.5f);
    }

    private void ConsumeContactDamage()
    {
        if (Enemies is null || IsInvulnerable || _contactDamageCooldown > 0f) return;
        float dmg = Enemies.QueryContactDamage(GlobalPosition, Tune.PlayerHitboxRadius);
        if (dmg <= 0f) return;
        _contactDamageCooldown = 0.6f;
        TakeHit(dmg);
    }

    private void TakeHit(float hearts)
    {
        // ELDER SIGN (docs/04 §5.3) — once per room, a hit that would kill you does not.
        //
        // Checked BEFORE armour, and before HitsTaken is incremented. Armour absorbs the
        // hit entirely and would consume the save for nothing; and a negated lethal hit is
        // not a hit taken, which matters because HitsTaken feeds the no-damage room clear
        // and the M1 metrics.
        if (Circle.Effects.NegateLethalOncePerRoom && !_lethalNegationSpent
            && Armour <= 0 && Hearts - hearts <= 0f)
        {
            _lethalNegationSpent = true;
            _damageIFrames = 1.2f;
            AddTrauma(0.6f);
            PendingHitStop = HitStop.PlayerDamaged;
            GD.Print("[Elder Sign] a lethal hit was negated.");
            return;
        }

        HitsTaken++;
        _damageIFrames = 1.0f;
        PendingHitStop = HitStop.PlayerDamaged;
        AddTrauma(0.4f);

        // ARMOUR (docs/02 §2): absorbs one hit of any size, consumed entirely.
        //
        // It absorbs the SANITY cost as well as the health, and that is the part that
        // matters. The damage spiral is get hit -> lose Sanity -> cannot reload -> cannot
        // kill -> get hit again; armour breaking that chain at the first link is worth far
        // more than the half heart it saves.
        if (Armour > 0)
        {
            Armour--;
            ArmourAbsorbed++;
            Telemetry?.NoteHitTaken();
            return;
        }

        Hearts = Mathf.Max(0f, Hearts - ApplyIncomingDamageBonus(hearts));

        // Salt Ward halves this and nothing removes it — it is one of the two mechanisms
        // that punish both extremes (docs/02 §3.3), so docs/04 §8.6 allows a discount only.
        float sanityCost = Tune.SanityHitCost * Circle.Effects.HitSanityCostMultiplier;

        Telemetry?.NoteHitTaken();
        Telemetry?.NoteSanitySpend(sanityCost);

        // Damage compounds: being hit also costs Sanity, so you get hit and then cannot
        // afford to reload. One of the two mechanisms that punish both extremes.
        //
        // Note this does NOT start Ascension. Drain latches the trigger inside
        // SanitySystem and the poll at the top of _PhysicsProcess starts it, so being hit
        // to zero and spending to zero go through exactly the same path.
        Sanity.Drain(sanityCost);
        NoteSanitySpend();

        if (IsDead) EmitSignal(SignalName.Died);
    }

    /// <summary>
    /// The Unblinking Eye's drawback (docs/03 §3.3): +25% damage taken.
    ///
    /// Health is authored in HALF HEARTS and the design is explicit that hits are events
    /// rather than chip damage (docs/02 §2). A naive multiply gives 0.625 of a heart, which
    /// either renders as a broken heart icon or rounds back to 0.5 and makes the drawback
    /// imaginary. Accumulating the excess and spending it a half-heart at a time preserves
    /// both: the arithmetic is exactly +25% over a run, and the player reads it as "every
    /// fourth hit hurts twice as much", which is a rule they can actually feel.
    /// </summary>
    private float _damageOverflow;

    private float ApplyIncomingDamageBonus(float hearts)
    {
        float bonus = 0f;
        foreach (Weapon w in Weapons.Weapons) bonus += w.IncomingDamageBonus;
        if (bonus <= 0f) return hearts;

        _damageOverflow += hearts * bonus;
        if (_damageOverflow < 0.5f) return hearts;

        _damageOverflow -= 0.5f;
        return hearts + 0.5f;
    }

    /// <summary>
    /// The boss grab (docs/05 §7): it enters you, and the bill is Sanity rather than health.
    ///
    /// Routed through Drain rather than TrySpend, and deliberately so. Drain is the
    /// unconditional path — it can take you to zero, and reaching zero from ANY direction
    /// latches the Ascension trigger through the one funnel in SanitySystem. A grab that
    /// could not push you into Ascension would make the boss the one enemy in the game that
    /// cannot produce the game's signature moment, which is exactly backwards.
    ///
    /// It grants damage i-frames but does NOT count as a hit taken: no heart was lost, and
    /// counting it would spoil both the clean-room bonus and the M1 hit metrics.
    /// </summary>
    public void SufferGrab(float sanityCost)
    {
        _damageIFrames = 1.0f;
        PendingHitStop = HitStop.PlayerDamaged;
        AddTrauma(0.5f);

        Sanity.Drain(sanityCost);
        NoteSanitySpend();
        Telemetry?.NoteSanitySpend(sanityCost);
    }

    public void Heal(float hearts) => Hearts = Mathf.Min(MaxHearts, Hearts + hearts);

    public void ResetForTest(Vector2 position)
    {
        GlobalPosition = position;
        Velocity = Vector2.Zero;
        _smoothedVelocity = Vector2.Zero;
        Phase = BlinkPhase.None;
        _blinkFrame = 0;
        _blinkCooldown = 0f;
        _damageIFrames = 0f;
        MaxHearts = 3f;             // undo any Ascension debt from the previous run
        Hearts = MaxHearts;
        HitsTaken = 0;
        DeniedBlinkCount = 0;
        DeniedSustainCount = 0;
        Corruption = 0f;
        Armour = 0;
        ArmourAbsorbed = 0;
        Gold = 0;
        Keys = 0;
        CandlesCollected = 0;
        Ascension.ResetForRun();
        Circle.ResetForRun();
        Sanity.Suspended = false;

        // Sanity and the Lucid Ceiling must reset too, or a new run inherits the previous
        // run's descent — the next attempt would start mid-ladder and every metric keyed
        // to time-in-band would be quietly wrong.
        _maxSanityPenalty = 0f;
        _bonusMaxSanity = 0f;
        _damageOverflow = 0f;
        _lethalNegationSpent = false;
        _timeSinceSanitySpend = 0f;

        OnSigilsChanged();
        Sanity.DebugSetCurrent(Sanity.Max);
        Sanity.ResetCeiling();
    }
}
