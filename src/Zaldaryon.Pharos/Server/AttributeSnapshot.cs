using System.Collections;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Zaldaryon.Pharos.Server;

// Shared by region text and the Verify integration. Keep values typed, never use attribute.ToString().
internal static class AttributeSnapshot
{
    internal static object? Capture(object? value) => Capture(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static object? Capture(object? value, HashSet<object> ancestors)
    {
        if (value is null || value is string || value is bool || value is byte || value is sbyte || value is short ||
            value is ushort || value is int || value is uint || value is long || value is ulong) return value;
        if (value is float f) return Math.Round((double)f, 4);
        if (value is double d) return Math.Round(d, 4);
        if (value is decimal m) return Math.Round(m, 4);
        if (value is AssetLocation code) return code.ToString();
        if (ancestors.Count >= 128 || !ancestors.Add(value))
            throw new InvalidOperationException("Snapshot attributes contain a cycle or exceed 128 nested containers.");
        try
        {
            if (value is ITreeAttribute tree)
            {
                SortedDictionary<string, object?> result = new(StringComparer.Ordinal);
                foreach (var pair in tree)
                {
                    result.Add(pair.Key, IsIdentity(pair.Key) ? "{Scrubbed}" : Capture(pair.Value, ancestors));
                }
                return result;
            }
            if (value is IAttribute attribute) return Capture(attribute.GetValue(), ancestors);
            if (value is ItemStack stack)
                return new { Code = stack.Collectible?.Code?.ToString(), Quantity = stack.StackSize, Attributes = Capture(stack.Attributes, ancestors) };
            if (value is IEnumerable items)
            {
                List<object?> result = [];
                foreach (var item in items) result.Add(Capture(item, ancestors));
                return result.ToArray();
            }
            throw new NotSupportedException($"Cannot snapshot attribute value of type {value.GetType().FullName}.");
        }
        finally { ancestors.Remove(value); }
    }

    internal static bool IsIdentity(string key) => key.Equals("entityId", StringComparison.OrdinalIgnoreCase)
        || key.Equals("playerUID", StringComparison.OrdinalIgnoreCase);
}
