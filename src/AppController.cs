using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace OixNodeHelper
{
    public sealed class AppController : IDisposable
    {
        public const string AppVersion = "0.3.0";
        private readonly object _sync = new object();
        private readonly AppPaths _paths;
        private readonly SettingsStore _settingsStore;
        private readonly CredentialStore _credentialStore;
        private readonly SafeLogger _log;
        private readonly CoreClient _coreClient;
        private readonly NodeMapper _mapper;
        private readonly RuntimeConfigBuilder _configBuilder;
        private readonly CoreSupervisor _supervisor;
        private readonly ProviderServer _server;
        private AppSettings _settings;
        private CredentialBundle _credentials;
        private List<NodeInfo> _nodes = new List<NodeInfo>();
        private Timer _timer;
        private int _refreshing;
        private int _consecutiveEmptyRefreshes;
        private int _failureCount;
        private string _lastRefreshUtc = "";
        private string _lastError = "";
        private string _stage = "正在启动";
        private bool _coreReachable;
        private bool _disposed;

        public event EventHandler StatusChanged;

        public AppController()
            : this(null)
        {
        }

        internal AppController(string dataRoot)
        {
            _paths = String.IsNullOrWhiteSpace(dataRoot) ? new AppPaths() : new AppPaths(dataRoot);
            _settingsStore = new SettingsStore(_paths);
            _credentialStore = new CredentialStore(_paths);
            _settings = _settingsStore.Load();
            _credentials = _credentialStore.Load();
            if (String.IsNullOrEmpty(_credentials.ControllerSecret))
            {
                _credentials.ControllerSecret = Guid.NewGuid().ToString("N");
                // CurrentUser DPAPI can be unavailable in some sandboxed or
                // profile-less sessions. Keep the generated secret in memory so
                // diagnostics and the local provider can still start; saving a
                // user-entered token will continue to surface a clear error.
                try { _credentialStore.Save(_credentials); } catch { }
            }
            _log = new SafeLogger(_paths.LogFile, delegate { return _credentials; });
            _coreClient = new CoreClient();
            _mapper = new NodeMapper(_paths.PortMapFile);
            _configBuilder = new RuntimeConfigBuilder();
            _supervisor = new CoreSupervisor(_log);
            _nodes = JsonFiles.Load(_paths.NodeCacheFile, new List<NodeInfo>());
            if (_nodes == null) _nodes = new List<NodeInfo>();
            _server = new ProviderServer(GetNodesSnapshot, GetHealth, RefreshAsync,
                delegate { return Credentials.ControllerSecret; }, _log);
        }

        public AppSettings Settings { get { lock (_sync) return CloneSettings(_settings); } }
        public CredentialBundle Credentials { get { lock (_sync) return CloneCredentials(_credentials); } }
        public AppPaths Paths { get { return _paths; } }

        public void Start()
        {
            _server.Start(_settings.ProviderPort);
            ApplyAutoStart(_settings);
            _timer = new Timer(delegate { RefreshAsync(); }, null, 1000, Timeout.Infinite);
        }

        public void SaveConfiguration(AppSettings settings, CredentialBundle credentials)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (credentials == null) credentials = new CredentialBundle();
            ValidateSettings(settings);
            int oldProviderPort;
            string oldControllerUrl;
            lock (_sync)
            {
                oldProviderPort = _settings.ProviderPort;
                oldControllerUrl = _settings.ControllerUrl;
            }
            if (oldProviderPort != settings.ProviderPort && !CanBind(settings.ProviderPort))
                throw new ArgumentException("Provider port " + settings.ProviderPort + " is already in use.");
            Uri oldController = new Uri(oldControllerUrl);
            Uri newController = new Uri(settings.ControllerUrl);
            if (oldController.Port != newController.Port && !CanBind(newController.Port))
                throw new ArgumentException("Controller port " + newController.Port + " is already in use.");
            lock (_sync)
            {
                _settings = CloneSettings(settings);
                _credentials = CloneCredentials(credentials);
                _settingsStore.Save(_settings);
                _credentialStore.Save(_credentials);
            }
            ApplyAutoStart(settings);
            if (oldProviderPort != settings.ProviderPort) _server.Start(settings.ProviderPort);
            if (_timer != null) _timer.Change(1000, Timeout.Infinite);
            RestartAsync();
        }

        public void RefreshAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate { RefreshWorker(true, false); });
        }

        public void ForceRefreshAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate { RefreshWorker(true, false); });
        }

        public void RestartAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate { RefreshWorker(true, true); });
        }

        private void RefreshWorker(bool refreshProviders, bool forceRestart)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
            bool succeeded = false;
            try
            {
                AppSettings settings = Settings;
                CredentialBundle credentials = Credentials;
                SetStage("检查配置");
                if (forceRestart) _supervisor.Stop();

                EnsureConfigured(settings, credentials);
                if (!_supervisor.IsRunning)
                {
                    string startConfig;
                    if (File.Exists(_paths.RuntimeConfigFile)) startConfig = _paths.RuntimeConfigFile;
                    else startConfig = _configBuilder.BuildBootstrap(_paths.BootstrapConfigFile, settings, credentials);
                    SetStage("启动官方核心");
                    _supervisor.Start(settings, credentials, startConfig, _paths.CoreDataDirectory);
                }

                SetStage("等待核心 Controller");
                if (!WaitForCore(settings, credentials, 30000))
                    throw new InvalidOperationException("Cannot reach core controller at " + settings.ControllerUrl + ".");
                lock (_sync) _coreReachable = true;
                Retry(delegate { _coreClient.SetOixOptions(settings, credentials); }, 3);

                if (refreshProviders)
                {
                    SetStage("通过 Token 更新 OixCloud 节点");
                    Retry(delegate { _coreClient.RefreshProviders(settings, credentials); }, 3);
                    Thread.Sleep(500);
                }
                SetStage("读取节点");
                List<NodeInfo> discovered = Retry(delegate { return _coreClient.GetNodes(settings, credentials); }, 3);
                if (discovered.Count == 0 && GetNodesSnapshot().Count == 0)
                {
                    _consecutiveEmptyRefreshes++;
                    throw new InvalidOperationException("No OixCloud nodes were returned. Verify the Access Token and account status.");
                }
                if (discovered.Count == 0 && GetNodesSnapshot().Count > 0)
                {
                    _consecutiveEmptyRefreshes++;
                    if (_consecutiveEmptyRefreshes < settings.EmptyRefreshThreshold)
                        throw new InvalidOperationException("OixCloud returned no nodes; the last good nodes were retained (" +
                            _consecutiveEmptyRefreshes + "/" + settings.EmptyRefreshThreshold + ").");
                }
                else if (discovered.Count > 0)
                {
                    _consecutiveEmptyRefreshes = 0;
                }
                List<NodeInfo> mapped = _mapper.Assign(discovered, settings.BaseNodePort, settings.MaxNodes, settings.PortRetentionDays);

                bool changed;
                lock (_sync) changed = !SameNodes(_nodes, mapped);
                if (changed || forceRestart || !_configBuilder.IsCurrent(_paths.RuntimeConfigFile))
                {
                    SetStage("验证候选配置");
                    _configBuilder.Build(_paths.CandidateConfigFile, settings, credentials, mapped);
                    if (!_supervisor.ValidateConfig(settings, credentials, _paths.CandidateConfigFile, _paths.CoreDataDirectory))
                        throw new InvalidOperationException("The generated candidate configuration failed mihomo validation.");
                    ApplyCandidateWithRollback(settings, credentials, mapped);
                }

                lock (_sync)
                {
                    _nodes = mapped;
                    _lastRefreshUtc = DateTime.UtcNow.ToString("o");
                    _lastError = "";
                    _stage = "运行正常";
                }
                JsonFiles.Save(_paths.NodeCacheFile, mapped);
                _log.Info("Node refresh completed: " + mapped.Count + " node(s).");
                succeeded = true;
            }
            catch (Exception ex)
            {
                lock (_sync) _lastError = ex.Message;
                lock (_sync) _coreReachable = false;
                lock (_sync) _stage = "更新失败，已保留上次结果";
                _log.Error("Refresh failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
                ScheduleNextRefresh(succeeded);
                RaiseStatusChanged();
            }
        }

        private void ScheduleNextRefresh(bool succeeded)
        {
            if (_disposed || _timer == null) return;
            if (succeeded) _failureCount = 0;
            else _failureCount = Math.Min(_failureCount + 1, 6);
            AppSettings settings = Settings;
            long multiplier = succeeded ? 1 : 1L << _failureCount;
            long seconds = Math.Min(3600L, settings.PollSeconds * multiplier);
            int jitter = succeeded ? 0 : new Random(unchecked(Environment.TickCount + _failureCount)).Next(0, Math.Max(2, (int)(seconds / 10)));
            _timer.Change((seconds + jitter) * 1000L, Timeout.Infinite);
        }

        private void EnsureConfigured(AppSettings settings, CredentialBundle credentials)
        {
            if (String.IsNullOrWhiteSpace(settings.CorePath) || !File.Exists(settings.CorePath))
                throw new InvalidOperationException("Please configure the official Windows mihomo-oix executable.");
            if (String.IsNullOrWhiteSpace(credentials.AccessToken))
                throw new InvalidOperationException("Please enter an oixCloud Access Token.");
        }

        private void ApplyCandidateWithRollback(AppSettings settings, CredentialBundle credentials, List<NodeInfo> mapped)
        {
            bool hadRuntime = File.Exists(_paths.RuntimeConfigFile);
            if (hadRuntime) File.Copy(_paths.RuntimeConfigFile, _paths.LastGoodConfigFile, true);
            File.Copy(_paths.CandidateConfigFile, _paths.RuntimeConfigFile, true);
            try
            {
                SetStage("热加载节点监听端口");
                _coreClient.ReloadConfig(settings, credentials, _paths.RuntimeConfigFile);
                if (!WaitForCore(settings, credentials, 15000))
                    throw new InvalidOperationException("Core Controller was unavailable after hot reload.");
                SetStage("绑定节点到本地端口");
                _coreClient.BindNodeRoutes(settings, credentials, mapped);
                if (!WaitForListeners(mapped, 15000))
                    throw new InvalidOperationException("One or more local node listeners did not become ready.");
                File.Copy(_paths.RuntimeConfigFile, _paths.LastGoodConfigFile, true);
            }
            catch
            {
                if (hadRuntime && File.Exists(_paths.LastGoodConfigFile))
                {
                    File.Copy(_paths.LastGoodConfigFile, _paths.RuntimeConfigFile, true);
                    try { _coreClient.ReloadConfig(settings, credentials, _paths.RuntimeConfigFile); }
                    catch { _supervisor.Start(settings, credentials, _paths.RuntimeConfigFile, _paths.CoreDataDirectory); }
                }
                throw;
            }
            finally
            {
                try { if (File.Exists(_paths.CandidateConfigFile)) File.Delete(_paths.CandidateConfigFile); } catch { }
            }
        }

        private static bool WaitForListeners(List<NodeInfo> nodes, int timeoutMilliseconds)
        {
            if (nodes.Count == 0) return true;
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                bool allReady = true;
                foreach (NodeInfo node in nodes)
                {
                    try
                    {
                        using (System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient())
                        {
                            IAsyncResult result = client.BeginConnect("127.0.0.1", node.Port, null, null);
                            System.Threading.WaitHandle handle = result.AsyncWaitHandle;
                            try
                            {
                                if (!handle.WaitOne(250) || !client.Connected) allReady = false;
                                else client.EndConnect(result);
                            }
                            finally { handle.Close(); }
                        }
                    }
                    catch { allReady = false; }
                    if (!allReady) break;
                }
                if (allReady) return true;
                Thread.Sleep(300);
            }
            return false;
        }

        private static void Retry(Action action, int attempts)
        {
            Retry<object>(delegate { action(); return null; }, attempts);
        }

        private static T Retry<T>(Func<T> action, int attempts)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try { return action(); }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < attempts) Thread.Sleep(400 * attempt);
                }
            }
            throw last ?? new InvalidOperationException("Operation failed.");
        }

        private void SetStage(string stage)
        {
            lock (_sync) _stage = stage;
            RaiseStatusChanged();
        }

        private bool WaitForCore(AppSettings settings, CredentialBundle credentials, int timeoutMilliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (_coreClient.IsReachable(settings, credentials)) return true;
                Thread.Sleep(500);
            }
            return false;
        }

        private static bool SameNodes(List<NodeInfo> left, List<NodeInfo> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!String.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal) || left[i].Port != right[i].Port)
                    return false;
            }
            return true;
        }

        private static bool CanBind(int port)
        {
            System.Net.Sockets.TcpListener listener = null;
            try
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch { return false; }
            finally { try { if (listener != null) listener.Stop(); } catch { } }
        }

        public List<NodeInfo> GetNodesSnapshot()
        {
            lock (_sync)
            {
                return _nodes.Select(delegate(NodeInfo n)
                {
                    return new NodeInfo { Name = n.Name, Type = n.Type, Port = n.Port };
                }).ToList();
            }
        }

        public HealthDocument GetHealth()
        {
            bool supervisedCoreRunning = _supervisor.IsRunning;
            lock (_sync)
            {
                bool coreRunning = supervisedCoreRunning || _coreReachable;
                return new HealthDocument
                {
                    Status = String.IsNullOrEmpty(_lastError) ? (coreRunning ? "ok" : "degraded") : "error",
                    CoreRunning = coreRunning,
                    NodeCount = _nodes.Count,
                    LastRefreshUtc = _lastRefreshUtc,
                    LastError = _lastError,
                    Version = AppVersion,
                    Stage = _stage,
                    ConsecutiveEmptyRefreshes = _consecutiveEmptyRefreshes
                };
            }
        }

        public string GetStatusText()
        {
            HealthDocument health = GetHealth();
            if (!String.IsNullOrEmpty(health.LastError)) return "错误：" + health.LastError;
            if (health.NodeCount == 0) return health.Stage;
            return health.Stage + " · " + health.NodeCount + " 个节点";
        }

        private void RaiseStatusChanged()
        {
            EventHandler handler = StatusChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void OpenDataFolder()
        {
            Process.Start("explorer.exe", "\"" + _paths.Root + "\"");
        }

        public void OpenProvider()
        {
            Process.Start("http://127.0.0.1:" + Settings.ProviderPort + "/clash");
        }

        private static void ValidateSettings(AppSettings settings)
        {
            Uri uri;
            if (!Uri.TryCreate(settings.ControllerUrl, UriKind.Absolute, out uri) || uri.Scheme != "http")
                throw new ArgumentException("Controller URL must be an absolute http:// URL.");
            if (!(uri.Host == "127.0.0.1" || String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Controller URL must use localhost for security.");
            if (settings.ProviderPort < 1024 || settings.ProviderPort > 65535) throw new ArgumentException("Provider port is invalid.");
            if (settings.BaseNodePort < 1024 || settings.BaseNodePort > 65000) throw new ArgumentException("Base node port is invalid.");
            if (settings.MaxNodes < 1 || settings.MaxNodes > 500) throw new ArgumentException("Max nodes must be between 1 and 500.");
            if (settings.PortRetentionDays < 1 || settings.PortRetentionDays > 365) throw new ArgumentException("Port retention must be between 1 and 365 days.");
            if (settings.EmptyRefreshThreshold < 1 || settings.EmptyRefreshThreshold > 10) throw new ArgumentException("Empty refresh threshold must be between 1 and 10.");
            if (settings.BaseNodePort + settings.MaxNodes >= 65535) throw new ArgumentException("Node port range exceeds 65535.");
            if (settings.ProviderPort >= settings.BaseNodePort && settings.ProviderPort < settings.BaseNodePort + settings.MaxNodes)
                throw new ArgumentException("Provider port overlaps the node port range.");
        }

        private static void ApplyAutoStart(AppSettings settings)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null) return;
                    string executable = settings.FrontendPath;
                    if (String.IsNullOrWhiteSpace(executable) &&
                        !String.Equals(Path.GetFileName(System.Windows.Forms.Application.ExecutablePath), "OixNodeHost.exe", StringComparison.OrdinalIgnoreCase))
                        executable = System.Windows.Forms.Application.ExecutablePath;
                    if (settings.StartWithWindows && !String.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                        key.SetValue("OixNodeHelper", "\"" + executable + "\"");
                    else if (!settings.StartWithWindows) key.DeleteValue("OixNodeHelper", false);
                }
            }
            catch { }
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            return new AppSettings
            {
                CorePath = source.CorePath,
                ControllerUrl = source.ControllerUrl, ProviderPort = source.ProviderPort,
                BaseNodePort = source.BaseNodePort, MaxNodes = source.MaxNodes,
                PollSeconds = source.PollSeconds, IncludeRegex = source.IncludeRegex,
                ExcludeRegex = source.ExcludeRegex, OixParams = source.OixParams,
                PortRetentionDays = source.PortRetentionDays, EmptyRefreshThreshold = source.EmptyRefreshThreshold,
                StartWithWindows = source.StartWithWindows, FrontendPath = source.FrontendPath
            };
        }

        private static CredentialBundle CloneCredentials(CredentialBundle source)
        {
            return new CredentialBundle { AccessToken = source.AccessToken, ControllerSecret = source.ControllerSecret };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer != null) _timer.Dispose();
            _server.Dispose();
            _supervisor.Dispose();
        }
    }
}
