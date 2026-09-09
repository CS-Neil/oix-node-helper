using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OixNodeHelper
{
    public enum CoreHealthLevel
    {
        Healthy = 0,
        Degraded = 1,
        Critical = 2
    }

    public sealed class CoreHealthSample
    {
        public int Connections { get; set; }
        public int Handles { get; set; }
        public int MemoryMb { get; set; }
        public int EphemeralPortsInUse { get; set; }
        public CoreHealthLevel Level { get; set; }

        public CoreHealthSample()
        {
            Level = CoreHealthLevel.Healthy;
        }
    }

    /// <summary>
    /// Watches the supervised core for the failure mode that makes FlClash report
    /// timeouts on every local node: a routing loop (FlClash TUN with fake-ip
    /// hands the core 198.18.x.x for its own upstream, the dial re-enters FlClash
    /// and comes back to this core) leaks sockets that never connect and never
    /// close, until the Windows ephemeral port pool is exhausted and every new
    /// dial hangs. Individual listeners keep answering, so the only early signal
    /// is the socket, handle and memory growth measured here.
    /// </summary>
    public static class CoreHealthMonitor
    {
        // Windows default dynamic port range is 49152-65535 (16384 ports), minus
        // whatever Hyper-V and friends reserve inside it.
        public const int EphemeralPortFloor = 49152;
        private const int EphemeralPortPoolSize = 65536 - EphemeralPortFloor;

        public const int DegradedConnections = 3000;
        public const int DegradedHandles = 6000;
        public const int DegradedMemoryMb = 600;
        public const int DegradedEphemeralPorts = EphemeralPortPoolSize * 3 / 4;

        public const int CriticalConnections = DegradedConnections * 2;
        public const int CriticalHandles = DegradedHandles * 2;
        public const int CriticalMemoryMb = DegradedMemoryMb * 2;
        public const int CriticalEphemeralPorts = EphemeralPortPoolSize * 9 / 10;

        /// <summary>
        /// Pure threshold evaluation, kept separate from sampling so the self test
        /// can exercise it without a running core.
        /// </summary>
        public static CoreHealthLevel Evaluate(int connections, int handles, int memoryMb, int ephemeralPortsInUse)
        {
            if (connections >= CriticalConnections || handles >= CriticalHandles ||
                memoryMb >= CriticalMemoryMb || ephemeralPortsInUse >= CriticalEphemeralPorts)
                return CoreHealthLevel.Critical;
            if (connections >= DegradedConnections || handles >= DegradedHandles ||
                memoryMb >= DegradedMemoryMb || ephemeralPortsInUse >= DegradedEphemeralPorts)
                return CoreHealthLevel.Degraded;
            return CoreHealthLevel.Healthy;
        }

        public static CoreHealthSample Sample(int processId)
        {
            CoreHealthSample sample = new CoreHealthSample();
            if (processId <= 0) return sample;

            int connections, ephemeral;
            CountConnections(processId, out connections, out ephemeral);
            sample.Connections = connections;
            sample.EphemeralPortsInUse = ephemeral;

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    sample.Handles = process.HandleCount;
                    sample.MemoryMb = (int)(process.WorkingSet64 / (1024L * 1024L));
                }
            }
            catch { }

            sample.Level = Evaluate(sample.Connections, sample.Handles, sample.MemoryMb, sample.EphemeralPortsInUse);
            return sample;
        }

        private static void CountConnections(int processId, out int owned, out int ephemeralInUse)
        {
            owned = 0;
            ephemeralInUse = 0;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                int result = NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref size, false,
                    NativeMethods.AF_INET, NativeMethods.TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != NativeMethods.ERROR_INSUFFICIENT_BUFFER && result != 0) return;

                buffer = Marshal.AllocHGlobal(size);
                result = NativeMethods.GetExtendedTcpTable(buffer, ref size, false,
                    NativeMethods.AF_INET, NativeMethods.TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != 0) return;

                int rows = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf(typeof(NativeMethods.MIB_TCPROW_OWNER_PID));
                IntPtr cursor = new IntPtr(buffer.ToInt64() + 4);
                for (int i = 0; i < rows; i++)
                {
                    NativeMethods.MIB_TCPROW_OWNER_PID row = (NativeMethods.MIB_TCPROW_OWNER_PID)
                        Marshal.PtrToStructure(cursor, typeof(NativeMethods.MIB_TCPROW_OWNER_PID));
                    cursor = new IntPtr(cursor.ToInt64() + rowSize);

                    if (row.owningPid == processId) owned++;
                    if (LocalPort(row) >= EphemeralPortFloor) ephemeralInUse++;
                }
            }
            catch { }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        private static int LocalPort(NativeMethods.MIB_TCPROW_OWNER_PID row)
        {
            // The port arrives in network byte order inside a 32 bit field.
            return (int)(((row.localPort & 0xFF) << 8) | ((row.localPort >> 8) & 0xFF));
        }

        private static class NativeMethods
        {
            public const int AF_INET = 2;
            public const int TCP_TABLE_OWNER_PID_ALL = 5;
            public const int ERROR_INSUFFICIENT_BUFFER = 122;

            [StructLayout(LayoutKind.Sequential)]
            public struct MIB_TCPROW_OWNER_PID
            {
                public uint state;
                public uint localAddr;
                public uint localPort;
                public uint remoteAddr;
                public uint remotePort;
                public int owningPid;
            }

            [DllImport("iphlpapi.dll", SetLastError = true)]
            public static extern int GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool order,
                int addressFamily, int tableClass, int reserved);
        }
    }
}
