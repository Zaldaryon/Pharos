namespace Zaldaryon.Pharos.Core;

/// <summary>Tracks process-local game sessions so cache generation cannot replace global game paths underneath them.</summary>
internal static class GameSessionActivity
{
    private static int s_active;
    public static int Active => Volatile.Read(ref s_active);
    public static void Started() => Interlocked.Increment(ref s_active);
    public static void Stopped() => Interlocked.Decrement(ref s_active);

    public static void Release(ref int registration)
    {
        if (Interlocked.Exchange(ref registration, 0) != 0) Stopped();
    }
}
