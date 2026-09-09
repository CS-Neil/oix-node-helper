using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace OixNodeHelper
{
    public sealed class AppPaths
    {
        public string Root { get; private set; }
        public string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }
        public string CredentialsFile { get { return Path.Combine(Root, "credentials.dat"); } }
        public string PortMapFile { get { return Path.Combine(Root, "ports.json"); } }
        public string NodeCacheFile { get { return Path.Combine(Root, "nodes.json"); } }
        public string BootstrapConfigFile { get { return Path.Combine(CoreDataDirectory, "bootstrap.yaml"); } }
        public string RuntimeConfigFile { get { return Path.Combine(CoreDataDirectory, "runtime.yaml"); } }
        public string CandidateConfigFile { get { return Path.Combine(CoreDataDirectory, "runtime.next.yaml"); } }
        public string LastGoodConfigFile { get { return Path.Combine(CoreDataDirectory, "runtime.last-good.yaml"); } }
        public string LogFile { get { return Path.Combine(Root, "helper.log"); } }
        public string CoreDataDirectory { get { return Path.Combine(Root, "core-data"); } }

        public AppPaths()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OixNodeHelper"))
        {
        }

        internal AppPaths(string root)
        {
            Root = root;
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(CoreDataDirectory);
        }
    }

    public static class JsonFiles
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static T Load<T>(string path, T fallback)
        {
            try
            {
                if (!File.Exists(path)) return fallback;
                string json = File.ReadAllText(path, Encoding.UTF8);
                T value = Serializer.Deserialize<T>(json);
                return value == null ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        public static void Save<T>(string path, T value)
        {
            string json = Serializer.Serialize(value);
            AtomicWrite(path, Encoding.UTF8.GetBytes(json));
        }

        public static void AtomicWrite(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path))
            {
                string backup = path + ".bak";
                try
                {
                    File.Replace(temp, path, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                catch
                {
                    File.Delete(path);
                    File.Move(temp, path);
                }
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }

    public sealed class SettingsStore
    {
        private readonly AppPaths _paths;

        public SettingsStore(AppPaths paths)
        {
            _paths = paths;
        }

        public AppSettings Load()
        {
            AppSettings value = JsonFiles.Load(_paths.SettingsFile, AppSettings.CreateDefault());
            Normalize(value);
            return value;
        }

        public void Save(AppSettings value)
        {
            Normalize(value);
            JsonFiles.Save(_paths.SettingsFile, value);
        }

        private static void Normalize(AppSettings value)
        {
            if (String.IsNullOrWhiteSpace(value.ControllerUrl)) value.ControllerUrl = "http://127.0.0.1:6173";
            value.ControllerUrl = value.ControllerUrl.TrimEnd('/');
            if (value.ProviderPort < 1024 || value.ProviderPort > 65535) value.ProviderPort = 6172;
            if (value.BaseNodePort < 1024 || value.BaseNodePort > 65435) value.BaseNodePort = 7200;
            if (value.MaxNodes < 1 || value.MaxNodes > 500) value.MaxNodes = 100;
            // Below 120s the helper forces a full upstream subscription pull every tick,
            // which is unnecessary and keeps feeding new connections to any routing
            // loop. Persisted values under the floor are migrated once.
            if (value.PollSeconds < 120 || value.PollSeconds > 86400) value.PollSeconds = 300;
            if (value.CorePath == null) value.CorePath = "";
            if (value.IncludeRegex == null) value.IncludeRegex = "";
            if (value.ExcludeRegex == null) value.ExcludeRegex = "";
            if (value.OixParams == null) value.OixParams = "";
            if (value.FrontendPath == null) value.FrontendPath = "";
            if (value.PortRetentionDays < 1 || value.PortRetentionDays > 365) value.PortRetentionDays = 14;
            if (value.EmptyRefreshThreshold < 1 || value.EmptyRefreshThreshold > 10) value.EmptyRefreshThreshold = 3;
            if (String.IsNullOrWhiteSpace(value.CorePath))
            {
                string besideApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "mihomo-oix.exe");
                string besideProject = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "core", "mihomo-oix.exe"));
                if (File.Exists(besideApp)) value.CorePath = besideApp;
                else if (File.Exists(besideProject)) value.CorePath = besideProject;
            }
        }
    }

    public sealed class CredentialStore
    {
        private readonly AppPaths _paths;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OixNodeHelper.Credentials.v1");

        public CredentialStore(AppPaths paths)
        {
            _paths = paths;
        }

        public CredentialBundle Load()
        {
            if (!File.Exists(_paths.CredentialsFile)) return new CredentialBundle();
            byte[] clear = null;
            try
            {
                byte[] encrypted = File.ReadAllBytes(_paths.CredentialsFile);
                clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(clear);
                CredentialBundle result = new JavaScriptSerializer().Deserialize<CredentialBundle>(json);
                return result ?? new CredentialBundle();
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Unable to decrypt credentials.dat for the current Windows user. The file was not overwritten.", ex);
            }
            finally { if (clear != null) Array.Clear(clear, 0, clear.Length); }
        }

        public void Save(CredentialBundle value)
        {
            string json = new JavaScriptSerializer().Serialize(value ?? new CredentialBundle());
            byte[] clear = Encoding.UTF8.GetBytes(json);
            try
            {
                byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
                JsonFiles.AtomicWrite(_paths.CredentialsFile, encrypted);
            }
            finally { Array.Clear(clear, 0, clear.Length); }
        }
    }

    public sealed class SafeLogger
    {
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly Func<CredentialBundle> _credentials;

        public SafeLogger(string path, Func<CredentialBundle> credentials)
        {
            _path = path;
            _credentials = credentials;
        }

        public void Info(string message) { Write("INFO", message); }
        public void Error(string message) { Write("ERROR", message); }

        public void Write(string level, string message)
        {
            try
            {
                string safe = message ?? "";
                CredentialBundle credentials = _credentials == null ? null : _credentials();
                if (credentials != null)
                {
                    safe = Redact(safe, credentials.AccessToken);
                    safe = Redact(safe, credentials.ControllerSecret);
                }
                string line = DateTime.UtcNow.ToString("o") + " [" + level + "] " + safe + Environment.NewLine;
                lock (_sync)
                {
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static string Redact(string input, string secret)
        {
            if (String.IsNullOrEmpty(secret)) return input;
            return input.Replace(secret, "***");
        }
    }
}
