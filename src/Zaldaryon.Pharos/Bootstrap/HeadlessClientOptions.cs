namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Configuration options for bootstrapping a headless Vintage Story client.
/// </summary>
public sealed class HeadlessClientOptions
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public string? GameInstallPath { get; init; }
    public string? DataPath { get; init; }
    public string? AssetsPath { get; init; }
    public bool DisableAudio { get; init; } = true;
    public bool UseNullAudioDevice { get; init; } = true;
}
