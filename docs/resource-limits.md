# Resource limits for benchmark processes

Pharos can measure process CPU time, working set, private bytes, managed heap,
allocations, GC counts, and GL timer-query duration. Measurements are enabled by
the benchmark scenario; hard CPU and commit-memory limits are optional and are
never applied unless a caller explicitly requests them.

On Windows, `WindowsJobObjectResourceLimiter` assigns a process to a Job Object.
The hard CPU rate applies to the process tree and is expressed as a percentage
of total machine CPU capacity (100% is approximately one fully occupied logical
processor). The memory quota limits committed memory for the whole job; it is
not a working-set ceiling. Descendants inherit the job unless they explicitly
break away. Assignment can fail when Windows or a parent job disallows nested
jobs; Pharos reports that as an error rather than silently running uncapped.

```csharp
using var limits = WindowsJobObjectResourceLimiter.ApplyToCurrentProcess(
    new ProcessResourceLimitOptions(
        CpuLimitPercent: 50,
        JobMemoryLimitBytes: 4L * 1024 * 1024 * 1024));
```

The CPU value is capped at 100%, so use a value in `(0, 100]`.
Memory pressure tests should use a separate process and a generous cap so a
failed allocation cannot take down a developer's test runner. Set
`KillProcessesOnClose` only when the job is disposable.

GPU timer queries measure elapsed GPU work for selected OpenGL commands, not
whole-device utilization, VRAM use, power, or presentation rate. Windows Job
Objects do not provide a generic per-process GPU quota. To bound GPU demand,
control the workload (frame rate, render scale, view distance, or concurrent
work) and report the resulting GPU-time distribution; hardware scheduling and
VRAM limits require device/vendor-specific tooling.

For valid Vanilla-versus-Optimum performance comparisons, leave hard caps off.
Use identical limits only in a distinct constrained-resource/stress run, and
record the configured quotas with its result.
