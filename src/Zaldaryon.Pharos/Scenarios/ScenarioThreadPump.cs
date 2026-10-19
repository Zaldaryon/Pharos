using System.Collections.Concurrent;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>Runs async setup continuations on the thread that owns a fixture-mode game client.</summary>
public sealed class ScenarioThreadPump : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

    public bool Run(Task task, CancellationToken abort, TimeSpan grace)
    {
        while (!task.IsCompleted && !abort.IsCancellationRequested)
        {
            if (_queue.TryTake(out var work, 25)) work.Callback(work.State);
        }
        if (!task.IsCompleted && abort.IsCancellationRequested)
        {
            DateTime deadline = DateTime.UtcNow + grace;
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var work, 25)) work.Callback(work.State);
            return task.IsCompleted;
        }
        while (_queue.TryTake(out var work)) work.Callback(work.State);
        task.GetAwaiter().GetResult();
        return true;
    }
}
