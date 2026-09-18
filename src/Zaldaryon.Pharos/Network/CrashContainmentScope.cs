using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Wraps execution in try/catch to contain crashes and record exceptions without killing the test process.
/// Useful for testing client resilience to tick exceptions and other recoverable errors.
/// </summary>
public sealed class CrashContainmentScope
{
    private readonly object _lock = new();
    private readonly List<CrashContainmentResult> _history = new();
    private int _crashCount;
    private int _successCount;

    /// <summary>
    /// Total number of crashes recorded.
    /// </summary>
    public int CrashCount
    {
        get { lock (_lock) return _crashCount; }
    }

    /// <summary>
    /// Total number of successful executions.
    /// </summary>
    public int SuccessCount
    {
        get { lock (_lock) return _successCount; }
    }

    /// <summary>
    /// History of all execution results.
    /// </summary>
    public IReadOnlyList<CrashContainmentResult> History
    {
        get { lock (_lock) return _history.ToList().AsReadOnly(); }
    }

    /// <summary>
    /// Most recent execution result, or null if no executions have occurred.
    /// </summary>
    public CrashContainmentResult? LastResult
    {
        get
        {
            lock (_lock)
            {
                return _history.Count > 0 ? _history[^1] : null;
            }
        }
    }

    /// <summary>
    /// Runs an action within crash containment, catching any exceptions.
    /// </summary>
    public CrashContainmentResult RunWithContainment(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CrashContainmentResult result;
        try
        {
            action();
            result = CrashContainmentResult.Success;
        }
        catch (Exception ex)
        {
            result = CrashContainmentResult.FromException(ex);
        }

        lock (_lock)
        {
            _history.Add(result);
            if (result.Crashed)
            {
                _crashCount++;
            }
            else
            {
                _successCount++;
            }
        }

        return result;
    }

    /// <summary>
    /// Runs an async action within crash containment, catching any exceptions.
    /// </summary>
    public async Task<CrashContainmentResult> RunWithContainmentAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CrashContainmentResult result;
        try
        {
            await action();
            result = CrashContainmentResult.Success;
        }
        catch (Exception ex)
        {
            result = CrashContainmentResult.FromException(ex);
        }

        lock (_lock)
        {
            _history.Add(result);
            if (result.Crashed)
            {
                _crashCount++;
            }
            else
            {
                _successCount++;
            }
        }

        return result;
    }

    /// <summary>
    /// Runs a function within crash containment, returning the result or default on crash.
    /// </summary>
    public (CrashContainmentResult Result, T? Value) RunWithContainment<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        CrashContainmentResult result;
        T? value = default;
        try
        {
            value = func();
            result = CrashContainmentResult.Success;
        }
        catch (Exception ex)
        {
            result = CrashContainmentResult.FromException(ex);
        }

        lock (_lock)
        {
            _history.Add(result);
            if (result.Crashed)
            {
                _crashCount++;
            }
            else
            {
                _successCount++;
            }
        }

        return (result, value);
    }

    /// <summary>
    /// Runs multiple iterations, stopping on the first crash if specified.
    /// </summary>
    public IReadOnlyList<CrashContainmentResult> RunIterations(
        Action iteration,
        int count,
        bool stopOnFirstCrash = false)
    {
        ArgumentNullException.ThrowIfNull(iteration);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

        var results = new List<CrashContainmentResult>(count);

        for (int i = 0; i < count; i++)
        {
            var result = RunWithContainment(iteration);
            results.Add(result);

            if (stopOnFirstCrash && result.Crashed)
            {
                break;
            }
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Clears all recorded history and resets counts.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _history.Clear();
            _crashCount = 0;
            _successCount = 0;
        }
    }

    /// <summary>
    /// Gets statistics about crash containment executions.
    /// </summary>
    public CrashStatistics GetStatistics()
    {
        lock (_lock)
        {
            int total = _crashCount + _successCount;
            float crashRate = total > 0 ? (float)_crashCount / total : 0f;

            return new CrashStatistics(
                TotalExecutions: total,
                CrashCount: _crashCount,
                SuccessCount: _successCount,
                CrashRate: crashRate);
        }
    }
}

/// <summary>
/// Statistics about crash containment executions.
/// </summary>
public sealed record CrashStatistics(
    int TotalExecutions,
    int CrashCount,
    int SuccessCount,
    float CrashRate);
