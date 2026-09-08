using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace OixNodeHelper
{
    public static class ProviderRenderer
    {
        public static string RenderClash(IList<NodeInfo> nodes)
        {
            StringBuilder yaml = new StringBuilder();
            if (nodes == null || nodes.Count == 0) return "proxies: []\n";
            yaml.AppendLine("proxies:");
            foreach (NodeInfo node in nodes)
            {
                yaml.AppendLine("  - name: " + RuntimeConfigBuilder.QuoteYaml(node.Name));
                yaml.AppendLine("    type: socks5");
                yaml.AppendLine("    server: 127.0.0.1");
                yaml.AppendLine("    port: " + node.Port);
                yaml.AppendLine("    udp: true");
            }
            return yaml.ToString();
        }

        public static string RenderSurge(IList<NodeInfo> nodes)
        {
            StringBuilder result = new StringBuilder();
            if (nodes == null) return "";
            foreach (NodeInfo node in nodes)
            {
                result.Append(EscapeSurgeName(node.Name));
                result.Append(" = socks5, 127.0.0.1, ");
                result.Append(node.Port);
                result.AppendLine(", udp-relay=true");
            }
            return result.ToString();
        }

        public static string RenderNodesJson(IList<NodeInfo> nodes)
        {
            return new JavaScriptSerializer().Serialize(nodes ?? new List<NodeInfo>());
        }

        private static string EscapeSurgeName(string value)
        {
            return (value ?? "").Replace("=", "-").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
