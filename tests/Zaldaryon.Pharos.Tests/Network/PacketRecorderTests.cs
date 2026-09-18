using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>
/// Pure-logic tests for PacketRecorder, RecordedPacket, and PacketReplayHarness.
/// These tests are headless-safe and do not require a live server.
/// </summary>
public sealed class PacketRecorderTests : IDisposable
{
    private readonly string _tempDir;

    public PacketRecorderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pharos-packet-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    // -------------------------------------------------------------------------
    // RecordedPacket record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordedPacket_FromBytes_EncodesPayloadAsBase64()
    {
        byte[] payload = new byte[] { 0x01, 0x02, 0x03, 0xFF };
        var packet = RecordedPacket.FromBytes(42, PacketDirection.Inbound, 100, payload, "TestPacket");

        Assert.Equal(42, packet.PacketId);
        Assert.Equal(PacketDirection.Inbound, packet.Direction);
        Assert.Equal(100, packet.TimestampMs);
        Assert.Equal("TestPacket", packet.PacketTypeName);
        Assert.Equal(Convert.ToBase64String(payload), packet.PayloadBase64);
    }

    [Fact]
    public void RecordedPacket_GetPayloadBytes_DecodesBase64Correctly()
    {
        byte[] original = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var packet = RecordedPacket.FromBytes(1, PacketDirection.Outbound, 50, original);

        byte[] decoded = packet.GetPayloadBytes();

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void RecordedPacket_WithNullTypeName_SetsNullProperty()
    {
        var packet = new RecordedPacket(1, PacketDirection.Inbound, 0, "AAAA", null);

        Assert.Null(packet.PacketTypeName);
    }

    // -------------------------------------------------------------------------
    // PacketRecorder Start/Stop/Record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PacketRecorder_Start_EnablesRecording()
    {
        var recorder = new PacketRecorder();

        Assert.False(recorder.IsRecording);

        recorder.Start();

        Assert.True(recorder.IsRecording);
        Assert.Equal(0, recorder.PacketCount);
    }

    [Fact]
    public void PacketRecorder_Stop_DisablesRecording()
    {
        var recorder = new PacketRecorder();
        recorder.Start();
        recorder.Record(1, PacketDirection.Inbound, new byte[] { 1, 2, 3 });

        recorder.Stop();

        Assert.False(recorder.IsRecording);
        Assert.Equal(1, recorder.PacketCount);
    }

    [Fact]
    public void PacketRecorder_Record_IgnoresWhenNotRecording()
    {
        var recorder = new PacketRecorder();

        recorder.Record(1, PacketDirection.Inbound, new byte[] { 1, 2, 3 });

        Assert.Equal(0, recorder.PacketCount);
    }

    [Fact]
    public void PacketRecorder_Record_CapturesPacketsWhenRecording()
    {
        var recorder = new PacketRecorder();
        recorder.Start();

        recorder.Record(1, PacketDirection.Inbound, new byte[] { 1 }, "Packet1");
        recorder.Record(2, PacketDirection.Outbound, new byte[] { 2 }, "Packet2");
        recorder.Record(3, PacketDirection.Inbound, new byte[] { 3 }, "Packet3");

        Assert.Equal(3, recorder.PacketCount);

        var packets = recorder.GetRecordedPackets();
        Assert.Equal(3, packets.Count);
        Assert.Equal(1, packets[0].PacketId);
        Assert.Equal(2, packets[1].PacketId);
        Assert.Equal(3, packets[2].PacketId);
    }

    [Fact]
    public void PacketRecorder_Start_ClearsPreviousRecording()
    {
        var recorder = new PacketRecorder();
        recorder.Start();
        recorder.Record(1, PacketDirection.Inbound, new byte[] { 1 });

        recorder.Start();

        Assert.Equal(0, recorder.PacketCount);
    }

    [Fact]
    public void PacketRecorder_Clear_RemovesPacketsWithoutStoppingRecording()
    {
        var recorder = new PacketRecorder();
        recorder.Start();
        recorder.Record(1, PacketDirection.Inbound, new byte[] { 1 });

        recorder.Clear();

        Assert.True(recorder.IsRecording);
        Assert.Equal(0, recorder.PacketCount);
    }

    // -------------------------------------------------------------------------
    // PacketRecorder JSON serialization tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PacketRecorder_ToJson_SerializesPacketsCorrectly()
    {
        var recorder = new PacketRecorder();
        recorder.Start();
        recorder.Record(42, PacketDirection.Inbound, new byte[] { 0xAB, 0xCD }, "TestType");
        recorder.Stop();

        string json = recorder.ToJson();

        Assert.Contains("\"packetId\": 42", json);
        Assert.Contains("\"direction\": \"inbound\"", json);
        Assert.Contains("\"packetTypeName\": \"TestType\"", json);
    }

    [Fact]
    public void PacketRecorder_FromJson_DeserializesPacketsCorrectly()
    {
        var original = new PacketRecorder();
        original.Start();
        original.Record(1, PacketDirection.Inbound, new byte[] { 0x01 }, "First");
        original.Record(2, PacketDirection.Outbound, new byte[] { 0x02 }, "Second");
        original.Stop();

        string json = original.ToJson();

        var loaded = new PacketRecorder();
        loaded.FromJson(json);

        var packets = loaded.GetRecordedPackets();
        Assert.Equal(2, packets.Count);
        Assert.Equal(1, packets[0].PacketId);
        Assert.Equal(PacketDirection.Inbound, packets[0].Direction);
        Assert.Equal(2, packets[1].PacketId);
        Assert.Equal(PacketDirection.Outbound, packets[1].Direction);
    }

    [Fact]
    public void PacketRecorder_JsonRoundtrip_PreservesAllFields()
    {
        var original = new PacketRecorder();
        original.Start();
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };
        original.Record(999, PacketDirection.Outbound, payload, "ComplexPacket");
        original.Stop();

        string json = original.ToJson();
        var loaded = PacketRecorder.FromJsonString(json);

        var packets = loaded.GetRecordedPackets();
        Assert.Single(packets);

        var packet = packets[0];
        Assert.Equal(999, packet.PacketId);
        Assert.Equal(PacketDirection.Outbound, packet.Direction);
        Assert.Equal("ComplexPacket", packet.PacketTypeName);
        Assert.Equal(payload, packet.GetPayloadBytes());
    }

    [Fact]
    public void PacketRecorder_SaveAndLoadJson_WorksWithFile()
    {
        var original = new PacketRecorder();
        original.Start();
        original.Record(123, PacketDirection.Inbound, new byte[] { 1, 2, 3 }, "FileTest");
        original.Stop();

        string filePath = Path.Combine(_tempDir, "packets.json");
        original.SaveToJson(filePath);

        Assert.True(File.Exists(filePath));

        var loaded = PacketRecorder.FromJsonFile(filePath);
        var packets = loaded.GetRecordedPackets();

        Assert.Single(packets);
        Assert.Equal(123, packets[0].PacketId);
    }

    // -------------------------------------------------------------------------
    // PacketRecorder timestamp ordering tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PacketRecorder_Timestamps_AreMonotonicallyIncreasing()
    {
        var recorder = new PacketRecorder();
        recorder.Start();

        for (int i = 0; i < 10; i++)
        {
            recorder.Record(i, PacketDirection.Inbound, new byte[] { (byte)i });
            Thread.Sleep(5); // Small delay to ensure timestamp progression
        }

        recorder.Stop();

        var packets = recorder.GetRecordedPackets();
        for (int i = 1; i < packets.Count; i++)
        {
            Assert.True(packets[i].TimestampMs >= packets[i - 1].TimestampMs,
                $"Timestamp at index {i} ({packets[i].TimestampMs}) should be >= timestamp at index {i - 1} ({packets[i - 1].TimestampMs})");
        }
    }

    // -------------------------------------------------------------------------
    // PacketReplayHarness tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PacketReplayHarness_Start_InitializesReplayState()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 100, "BB==")
        };

        harness.Start(packets);

        Assert.True(harness.IsReplaying);
        Assert.Equal(0, harness.CurrentIndex);
        Assert.Equal(2, harness.TotalPackets);
        Assert.Equal(0, harness.CurrentTimeMs);
    }

    [Fact]
    public void PacketReplayHarness_Tick_DeliversPacketsAtCorrectTime()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 50, "BB=="),
            new(3, PacketDirection.Inbound, 100, "CC==")
        };

        harness.Start(packets);

        // First tick: 30ms - should deliver packet at 0ms
        var batch1 = harness.Tick(30);
        Assert.Single(batch1);
        Assert.Equal(1, batch1[0].PacketId);

        // Second tick: 30ms more (60ms total) - should deliver packet at 50ms
        var batch2 = harness.Tick(30);
        Assert.Single(batch2);
        Assert.Equal(2, batch2[0].PacketId);

        // Third tick: 50ms more (110ms total) - should deliver packet at 100ms
        var batch3 = harness.Tick(50);
        Assert.Single(batch3);
        Assert.Equal(3, batch3[0].PacketId);

        Assert.False(harness.IsReplaying);
    }

    [Fact]
    public void PacketReplayHarness_Tick_DeliversMultiplePacketsInSingleTick()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 10, "BB=="),
            new(3, PacketDirection.Inbound, 20, "CC=="),
            new(4, PacketDirection.Inbound, 1000, "DD==")
        };

        harness.Start(packets);

        // Single large tick should deliver first 3 packets
        var batch = harness.Tick(50);

        Assert.Equal(3, batch.Count);
        Assert.Equal(1, batch[0].PacketId);
        Assert.Equal(2, batch[1].PacketId);
        Assert.Equal(3, batch[2].PacketId);
        Assert.Equal(1, harness.RemainingPackets);
    }

    [Fact]
    public void PacketReplayHarness_Peek_ReturnsNextPacketWithoutAdvancing()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 100, "BB==")
        };

        harness.Start(packets);

        var peeked1 = harness.Peek();
        var peeked2 = harness.Peek();

        Assert.NotNull(peeked1);
        Assert.NotNull(peeked2);
        Assert.Equal(1, peeked1.PacketId);
        Assert.Equal(1, peeked2.PacketId);
        Assert.Equal(0, harness.CurrentIndex);
    }

    [Fact]
    public void PacketReplayHarness_Next_ConsumesPacketAndAdvances()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 50, "AA=="),
            new(2, PacketDirection.Inbound, 100, "BB==")
        };

        harness.Start(packets);

        var first = harness.Next();
        var second = harness.Next();
        var third = harness.Next();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(third);
        Assert.Equal(1, first.PacketId);
        Assert.Equal(2, second.PacketId);
        Assert.Equal(2, harness.CurrentIndex);
    }

    [Fact]
    public void PacketReplayHarness_Reset_RestartsFromBeginning()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 100, "BB==")
        };

        harness.Start(packets);
        harness.Next();
        harness.Next();

        harness.Reset();

        Assert.Equal(0, harness.CurrentIndex);
        Assert.Equal(0, harness.CurrentTimeMs);
        Assert.True(harness.IsReplaying);
    }

    [Fact]
    public void PacketReplayHarness_Stop_ClearsReplayState()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA==")
        };

        harness.Start(packets);

        harness.Stop();

        Assert.False(harness.IsReplaying);
        Assert.Equal(0, harness.TotalPackets);
    }

    [Fact]
    public void PacketReplayHarness_ReplayDry_CompletesAllPackets()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 16, "BB=="),
            new(3, PacketDirection.Inbound, 32, "CC=="),
            new(4, PacketDirection.Inbound, 48, "DD==")
        };

        var result = harness.ReplayDry(packets, tickMs: 16);

        Assert.True(result.Completed);
        Assert.Equal(4, result.PacketsDelivered);
        Assert.True(result.TicksElapsed >= 3);
    }

    [Fact]
    public void PacketReplayHarness_PeekUntil_ReturnsPacketsUpToTime()
    {
        var harness = new PacketReplayHarness();
        var packets = new List<RecordedPacket>
        {
            new(1, PacketDirection.Inbound, 0, "AA=="),
            new(2, PacketDirection.Inbound, 50, "BB=="),
            new(3, PacketDirection.Inbound, 100, "CC=="),
            new(4, PacketDirection.Inbound, 200, "DD==")
        };

        harness.Start(packets);

        var peeked = harness.PeekUntil(100);

        Assert.Equal(3, peeked.Count);
        Assert.Equal(1, peeked[0].PacketId);
        Assert.Equal(2, peeked[1].PacketId);
        Assert.Equal(3, peeked[2].PacketId);
        // Should not have advanced
        Assert.Equal(0, harness.CurrentIndex);
    }

    // -------------------------------------------------------------------------
    // Integration tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PacketRecorder_FullWorkflow_RecordSerializeReplay()
    {
        // Record
        var recorder = new PacketRecorder();
        recorder.Start();
        recorder.Record(1, PacketDirection.Inbound, new byte[] { 0x01 }, "Login");
        recorder.Record(2, PacketDirection.Inbound, new byte[] { 0x02 }, "WorldData");
        recorder.Record(3, PacketDirection.Outbound, new byte[] { 0x03 }, "PlayerMove");
        recorder.Stop();

        // Serialize
        string json = recorder.ToJson();

        // Deserialize
        var loaded = PacketRecorder.FromJsonString(json);
        var packets = loaded.GetRecordedPackets();

        // Replay
        var harness = new PacketReplayHarness();
        var result = harness.ReplayDry(packets, tickMs: 1);

        Assert.True(result.Completed);
        Assert.Equal(3, result.PacketsDelivered);
    }

    [Fact]
    public void PacketRecorder_RecordBase64_WorksCorrectly()
    {
        var recorder = new PacketRecorder();
        recorder.Start();

        string base64Payload = Convert.ToBase64String(new byte[] { 0xAA, 0xBB, 0xCC });
        recorder.RecordBase64(42, PacketDirection.Inbound, base64Payload, "Base64Test");

        recorder.Stop();

        var packets = recorder.GetRecordedPackets();
        Assert.Single(packets);
        Assert.Equal(base64Payload, packets[0].PayloadBase64);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, packets[0].GetPayloadBytes());
    }
}
