using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>A deterministic horizontal crowd arrangement at the supplied absolute height; terrain is not changed.</summary>
public sealed class PlayerLayout
{
    private readonly Vec3d _center;
    private readonly double _distance;
    private readonly int? _columns;

    private PlayerLayout(Vec3d center, double distance, int? columns)
    {
        ArgumentNullException.ThrowIfNull(center);
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(center.Z))
            throw new ArgumentException("Layout center must be finite.", nameof(center));
        if (!double.IsFinite(distance) || distance <= 0) throw new ArgumentOutOfRangeException(nameof(distance));
        if (columns is <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        _center = center.Clone();
        _distance = distance;
        _columns = columns;
    }

    /// <summary>Equally spaced players around a circle in X/Z, starting on its positive X axis.</summary>
    public static PlayerLayout Ring(Vec3d center, double radius) => new(center, radius, null);

    /// <summary>A row-major grid centered around center, with the given number of columns.</summary>
    public static PlayerLayout Grid(Vec3d center, double spacing = 4, int columns = 4) => new(center, spacing, columns);

    /// <summary>Computes a fresh position. Count must be positive and index must belong to it.</summary>
    public Vec3d Position(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        Vec3d position;
        if (_columns is not { } columns)
        {
            double angle = 2 * Math.PI * index / count;
            position = _center.AddCopy(Math.Cos(angle) * _distance, 0, Math.Sin(angle) * _distance);
        }
        else
        {
            int width = Math.Min(columns, count);
            int rows = (count - 1) / width + 1;
            position = _center.AddCopy((index % width - (width - 1) / 2d) * _distance, 0,
                (index / width - (rows - 1) / 2d) * _distance);
        }
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
            throw new ArgumentOutOfRangeException(nameof(count), "Layout coordinates overflowed.");
        return position;
    }
}
