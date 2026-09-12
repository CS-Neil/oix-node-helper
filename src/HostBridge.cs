using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace OixNodeHelper
{
    /// <summary>
    /// Authenticated, loopback-only NDJSON bridge used by the Flutter front end.
    /// Each request uses a fresh TCP connection. events.subscribe keeps its
    /// connection open and receives a snapshot whenever AppController changes.
    /// </summary>
    public sealed class HostBridge : IDisposable
    {
        public const int ProtocolVersion = 1;
        private const int MaxRequestLength = 1024 * 1024;

        private readonly AppController _controller;
        private readonly string _sessionKey;
        private readonly ManualResetEvent _shutdown;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _clientsSync = new object();
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _disposed;

        public HostBridge(AppController controller, string sessionKey, ManualResetEvent shutdown)
        {
            if (controller == null) throw new ArgumentNullException("controller");
            if (String.IsNullOrWhiteSpace(sessionKey)) throw new ArgumentException("A session key is required.", "sessionKey");
            if (shutdown == null) throw new ArgumentNullException("shutdown");
            _controller = controller;
            _sessionKey = sessionKey;
            _shutdown = shutdown;
            _json.MaxJsonLength = MaxRequestLength;
        }

        public int Port { get; private set; }

        public void Start()
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Name = "OixNodeHost IPC";
            _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (!_disposed)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    lock (_clientsSync) _clients.Add(client);
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (SocketException)
                {
                    if (!_disposed && client != null) client.Close();
                }
                catch (ObjectDisposedException) { }
                catch
                {
                    if (client != null) client.Close();
                }
            }
        }

        private void HandleClient(object state)
        {
            TcpClient client = (TcpClient)state;
            try
            {
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    writer.NewLine = "\n";
                    writer.AutoFlush = true;
                    string line = reader.ReadLine();
                    if (String.IsNullOrEmpty(line) || line.Length > MaxRequestLength)
                    {
                        WriteError(writer, null, "invalid_request", "The request is empty or too large.");
                        return;
                    }

                    Dictionary<string, object> request = _json.DeserializeObject(line) as Dictionary<string, object>;
                    if (request == null)
                    {
                        WriteError(writer, null, "invalid_request", "The request must be a JSON object.");
                        return;
                    }
                    object id = GetValue(request, "id");
                    string auth = AsString(GetValue(request, "auth"));
                    if (!FixedTimeEquals(auth, _sessionKey))
                    {
                        WriteError(writer, id, "unauthorized", "The session key is invalid.");
                        return;
                    }

                    string method = AsString(GetValue(request, "method"));
                    if (String.Equals(method, "events.subscribe", StringComparison.Ordinal))
                    {
                        Subscribe(writer, id);
                        return;
                    }

                    bool requestShutdown;
                    object result = Dispatch(method, GetValue(request, "params") as Dictionary<string, object>, out requestShutdown);
                    WriteResult(writer, id, result);
                    if (requestShutdown) _shutdown.Set();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    using (NetworkStream stream = client.GetStream())
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    {
                        writer.AutoFlush = true;
                        WriteError(writer, null, "host_error", ex.Message);
                    }
                }
                catch { }
            }
            finally
            {
                lock (_clientsSync) _clients.Remove(client);
                try { client.Close(); } catch { }
            }
        }

        private void Subscribe(StreamWriter writer, object id)
        {
            using (AutoResetEvent changed = new AutoResetEvent(false))
            {
                EventHandler handler = delegate { changed.Set(); };
                _controller.StatusChanged += handler;
                try
                {
                    WriteResult(writer, id, new Dictionary<string, object> { { "subscribed", true } });
                    WriteEvent(writer, "snapshot", BuildSnapshot());
                    while (!_disposed && !_shutdown.WaitOne(0))
                    {
                        changed.WaitOne(30000);
                        WriteEvent(writer, "snapshot", BuildSnapshot());
                    }
                }
                finally
                {
                    _controller.StatusChanged -= handler;
                }
            }
        }

        private object Dispatch(string method, Dictionary<string, object> parameters, out bool requestShutdown)
        {
            requestShutdown = false;
            switch (method)
            {
                case "snapshot.get":
                    return BuildSnapshot();
                case "settings.get":
                    return BuildSettings();
                case "settings.save":
                    return SaveSettings(parameters);
                case "actions.refresh":
                    _controller.ForceRefreshAsync();
                    return Accepted();
                case "actions.restart":
                    _controller.RestartAsync();
                    return Accepted();
                case "actions.openDataFolder":
                    _controller.OpenDataFolder();
                    return Accepted();
                case "actions.openProvider":
                    _controller.OpenProvider();
                    return Accepted();
                case "logs.get":
                    return ReadLog(parameters);
                case "host.shutdown":
                    requestShutdown = true;
                    return Accepted();
                default:
                    throw new InvalidOperationException("Unknown host method: " + method);
            }
        }

        private Dictionary<string, object> BuildSnapshot()
        {
            HealthDocument health = _controller.GetHealth();
            List<NodeInfo> nodes = _controller.GetNodesSnapshot();
            List<object> nodeDocuments = new List<object>();
            foreach (NodeInfo node in nodes)
            {
                nodeDocuments.Add(new Dictionary<string, object>
                {
                    { "name", node.Name ?? "" },
                    { "type", node.Type ?? "" },
                    { "port", node.Port },
                    { "endpoint", "127.0.0.1:" + node.Port.ToString(CultureInfo.InvariantCulture) }
                });
            }
            return new Dictionary<string, object>
            {
                { "protocolVersion", ProtocolVersion },
                { "health", new Dictionary<string, object>
                    {
                        { "status", health.Status ?? "degraded" },
                        { "coreRunning", health.CoreRunning },
                        { "nodeCount", health.NodeCount },
                        { "lastRefreshUtc", health.LastRefreshUtc ?? "" },
                        { "lastError", health.LastError ?? "" },
                        { "version", health.Version ?? "" },
                        { "stage", health.Stage ?? "" },
                        { "consecutiveEmptyRefreshes", health.ConsecutiveEmptyRefreshes },
                        { "coreHealth", health.CoreHealth ?? "Healthy" },
                        { "coreConnections", health.CoreConnections },
                        { "coreHandles", health.CoreHandles },
                        { "coreMemoryMb", health.CoreMemoryMb },
                        { "ephemeralPortsInUse", health.EphemeralPortsInUse },
                        { "oixParamsEffective", health.OixParamsEffective ?? "" },
                        { "oixParamsDefault", health.OixParamsDefault ?? "" },
                        { "oixParamsSource", health.OixParamsSource ?? "" }
                    }
                },
                { "nodes", nodeDocuments },
                { "providerUrl", "http://127.0.0.1:" + _controller.Settings.ProviderPort.ToString(CultureInfo.InvariantCulture) + "/clash" }
            };
        }

        private Dictionary<string, object> BuildSettings()
        {
            AppSettings settings = _controller.Settings;
            CredentialBundle credentials = _controller.Credentials;
            return new Dictionary<string, object>
            {
                { "corePath", settings.CorePath ?? "" },
                { "controllerUrl", settings.ControllerUrl ?? "" },
                { "providerPort", settings.ProviderPort },
                { "baseNodePort", settings.BaseNodePort },
                { "maxNodes", settings.MaxNodes },
                { "pollSeconds", settings.PollSeconds },
                { "includeRegex", settings.IncludeRegex ?? "" },
                { "excludeRegex", settings.ExcludeRegex ?? "" },
                { "oixParams", settings.OixParams ?? "" },
                { "portRetentionDays", settings.PortRetentionDays },
                { "emptyRefreshThreshold", settings.EmptyRefreshThreshold },
                { "startWithWindows", settings.StartWithWindows },
                { "tokenConfigured", !String.IsNullOrWhiteSpace(credentials.AccessToken) }
            };
        }

        private Dictionary<string, object> SaveSettings(Dictionary<string, object> values)
        {
            if (values == null) throw new ArgumentException("Settings are required.");
            AppSettings current = _controller.Settings;
            CredentialBundle currentCredentials = _controller.Credentials;
            AppSettings settings = new AppSettings
            {
                CorePath = ReadString(values, "corePath", current.CorePath),
                ControllerUrl = ReadString(values, "controllerUrl", current.ControllerUrl),
                ProviderPort = ReadInt(values, "providerPort", current.ProviderPort),
                BaseNodePort = ReadInt(values, "baseNodePort", current.BaseNodePort),
                MaxNodes = ReadInt(values, "maxNodes", current.MaxNodes),
                PollSeconds = ReadInt(values, "pollSeconds", current.PollSeconds),
                IncludeRegex = ReadString(values, "includeRegex", current.IncludeRegex),
                ExcludeRegex = ReadString(values, "excludeRegex", current.ExcludeRegex),
                OixParams = ReadString(values, "oixParams", current.OixParams),
                PortRetentionDays = ReadInt(values, "portRetentionDays", current.PortRetentionDays),
                EmptyRefreshThreshold = ReadInt(values, "emptyRefreshThreshold", current.EmptyRefreshThreshold),
                StartWithWindows = ReadBool(values, "startWithWindows", current.StartWithWindows),
                FrontendPath = ReadString(values, "frontendPath", current.FrontendPath)
            };
            string accessToken = ReadString(values, "accessToken", currentCredentials.AccessToken);
            if (ReadBool(values, "clearAccessToken", false)) accessToken = "";
            CredentialBundle credentials = new CredentialBundle
            {
                AccessToken = accessToken,
                ControllerSecret = currentCredentials.ControllerSecret
            };
            _controller.SaveConfiguration(settings, credentials);
            return BuildSettings();
        }

        private Dictionary<string, object> ReadLog(Dictionary<string, object> parameters)
        {
            int limit = Math.Max(20, Math.Min(1000, ReadInt(parameters, "limit", 300)));
            Queue<string> tail = new Queue<string>();
            string path = _controller.Paths.LogFile;
            if (File.Exists(path))
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        tail.Enqueue(line);
                        if (tail.Count > limit) tail.Dequeue();
                    }
                }
            }
            return new Dictionary<string, object> { { "lines", tail.ToArray() } };
        }

        private static Dictionary<string, object> Accepted()
        {
            return new Dictionary<string, object> { { "accepted", true } };
        }

        private void WriteResult(StreamWriter writer, object id, object result)
        {
            writer.WriteLine(_json.Serialize(new Dictionary<string, object>
            {
                { "id", id }, { "ok", true }, { "result", result }
            }));
        }

        private void WriteError(StreamWriter writer, object id, string code, string message)
        {
            writer.WriteLine(_json.Serialize(new Dictionary<string, object>
            {
                { "id", id }, { "ok", false },
                { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } }
            }));
        }

        private void WriteEvent(StreamWriter writer, string name, object data)
        {
            writer.WriteLine(_json.Serialize(new Dictionary<string, object>
            {
                { "event", name }, { "data", data }
            }));
        }

        private static object GetValue(Dictionary<string, object> values, string key)
        {
            if (values == null) return null;
            object value;
            return values.TryGetValue(key, out value) ? value : null;
        }

        private static string AsString(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string ReadString(Dictionary<string, object> values, string key, string fallback)
        {
            object value = GetValue(values, key);
            return value == null ? fallback : AsString(value);
        }

        private static int ReadInt(Dictionary<string, object> values, string key, int fallback)
        {
            object value = GetValue(values, key);
            if (value == null) return fallback;
            int parsed;
            return Int32.TryParse(AsString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool ReadBool(Dictionary<string, object> values, string key, bool fallback)
        {
            object value = GetValue(values, key);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return Boolean.TryParse(AsString(value), out parsed) ? parsed : fallback;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null) left = "";
            if (right == null) right = "";
            int difference = left.Length ^ right.Length;
            int length = Math.Max(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                char a = i < left.Length ? left[i] : (char)0;
                char b = i < right.Length ? right[i] : (char)0;
                difference |= a ^ b;
            }
            return difference == 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_listener != null) _listener.Stop(); } catch { }
            lock (_clientsSync)
            {
                foreach (TcpClient client in _clients.ToArray())
                {
                    try { client.Close(); } catch { }
                }
                _clients.Clear();
            }
        }
    }
}
