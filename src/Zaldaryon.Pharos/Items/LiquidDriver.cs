using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Items;

/// <summary>What a liquid container holds.</summary>
/// <param name="LiquidCode">The code of the liquid, or an empty code when the container is empty.</param>
/// <param name="Litres">How many litres the container holds.</param>
/// <param name="CapacityLitres">How many litres the container can hold.</param>
public sealed record LiquidContents(string LiquidCode, float Litres, float CapacityLitres);

/// <summary>The liquid containers of the server, and the pouring between them.</summary>
public sealed class LiquidDriver
{
    private readonly EmbeddedServerHost _host;

    internal LiquidDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>What a container stack holds, read the way the game reads it.</summary>
    /// <param name="stack">The stack of the container.</param>
    public LiquidContents In(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return _host.RunOnGameThread(() =>
        {
            object? container = LiquidInternals.ContainerOf(stack);
            if (container == null) return new LiquidContents("", 0, 0);

            float litres = LiquidInternals.CurrentLitres(container, stack);
            float capacity = LiquidInternals.CapacityOf(container);
            return new LiquidContents(LiquidInternals.LiquidCode(stack), litres, capacity);
        });
    }

    /// <summary>What the container at a position holds.</summary>
    /// <param name="position">Where the container stands.</param>
    public LiquidContents In(BlockPos position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return _host.RunOnGameThread(() =>
        {
            object? container = LiquidInternals.ContainerAt(_host, position);
            if (container == null) return new LiquidContents("", 0, 0);

            float litres = LiquidInternals.CurrentLitresAt(container, position);
            float capacity = LiquidInternals.CapacityOf(container);
            return new LiquidContents("", litres, capacity);
        });
    }

    /// <summary>
    /// Pours from one container into another, the way the game pours when a player does, and
    /// returns how many litres moved.
    /// </summary>
    /// <param name="from">The container to pour from.</param>
    /// <param name="to">The container to pour into.</param>
    /// <param name="litres">How many litres to pour.</param>
    /// <returns>How many litres moved.</returns>
    public float Pour(ItemStack from, ItemStack to, float litres)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentOutOfRangeException.ThrowIfNegative(litres);
        if (litres == 0) return 0;

        return _host.RunOnGameThread(() =>
        {
            object? source = LiquidInternals.ContainerOf(from);
            object? sink = LiquidInternals.ContainerOf(to);
            if (source == null || sink == null) return 0f;

            ItemStack? held = LiquidInternals.Content(source, from);
            float itemsPerLitre = LiquidInternals.ItemsPerLitre(held);
            if (itemsPerLitre <= 0) return 0f;

            int wanted = (int)Math.Round(litres * itemsPerLitre);
            ItemStack? content = LiquidInternals.TakeContent(source, from, wanted);
            if (content == null || content.StackSize <= 0) return 0f;

            int moved = LiquidInternals.PutLiquid(sink, to, content, litres);
            return moved / itemsPerLitre;
        });
    }

    /// <summary>Pours from a container into the one at a position.</summary>
    /// <param name="from">The container to pour from.</param>
    /// <param name="to">Where the container to pour into stands.</param>
    /// <param name="litres">How many litres to pour.</param>
    /// <returns>How many litres moved.</returns>
    public float Pour(ItemStack from, BlockPos to, float litres)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentOutOfRangeException.ThrowIfNegative(litres);
        if (litres == 0) return 0;

        return _host.RunOnGameThread(() =>
        {
            object? source = LiquidInternals.ContainerOf(from);
            object? sink = LiquidInternals.ContainerAt(_host, to);
            if (source == null || sink == null) return 0f;

            ItemStack? held = LiquidInternals.Content(source, from);
            float itemsPerLitre = LiquidInternals.ItemsPerLitre(held);
            if (itemsPerLitre <= 0) return 0f;

            int wanted = (int)Math.Round(litres * itemsPerLitre);
            ItemStack? content = LiquidInternals.TakeContent(source, from, wanted);
            if (content == null || content.StackSize <= 0) return 0f;

            int moved = LiquidInternals.PutLiquidAt(sink, to, content, litres);
            return moved / itemsPerLitre;
        });
    }
}

/// <summary>
/// The liquid interfaces of the game, which live in the survival mod's assembly and are reached by
/// name.
/// </summary>
internal static class LiquidInternals
{
    private const string InterfaceName = "ILiquidInterface";
    private const string SourceName = "ILiquidSource";
    private const string SinkName = "ILiquidSink";

    private static Type? s_interface;
    private static Type? s_source;
    private static Type? s_sink;

    /// <summary>The object that holds the liquid, the stack's collectible or the block entity.</summary>
    public static object? ContainerOf(ItemStack stack)
    {
        EnsureTypes();
        if (stack?.Collectible == null) return null;

        foreach (CollectibleBehavior behavior in stack.Collectible.CollectibleBehaviors)
        {
            if (s_interface != null && s_interface.IsInstanceOfType(behavior)) return behavior;
        }

        return s_interface != null && s_interface.IsInstanceOfType(stack.Collectible) ? stack.Collectible : null;
    }

    /// <summary>The object that holds the liquid at a position.</summary>
    public static object? ContainerAt(EmbeddedServerHost host, BlockPos position)
    {
        EnsureTypes();
        BlockEntity? blockEntity = host.Api.World.BlockAccessor.GetBlockEntity(position);
        return blockEntity != null && s_interface != null && s_interface.IsInstanceOfType(blockEntity) ? blockEntity : null;
    }

    public static float CurrentLitres(object container, ItemStack stack) =>
        Invoke<float>(s_interface, "GetCurrentLitres", container, [stack]);

    public static float CurrentLitresAt(object container, BlockPos position) =>
        Invoke<float>(s_interface, "GetCurrentLitres", container, [position]);

    public static float CapacityOf(object container)
    {
        object? capacity = s_interface?.GetProperty("CapacityLitres")?.GetValue(container);
        return capacity is float value ? value : 0f;
    }

    /// <summary>The stack a container holds, as the game reads it.</summary>
    public static ItemStack? Content(object container, ItemStack stack) =>
        Invoke<ItemStack>(container.GetType(), "GetContent", container, [stack]);

    public static ItemStack? TakeContent(object source, ItemStack stack, int quantity) =>
        Invoke<ItemStack>(s_source, "TryTakeContent", source, [stack, quantity]);

    public static int PutLiquid(object sink, ItemStack stack, ItemStack liquid, float litres) =>
        Invoke<int>(s_sink, "TryPutLiquid", sink, [stack, liquid, litres]);

    public static int PutLiquidAt(object sink, BlockPos position, ItemStack liquid, float litres) =>
        Invoke<int>(s_sink, "TryPutLiquid", sink, [position, liquid, litres]);

    /// <summary>How many items of the liquid make a litre, as the liquid's own item declares.</summary>
    public static float ItemsPerLitre(ItemStack stack)
    {
        if (stack?.ItemAttributes == null) return 0f;
        if (!stack.ItemAttributes["waterTightContainerProps"].Exists) return 0f;
        if (!stack.ItemAttributes["waterTightContainerProps"]["itemsPerLitre"].Exists) return 0f;
        return stack.ItemAttributes["waterTightContainerProps"]["itemsPerLitre"].AsFloat(0f);
    }

    /// <summary>The code of the liquid a container stack holds.</summary>
    public static string LiquidCode(ItemStack stack)
    {
        if (stack?.Attributes == null) return "";
        if (!stack.Attributes.HasAttribute("liquid")) return "";
        return stack.Attributes["liquid"]?.ToString() ?? "";
    }

    private static void EnsureTypes()
    {
        if (s_interface != null && s_source != null && s_sink != null) return;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = [.. ex.Types.Where(type => type != null)];
            }

            foreach (Type type in types)
            {
                if (type.Name == InterfaceName) s_interface ??= type;
                if (type.Name == SourceName) s_source ??= type;
                if (type.Name == SinkName) s_sink ??= type;
            }
        }
    }

    private static T? Invoke<T>(Type? type, string method, object target, object[] arguments)
    {
        if (type == null) return default;
        MethodInfo? found = type.GetMethods()
            .FirstOrDefault(candidate => candidate.Name == method
                && candidate.GetParameters().Length == arguments.Length
                && candidate.GetParameters().Zip(arguments).All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second)));
        if (found == null) return default;

        object? result = found.Invoke(target, arguments);
        return result is T value ? value : default;
    }

    private static T? Invoke<T>(Type? type, string method, object target, object[] arguments, T fallback) =>
        Invoke<T>(type, method, target, arguments) ?? fallback;
}
