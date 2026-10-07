using System.Runtime.InteropServices;

namespace Zaldaryon.Pharos.Platform;

/// <summary>Hands memory the native allocator has freed back to the operating system.</summary>
/// <remarks>
/// A client that joined a world allocates hundreds of megabytes natively, for textures, chunk
/// meshes and the software renderer's buffers. glibc keeps what is freed in its arenas instead
/// of returning it, so without a trim each client a test run boots and disposes adds about half a
/// gigabyte to the process until it runs out of memory.
/// </remarks>
internal static partial class NativeHeap
{
    private static bool s_unavailable;

    /// <summary>Returns freed native memory to the operating system, where the C library can.</summary>
    public static void Trim()
    {
        if (!OperatingSystem.IsLinux() || s_unavailable) return;

        try
        {
            malloc_trim(0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not glibc, such as musl: nothing to trim.
            s_unavailable = true;
        }
    }

    /// <summary>
    /// Collects the garbage a disposed client or server left, runs the finalizers that free
    /// their native memory, and hands what was freed back to the operating system.
    /// </summary>
    /// <remarks>
    /// A disposed client leaves close to a gigabyte of managed garbage, and the textures and
    /// buffers its finalizers free. The GC can let several of those pile up before it collects,
    /// so a run that boots one per test grows by gigabytes. Call it once nothing references the
    /// disposed hosts any more.
    /// </remarks>
    public static void Reclaim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Trim();
    }

    [LibraryImport("libc", EntryPoint = "malloc_trim")]
    private static partial int malloc_trim(nuint pad);
}
