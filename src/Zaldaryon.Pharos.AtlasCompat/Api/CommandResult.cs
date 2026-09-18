// Atlas.Api.CommandResult is a type-forwarding alias to Zaldaryon.Pharos.XUnit.CommandResult.
// For migration compatibility, this file provides a using alias for the Atlas namespace.
//
// Usage:
//   using Atlas.Api;
//   CommandResult result = ...;  // Actually uses Zaldaryon.Pharos.XUnit.CommandResult
//
// After migration, replace with:
//   using Zaldaryon.Pharos.XUnit;
//   CommandResult result = ...;

using PharosCommandResult = Zaldaryon.Pharos.XUnit.CommandResult;

namespace Atlas.Api;

/// <summary>
/// Compatibility alias for Atlas.Api.CommandResult.
/// This class wraps <see cref="Zaldaryon.Pharos.XUnit.CommandResult"/> for source compatibility.
/// </summary>
/// <remarks>
/// <para>
/// For new code, use <see cref="Zaldaryon.Pharos.XUnit.CommandResult"/> directly.
/// </para>
/// </remarks>
[Obsolete("Use CommandResult from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
public sealed class CommandResult
{
    private readonly PharosCommandResult _inner;

    /// <summary>
    /// Initializes a new command result wrapping a Pharos command result.
    /// </summary>
    public CommandResult(PharosCommandResult inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// Gets the underlying Pharos command result.
    /// </summary>
    public PharosCommandResult Inner => _inner;

    /// <summary>
    /// Returns true if the command executed successfully.
    /// </summary>
    public bool Ok => _inner.Ok;

    /// <summary>
    /// Gets the status message from the command execution.
    /// </summary>
    public string? StatusMessage => _inner.StatusMessage;

    /// <summary>
    /// Gets the return value from the command execution.
    /// </summary>
    public object? ReturnValue => _inner.ReturnValue;

    /// <summary>
    /// Implicitly converts a Pharos CommandResult to an Atlas CommandResult.
    /// </summary>
    public static implicit operator CommandResult(PharosCommandResult pharosResult)
        => new(pharosResult);

    /// <summary>
    /// Implicitly converts an Atlas CommandResult to a Pharos CommandResult.
    /// </summary>
    public static implicit operator PharosCommandResult(CommandResult atlasResult)
        => atlasResult._inner;
}
