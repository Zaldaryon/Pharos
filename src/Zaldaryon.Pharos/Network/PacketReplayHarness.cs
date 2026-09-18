using System.Collections.Generic;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Replays recorded packets against a HeadlessClient in deterministic tick order.
/// </summary>
public sealed class PacketReplayHarness
{
    private int _currentIndex;
    private IReadOnlyList<RecordedPacket>? _packets;
    private long _currentTimeMs;

    /// <summary>
    /// Whether a replay session is currently active.
    /// </summary>
    public bool IsReplaying => _packets != null && _currentIndex < _packets.Count;

    /// <summary>
    /// The current replay index position.
    /// </summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// Total number of packets in the current replay session.
    /// </summary>
    public int TotalPackets => _packets?.Count ?? 0;

    /// <summary>
    /// Current virtual timestamp in milliseconds.
    /// </summary>
    public long CurrentTimeMs => _currentTimeMs;

    /// <summary>
    /// Number of packets remaining to replay.
    /// </summary>
    public int RemainingPackets => _packets != null ? Math.Max(0, _packets.Count - _currentIndex) : 0;

    /// <summary>
    /// Starts a replay session with the given packets.
    /// </summary>
    public void Start(IReadOnlyList<RecordedPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);
        _packets = packets;
        _currentIndex = 0;
        _currentTimeMs = 0;
    }

    /// <summary>
    /// Resets the replay to the beginning.
    /// </summary>
    public void Reset()
    {
        _currentIndex = 0;
        _currentTimeMs = 0;
    }

    /// <summary>
    /// Clears the replay session.
    /// </summary>
    public void Stop()
    {
        _packets = null;
        _currentIndex = 0;
        _currentTimeMs = 0;
    }

    /// <summary>
    /// Advances the virtual clock by the given delta time in milliseconds and returns
    /// all packets that should be delivered at or before the new time.
    /// </summary>
    public IReadOnlyList<RecordedPacket> Tick(long deltaMs)
    {
        if (_packets == null || _currentIndex >= _packets.Count)
            return Array.Empty<RecordedPacket>();

        _currentTimeMs += deltaMs;

        var delivered = new List<RecordedPacket>();
        while (_currentIndex < _packets.Count && _packets[_currentIndex].TimestampMs <= _currentTimeMs)
        {
            delivered.Add(_packets[_currentIndex]);
            _currentIndex++;
        }

        return delivered;
    }

    /// <summary>
    /// Gets the next packet without advancing, or null if no packets remain.
    /// </summary>
    public RecordedPacket? Peek()
    {
        if (_packets == null || _currentIndex >= _packets.Count)
            return null;

        return _packets[_currentIndex];
    }

    /// <summary>
    /// Gets and consumes the next packet, or null if no packets remain.
    /// </summary>
    public RecordedPacket? Next()
    {
        if (_packets == null || _currentIndex >= _packets.Count)
            return null;

        var packet = _packets[_currentIndex];
        _currentIndex++;
        _currentTimeMs = Math.Max(_currentTimeMs, packet.TimestampMs);
        return packet;
    }

    /// <summary>
    /// Gets all packets with timestamps at or before the specified time without advancing.
    /// </summary>
    public IReadOnlyList<RecordedPacket> PeekUntil(long timeMs)
    {
        if (_packets == null)
            return Array.Empty<RecordedPacket>();

        var result = new List<RecordedPacket>();
        for (int i = _currentIndex; i < _packets.Count && _packets[i].TimestampMs <= timeMs; i++)
        {
            result.Add(_packets[i]);
        }
        return result;
    }

    /// <summary>
    /// Replays all packets synchronously against a HeadlessClient, advancing one tick per packet batch.
    /// </summary>
    public ReplayResult Replay(HeadlessClient client, IReadOnlyList<RecordedPacket> packets, float tickDt = 1f / 60f)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(packets);

        Start(packets);

        int deliveredCount = 0;
        int tickCount = 0;
        long tickMs = (long)(tickDt * 1000);

        while (IsReplaying)
        {
            var batch = Tick(tickMs);
            deliveredCount += batch.Count;

            // Process the client tick - in a real integration this would inject packets
            // For now we just advance the client frame to drive state machine
            client.Step(tickDt);
            tickCount++;

            // Safety limit
            if (tickCount > 100000)
                break;
        }

        Stop();

        return new ReplayResult(deliveredCount, tickCount, deliveredCount == packets.Count);
    }

    /// <summary>
    /// Replays all remaining packets without a client, returning delivery statistics.
    /// Useful for testing replay logic in isolation.
    /// </summary>
    public ReplayResult ReplayDry(IReadOnlyList<RecordedPacket> packets, long tickMs = 16)
    {
        ArgumentNullException.ThrowIfNull(packets);

        Start(packets);

        int deliveredCount = 0;
        int tickCount = 0;

        while (IsReplaying)
        {
            var batch = Tick(tickMs);
            deliveredCount += batch.Count;
            tickCount++;

            // Safety limit
            if (tickCount > 100000)
                break;
        }

        Stop();

        return new ReplayResult(deliveredCount, tickCount, deliveredCount == packets.Count);
    }
}

/// <summary>
/// Result of a packet replay session.
/// </summary>
/// <param name="PacketsDelivered">Total number of packets delivered.</param>
/// <param name="TicksElapsed">Number of ticks that elapsed during replay.</param>
/// <param name="Completed">Whether all packets were successfully delivered.</param>
public sealed record ReplayResult(int PacketsDelivered, int TicksElapsed, bool Completed);
