namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Represents the outcome of a mod staging operation.
/// </summary>
public sealed class ModStagingResult
{
    /// <summary>
    /// Gets whether the staging operation completed successfully.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the list of files that were staged. Empty on failure.
    /// </summary>
    public IReadOnlyList<string> StagedFiles { get; }

    /// <summary>
    /// Gets the error message if staging failed. Null on success.
    /// </summary>
    public string? ErrorMessage { get; }

    private ModStagingResult(bool success, IReadOnlyList<string> stagedFiles, string? errorMessage)
    {
        Success = success;
        StagedFiles = stagedFiles;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful staging result with the list of staged files.
    /// </summary>
    /// <param name="files">The list of files that were staged.</param>
    /// <returns>A successful ModStagingResult.</returns>
    public static ModStagingResult Of(IReadOnlyList<string> files)
    {
        return new ModStagingResult(true, files, null);
    }

    /// <summary>
    /// Creates a failed staging result with an error message.
    /// </summary>
    /// <param name="message">The error message describing the failure.</param>
    /// <returns>A failed ModStagingResult.</returns>
    public static ModStagingResult Failure(string message)
    {
        return new ModStagingResult(false, Array.Empty<string>(), message);
    }
}
