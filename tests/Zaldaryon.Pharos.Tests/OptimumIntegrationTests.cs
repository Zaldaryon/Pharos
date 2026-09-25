using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

// Optimum ships a patched VintagestoryAPI, so these checks bind by reflection and stay compilable
// against the official client that CI downloads. Without an Optimum build they skip by design.
public sealed class OptimumFactAttribute : FactAttribute
{
    internal const string DiagnosticsTypeName = "Vintagestory.API.Config.OptimumDiagnostics, VintagestoryAPI";

    public OptimumFactAttribute()
    {
        if (Type.GetType(DiagnosticsTypeName) is null)
        {
            Skip = "VINTAGE_STORY does not point at an Optimum build.";
        }
    }
}

[Collection("Sequential")]
public sealed class OptimumIntegrationTests
{
    static OptimumIntegrationTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [OptimumFact]
    public async Task OptimumFork_TessellatesInjectedChunk_RecordsTessellationProgress()
    {
        // VintagestoryAPI.dll is copied from VINTAGE_STORY at build time, so the diagnostics type
        // resolving at all is the proof that the loaded engine is the patched one.
        Type diagnostics = Type.GetType(OptimumFactAttribute.DiagnosticsTypeName)!;

        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        ChunkPos targetChunk = new(10, 2, 10);
        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(targetChunk)
            .WithDefaultSunlight(31)
            .Fill(0, 0, 0, 31, 1, 31, 1)
            .SetBlock(16, 2, 16, 2)
            .Build();

        client.InjectChunk(fixture, triggerTesselation: true);

        // The frame loop drives Optimum's patched tesselator pool, upload handoff and diagnostics,
        // so assert through it rather than calling the tessellation entry point directly.
        bool meshed = await client.WaitForChunkMeshed(targetChunk, maxFrames: 10, dt: 1f / 60f);
        Assert.True(meshed, "Injected chunk must mesh through the frame loop.");

        string summary = ReadTessellationSummary(diagnostics);
        Assert.True(ReadTessellatedChunks(summary) > 0, $"Optimum tessellation counter must advance. {summary}");
    }

    private static string ReadTessellationSummary(Type diagnostics)
    {
        MethodInfo summaryMethod = diagnostics.GetMethod("GetTessellationSummary", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"{OptimumFactAttribute.DiagnosticsTypeName} must expose a public static GetTessellationSummary() for headless progress reporting.");

        return (string)summaryMethod.Invoke(null, null)!;
    }

    private static long ReadTessellatedChunks(string summary)
    {
        const string marker = "chunks=";
        int start = summary.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return 0;
        }

        start += marker.Length;
        int end = summary.IndexOf(',', start);
        if (end < 0)
        {
            end = summary.Length;
        }

        return long.TryParse(summary[start..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out long chunks)
            ? chunks
            : 0;
    }
}
