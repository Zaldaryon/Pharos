using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Thrown when a <see cref="StrictBootAttribute"/> class's client or server logged, while
/// booting, what its <see cref="AllowBootDiagnosticAttribute"/>s do not allow.
/// </summary>
public sealed class BootDiagnosticsException : Exception
{
    /// <summary>Creates the exception for <paramref name="result"/>.</summary>
    public BootDiagnosticsException(BootDiagnosticsResult result)
        : base("The boot logged diagnostics the class does not allow ([StrictBoot]). Allow expected ones with [AllowBootDiagnostic]." + Environment.NewLine + result.Describe())
    {
        Result = result;
    }

    /// <summary>What was unexpected and which allowances were not met.</summary>
    public BootDiagnosticsResult Result { get; }

    /// <summary>The test whose boot failed.</summary>
    internal string? Test { get; init; }
}
