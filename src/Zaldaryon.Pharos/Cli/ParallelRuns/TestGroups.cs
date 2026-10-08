using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>How tests are split between workers.</summary>
public enum RunGrouping
{
    /// <summary>Each test class runs in a worker of its own.</summary>
    Class,

    /// <summary>Each xUnit test collection runs in a worker of its own.</summary>
    Collection,
}

/// <summary>Tests that run together in one worker.</summary>
/// <param name="Name">The class or collection.</param>
/// <param name="Tests">Fully qualified test names, a theory once.</param>
internal sealed record TestGroup(string Name, IReadOnlyList<string> Tests);

/// <summary>Splits listed tests into the groups workers run.</summary>
internal static class TestGroups
{
    /// <summary>The class part of a fully qualified test name: all but the method.</summary>
    public static string ClassOf(string fullyQualifiedName)
    {
        int dot = fullyQualifiedName.LastIndexOf('.');
        return dot > 0 ? fullyQualifiedName[..dot] : fullyQualifiedName;
    }

    /// <summary>The group that holds every test outside <c>Category=Live</c>.</summary>
    public const string OtherTestsGroup = "Tests outside Category=Live";

    /// <summary>
    /// Groups <paramref name="tests"/> by class, or by the collection of each class in
    /// <paramref name="collections"/>.
    /// </summary>
    /// <param name="tests">Fully qualified test names.</param>
    /// <param name="grouping">By class or by collection.</param>
    /// <param name="collections">Each class's collection, for <see cref="RunGrouping.Collection"/>.</param>
    /// <param name="live">
    /// The tests that boot the game. When there are some, every other test goes in one group:
    /// those are quick, and a worker each would spend more time starting than testing.
    /// </param>
    /// <param name="durations">How long each test took last time, to start the longest groups first.</param>
    public static IReadOnlyList<TestGroup> Create(
        IEnumerable<string> tests,
        RunGrouping grouping,
        IReadOnlyDictionary<string, string>? collections = null,
        IReadOnlySet<string>? live = null,
        IReadOnlyDictionary<string, TimeSpan>? durations = null)
    {
        bool splitLive = live is { Count: > 0 };
        List<TestGroup> groups = tests
            .Distinct(StringComparer.Ordinal)
            .GroupBy(test => splitLive && !live!.Contains(test) ? OtherTestsGroup
                : grouping == RunGrouping.Collection && collections != null && collections.TryGetValue(ClassOf(test), out string? collection) ? collection
                : ClassOf(test), StringComparer.Ordinal)
            .Select(g => new TestGroup(g.Key, g.Order(StringComparer.Ordinal).ToList()))
            .ToList();

        // Longest first, so the last group to finish is a short one. A group not seen last time
        // could be long: it goes first; without any history, the larger groups do.
        TimeSpan? Took(TestGroup group) =>
            durations == null || group.Tests.Any(t => !durations.ContainsKey(t)) ? null : group.Tests.Aggregate(TimeSpan.Zero, (sum, t) => sum + durations[t]);
        return groups
            .OrderByDescending(g => Took(g) ?? TimeSpan.MaxValue)
            .ThenByDescending(g => g.Tests.Count)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>How long each test took in a previous merged TRX, theory rows added up.</summary>
    public static IReadOnlyDictionary<string, TimeSpan> Durations(IEnumerable<TrxResult> results)
    {
        Dictionary<string, TimeSpan> durations = new(StringComparer.Ordinal);
        foreach (TrxResult result in results)
        {
            durations[result.FullyQualifiedName] = durations.GetValueOrDefault(result.FullyQualifiedName) + result.Duration;
        }

        return durations;
    }

    /// <summary>
    /// The xUnit collection of each test class in the assembly at <paramref name="assemblyPath"/>,
    /// keyed by the class's name as test names spell it (nested classes with <c>+</c>). Classes
    /// without a collection are left out: each is a collection of its own. With
    /// <c>[assembly: CollectionBehavior(CollectionPerAssembly)]</c>, every class is in one.
    /// </summary>
    /// <remarks>Read from the metadata alone, so nothing the assembly references is loaded.</remarks>
    public static IReadOnlyDictionary<string, string> Collections(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        AttributeValues values = new();

        bool perAssembly = false;
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (!IsXunitAttribute(reader, attribute, "CollectionBehaviorAttribute")) continue;
            CustomAttributeValue<object?> value = attribute.DecodeValue(values);
            // CollectionBehavior.CollectionPerAssembly is 0.
            perAssembly = value.FixedArguments is [{ Value: int behavior }] && behavior == 0;
        }

        Dictionary<TypeDefinitionHandle, string?> own = [];
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            own[handle] = CollectionOn(reader, reader.GetTypeDefinition(handle), values);
        }

        Dictionary<string, string> collections = new(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            string name = NameOf(reader, handle);
            if (perAssembly)
            {
                collections[name] = "(assembly)";
                continue;
            }

            // [Collection] is inherited: a class without one takes its base class's.
            for (TypeDefinitionHandle? current = handle; current is { } type;)
            {
                if (own.GetValueOrDefault(type) is { } collection)
                {
                    collections[name] = collection;
                    break;
                }

                current = reader.GetTypeDefinition(type).BaseType is { IsNil: false, Kind: HandleKind.TypeDefinition } baseType ? (TypeDefinitionHandle)baseType : null;
            }
        }

        return collections;
    }

    private static string? CollectionOn(MetadataReader reader, TypeDefinition type, AttributeValues values)
    {
        foreach (CustomAttributeHandle handle in type.GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (IsXunitAttribute(reader, attribute, "CollectionAttribute")
                && attribute.DecodeValue(values).FixedArguments is [{ Value: string name }])
            {
                return name;
            }
        }

        return null;
    }

    private static bool IsXunitAttribute(MetadataReader reader, CustomAttribute attribute, string name)
    {
        EntityHandle parent = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };

        return parent.Kind switch
        {
            HandleKind.TypeReference => reader.GetTypeReference((TypeReferenceHandle)parent) is var r
                && reader.GetString(r.Name) == name && reader.GetString(r.Namespace) == "Xunit",
            HandleKind.TypeDefinition => reader.GetTypeDefinition((TypeDefinitionHandle)parent) is var d
                && reader.GetString(d.Name) == name && reader.GetString(d.Namespace) == "Xunit",
            _ => false,
        };
    }

    // As test names spell it: Namespace.Outer+Inner.
    private static string NameOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return NameOf(reader, declaring) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    // Decodes the attribute arguments this needs: strings and the CollectionBehavior enum.
    private sealed class AttributeValues : ICustomAttributeTypeProvider<object?>
    {
        public object? GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode;

        public object? GetSystemType() => typeof(Type);

        public object? GetSZArrayType(object? elementType) => null;

        public object? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;

        // System.Type arguments are serialized names, not enums: the decoder must know them apart.
        public object? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            return reader.GetString(reference.Namespace) == "System" && reader.GetString(reference.Name) == "Type" ? typeof(Type) : null;
        }

        public object? GetTypeFromSerializedName(string name) => null;

        public PrimitiveTypeCode GetUnderlyingEnumType(object? type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(object? type) => type is Type;
    }
}

/// <summary>vstest filter expressions.</summary>
internal static class Filter
{
    /// <summary>A filter that matches <paramref name="fullyQualifiedName"/> exactly.</summary>
    public static string ForTest(string fullyQualifiedName) => "FullyQualifiedName=" + Escape(fullyQualifiedName);

    /// <summary>A filter that matches exactly the tests of <paramref name="group"/>.</summary>
    public static string ForGroup(TestGroup group) => string.Join("|", group.Tests.Select(ForTest));

    // The filter syntax's own characters, escaped as vstest expects.
    private static string Escape(string value)
    {
        System.Text.StringBuilder escaped = new(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or '(' or ')' or '|' or '&' or '=' or '!' or '~') escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
