using System.Collections.Generic;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Immutable snapshot of shader program state captured on demand.
/// Call <see cref="ShaderInspector.Snapshot"/> to obtain one.
/// </summary>
public sealed record ShaderSnapshot
{
    /// <summary>
    /// OpenGL program ID of the shader that was active when the snapshot was taken, or 0 if none.
    /// </summary>
    public int ActiveProgramId { get; init; }

    /// <summary>
    /// Pass name of the active shader program (e.g. "chunkopaque"), or an empty string if none.
    /// </summary>
    public string ActiveProgramName { get; init; } = string.Empty;

    /// <summary>
    /// Last uploaded value for each uniform name across all programs observed since
    /// <see cref="ShaderInspector.Enable"/> or the last <see cref="ShaderInspector.Reset"/>.
    /// Values are boxed: float, int, float[], or int[] depending on the uniform type.
    /// </summary>
    public IReadOnlyDictionary<string, object> Uniforms { get; init; }
        = new Dictionary<string, object>();

    /// <summary>
    /// Currently bound texture ID per texture unit. Key = texture unit index, value = texture ID.
    /// </summary>
    public IReadOnlyDictionary<int, int> BoundTextures { get; init; }
        = new Dictionary<int, int>();

    /// <summary>
    /// Number of duplicate (redundant) uploads detected per uniform name.
    /// A redundant upload is one where the same value was uploaded to the same uniform
    /// without an intervening different value.
    /// </summary>
    public IReadOnlyDictionary<string, int> RedundantUploads { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Total number of uniform uploads intercepted since the last Reset.</summary>
    public int TotalUploads { get; init; }

    /// <summary>Total number of redundant (duplicate-value) uploads detected since the last Reset.</summary>
    public int RedundantUploadCount { get; init; }

    /// <summary>An empty snapshot with zero counts and empty collections.</summary>
    public static readonly ShaderSnapshot Empty = new();
}
