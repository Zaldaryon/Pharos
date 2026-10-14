using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Applies climate overrides to the server's climate lookups. The postfixes copy the result only where an
/// override covers the position, so lookups outside every override return the game's own values.
/// </summary>
internal static class ClimatePatches
{
    private const string HarmonyId = "zaldaryon.pharos.climate";
    private static readonly object s_lock = new();
    private static bool s_installed;
    private static readonly List<ClimateOverrideRule> s_rules = [];
    private static readonly FieldInfo[] s_climateFields = typeof(ClimateCondition).GetFields(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>One active override: the region and the values to set inside it.</summary>
    internal sealed record ClimateOverrideRule(Cuboidi Region, float? Temperature, float? Rainfall);

    /// <summary>Adds an override rule. Installs the patches on the first call.</summary>
    internal static void Add(ClimateOverrideRule rule)
    {
        lock (s_lock)
        {
            Install();
            s_rules.Add(rule);
        }
    }

    /// <summary>Removes an override rule. Returns false when it was not active.</summary>
    internal static bool Remove(ClimateOverrideRule rule)
    {
        lock (s_lock) return s_rules.Remove(rule);
    }

    /// <summary>Removes every override rule, for a rollback.</summary>
    internal static void Clear()
    {
        lock (s_lock) s_rules.Clear();
    }

    private static void Install()
    {
        if (s_installed) return;
        s_installed = true;
        Harmony harmony = new(HarmonyId);
        Type accessor = typeof(Vintagestory.Common.BlockAccessorBase);
        harmony.Patch(AccessTools.Method(accessor, nameof(IBlockAccessor.GetClimateAt), [typeof(BlockPos), typeof(EnumGetClimateMode), typeof(double)]),
            postfix: new HarmonyMethod(typeof(ClimatePatches), nameof(AfterNowValues)));
        harmony.Patch(AccessTools.Method(accessor, nameof(IBlockAccessor.GetClimateAt), [typeof(BlockPos), typeof(ClimateCondition), typeof(EnumGetClimateMode), typeof(double)]),
            postfix: new HarmonyMethod(typeof(ClimatePatches), nameof(AfterLerpedValues)));
        harmony.Patch(AccessTools.Method(accessor, nameof(IBlockAccessor.GetClimateAt), [typeof(BlockPos), typeof(int)]),
            postfix: new HarmonyMethod(typeof(ClimatePatches), nameof(AfterSmoothedValues)));
    }

    private static void AfterNowValues([HarmonyArgument(0)] BlockPos pos, ref ClimateCondition __result) => Apply(pos, ref __result);

    // This overload writes into the condition the caller passed and returns that same object, so the override
    // must change the argument too. The farmland's fast-forward reads the argument, not the return value.
    private static void AfterLerpedValues([HarmonyArgument(0)] BlockPos pos, [HarmonyArgument(1)] ClimateCondition conds, ref ClimateCondition __result)
    {
        if (conds is null) return;
        ClimateOverrideRule? rule = RuleFor(pos);
        if (rule is null) return;
        if (rule.Temperature is { } temperature) conds.Temperature = temperature;
        if (rule.Rainfall is { } rainfall) conds.Rainfall = rainfall;
        __result = conds;
    }

    private static void AfterSmoothedValues([HarmonyArgument(0)] BlockPos pos, ref ClimateCondition __result) => Apply(pos, ref __result);

    private static ClimateOverrideRule? RuleFor(BlockPos pos)
    {
        if (pos is null) return null;
        lock (s_lock)
        {
            if (s_rules.Count == 0) return null;
            return s_rules.LastOrDefault(candidate => candidate.Region.Contains(pos));
        }
    }

    private static void Apply(BlockPos pos, ref ClimateCondition result)
    {
        if (result is null) return;
        ClimateOverrideRule? rule = RuleFor(pos);
        if (rule is null) return;

        ClimateCondition copy = new();
        foreach (FieldInfo field in s_climateFields) field.SetValue(copy, field.GetValue(result));
        if (rule.Temperature is { } temperature) copy.Temperature = temperature;
        if (rule.Rainfall is { } rainfall) copy.Rainfall = rainfall;
        result = copy;
    }
}
