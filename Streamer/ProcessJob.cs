using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Streamer
{
    // Minimal Job object helper to ensure child processes are terminated when the job is closed.
    internal sealed class ProcessJob : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed = false;

        public ProcessJob()
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // Set JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr p = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, p, false);
                if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, p, (uint)length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        public void AddProcess(Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (_handle == IntPtr.Zero) return;
            if (!AssignProcessToJobObject(_handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        #region Native
        private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public int LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public int ActiveProcessLimit;
            public long Affinity;
            public int PriorityClass;
            public int SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount; public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
        #endregion
    }
}
