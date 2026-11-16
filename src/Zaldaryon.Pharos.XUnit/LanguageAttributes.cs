using System.Reflection;
using Xunit.Sdk;
using Zaldaryon.Pharos.Translations;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Runs a theory once per language shipped by Vintage Story, passing the language code as its
/// <see cref="string"/> parameter to apply with <c>Client.Lang.Use(lang)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllLanguagesAttribute : DataAttribute
{
    /// <summary>Whether to append the synthetic <c>pseudo-long</c> locale to the sweep.</summary>
    public bool IncludePseudoLong { get; set; }

    /// <summary>Language codes to skip from the sweep.</summary>
    public string[] Exclude { get; set; } = [];

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        LanguageCatalog.EnsureStringParameter(testMethod, nameof(AllLanguagesAttribute));
        return LanguageCatalog.SelectSweepLanguages(IncludePseudoLong, Exclude)
            .Select(lang => new object[] { lang });
    }
}

/// <summary>
/// Runs a theory once per specified language code, passing each code as its <see cref="string"/>
/// parameter to apply with <c>Client.Lang.Use(lang)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LanguagesAttribute(params string[] languages) : DataAttribute
{
    /// <summary>The language codes to run.</summary>
    public IReadOnlyList<string> Languages { get; } = LanguageCatalog.ValidateExplicitLanguages(languages);

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        LanguageCatalog.EnsureStringParameter(testMethod, nameof(LanguagesAttribute));
        return Languages.Select(lang => new object[] { lang });
    }
}
