using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>Runs an xUnit v3 server theory once per role, and records whether each role may run the command.</summary>
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
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        if (testMethod.GetParameters() is not [{ ParameterType: var type }] || type != typeof(CommandPrivilegeCase))
            throw new ArgumentException("[CommandPrivilegeMatrix] requires a theory method with one CommandPrivilegeCase parameter.");

        string[]? expectations = Expected?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (expectations != null && expectations.Length > Roles.Count)
            throw new ArgumentException($"[CommandPrivilegeMatrix] expects {expectations.Length} outcomes for {Roles.Count} role(s).");

        IReadOnlyCollection<ITheoryDataRow> rows = Roles.Select((role, index) => (ITheoryDataRow)new TheoryDataRow(
            new CommandPrivilegeCase(Command, role, expectations != null && index < expectations.Length ? expectations[index] : null))).ToList();
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}
