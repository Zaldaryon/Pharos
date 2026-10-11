using System.Globalization;
using System.Reflection;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// The seed a scenario test runs with, from <see cref="SeededAttribute"/> and
/// <c>PHAROS_RANDOM_SEED</c>, or why it cannot run with one.
/// </summary>
/// <param name="Seed">The seed.</param>
/// <param name="Source">Where it came from, as the test's output says it.</param>
/// <param name="Error">Why the seed could not be read; the test fails before its body.</param>
internal sealed record ScenarioRandomSeed(int Seed, string Source, string? Error = null)
{
    /// <summary>
    /// The seed of the test, or null when no <see cref="SeededAttribute"/> covers it.
    /// <paramref name="environment"/> reads a variable; defaults to the process environment.
    /// </summary>
    public static ScenarioRandomSeed? Resolve(Type testClass, MethodInfo method, object?[]? arguments, Func<string, string?>? environment = null)
    {
        (SeededAttribute attribute, string where)? found =
            method.GetCustomAttribute<SeededAttribute>(inherit: true) is { } onMethod ? (onMethod, "the method")
            : testClass.GetCustomAttribute<SeededAttribute>(inherit: true) is { } onClass ? (onClass, "the class")
            : testClass.Assembly.GetCustomAttribute<SeededAttribute>() is { } onAssembly ? (onAssembly, "the assembly")
            : null;
        if (found is not { } seeded) return null;

        string? variable = (environment ?? Environment.GetEnvironmentVariable)(SeededAttribute.SeedVariable);
        if (!string.IsNullOrWhiteSpace(variable))
        {
            return int.TryParse(variable.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fromVariable)
                ? new ScenarioRandomSeed(fromVariable, SeededAttribute.SeedVariable)
                : new ScenarioRandomSeed(0, SeededAttribute.SeedVariable, $"{SeededAttribute.SeedVariable} must be a 32-bit integer, not '{variable}'.");
        }

        if (seeded.attribute.Seed is { } fixedSeed) return new ScenarioRandomSeed(fixedSeed, $"[Seeded] on {seeded.where}");

        return new ScenarioRandomSeed(SeededAttribute.SeedFor(TestName(testClass, method, arguments)), $"a hash of the test's name, [Seeded] on {seeded.where}");
    }

    /// <summary>
    /// The name the default seed hashes: the class's full name and the method, then the theory
    /// arguments, formatted under the invariant culture.
    /// </summary>
    public static string TestName(Type testClass, MethodInfo method, object?[]? arguments)
    {
        string name = $"{testClass.FullName}.{method.Name}";
        return arguments is { Length: > 0 } ? name + "(" + string.Join(", ", arguments.Select(Format)) + ")" : name;
    }

    private static string Format(object? argument) => argument switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        char character => "'" + character + "'",
        Type type => "typeof(" + type.FullName + ")",
        System.Collections.IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Format)) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => argument.ToString() ?? "",
    };

    /// <summary>The line the test's output starts with, after the versions.</summary>
    public string OutputLine => Error != null
        ? $"Random seed: none, {Error}{Environment.NewLine}"
        : $"Random seed: {Seed} ({Source}). Replay it with {SeededAttribute.SeedVariable}={Seed}.{Environment.NewLine}";
}
