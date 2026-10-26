using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A snapshot of a captured framebuffer.</summary>
public sealed record PipelineFramebufferSnapshot(int Width, int Height, string Attachment);

/// <summary>
/// Reads the client's render pipeline: framebuffers and stages.
/// </summary>
public sealed class PipelineDriver
{
    private readonly HeadlessClient _client;

    internal PipelineDriver(HeadlessClient client) => _client = client;

    /// <summary>Lists the available framebuffers.</summary>
    public IReadOnlyList<string> Framebuffers() => _client.RunOnClientThread(() =>
    {
        return Enum.GetNames<EnumFrameBuffer>().ToList();
    });

    /// <summary>Captures a framebuffer at the end of the current frame.</summary>
    public PipelineFramebufferSnapshot Capture(EnumFrameBuffer buffer, string attachment = "Color") =>
        _client.RunOnClientThread(() =>
        {
            var fb = _client.Client.api.Render.FrameBuffers[(int)buffer];
            return new PipelineFramebufferSnapshot(fb.Width, fb.Height, attachment.ToString());
        });
}