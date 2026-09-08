using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace OixNodeHelper
{
    public sealed class CoreClient
    {
        private const string OixProviderName = "oixCloud";
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public bool IsReachable(AppSettings settings, CredentialBundle credentials)
        {
            try
            {
                Request(settings.ControllerUrl + "/version", "GET", credentials.ControllerSecret, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public List<NodeInfo> GetNodes(AppSettings settings, CredentialBundle credentials)
        {
            string json = Request(settings.ControllerUrl + "/providers/proxies", "GET", credentials.ControllerSecret, null);
            return ParseProviderNodes(json, settings);
        }

        internal List<NodeInfo> ParseProviderNodes(string json, AppSettings settings)
        {
            Dictionary<string, object> provider = FindProvider(json, OixProviderName);
            object proxiesObject;
            if (!provider.TryGetValue("proxies", out proxiesObject) || proxiesObject == null)
                throw new InvalidDataException("The oixCloud provider response has no proxies list.");

            IEnumerable proxies = proxiesObject as IEnumerable;
            if (proxies == null || proxiesObject is string)
                throw new InvalidDataException("The oixCloud provider proxies list is invalid.");

            Regex include = CompileOptional(settings.IncludeRegex);
            Regex exclude = CompileOptional(settings.ExcludeRegex);
            List<NodeInfo> result = new List<NodeInfo>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (object item in proxies)
            {
                Dictionary<string, object> proxy = item as Dictionary<string, object>;
                if (proxy == null) continue;
                string type = GetString(proxy, "type");
                string name = GetString(proxy, "name");
                if (!IsLeafProxy(type, name)) continue;
                if (include != null && !include.IsMatch(name)) continue;
                if (exclude != null && exclude.IsMatch(name)) continue;
                if (!names.Add(name)) continue;
                result.Add(new NodeInfo { Name = name, Type = type });
            }

            result.Sort(delegate(NodeInfo a, NodeInfo b)
            {
                return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            });

            if (result.Count > settings.MaxNodes)
                result.RemoveRange(settings.MaxNodes, result.Count - settings.MaxNodes);
            return result;
        }

        public int RefreshProviders(AppSettings settings, CredentialBundle credentials)
        {
            string json = Request(settings.ControllerUrl + "/providers/proxies", "GET", credentials.ControllerSecret, null);
            FindProvider(json, OixProviderName);
            string escaped = Uri.EscapeDataString(OixProviderName);
            Request(settings.ControllerUrl + "/providers/proxies/" + escaped, "PUT", credentials.ControllerSecret, new byte[0], 30000);
            return 1;
        }

        private Dictionary<string, object> FindProvider(string json, string providerName)
        {
            object rootObject = _serializer.DeserializeObject(json);
            Dictionary<string, object> root = rootObject as Dictionary<string, object>;
            object providersObject;
            if (root == null || !root.TryGetValue("providers", out providersObject))
                throw new InvalidDataException("Core /providers/proxies response has no providers object.");
            Dictionary<string, object> providers = providersObject as Dictionary<string, object>;
            if (providers == null)
                throw new InvalidDataException("Core /providers/proxies response is invalid.");

            foreach (KeyValuePair<string, object> item in providers)
            {
                if (!String.Equals(item.Key, providerName, StringComparison.OrdinalIgnoreCase)) continue;
                Dictionary<string, object> provider = item.Value as Dictionary<string, object>;
                if (provider == null)
                    throw new InvalidDataException("The oixCloud provider response is invalid.");
                return provider;
            }
            throw new InvalidDataException("The oixCloud provider was not created. Verify the Access Token and subscription binding.");
        }

        public void ReloadConfig(AppSettings settings, CredentialBundle credentials, string path)
        {
            string json = _serializer.Serialize(new Dictionary<string, object>
            {
                { "path", path },
                { "payload", "" }
            });
            Request(settings.ControllerUrl + "/configs?force=true", "PUT", credentials.ControllerSecret, Encoding.UTF8.GetBytes(json), 30000);
        }

        public void BindNodeRoutes(AppSettings settings, CredentialBundle credentials, IList<NodeInfo> nodes)
        {
            if (nodes == null) return;
            foreach (NodeInfo node in nodes)
            {
                string json = _serializer.Serialize(new Dictionary<string, object> { { "name", node.Name } });
                string group = Uri.EscapeDataString(RuntimeConfigBuilder.RouteName(node.Port));
                Request(settings.ControllerUrl + "/proxies/" + group, "PUT", credentials.ControllerSecret,
                    Encoding.UTF8.GetBytes(json), 10000);
            }
        }

        public void SetOixOptions(AppSettings settings, CredentialBundle credentials)
        {
            if (String.IsNullOrWhiteSpace(settings.OixParams)) return;
            string json = _serializer.Serialize(new Dictionary<string, object> { { "params", settings.OixParams } });
            Request(settings.ControllerUrl + "/oix/options", "PUT", credentials.ControllerSecret, Encoding.UTF8.GetBytes(json), 10000);
        }

        private static Regex CompileOptional(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return new Regex(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsLeafProxy(string type, string name)
        {
            string normalized = (type ?? "").Replace("-", "").Replace("_", "").ToLowerInvariant();
            switch (normalized)
            {
                case "selector":
                case "urltest":
                case "fallback":
                case "loadbalance":
                case "relay":
                case "compatible":
                case "smart":
                case "group":
                case "direct":
                case "reject":
                case "rejectdrop":
                case "pass":
                case "passrule":
                    return false;
            }
            if (String.Equals(name, "GLOBAL", StringComparison.OrdinalIgnoreCase)) return false;
            return !String.IsNullOrWhiteSpace(type);
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }

        private static string Request(string url, string method, string secret, byte[] body, int timeout = 5000)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            request.Proxy = null;
            request.UserAgent = "OixNodeHelper/0.1";
            if (!String.IsNullOrEmpty(secret)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + secret;
            if (body != null)
            {
                request.ContentLength = body.Length;
                request.ContentType = "application/json";
                using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            }
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
