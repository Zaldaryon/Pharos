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

    /// <summary>
    /// How much of the vanilla client startup to run. <see cref="ClientBootMode.Fixture"/> by
    /// default; use <see cref="ClientBootMode.Engine"/> to join a real server.
    /// </summary>
    public ClientBootMode BootMode { get; init; } = ClientBootMode.Fixture;

    /// <summary>
    /// Whether an engine-mode client goes through the survival mod's "create character" dialog on
    /// its own when it joins as a new player: it confirms the appearance the dialog opens with,
    /// moves to <see cref="CharacterClass"/> and confirms it, one step per frame. Without it the
    /// client never reports ready, so the server never sends it chunks. Turn it off to drive the
    /// dialog from a test.
    /// </summary>
    public bool CompleteCharacterSelection { get; init; } = true;

    /// <summary>
    /// The character class picked in the dialog when <see cref="CompleteCharacterSelection"/> is on,
    /// by its code in <c>config/characterclasses.json</c>.
    /// </summary>
    public string CharacterClass { get; init; } = "commoner";
}
