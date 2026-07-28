using CultistOfCthulhu.Core;
using Godot;

namespace CultistOfCthulhu.Player;

/// <summary>
/// docs/02 §6 — Sanity zero does not kill you. It changes you.
///
/// The emotional peak of a run: 20 seconds of invulnerable, monstrous power, and then a
/// bill. This is what makes running out of Sanity an EVENT rather than a fail state, and
/// it is the floor beneath the whole ladder — without it, the bottom band leads nowhere.
///
/// THE GOVERNING CONSTRAINT: Ascension must never be optimal to farm. Fable's review found
/// the original spec was farmable to infinity, because two clauses cancelled the cost:
///   - "cannot kill you, floors at half a heart" meant that at low health the heart cost
///     simply vanished, so Ascending cost nothing;
///   - max Sanity floored at 40, so after six Ascensions the escalating penalty stopped
///     escalating.
/// The loop was: drain 40 Sanity, get 20s of invulnerability, repeat forever. Low health
/// became SAFER than high health, inverting the entire damage model.
///
/// Two mechanisms close it, and both are load-bearing:
///   1. THE DEBT RULE — if the heart cost cannot be paid from current hearts, the unpaid
///      remainder is taken from MAX hearts instead. The cost is always paid by something.
///   2. DIMINISHING DURATION — 20 / 14 / 10 / 7 / 5 seconds. Repetition is allowed;
///      farming is not, because the payout shrinks toward a genuine emergency button.
/// </summary>
public sealed class AscensionController
{
    public bool IsAscended { get; private set; }
    public float TimeRemaining { get; private set; }
    public int AscensionCount { get; private set; }

    /// <summary>0..1 through the current window, for the UI and the visual effect.</summary>
    public float Progress => _windowDuration <= 0f ? 0f : 1f - TimeRemaining / _windowDuration;

    /// <summary>Short white-out at the moment of transformation (docs/02 §6 step 1).</summary>
    public float WhiteoutRemaining { get; private set; }
    public const float WhiteoutDuration = 0.8f;

    private float _windowDuration;
    private float _attackCooldown;

    /// <summary>Costs applied on the most recent exit — surfaced so the HUD and telemetry
    /// can show the player what Ascending actually cost them.</summary>
    public float LastHeartCost { get; private set; }
    public float LastMaxHeartDebt { get; private set; }
    public float LastMaxSanityPenalty { get; private set; }

    /// <summary>True when the last exit could not be paid at all — a fatal default.</summary>
    public bool LastDefaulted { get; private set; }

    public float DurationForNext()
    {
        int i = Mathf.Min(AscensionCount, Tune.AscensionDurations.Length - 1);
        return Tune.AscensionDurations[i];
    }

    /// <summary>Heart cost of the NEXT exit. Shown so the decision is informed.</summary>
    public float HeartCostForNext()
        => Tune.AscensionExitHeartCost + Tune.AscensionHeartCostEscalation * AscensionCount;

    public void Begin(SanitySystem sanity)
    {
        IsAscended = true;
        _windowDuration = DurationForNext();
        TimeRemaining = _windowDuration;
        WhiteoutRemaining = WhiteoutDuration;
        _attackCooldown = 0f;

        // The bar is not a resource during the window — it is the thing that ran out.
        sanity.Suspended = true;
    }

    /// <summary>Advance. Returns true on the tick the window closes.</summary>
    public bool Tick(float dt)
    {
        if (!IsAscended) return false;

        if (WhiteoutRemaining > 0f) WhiteoutRemaining -= dt;
        if (_attackCooldown > 0f) _attackCooldown -= dt;

        TimeRemaining -= dt;
        if (TimeRemaining > 0f) return false;

        IsAscended = false;
        TimeRemaining = 0f;
        return true;
    }

    public bool TryConsumeAttack()
    {
        if (!IsAscended || _attackCooldown > 0f) return false;
        _attackCooldown = 1f / Tune.AscensionAttackRate;
        return true;
    }

    /// <summary>
    /// Apply the bill. Returns the hearts to deduct from CURRENT health and, via
    /// <paramref name="maxHeartDebt"/>, the permanent container loss taken when current
    /// hearts could not cover it.
    /// </summary>
    public void ResolveExit(SanitySystem sanity, float currentHearts, float maxHearts,
                            out float heartsToDeduct, out float maxHeartDebt, out bool defaulted)
    {
        float cost = HeartCostForNext();

        // The heart cost can never itself be lethal — but the shortfall is not forgiven.
        // Whatever current health cannot pay is taken permanently out of max containers,
        // which is what stops low health from being the cheapest place to Ascend.
        float payableFromCurrent = Mathf.Max(0f, currentHearts - Tune.AscensionHeartFloor);
        heartsToDeduct = Mathf.Min(cost, payableFromCurrent);

        float unpaid = cost - heartsToDeduct;
        float reducibleContainers = Mathf.Max(0f, maxHearts - Tune.AscensionMinContainers);
        maxHeartDebt = Mathf.Min(unpaid, reducibleContainers);

        // DEFAULTING IS FATAL, and this clause is load-bearing.
        //
        // Without it the debt rule only defers the exploit. Once max hearts reach their
        // floor there is nothing left to take, so the unpaid remainder is silently
        // forgiven and Ascension becomes free again — the same infinite loop Fable's
        // review found, arriving three Ascensions later instead of immediately.
        //
        // Making an unpayable bill lethal bounds the whole system: with escalating costs
        // and a fixed pool to pay from, there is a hard maximum number of Ascensions per
        // run, and approaching it is visibly terminal. It is also the correct fiction —
        // Ascension is a loan against yourself, and you do not come back from defaulting.
        defaulted = unpaid - maxHeartDebt > 0.001f;

        LastHeartCost = heartsToDeduct;
        LastMaxHeartDebt = maxHeartDebt;
        LastDefaulted = defaulted;

        float maxBefore = sanity.Max;
        sanity.Suspended = false;
        sanity.ReduceMax(Tune.AscensionMaxSanityPenalty, Tune.AscensionMaxSanityFloor);
        LastMaxSanityPenalty = maxBefore - sanity.Max;

        sanity.RestoreTo(Mathf.Min(Tune.AscensionExitSanity, sanity.Max));

        AscensionCount++;
    }

    public void ResetForRun()
    {
        IsAscended = false;
        AscensionCount = 0;
        TimeRemaining = 0f;
        WhiteoutRemaining = 0f;
        LastHeartCost = 0f;
        LastMaxHeartDebt = 0f;
        LastMaxSanityPenalty = 0f;
        LastDefaulted = false;
    }
}
