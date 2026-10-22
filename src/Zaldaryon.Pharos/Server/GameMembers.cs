using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The members of the game's own classes, found by name: Pharos does not build against the
/// essentials and survival mods. A member the game renamed is reported by name.
/// </summary>
internal static class GameMembers
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Reads a property or a field by name.</summary>
    public static object? Get(object target, string name)
    {
        Type type = target.GetType();
        if (type.GetProperty(name, Flags) is { } property) return property.GetValue(target);
        if (type.GetField(name, Flags) is { } field) return field.GetValue(target);
        throw Missing(type, name);
    }

    /// <summary>Reads a property or a field by name, or null when the member is not there.</summary>
    public static object? TryGet(object target, string name)
    {
        Type type = target.GetType();
        if (type.GetProperty(name, Flags) is { } property) return property.GetValue(target);
        if (type.GetField(name, Flags) is { } field) return field.GetValue(target);
        return null;
    }

    /// <summary>Writes a property or a field by name.</summary>
    public static void Set(object target, string name, object? value)
    {
        Type type = target.GetType();
        if (type.GetProperty(name, Flags) is { CanWrite: true } property) property.SetValue(target, value);
        else if (type.GetField(name, Flags) is { } field) field.SetValue(target, value);
        else throw Missing(type, name);
    }

    /// <summary>Calls a method by name.</summary>
    public static object? Call(object target, string name, params object?[] args)
    {
        Type type = target.GetType();
        MethodInfo method = type.GetMethods(Flags)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Zip(args).All(p => p.Second == null || p.First.ParameterType.IsInstanceOfType(p.Second)))
            ?? throw Missing(type, name);
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// The behavior of an entity whose type, or one of the types it derives from, has the given
    /// name. The game's own behaviors live in its mods, so they are found by name.
    /// </summary>
    public static object? FindBehavior(Entity entity, string typeName)
    {
        foreach (EntityBehavior behavior in entity.SidedProperties.Behaviors)
        {
            if (behavior == null) continue;
            for (Type? type = behavior.GetType(); type != null; type = type.BaseType)
            {
                if (type.Name == typeName) return behavior;
            }
        }

        return null;
    }

    /// <summary>The block entity at a position whose type has the given name, or null when there is none.</summary>
    public static object? FindBlockEntity(IBlockAccessor blockAccessor, BlockPos pos, string typeName)
    {
        BlockEntity? blockEntity = blockAccessor.GetBlockEntity(pos);
        if (blockEntity == null) return null;
        if (blockEntity.GetType().Name == typeName) return blockEntity;

        return null;
    }

    private static MissingMemberException Missing(Type type, string name) =>
        new($"The type {type.FullName} has no member named {name}.");
}
