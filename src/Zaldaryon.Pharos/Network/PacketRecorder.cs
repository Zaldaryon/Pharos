using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Records network packets during a session for later deterministic replay.
/// </summary>
public sealed class PacketRecorder
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _lock = new();
    private readonly List<RecordedPacket> _packets = new();
    private readonly Stopwatch _stopwatch = new();
    private bool _recording;

    /// <summary>
    /// Whether packet recording is currently active.
    /// </summary>
    public bool IsRecording
    {
        get { lock (_lock) return _recording; }
    }

    /// <summary>
    /// Number of packets recorded in the current session.
    /// </summary>
    public int PacketCount
    {
        get { lock (_lock) return _packets.Count; }
    }

    /// <summary>
    /// Starts recording packets. Clears any previously recorded packets.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            _packets.Clear();
            _stopwatch.Restart();
            _recording = true;
        }
    }

    /// <summary>
    /// Stops recording packets.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _recording = false;
            _stopwatch.Stop();
        }
    }

    /// <summary>
    /// Records a packet if recording is active.
    /// </summary>
    public void Record(int packetId, PacketDirection direction, byte[] payload, string? packetTypeName = null)
    {
        lock (_lock)
        {
            if (!_recording) return;

            _packets.Add(RecordedPacket.FromBytes(
                packetId,
                direction,
                _stopwatch.ElapsedMilliseconds,
                payload,
                packetTypeName));
        }
    }

    /// <summary>
    /// Records a packet with pre-encoded Base64 payload if recording is active.
    /// </summary>
    public void RecordBase64(int packetId, PacketDirection direction, string payloadBase64, string? packetTypeName = null)
    {
        lock (_lock)
        {
            if (!_recording) return;

            _packets.Add(new RecordedPacket(
                packetId,
                direction,
                _stopwatch.ElapsedMilliseconds,
                payloadBase64,
                packetTypeName));
        }
    }

    /// <summary>
    /// Gets a snapshot of all recorded packets.
    /// </summary>
    public IReadOnlyList<RecordedPacket> GetRecordedPackets()
    {
        lock (_lock)
        {
            return _packets.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Clears all recorded packets without stopping recording.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _packets.Clear();
        }
    }

    /// <summary>
    /// Serializes the recorded packets to a JSON string.
    /// </summary>
    public string ToJson()
    {
        lock (_lock)
        {
            return JsonSerializer.Serialize(_packets, s_jsonOptions);
        }
    }

    /// <summary>
    /// Loads packets from a JSON string, replacing any existing recorded packets.
    /// </summary>
    public void FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        var loaded = JsonSerializer.Deserialize<List<RecordedPacket>>(json, s_jsonOptions)
            ?? throw new JsonException("Failed to deserialize packet recording.");

        lock (_lock)
        {
            _packets.Clear();
            _packets.AddRange(loaded);
            _recording = false;
        }
    }

    /// <summary>
    /// Saves the recorded packets to a JSON file.
    /// </summary>
    public void SaveToJson(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = ToJson();
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Saves the recorded packets to a JSON file asynchronously.
    /// </summary>
    public async Task SaveToJsonAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = ToJson();
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Loads packets from a JSON file.
    /// </summary>
    public void LoadFromJson(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string json = File.ReadAllText(path);
        FromJson(json);
    }

    /// <summary>
    /// Loads packets from a JSON file asynchronously.
    /// </summary>
    public async Task LoadFromJsonAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string json = await File.ReadAllTextAsync(path, ct);
        FromJson(json);
    }

    /// <summary>
    /// Creates a new PacketRecorder pre-populated with packets loaded from a JSON string.
    /// </summary>
    public static PacketRecorder FromJsonString(string json)
    {
        var recorder = new PacketRecorder();
        recorder.FromJson(json);
        return recorder;
    }

    /// <summary>
    /// Creates a new PacketRecorder pre-populated with packets loaded from a JSON file.
    /// </summary>
    public static PacketRecorder FromJsonFile(string path)
    {
        var recorder = new PacketRecorder();
        recorder.LoadFromJson(path);
        return recorder;
    }
}
