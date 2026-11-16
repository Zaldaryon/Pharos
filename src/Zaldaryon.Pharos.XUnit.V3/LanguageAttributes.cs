using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;
using Zaldaryon.Pharos.Translations;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// Runs an xUnit v3 theory once per language shipped by Vintage Story, passing the language code
/// as its <see cref="string"/> parameter to apply with <c>Client.Lang.Use(lang)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllLanguagesAttribute : DataAttribute
{
    /// <summary>Whether to append the synthetic <c>pseudo-long</c> locale to the sweep.</summary>
    public bool IncludePseudoLong { get; set; }

    /// <summary>Language codes to skip from the sweep.</summary>
    public string[] Exclude { get; set; } = [];

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        LanguageCatalog.EnsureStringParameter(testMethod, nameof(AllLanguagesAttribute));
        IReadOnlyCollection<ITheoryDataRow> rows = LanguageCatalog.SelectSweepLanguages(IncludePseudoLong, Exclude)
            .Select(lang => (ITheoryDataRow)new TheoryDataRow(lang))
            .ToList();
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}

/// <summary>
/// Runs an xUnit v3 theory once per specified language code, passing each code as its
/// <see cref="string"/> parameter to apply with <c>Client.Lang.Use(lang)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LanguagesAttribute(params string[] languages) : DataAttribute
{
    /// <summary>The language codes to run.</summary>
    public IReadOnlyList<string> Languages { get; } = LanguageCatalog.ValidateExplicitLanguages(languages);

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        LanguageCatalog.EnsureStringParameter(testMethod, nameof(LanguagesAttribute));
        IReadOnlyCollection<ITheoryDataRow> rows = Languages
            .Select(lang => (ITheoryDataRow)new TheoryDataRow(lang))
            .ToList();
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}
