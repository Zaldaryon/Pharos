namespace Zaldaryon.Pharos.Server;

/// <summary>
/// A budget a soak run broke, with the reading that broke it.
/// </summary>
/// <param name="Budget">The budget's name, such as <c>entities</c> or <c>tick p99</c>.</param>
/// <param name="Limit">The limit the budget sets.</param>
/// <param name="Value">The reading that broke the limit.</param>
/// <param name="Tick">The tick the reading was taken at.</param>
/// <param name="TotalDays">The calendar day the reading was taken at.</param>
/// <param name="Gating">
/// Whether the breach fails the run. A breach that does not gate is recorded and reported, and
/// the run goes on.
/// </param>
public sealed record SoakBreach(string Budget, double Limit, double Value, long Tick, double TotalDays, bool Gating);

/// <summary>
/// A soak run broke a gating budget. The run stops at the first reading that breaks it.
/// </summary>
public sealed class SoakBudgetExceededException : Exception
{
    /// <summary>Creates the exception for <paramref name="breach"/> and the run's <paramref name="report"/>.</summary>
    public SoakBudgetExceededException(SoakBreach breach, SoakReport report)
        : base(Describe(breach))
    {
        Breach = breach;
        Report = report;
    }

    /// <summary>The breach that stopped the run.</summary>
    public SoakBreach Breach { get; }

    /// <summary>The run's report up to the breach.</summary>
    public SoakReport Report { get; }

    private static string Describe(SoakBreach breach) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Soak budget '{breach.Budget}' broke on day {breach.TotalDays:0.###} at tick {breach.Tick}: value {breach.Value:0.###} against a limit of {breach.Limit:0.###}.");
}
