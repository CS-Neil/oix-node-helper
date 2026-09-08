using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OixNodeHelper
{
    public sealed class CoreSupervisor : IDisposable
    {
        private readonly SafeLogger _log;
        private readonly object _sync = new object();
        private Process _process;
        private IntPtr _job = IntPtr.Zero;
        private bool _disposed;

        public CoreSupervisor(SafeLogger log)
        {
            _log = log;
            TryCreateJobObject();
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    try { return _process != null && !_process.HasExited; }
                    catch { return false; }
                }
            }
        }

        public int ProcessId
        {
            get
            {
                lock (_sync)
                {
                    try { return IsRunning ? _process.Id : 0; }
                    catch { return 0; }
                }
            }
        }

        public void Start(AppSettings settings, CredentialBundle credentials, string configPath, string dataDirectory)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException("CoreSupervisor");
                StopInternal();
                if (String.IsNullOrWhiteSpace(settings.CorePath) || !File.Exists(settings.CorePath))
                    throw new FileNotFoundException("mihomo-oix core executable was not found.", settings.CorePath);
                if (!File.Exists(configPath)) throw new FileNotFoundException("Runtime YAML was not found.", configPath);

                ProcessStartInfo start = CreateStartInfo(settings, credentials, configPath, dataDirectory, false);

                _process = new Process();
                _process.StartInfo = start;
                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;
                if (!_process.Start()) throw new InvalidOperationException("Unable to start mihomo-oix core.");
                AssignToJob(_process);
                _log.Info("Core started with PID " + _process.Id + ".");
            }
        }

        public bool ValidateConfig(AppSettings settings, CredentialBundle credentials, string configPath, string dataDirectory)
        {
            ProcessStartInfo start = CreateStartInfo(settings, credentials, configPath, dataDirectory, true);
            using (Process validation = Process.Start(start))
            {
                if (validation == null) return false;
                if (!validation.WaitForExit(30000))
                {
                    try { validation.Kill(); } catch { }
                    return false;
                }
                return validation.ExitCode == 0;
            }
        }

        private static ProcessStartInfo CreateStartInfo(AppSettings settings, CredentialBundle credentials, string configPath, string dataDirectory, bool validate)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = settings.CorePath;
            start.Arguments = "-d " + QuoteArgument(dataDirectory) + " -f " + QuoteArgument(configPath) +
                " -oix-provider-name oixCloud" +
                (String.IsNullOrEmpty(credentials.AccessToken) ? "" : " -oix-token " + QuoteArgument(credentials.AccessToken)) +
                (validate ? " -t" : "");
            start.WorkingDirectory = dataDirectory;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            return start;
        }

        public void Stop()
        {
            lock (_sync) StopInternal();
        }

        private void StopInternal()
        {
            if (_process == null) return;
            try
            {
                _process.Exited -= OnProcessExited;
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch { }
            try { _process.Dispose(); } catch { }
            _process = null;
        }

        private void OnProcessExited(object sender, EventArgs args)
        {
            try
            {
                Process process = sender as Process;
                int exitCode = process == null ? -1 : process.ExitCode;
                _log.Error("Core exited with code " + exitCode + ".");
            }
            catch { }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private void TryCreateJobObject()
        {
            try
            {
                _job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero) return;
                NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int length = Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr pointer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, pointer, false);
                    NativeMethods.SetInformationJobObject(_job, 9, pointer, (uint)length);
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            catch { _job = IntPtr.Zero; }
        }

        private void AssignToJob(Process process)
        {
            if (_job == IntPtr.Zero) return;
            try { NativeMethods.AssignProcessToJobObject(_job, process.Handle); }
            catch (Win32Exception) { }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                StopInternal();
                if (_job != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(_job);
                    _job = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        private static class NativeMethods
        {
            public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

            [StructLayout(LayoutKind.Sequential)]
            public struct IO_COUNTERS
            {
                public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
                public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
            public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll")]
            public static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
            [DllImport("kernel32.dll")]
            public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")]
            public static extern bool CloseHandle(IntPtr handle);
        }
    }
}
