using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OixNodeHelper
{
    public sealed class RuntimeConfigBuilder
    {
        private const string SchemaMarker = "# OixNodeHelper runtime schema: 3";

        // The helper core must never use the Windows system resolver. A running
        // FlClash TUN points the system DNS at itself with fake-ip enabled, so a
        // system lookup hands the core 198.18.x.x for its own upstream. Dialing
        // that address is captured by the TUN and routed back through FlClash,
        // which can return into this core own node ports and loop until the
        // Windows ephemeral port pool is exhausted.
        //
        // DoH over 443 is required: FlClash tun.dns-hijack (any:53) also
        // intercepts plain UDP/53 that leaves through the TUN. The URLs must be
        // IP literals so resolving the resolver never falls back to the system.
        // Change these two constants to use different upstream resolvers.
        private static readonly string[] BootstrapNameservers = { "223.5.5.5", "119.29.29.29" };
        private static readonly string[] SecureNameservers = { "https://223.5.5.5/dns-query", "https://1.12.12.12/dns-query" };

        public string Build(string outputPath, AppSettings settings, CredentialBundle credentials, IList<NodeInfo> nodes)
        {
            Uri controller = new Uri(settings.ControllerUrl);
            StringBuilder yaml = new StringBuilder();
            yaml.AppendLine(SchemaMarker);
            yaml.AppendLine("# Generated file. Do not edit; this file is replaced atomically.");
            yaml.AppendLine("mode: rule");
            yaml.AppendLine("log-level: warning");
            yaml.AppendLine("ipv6: false");
            yaml.AppendLine("allow-lan: false");
            yaml.AppendLine("bind-address: " + QuoteYaml("127.0.0.1"));
            yaml.AppendLine("external-controller: " + QuoteYaml(controller.Host + ":" + controller.Port));
            yaml.AppendLine("secret: " + QuoteYaml(credentials.ControllerSecret ?? ""));
            yaml.AppendLine("profile:");
            yaml.AppendLine("  store-selected: false");
            yaml.AppendLine("  store-fake-ip: false");
            yaml.AppendLine("dns:");
            yaml.AppendLine("  enable: true");
            yaml.AppendLine("  ipv6: false");
            yaml.AppendLine("  prefer-h3: false");
            yaml.AppendLine("  use-hosts: false");
            yaml.AppendLine("  use-system-hosts: false");
            yaml.AppendLine("  respect-rules: false");
            yaml.AppendLine("  enhanced-mode: normal");
            yaml.AppendLine("  default-nameserver:");
            foreach (string server in BootstrapNameservers)
                yaml.AppendLine("    - " + QuoteYaml(server));
            yaml.AppendLine("  nameserver:");
            foreach (string server in SecureNameservers)
                yaml.AppendLine("    - " + QuoteYaml(server));
            if (nodes == null || nodes.Count == 0)
            {
                yaml.AppendLine("proxy-groups: []");
                yaml.AppendLine("listeners: []");
            }
            else
            {
                yaml.AppendLine("proxy-groups:");
                foreach (NodeInfo node in nodes)
                {
                    yaml.AppendLine("  - name: " + QuoteYaml(RouteName(node.Port)));
                    yaml.AppendLine("    type: select");
                    yaml.AppendLine("    hidden: true");
                    yaml.AppendLine("    use:");
                    yaml.AppendLine("      - oixCloud");
                    yaml.AppendLine("    default-selected: " + QuoteYaml(node.Name));
                }
                yaml.AppendLine("listeners:");
                foreach (NodeInfo node in nodes)
                {
                    yaml.AppendLine("  - name: " + QuoteYaml("oix-local-" + node.Port));
                    yaml.AppendLine("    type: mixed");
                    yaml.AppendLine("    listen: 127.0.0.1");
                    yaml.AppendLine("    port: " + node.Port);
                    yaml.AppendLine("    udp: " + (node.Udp ? "true" : "false"));
                    yaml.AppendLine("    users: []");
                    yaml.AppendLine("    proxy: " + QuoteYaml(RouteName(node.Port)));
                }
            }
            yaml.AppendLine("rules:");
            yaml.AppendLine("  - MATCH,DIRECT");
            JsonFiles.AtomicWrite(outputPath, new UTF8Encoding(false).GetBytes(yaml.ToString()));
            return outputPath;
        }

        public string BuildBootstrap(string outputPath, AppSettings settings, CredentialBundle credentials)
        {
            return Build(outputPath, settings, credentials, new List<NodeInfo>());
        }

        public bool IsCurrent(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                    return String.Equals(reader.ReadLine(), SchemaMarker, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        internal static string QuoteYaml(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        internal static string RouteName(int port)
        {
            return "oix-route-" + port;
        }
    }
}
