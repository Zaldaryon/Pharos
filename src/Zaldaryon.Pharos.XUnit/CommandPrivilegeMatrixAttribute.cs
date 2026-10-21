using System.Reflection;
using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>Runs a server theory once per role, and records whether each role may run the command.</summary>
/// <remarks>
/// The method takes one <see cref="CommandPrivilegeCase"/> argument. Set <see cref="Expected"/> to
/// a comma-separated list of <c>allowed</c> and <c>denied</c>, one for each role, to have every row
/// checked.
/// <code>
/// [ServerTheory, CommandPrivilegeMatrix("/mycmd reset", "suplayer", "admin", Expected = "denied,allowed")]
/// public Task ResetNeedsAdmin(CommandPrivilegeCase privilege) => privilege.AssertAsync(Host!);
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CommandPrivilegeMatrixAttribute : DataAttribute
{
    /// <summary>Runs the command once per role.</summary>
    /// <param name="command">The command line, such as <c>/mycmd reset</c>.</param>
    /// <param name="roles">The role codes that run it, such as <c>suplayer</c>.</param>
    public CommandPrivilegeMatrixAttribute(string command, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (roles.Length == 0) throw new ArgumentException("[CommandPrivilegeMatrix] needs at least one role.", nameof(roles));
        if (roles.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A role of [CommandPrivilegeMatrix] cannot be empty.", nameof(roles));

        Command = command;
        Roles = Array.AsReadOnly((string[])roles.Clone());
    }

    /// <summary>The command line every row runs.</summary>
    public string Command { get; }

    /// <summary>The role codes, one row each.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// What each role should get, in the order of the roles: <c>allowed</c> or <c>denied</c>. A row
    /// without an expectation records the outcome only.
    /// </summary>
    public string? Expected { get; set; }

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        if (testMethod.GetParameters() is not [{ ParameterType: var type }] || type != typeof(CommandPrivilegeCase))
            throw new ArgumentException("[CommandPrivilegeMatrix] requires a theory method with one CommandPrivilegeCase parameter.");

        string[]? expectations = Expected?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (expectations != null && expectations.Length > Roles.Count)
            throw new ArgumentException($"[CommandPrivilegeMatrix] expects {expectations.Length} outcomes for {Roles.Count} role(s).");

        return Roles.Select((role, index) => new object[]
        {
            new CommandPrivilegeCase(Command, role, expectations != null && index < expectations.Length ? expectations[index] : null),
        });
    }
}
