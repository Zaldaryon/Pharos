namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Checks each sample of a soak run against the monitor's budgets. It takes the warm-up baseline at
/// the first checkpoint after the warm-up.
/// </summary>
internal sealed class SoakEvaluator
{
    private readonly IReadOnlyList<SoakBudget> _budgets;
    private readonly TimeSpan _warmUp;
    private readonly bool _enforceTiming;
    private readonly SoakBaseline _baseline = new();

    public SoakEvaluator(IReadOnlyList<SoakBudget> budgets, TimeSpan warmUp, bool enforceTiming)
    {
        _budgets = budgets;
        _warmUp = warmUp;
        _enforceTiming = enforceTiming;
    }

    /// <summary>The breaches <paramref name="sample"/> causes, gating or not, in budget order.</summary>
    public List<SoakBreach> Evaluate(SoakSample sample)
    {
        List<SoakBreach> breaches = [];
        if (_baseline.ManagedBytes is null && sample.Checkpoint && sample.Simulated >= _warmUp && sample.ManagedBytesAfterGc is { } heap)
        {
            _baseline.ManagedBytes = heap;
            _baseline.TickListeners = sample.TickListeners;
            return breaches;
        }

        foreach (SoakBudget budget in _budgets)
        {
            if (budget.Checkpoint && !sample.Checkpoint) continue;
            if (budget.Check(sample, _baseline, _enforceTiming) is { } breach) breaches.Add(breach);
        }

        return breaches;
    }
}
