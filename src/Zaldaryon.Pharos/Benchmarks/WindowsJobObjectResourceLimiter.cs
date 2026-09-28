using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Optional hard limits for a benchmark process tree on Windows. CPU is a percentage
/// of total machine capacity (100% is approximately one fully occupied logical CPU).
/// Memory is a commit limit for the whole job, not a working-set target.
/// </summary>
public sealed record ProcessResourceLimitOptions(
    double? CpuLimitPercent = null,
    long? JobMemoryLimitBytes = null,
    bool KillProcessesOnClose = false)
{
    public void Validate()
    {
        if (CpuLimitPercent is double cpu && (!double.IsFinite(cpu) || cpu < 0.01 || cpu > 100))
        {
            throw new ArgumentOutOfRangeException(nameof(CpuLimitPercent), cpu,
                "CPU limit must be between 0.01 and 100 percent of total machine capacity.");
        }

        if (JobMemoryLimitBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(JobMemoryLimitBytes), JobMemoryLimitBytes,
                "Job memory limit must be a positive byte count.");
        }

        if (CpuLimitPercent is null && JobMemoryLimitBytes is null)
        {
            throw new ArgumentException("At least one CPU or memory limit must be specified.");
        }
    }
}

/// <summary>
/// Applies opt-in Windows Job Object limits to a process and its non-breakaway descendants.
/// Limits are never enabled implicitly; dispose the returned handle to release the job.
/// </summary>
public sealed class WindowsJobObjectResourceLimiter : IDisposable
{
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const int JobObjectCpuRateControlInformationClass = 15;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint CpuRateControlEnable = 0x00000001;
    private const uint CpuRateControlHardCap = 0x00000004;

    private SafeJobHandle? _jobHandle;

    private WindowsJobObjectResourceLimiter(SafeJobHandle jobHandle) => _jobHandle = jobHandle;

    public static WindowsJobObjectResourceLimiter ApplyToCurrentProcess(ProcessResourceLimitOptions options) =>
        Apply(Process.GetCurrentProcess(), options);

    public static WindowsJobObjectResourceLimiter Apply(Process process, ProcessResourceLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Job Object resource limits require Windows.");
        }

        SafeJobHandle job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a Windows Job Object.");
        }

        try
        {
            var result = new WindowsJobObjectResourceLimiter(job);
            result.Configure(options);
            if (!AssignProcessToJobObject(job, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not assign the benchmark process to a Windows Job Object.");
            }
            return result;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _jobHandle?.Dispose();
        _jobHandle = null;
    }

    private void Configure(ProcessResourceLimitOptions options)
    {
        SafeJobHandle job = _jobHandle ?? throw new ObjectDisposedException(nameof(WindowsJobObjectResourceLimiter));
        var extended = new JobObjectExtendedLimitInformation();
        if (options.JobMemoryLimitBytes is long memoryBytes)
        {
            extended.BasicLimitInformation.LimitFlags |= JobObjectLimitJobMemory;
            extended.JobMemoryLimit = (UIntPtr)(ulong)memoryBytes;
        }
        if (options.KillProcessesOnClose)
        {
            extended.BasicLimitInformation.LimitFlags |= JobObjectLimitKillOnJobClose;
        }
        SetInformation(job, JobObjectExtendedLimitInformationClass, extended);

        if (options.CpuLimitPercent is double cpuPercent)
        {
            var cpu = new JobObjectCpuRateControlInformation
            {
                ControlFlags = CpuRateControlEnable | CpuRateControlHardCap,
                CpuRate = checked((uint)Math.Round(cpuPercent * 100, MidpointRounding.AwayFromZero))
            };
            SetInformation(job, JobObjectCpuRateControlInformationClass, cpu);
        }
    }

    private static void SetInformation<T>(SafeJobHandle job, int informationClass, T information)
        where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(information, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(job, informationClass, buffer, (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Could not configure Windows Job Object information class {informationClass}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        int informationClass,
        IntPtr jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectCpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
