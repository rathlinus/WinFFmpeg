using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinFFmpeg.Core;

/// <summary>
/// Ties child processes to the lifetime of this one with a Windows job object, so an
/// ffmpeg that is hours into an encode does not keep running unseen if the app crashes
/// or is ended from Task Manager.
/// </summary>
internal static unsafe partial class ChildProcessGuard
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    // Never closed explicitly: Windows closes it when this process ends, which is the trigger.
    private static readonly nint s_job = CreateJob();

    public static void Add(Process process)
    {
        if (s_job == 0)
        {
            return;
        }

        try
        {
            AssignProcessToJobObject(s_job, process.Handle);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process already exited.
        }
    }

    private static nint CreateJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        nint job = CreateJobObjectW(0, 0);
        if (job == 0)
        {
            return 0;
        }

        var info = default(ExtendedLimits);
        info.Basic.LimitFlags = KillOnJobClose;
        SetInformationJobObject(job, ExtendedLimitInformation, &info, (uint)sizeof(ExtendedLimits));
        return job;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint CreateJobObjectW(nint attributes, nint name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION and the structures it embeds (winnt.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
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
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
