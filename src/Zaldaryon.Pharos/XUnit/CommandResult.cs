using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Represents the result of executing a server console or player command.
/// </summary>
/// <param name="Status">The command execution status.</param>
/// <param name="StatusMessage">The message returned by the command, if any.</param>
/// <param name="ReturnValue">Optional return value from the command handler.</param>
public sealed record CommandResult(
    EnumCommandStatus Status,
    string? StatusMessage,
    object? ReturnValue = null)
{
    /// <summary>
    /// Returns true if the command ran and succeeded, or was deferred: not an error, not an
    /// unknown command.
    /// </summary>
    public bool Ok => Status is EnumCommandStatus.Success or EnumCommandStatus.Deferred;

    /// <summary>
    /// Creates a successful command result with an optional message.
    /// </summary>
    public static CommandResult Success(string? message = null, object? returnValue = null)
        => new(EnumCommandStatus.Success, message, returnValue);

    /// <summary>
    /// Creates an error command result with the specified message.
    /// </summary>
    public static CommandResult Error(string message)
        => new(EnumCommandStatus.Error, message);

    /// <summary>
    /// Creates a deferred command result indicating async execution.
    /// </summary>
    public static CommandResult Deferred()
        => new(EnumCommandStatus.Deferred, null);
}
