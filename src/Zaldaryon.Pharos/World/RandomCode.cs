using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.World;

/// <summary>
/// What the code of an assembly does with random generators: the fields Pharos seeds, and what
/// it cannot reach. Read once per assembly and kept.
/// </summary>
internal static partial class RandomCode
{
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> s_instanceFields = new();
    private static readonly ConcurrentDictionary<Assembly, StaticFieldSet> s_staticFields = new();
    private static readonly ConcurrentDictionary<Assembly, Lazy<Facts>> s_facts = new();

    /// <summary>The kinds of object whose random fields Pharos reaches on a live server or client.</summary>
    private static readonly Type[] s_reached =
    [
        typeof(ServerMain), typeof(ClientMain), typeof(ServerSystem), typeof(ClientSystem), typeof(ModSystem),
        typeof(CollectibleObject), typeof(CollectibleBehavior), typeof(Entity), typeof(EntityBehavior),
        typeof(BlockEntity), typeof(BlockEntityBehavior),
    ];

    // Where a reached object sets up its fields before Pharos seeds it.
    private static readonly HashSet<string> s_setupMethods =
    [
        "Initialize", "OnLoaded", "AfterInitialized", "Start", "StartPre", "StartServerSide", "StartClientSide",
        "AssetsLoaded", "AssetsFinalize", "OnBlockLoaded", "Init",
    ];

    /// <summary>The block entity method whose callers Pharos patches, to seed block entities made during a test.</summary>
    internal static readonly MethodInfo BlockEntityInitialize =
        typeof(BlockEntity).GetMethod(nameof(BlockEntity.Initialize), [typeof(ICoreAPI)])!;

    /// <summary>The entity method whose callers Pharos patches, to seed entities made during a test.</summary>
    internal static readonly MethodInfo EntityAfterInitialized =
        typeof(Entity).GetMethod(nameof(Entity.AfterInitialized), [typeof(bool)])!;

    /// <summary>
    /// The base method <paramref name="called"/> overrides when it is one Pharos seeds after: a
    /// block entity's <c>Initialize</c> or an entity's <c>AfterInitialized</c>; null otherwise.
    /// </summary>
    internal static MethodInfo? SeededAfter(MethodInfo called)
    {
        if (called.Name != BlockEntityInitialize.Name && called.Name != EntityAfterInitialized.Name) return null;
        MethodInfo definition = called.GetBaseDefinition();
        return definition == BlockEntityInitialize || definition == EntityAfterInitialized ? definition : null;
    }

    /// <summary>Whether Pharos reaches objects of <paramref name="type"/> on a live server or client.</summary>
    public static bool Reached(Type type) => s_reached.Any(r => r.IsAssignableFrom(type));

    /// <summary>Whether a field of this type holds a generator Pharos seeds.</summary>
    public static bool IsRandomType(Type type) => typeof(Random).IsAssignableFrom(type) || type == typeof(ThreadLocal<Random>);

    /// <summary>Whether <paramref name="field"/> holds a generator; false when its type cannot be loaded.</summary>
    public static bool IsRandomField(FieldInfo field)
    {
        try
        {
            return IsRandomType(field.FieldType);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The random instance fields of <paramref name="type"/> and its base types.</summary>
    public static FieldInfo[] InstanceFields(Type type) => s_instanceFields.GetOrAdd(type, static t =>
    {
        List<FieldInfo> fields = [];
        for (Type? current = t; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (IsRandomField(field)) fields.Add(field);
            }
        }

        return [.. fields];
    });

    /// <summary>The random static fields of <paramref name="assembly"/>.</summary>
    public static StaticFieldSet StaticFields(Assembly assembly) => s_staticFields.GetOrAdd(assembly, static a =>
    {
        List<FieldInfo> shared = [], perThread = [];
        List<UnseededRandom> unseedable = [];
        foreach (Type type in TypesOf(a))
        {
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch (Exception)
            {
                continue;
            }

            // A static constructor runs the first time the type is used, which can be after seeding. It would then
            // replace a seeded field with an unseeded one. Running it now makes the field's value the one seeding reseeds.
            if (fields.Any(IsRandomField))
            {
                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
                catch (Exception)
                {
                    // A constructor that fails leaves its fields as they are; the field is reported as it is.
                }
            }

            foreach (FieldInfo field in fields)
            {
                if (field.IsLiteral || !IsRandomField(field)) continue;
                if (type.ContainsGenericParameters)
                {
                    unseedable.Add(new UnseededRandom(Member(field), "a static field of a generic type, which has one value per type argument", a));
                }
                else if (field.IsDefined(typeof(ThreadStaticAttribute)))
                {
                    perThread.Add(field);
                }
                else
                {
                    shared.Add(field);
                }
            }
        }

        return new StaticFieldSet(shared, perThread, unseedable);
    });

    /// <summary>What <paramref name="assembly"/>'s code does that Pharos cannot seed, and who initializes block entities and entities.</summary>
    public static Facts FactsOf(Assembly assembly) => s_facts.GetOrAdd(assembly, a => new Lazy<Facts>(() => Read(a))).Value;

    /// <summary>A field as reports name it: the declaring type's full name and the field's name.</summary>
    public static string Member(FieldInfo field) => $"{field.DeclaringType?.FullName}.{field.Name}";

    /// <summary>A method as reports name it, lambdas and state machines under the method that holds them.</summary>
    public static string Member(MethodBase method)
    {
        Type? type = method.DeclaringType;
        string name = method.Name;
        if (Generated(name) is { } fromMethod)
        {
            name = fromMethod;
        }
        else if (type != null && Generated(type.Name) is { } fromType && type.Name.Contains("d__", StringComparison.Ordinal))
        {
            name = fromType;
        }

        while (type is { DeclaringType: not null } && (type.Name.StartsWith('<') || type.IsDefined(typeof(CompilerGeneratedAttribute))))
        {
            type = type.DeclaringType;
        }

        return $"{type?.FullName}.{name}";
    }

    private static string? Generated(string name) => GeneratedName().Match(name) is { Success: true } match && match.Groups[1].Length > 0 ? match.Groups[1].Value : null;

    [GeneratedRegex("^<([^>]*)>")]
    private static partial Regex GeneratedName();

    private static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.Where(t => t != null).Cast<Type>()];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static Facts Read(Assembly assembly)
    {
        List<UnseededRandom> unseeded = [];
        List<MethodBase> initializers = [];
        foreach (Type type in TypesOf(assembly))
        {
            bool generated = type.Name.StartsWith('<') || type.IsDefined(typeof(CompilerGeneratedAttribute));
            if (!generated && !typeof(Random).IsAssignableFrom(type) && !Reached(type))
            {
                foreach (FieldInfo field in SafeFields(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (IsRandomField(field)) unseeded.Add(new UnseededRandom(Member(field), "an instance field of an object Pharos does not reach", assembly));
                }
            }

            foreach (MethodBase method in SafeMethods(type))
            {
                ReadMethod(assembly, method, unseeded, initializers);
            }
        }

        return new Facts(unseeded.DistinctBy(u => (u.Member, u.Reason)).ToList(), initializers);
    }

    private static FieldInfo[] SafeFields(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetFields(flags);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IEnumerable<MethodBase> SafeMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        try
        {
            return [.. type.GetMethods(flags), .. type.GetConstructors(flags)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void ReadMethod(Assembly assembly, MethodBase method, List<UnseededRandom> unseeded, List<MethodBase> initializers)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return;
        }

        if (il == null || il.Length == 0) return;

        IReadOnlyList<Il.Instruction> code;
        try
        {
            code = Il.Read(il);
        }
        catch (Exception)
        {
            return;
        }

        bool initializes = false;
        for (int i = 0; i < code.Count; i++)
        {
            Il.Instruction instruction = code[i];
            if (instruction.OpCode == OpCodes.Callvirt && (i == 0 || code[i - 1].OpCode != OpCodes.Constrained)
                && ResolveMethod(method, instruction.Token) is MethodInfo called && SeededAfter(called) != null)
            {
                initializes = true;
            }

            if (instruction.OpCode != OpCodes.Newobj || ResolveMethod(method, instruction.Token) is not ConstructorInfo ctor
                || ctor.DeclaringType is not { } created || !typeof(Random).IsAssignableFrom(created)) continue;

            string? clock = ctor.GetParameters().Length == 0 ? "no seed" : ClockBefore(method, code, i);
            if (clock == null) continue;

            int next = i + 1;
            while (next < code.Count && code[next].OpCode == OpCodes.Dup) next++;
            Il.Instruction? after = next < code.Count ? code[next] : null;
            string seeded = clock == "no seed" ? "with no seed" : $"seeded from {clock}";

            // A static's initializer runs before Pharos seeds it; a store anywhere else, such as a
            // lazy x ??= new Random(), can make a new generator after.
            if (after?.OpCode == OpCodes.Stsfld)
            {
                if (method.IsConstructor && method.IsStatic) continue;
                FieldInfo? field = ResolveField(method, after.Value.Token);
                unseeded.Add(new UnseededRandom(Member(method), $"sets {(field != null ? Member(field) : "a static field")} to a new {created.Name} {seeded}, which a seeding made before does not cover", assembly));
                continue;
            }

            if (after?.OpCode == OpCodes.Stfld)
            {
                FieldInfo? field = ResolveField(method, after.Value.Token);
                if (field?.DeclaringType is not { } owner || !Reached(owner)) continue;
                if (method.IsConstructor || s_setupMethods.Contains(Generated(method.Name) ?? method.Name)) continue;
                unseeded.Add(new UnseededRandom(Member(method), $"sets {Member(field)} to a new Random {seeded}, after Pharos seeds it", assembly));
                continue;
            }

            // The factory of a ThreadLocal<Random> field, which Pharos replaces.
            if (after?.OpCode == OpCodes.Ret && IsFactory(method)) continue;

            unseeded.Add(new UnseededRandom(Member(method), $"creates a new {created.Name} {seeded} inside the method", assembly));
        }

        if (initializes) initializers.Add(method);
    }

    private static bool IsFactory(MethodBase method)
    {
        if (!method.Name.StartsWith('<') && method.DeclaringType?.Name.StartsWith('<') != true) return false;
        Type? owner = method.DeclaringType;
        while (owner is { DeclaringType: not null } && owner.Name.StartsWith('<')) owner = owner.DeclaringType;
        return owner != null && SafeFields(owner, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(f => IsRandomField(f) && f.FieldType == typeof(ThreadLocal<Random>));
    }

    // The clock the constructor's seed comes from, looking back over the instructions that build it.
    private static string? ClockBefore(MethodBase method, IReadOnlyList<Il.Instruction> code, int newobj)
    {
        for (int i = newobj - 1; i >= 0 && i >= newobj - 10; i--)
        {
            Il.Instruction instruction = code[i];
            if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return) break;
            // A temporary local is part of the same expression; a field store or a pop ends it.
            if (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld || instruction.OpCode == OpCodes.Pop) break;
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
            if (ResolveMethod(method, instruction.Token) is not MethodBase called) continue;

            Type? type = called.DeclaringType;
            string name = called.Name;
            if (type == typeof(Environment) && name is "get_TickCount" or "get_TickCount64") return "Environment.TickCount";
            if (type == typeof(DateTime) && name is "get_Now" or "get_UtcNow" or "get_Today") return "DateTime.Now";
            if (type == typeof(DateTimeOffset) && name is "get_Now" or "get_UtcNow") return "DateTimeOffset.Now";
            if (type == typeof(Stopwatch)) return "a Stopwatch";
            if (type == typeof(Guid) && name == "NewGuid") return "Guid.NewGuid";
            if (type == typeof(string) && name == "GetHashCode") return "string.GetHashCode";
            if (name == "get_ElapsedMilliseconds") return "ElapsedMilliseconds";
        }

        return null;
    }

    private static MethodBase? ResolveMethod(MethodBase context, int token)
    {
        try
        {
            return context.Module.ResolveMethod(token, GenericArguments(context.DeclaringType), context.IsGenericMethod ? context.GetGenericArguments() : null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FieldInfo? ResolveField(MethodBase context, int token)
    {
        try
        {
            return context.Module.ResolveField(token, GenericArguments(context.DeclaringType), context.IsGenericMethod ? context.GetGenericArguments() : null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Type[]? GenericArguments(Type? type) => type is { IsGenericType: true } ? type.GetGenericArguments() : null;

    /// <summary>The random static fields of an assembly.</summary>
    /// <param name="Shared">Fields with one value for the process.</param>
    /// <param name="PerThread"><c>[ThreadStatic]</c> fields, with one value per thread.</param>
    /// <param name="Unseedable">Fields Pharos cannot seed at all.</param>
    internal sealed record StaticFieldSet(IReadOnlyList<FieldInfo> Shared, IReadOnlyList<FieldInfo> PerThread, IReadOnlyList<UnseededRandom> Unseedable);

    /// <summary>What an assembly's code does.</summary>
    /// <param name="Unseeded">The fields Pharos does not reach and the generators made inside methods.</param>
    /// <param name="Initializers">The methods that initialize block entities or entities, which Pharos patches.</param>
    internal sealed record Facts(IReadOnlyList<UnseededRandom> Unseeded, IReadOnlyList<MethodBase> Initializers);

    /// <summary>A minimal IL reader: opcodes and their metadata tokens.</summary>
    internal static class Il
    {
        private static readonly OpCode[] s_oneByte = new OpCode[256];
        private static readonly OpCode[] s_twoByte = new OpCode[256];

        static Il()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode op) continue;
                if (op.Size == 1) s_oneByte[(byte)op.Value] = op;
                else s_twoByte[(byte)(op.Value & 0xFF)] = op;
            }
        }

        /// <summary>One instruction: its opcode, and its operand when that is a 4-byte token.</summary>
        public readonly record struct Instruction(OpCode OpCode, int Token);

        public static IReadOnlyList<Instruction> Read(byte[] il)
        {
            List<Instruction> code = [];
            int at = 0;
            while (at < il.Length)
            {
                OpCode op = il[at] == 0xFE ? s_twoByte[il[++at]] : s_oneByte[il[at]];
                at++;
                int token = 0;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                        at += 1;
                        break;
                    case OperandType.InlineVar:
                        at += 2;
                        break;
                    case OperandType.InlineI8 or OperandType.InlineR:
                        at += 8;
                        break;
                    case OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(il, at);
                        at += 4 + 4 * count;
                        break;
                    case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType or OperandType.InlineSig or OperandType.InlineString:
                        token = BitConverter.ToInt32(il, at);
                        at += 4;
                        break;
                    default:
                        at += 4;
                        break;
                }

                code.Add(new Instruction(op, token));
            }

            return code;
        }
    }
}
