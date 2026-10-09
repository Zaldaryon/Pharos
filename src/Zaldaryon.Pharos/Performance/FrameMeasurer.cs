using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Performance;

/// <summary>Opens a measured window on a client, and on a server when one is stepped, and reads it.</summary>
internal static class FrameMeasurer
{
    public static async Task<FrameMeasurement> MeasureAsync(HeadlessClient client, EmbeddedServerHost? server, int frames, int serverTicksPerFrame, System.Func<CancellationToken, Task> step, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serverTicksPerFrame);
        int expectedTicks = checked(frames * serverTicksPerFrame);
        if (!client.IsEngineMode) throw new InvalidOperationException("Frames are measured on an engine-mode client: a fixture-mode client runs no render loop.");

        FrameCollector collector = client.RunOnClientThread(() =>
        {
            if (FrameCollector.Active != null) throw new InvalidOperationException("This client's frames are already being measured.");
            FrameCollector created = new(frames, client.Client.eventManager);
            created.Open();
            return created;
        });

        ServerTickCollector? ticks = null;
        long processBefore = GC.GetTotalAllocatedBytes(precise: true);
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        try
        {
            if (server != null)
            {
                ticks = new ServerTickCollector(expectedTicks);
                server.RunOnGameThread(ticks.Open);
            }

            for (int i = 0; i < frames; i++) await step(ct).ConfigureAwait(false);
        }
        finally
        {
            Close(client, server);
        }

        long processAllocated = GC.GetTotalAllocatedBytes(precise: true) - processBefore;
        IReadOnlyList<RendererInfo> renderers = client.Renderers.All();
        List<RendererTiming> rendererTimings = [];
        foreach ((Vintagestory.API.Client.IRenderer renderer, Vintagestory.API.Client.EnumRenderStage stage, TimingStats perFrame, int calls) in collector.Renderers())
        {
            RendererInfo info = renderers.FirstOrDefault(r => ReferenceEquals(r.Renderer, renderer) && r.Stage == stage)
                ?? new RendererInfo(stage, -1, renderer.RenderOrder, renderer.RenderRange, "", renderer.GetType().FullName ?? renderer.GetType().Name, null) { Renderer = renderer };
            rendererTimings.Add(new RendererTiming(info, perFrame, calls));
        }

        return new FrameMeasurement(
            collector.Work(),
            collector.AllocatedBytes,
            processAllocated,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            collector.Stages().Select(s => new StageTiming(s.Stage, s.PerFrame, s.FramesRun, s.Calls)).ToList(),
            rendererTimings,
            collector.GameTickTimes(),
            collector.MainThreadTaskTimes(),
            ticks != null ? new ServerTickTiming(ticks.Ticks(), ticks.AllocatedBytes, ticks.SuspendedTicks) : null);
    }

    // On the hosts' own threads, past any test abort, so a measurement never stays open; a host
    // that died has nothing left to close, and a failure here never hides why the window ended.
    private static void Close(HeadlessClient client, EmbeddedServerHost? server)
    {
        try
        {
            if (client.FrameController.ClientThread is { } thread) thread.Invoke(FrameCollector.Close);
            else FrameCollector.Close();
        }
        catch (Exception)
        {
        }

        try
        {
            server?.RunOnGameThread(ServerTickCollector.Close);
        }
        catch (Exception)
        {
        }
    }
}
