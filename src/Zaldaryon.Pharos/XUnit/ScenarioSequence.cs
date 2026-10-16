using Zaldaryon.Pharos.Assertions;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>Builds a deterministic sequence of assertions and actions over game ticks or frames.</summary>
public sealed class ScenarioSequence
{
    private readonly Func<int, CancellationToken, Task> _advance;
    private readonly List<Func<int, CancellationToken, Task>> _steps = [];
    private readonly List<Action> _failures = [];
    private bool _hasRun;

    internal ScenarioSequence(Func<int, CancellationToken, Task> advance) => _advance = advance;

    /// <summary>Advances the simulation by the specified number of ticks or frames.</summary>
    public ScenarioSequence Idle(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        _steps.Add((remaining, ct) => AdvanceAsync(ticks, remaining, ct));
        return this;
    }

    /// <summary>Runs an action immediately.</summary>
    public ScenarioSequence Then(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _steps.Add((remaining, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            CheckFailures();
            action();
            CheckFailures();
            return Task.CompletedTask;
        });
        return this;
    }

    /// <summary>Advances until the assertion passes, within the sequence's remaining budget.</summary>
    public ScenarioSequence WaitUntil(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        _steps.Add(async (remaining, ct) =>
        {
            int used = 0;
            Exception? lastFailure = null;
            while (used <= remaining)
            {
                ct.ThrowIfCancellationRequested();
                CheckFailures();
                try { assertion(); return; }
                catch (Exception ex) when (ScenarioAssertionFailure.IsAssertion(ex)) { lastFailure = ex; }
                if (used == remaining)
                    throw new TimeoutException($"Sequence assertion did not pass within its remaining budget of {remaining} ticks or frames. Last failure: {lastFailure?.Message}", lastFailure);
                await AdvanceAsync(1, remaining - used, ct).ConfigureAwait(false);
                used++;
            }
        });
        return this;
    }

    /// <summary>Fails if this assertion passes at any point from here to the end of the sequence.</summary>
    public ScenarioSequence FailIf(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        _steps.Add((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            _failures.Add(assertion);
            CheckFailures();
            return Task.CompletedTask;
        });
        return this;
    }

    /// <summary>Runs the sequence with a total tick or frame budget.</summary>
    public async Task RunAsync(int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        if (_hasRun) throw new InvalidOperationException("A scenario sequence can only be run once.");
        _hasRun = true;
        int used = 0;
        foreach (Func<int, CancellationToken, Task> step in _steps)
        {
            ct.ThrowIfCancellationRequested();
            CheckFailures();
            int before = used;
            await step(maxTicks - used, ct).ConfigureAwait(false);
            // Each sequence action accounts for its exact simulated advances via this field.
            used = _usedTicks;
            if (used < before || used > maxTicks)
                throw new InvalidOperationException("The scenario sequence exceeded its tick or frame budget.");
        }
    }

    private int _usedTicks;

    private async Task AdvanceAsync(int count, int remaining, CancellationToken ct)
    {
        if (count > remaining) throw new TimeoutException($"Sequence needs {count} more ticks or frames, but only {remaining} remain.");
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            CheckFailures();
            await _advance(1, ct).ConfigureAwait(false);
            _usedTicks++;
            CheckFailures();
        }
    }

    private void CheckFailures()
    {
        foreach (Action assertion in _failures)
        {
            try { assertion(); }
            catch (Exception ex) when (ScenarioAssertionFailure.IsAssertion(ex)) { continue; }
            throw new InvalidOperationException("A FailIf assertion passed.");
        }
    }
}

internal static class ScenarioTickWait
{
    public static async Task SucceedWhenAsync(Action assertion, int maxTicks, Func<int, CancellationToken, Task> advance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        Exception? lastFailure = null;
        for (int tick = 0; tick <= maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            try { assertion(); return; }
            catch (Exception ex) when (ScenarioAssertionFailure.IsAssertion(ex)) { lastFailure = ex; }
            if (tick < maxTicks) await advance(1, ct).ConfigureAwait(false);
        }
        throw new TimeoutException($"Assertion did not pass within {maxTicks} ticks or frames. Last failure: {lastFailure?.Message}", lastFailure);
    }
}

internal static class ScenarioAssertionFailure
{
    public static bool IsAssertion(Exception exception)
    {
        if (exception is PharosAssertException) return true;
        for (Type? type = exception.GetType(); type != null; type = type.BaseType)
            if (type.FullName == "Xunit.Sdk.XunitException") return true;
        return false;
    }
}
