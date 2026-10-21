using Vintagestory.API.Common;
using Vintagestory.Common;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>One row of a command's privilege matrix: the command, the role that runs it, and what should happen.</summary>
/// <param name="Command">The command line, such as <c>/mycmd reset</c>.</param>
/// <param name="Role">The role code that runs it, such as <c>suplayer</c>.</param>
/// <param name="Expected">What the role should get: <c>allowed</c>, <c>denied</c>, or null when the row records the outcome only.</param>
public sealed record CommandPrivilegeCase(string Command, string Role, string? Expected)
{
    /// <summary>What a role that may run the command gets.</summary>
    public const string Allowed = "allowed";

    /// <summary>What a role that may not run the command gets.</summary>
    public const string Denied = "denied";

    /// <summary>
    /// Runs the command as the role and returns whether the role was allowed to run it. It fails the
    /// test when the row expects an outcome and the role got another one.
    /// </summary>
    /// <param name="host">The server that runs the command.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="InvalidOperationException">The server does not know the role.</exception>
    public async Task<bool> AssertAsync(EmbeddedServerHost host, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        Caller caller = host.RunOnGameThread(() => CallerFor(host, Role));
        CommandResult result = await host.Commands.ExecuteAsync(Command, caller, ct).ConfigureAwait(false);
        bool allowed = result.Status == EnumCommandStatus.Success;

        if (Expected != null && !string.Equals(Expected, allowed ? Allowed : Denied, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The role '{Role}' was {(allowed ? "allowed" : "denied")} to run '{Command}', but the row expected '{Expected}'. The server answered: {result.Status} {result.StatusMessage}");
        }

        return allowed;
    }

    /// <summary>A caller that has the role's privileges and nothing else.</summary>
    private static Caller CallerFor(EmbeddedServerHost host, string role)
    {
        PlayerRole? serverRole = host.Server.Config.RolesByCode
            .FirstOrDefault(candidate => string.Equals(candidate.Key, role, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (serverRole == null)
        {
            throw new InvalidOperationException(
                $"The server defines no role '{role}'. Its roles: {string.Join(", ", host.Server.Config.RolesByCode.Keys)}.");
        }

        return new Caller
        {
            Type = EnumCallerType.Console,
            CallerRole = role,
            CallerPrivileges = serverRole.Privileges?.ToArray() ?? [],
        };
    }
}
