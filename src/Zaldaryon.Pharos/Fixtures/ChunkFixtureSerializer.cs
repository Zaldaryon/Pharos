using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Zaldaryon.Pharos.Fixtures;

/// <summary>
/// Provides JSON serialization and deserialization for chunk fixtures.
/// </summary>
public static class ChunkFixtureSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Represents an individual modified block entry in the serialized fixture JSON.
    /// </summary>
    public sealed class SerializedBlock
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public string? Code { get; set; }
        public int? Id { get; set; }
        public byte? Sun { get; set; }
        public byte? Light { get; set; }
    }

    /// <summary>
    /// Represents a cuboid region fill entry in the serialized fixture JSON.
    /// </summary>
    public sealed class SerializedBox
    {
        public int MinX { get; set; }
        public int MinY { get; set; }
        public int MinZ { get; set; }
        public int MaxX { get; set; }
        public int MaxY { get; set; }
        public int MaxZ { get; set; }
        public string? Code { get; set; }
        public int? Id { get; set; }
    }

    /// <summary>
    /// Root document structure for JSON serialization.
    /// </summary>
    public sealed class ChunkFixtureDocument
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int ChunkZ { get; set; }
        public byte DefaultSunlight { get; set; } = 31;
        public List<SerializedBox>? Boxes { get; set; }
        public List<SerializedBlock>? Blocks { get; set; }
    }

    /// <summary>
    /// Serializes a ChunkFixture to a JSON string.
    /// </summary>
    public static string ToJson(ChunkFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var doc = new ChunkFixtureDocument
        {
            ChunkX = fixture.Position.X,
            ChunkY = fixture.Position.Y,
            ChunkZ = fixture.Position.Z,
            DefaultSunlight = fixture.DefaultSunlight,
            Blocks = new List<SerializedBlock>()
        };

        SortedSet<int> allIndices = new();
        foreach (int idx in fixture.BlockCodes.Keys) allIndices.Add(idx);
        foreach (int idx in fixture.BlockIds.Keys) allIndices.Add(idx);
        foreach (int idx in fixture.CustomSunlight.Keys) allIndices.Add(idx);
        foreach (int idx in fixture.CustomBlocklight.Keys) allIndices.Add(idx);

        foreach (int idx in allIndices)
        {
            var (x, y, z) = ChunkFixture.FromIndex(idx);
            var b = new SerializedBlock
            {
                X = x,
                Y = y,
                Z = z
            };

            if (fixture.BlockCodes.TryGetValue(idx, out string? code)) b.Code = code;
            if (fixture.BlockIds.TryGetValue(idx, out int id)) b.Id = id;
            if (fixture.CustomSunlight.TryGetValue(idx, out byte sun)) b.Sun = sun;
            if (fixture.CustomBlocklight.TryGetValue(idx, out byte light)) b.Light = light;

            doc.Blocks.Add(b);
        }

        return JsonSerializer.Serialize(doc, s_options);
    }

    /// <summary>
    /// Deserializes a ChunkFixture from a JSON string.
    /// </summary>
    public static ChunkFixture FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        var doc = JsonSerializer.Deserialize<ChunkFixtureDocument>(json, s_options)
            ?? throw new JsonException("Failed to deserialize ChunkFixture JSON document.");

        var builder = new ChunkFixtureBuilder()
            .At(doc.ChunkX, doc.ChunkY, doc.ChunkZ)
            .WithDefaultSunlight(doc.DefaultSunlight);

        if (doc.Boxes != null)
        {
            foreach (var box in doc.Boxes)
            {
                if (!string.IsNullOrEmpty(box.Code))
                {
                    builder.Fill(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ, box.Code);
                }
                else if (box.Id.HasValue)
                {
                    builder.Fill(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ, box.Id.Value);
                }
            }
        }

        if (doc.Blocks != null)
        {
            foreach (var block in doc.Blocks)
            {
                if (!string.IsNullOrEmpty(block.Code))
                {
                    builder.SetBlock(block.X, block.Y, block.Z, block.Code);
                }
                else if (block.Id.HasValue)
                {
                    builder.SetBlock(block.X, block.Y, block.Z, block.Id.Value);
                }

                if (block.Sun.HasValue)
                {
                    builder.SetSunlight(block.X, block.Y, block.Z, block.Sun.Value);
                }

                if (block.Light.HasValue)
                {
                    builder.SetBlocklight(block.X, block.Y, block.Z, block.Light.Value);
                }
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Saves a chunk fixture directly to a JSON file.
    /// </summary>
    public static async Task SaveToFileAsync(ChunkFixture fixture, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = ToJson(fixture);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    /// <summary>
    /// Loads a chunk fixture directly from a JSON file.
    /// </summary>
    public static async Task<ChunkFixture> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        string json = await File.ReadAllTextAsync(filePath, ct);
        return FromJson(json);
    }
}
