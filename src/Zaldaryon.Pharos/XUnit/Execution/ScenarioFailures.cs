namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>Teardown checks that may each fail, reported together.</summary>
internal static class ScenarioFailures
{
    /// <summary>
    /// Throws <paramref name="first"/> and whatever <paramref name="then"/> throws: one alone as it
    /// is, both as an <see cref="AggregateException"/>.
    /// </summary>
    public static void ThrowAll(Exception? first, Action then)
    {
        Exception? second = null;
        try
        {
            then();
        }
        catch (Exception ex)
        {
            second = ex;
        }

        if (first != null && second != null) throw new AggregateException(first, second);
        if (first != null) throw first;
        if (second != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(second).Throw();
    }
}
