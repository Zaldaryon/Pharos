using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Simulates server disconnects for testing client resilience and reconnection handling.
/// Supports various disconnect reasons, reconnect attempt tracking, and crash simulation.
/// </summary>
public sealed class DisconnectSimulator
{
    private readonly object _lock = new();
    private readonly List<DisconnectEvent> _history = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly CrashContainmentScope _crashScope = new();

    private bool _isDisconnected;
    private DisconnectReason? _lastDisconnectReason;
    private int _reconnectAttempts;
    private int _maxReconnectAttempts = 3;

    /// <summary>
    /// Creates a new DisconnectSimulator and starts the timing clock.
    /// </summary>
    public DisconnectSimulator()
    {
        _stopwatch.Start();
    }

    /// <summary>
    /// Whether the client is currently in a disconnected state.
    /// </summary>
    public bool IsDisconnected
    {
        get { lock (_lock) return _isDisconnected; }
    }

    /// <summary>
    /// Whether the client is connected (not disconnected).
    /// </summary>
    public bool IsConnected => !IsDisconnected;

    /// <summary>
    /// The reason for the most recent disconnect, if any.
    /// </summary>
    public DisconnectReason? LastDisconnectReason
    {
        get { lock (_lock) return _lastDisconnectReason; }
    }

    /// <summary>
    /// Number of reconnect attempts made since the last disconnect.
    /// </summary>
    public int ReconnectAttempts
    {
        get { lock (_lock) return _reconnectAttempts; }
    }

    /// <summary>
    /// Maximum number of reconnect attempts allowed before giving up.
    /// </summary>
    public int MaxReconnectAttempts
    {
        get { lock (_lock) return _maxReconnectAttempts; }
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "MaxReconnectAttempts cannot be negative.");
            lock (_lock) _maxReconnectAttempts = value;
        }
    }

    /// <summary>
    /// Whether reconnect attempts have been exhausted.
    /// </summary>
    public bool ReconnectAttemptsExhausted
    {
        get { lock (_lock) return _reconnectAttempts >= _maxReconnectAttempts; }
    }

    /// <summary>
    /// Total number of disconnects recorded.
    /// </summary>
    public int DisconnectCount
    {
        get { lock (_lock) return _history.Count; }
    }

    /// <summary>
    /// The crash containment scope for testing tick exceptions.
    /// </summary>
    public CrashContainmentScope CrashScope => _crashScope;

    /// <summary>
    /// History of all disconnect events.
    /// </summary>
    public IReadOnlyList<DisconnectEvent> GetDisconnectHistory()
    {
        lock (_lock)
        {
            return _history.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Simulates a disconnect with the specified reason.
    /// </summary>
    public DisconnectEvent SimulateDisconnect(DisconnectReason reason, string? message = null)
    {
        lock (_lock)
        {
            _isDisconnected = true;
            _lastDisconnectReason = reason;
            _reconnectAttempts = 0;

            var disconnectEvent = new DisconnectEvent(
                Reason: reason,
                TimestampMs: _stopwatch.ElapsedMilliseconds,
                ReconnectAttempts: 0,
                Message: message);

            _history.Add(disconnectEvent);

            return disconnectEvent;
        }
    }

    /// <summary>
    /// Simulates a server crash (sudden disconnect with no graceful shutdown).
    /// </summary>
    public DisconnectEvent SimulateServerCrash()
    {
        return SimulateDisconnect(DisconnectReason.SimulatedCrash, "Simulated server crash for testing");
    }

    /// <summary>
    /// Simulates a timeout disconnect.
    /// </summary>
    public DisconnectEvent SimulateTimeout(int timeoutMs = 30000)
    {
        return SimulateDisconnect(DisconnectReason.Timeout, $"Connection timed out after {timeoutMs}ms");
    }

    /// <summary>
    /// Simulates being kicked from the server.
    /// </summary>
    public DisconnectEvent SimulateKick(string? kickReason = null)
    {
        return SimulateDisconnect(DisconnectReason.Kicked, kickReason ?? "Kicked by server administrator");
    }

    /// <summary>
    /// Simulates a graceful server shutdown.
    /// </summary>
    public DisconnectEvent SimulateServerShutdown()
    {
        return SimulateDisconnect(DisconnectReason.ServerShutdown, "Server is shutting down");
    }

    /// <summary>
    /// Simulates a network error.
    /// </summary>
    public DisconnectEvent SimulateNetworkError(string? errorDetails = null)
    {
        return SimulateDisconnect(DisconnectReason.NetworkError, errorDetails ?? "Network connection lost");
    }

    /// <summary>
    /// Attempts to reconnect after a disconnect.
    /// Returns true if reconnection was successful (simulated), false if attempts exhausted.
    /// </summary>
    public bool AttemptReconnect()
    {
        lock (_lock)
        {
            if (!_isDisconnected)
            {
                return true; // Already connected
            }

            _reconnectAttempts++;

            // Update the last event's reconnect count
            if (_history.Count > 0)
            {
                var lastEvent = _history[^1];
                _history[^1] = lastEvent with { ReconnectAttempts = _reconnectAttempts };
            }

            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                return false; // Exhausted attempts
            }

            return true; // Attempt counted, but not necessarily successful
        }
    }

    /// <summary>
    /// Simulates a successful reconnection.
    /// </summary>
    public void SimulateReconnectSuccess()
    {
        lock (_lock)
        {
            _isDisconnected = false;
            _lastDisconnectReason = null;
            // Keep reconnect attempts for diagnostics
        }
    }

    /// <summary>
    /// Runs a tick action with crash containment.
    /// </summary>
    public CrashContainmentResult RunTickWithContainment(Action tickAction)
    {
        return _crashScope.RunWithContainment(tickAction);
    }

    /// <summary>
    /// Runs multiple tick iterations with crash containment.
    /// </summary>
    public IReadOnlyList<CrashContainmentResult> RunTicksWithContainment(
        Action tickAction,
        int tickCount,
        bool stopOnFirstCrash = false)
    {
        return _crashScope.RunIterations(tickAction, tickCount, stopOnFirstCrash);
    }

    /// <summary>
    /// Resets the simulator to initial state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _isDisconnected = false;
            _lastDisconnectReason = null;
            _reconnectAttempts = 0;
            _history.Clear();
            _crashScope.Reset();
            _stopwatch.Restart();
        }
    }

    /// <summary>
    /// Gets a summary of disconnect statistics.
    /// </summary>
    public DisconnectStatistics GetStatistics()
    {
        lock (_lock)
        {
            int timeoutCount = 0;
            int kickCount = 0;
            int networkErrorCount = 0;
            int serverShutdownCount = 0;
            int crashCount = 0;
            int totalReconnectAttempts = 0;

            foreach (var evt in _history)
            {
                switch (evt.Reason)
                {
                    case DisconnectReason.Timeout:
                        timeoutCount++;
                        break;
                    case DisconnectReason.Kicked:
                        kickCount++;
                        break;
                    case DisconnectReason.NetworkError:
                        networkErrorCount++;
                        break;
                    case DisconnectReason.ServerShutdown:
                        serverShutdownCount++;
                        break;
                    case DisconnectReason.SimulatedCrash:
                        crashCount++;
                        break;
                }

                totalReconnectAttempts += evt.ReconnectAttempts;
            }

            return new DisconnectStatistics(
                TotalDisconnects: _history.Count,
                TimeoutCount: timeoutCount,
                KickCount: kickCount,
                NetworkErrorCount: networkErrorCount,
                ServerShutdownCount: serverShutdownCount,
                SimulatedCrashCount: crashCount,
                TotalReconnectAttempts: totalReconnectAttempts,
                CrashStatistics: _crashScope.GetStatistics());
        }
    }
}

/// <summary>
/// Statistics about disconnect simulation.
/// </summary>
public sealed record DisconnectStatistics(
    int TotalDisconnects,
    int TimeoutCount,
    int KickCount,
    int NetworkErrorCount,
    int ServerShutdownCount,
    int SimulatedCrashCount,
    int TotalReconnectAttempts,
    CrashStatistics CrashStatistics);
