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
    public bool ConfigureMesaEnvironment { get; init; } = true;
    public bool ForceSoftwareRendering { get; init; }
    public string MesaGlVersionOverride { get; init; } = "4.5";
    public string MesaGlslVersionOverride { get; init; } = "450";
    public string? LinuxDisplay { get; init; }
}
