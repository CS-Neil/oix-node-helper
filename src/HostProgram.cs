using System;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace OixNodeHelper
{
    internal static class HostProgram
    {
        private static int Main(string[] args)
        {
            string sessionKey = Console.ReadLine();
            if (String.IsNullOrWhiteSpace(sessionKey)) return 2;

            int parentPid = ParseParentPid(args);
            string dataRoot = ParseArgument(args, "--data-root=");
            bool diagnosticNoStart = HasArgument(args, "--diagnostic-no-start");
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\OixNodeHost.SingleInstance", out created))
            {
                if (!created)
                {
                    WriteHandshake(0, false, "OixNodeHost is already running.");
                    return 3;
                }
                try
                {
                    using (ManualResetEvent shutdown = new ManualResetEvent(false))
                    using (AppController controller = new AppController(dataRoot))
                    using (HostBridge bridge = new HostBridge(controller, sessionKey, shutdown))
                    using (Timer parentTimer = CreateParentMonitor(parentPid, shutdown))
                    {
                        bridge.Start();
                        if (!diagnosticNoStart) controller.Start();
                        WriteHandshake(bridge.Port, true, "");
                        shutdown.WaitOne();
                        GC.KeepAlive(parentTimer);
                        GC.KeepAlive(mutex);
                        return 0;
                    }
                }
                catch (Exception ex)
                {
                    WriteHandshake(0, false, ex.Message);
                    return 1;
                }
            }
        }

        private static Timer CreateParentMonitor(int parentPid, ManualResetEvent shutdown)
        {
            if (parentPid <= 0) return null;
            return new Timer(delegate
            {
                try
                {
                    Process parent = Process.GetProcessById(parentPid);
                    if (parent.HasExited) shutdown.Set();
                    parent.Dispose();
                }
                catch { shutdown.Set(); }
            }, null, 2000, 2000);
        }

        private static int ParseParentPid(string[] args)
        {
            foreach (string arg in args ?? new string[0])
            {
                const string prefix = "--parent-pid=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    int pid;
                    if (Int32.TryParse(arg.Substring(prefix.Length), out pid)) return pid;
                }
            }
            return 0;
        }

        private static string ParseArgument(string[] args, string prefix)
        {
            foreach (string arg in args ?? new string[0])
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length);
            }
            return null;
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string arg in args ?? new string[0])
            {
                if (String.Equals(arg, expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void WriteHandshake(int port, bool ok, string error)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            string line = json.Serialize(new
            {
                ok = ok,
                protocolVersion = HostBridge.ProtocolVersion,
                port = port,
                processId = Process.GetCurrentProcess().Id,
                error = error ?? ""
            });
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }
}
