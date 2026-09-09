using System;
using System.Collections.Generic;
using System.Linq;

namespace OixNodeHelper
{
    public sealed class NodeMapper
    {
        private readonly string _path;
        private readonly object _sync = new object();
        private PortMapDocument _document;

        public NodeMapper(string path)
        {
            _path = path;
            _document = JsonFiles.Load(path, new PortMapDocument());
            if (_document.Entries == null) _document.Entries = new List<PortEntry>();
        }

        public List<NodeInfo> Assign(List<NodeInfo> nodes, int basePort, int maxNodes, int retentionDays)
        {
            lock (_sync)
            {
                List<NodeInfo> activeNodes = nodes
                    .Take(maxNodes)
                    .GroupBy(delegate(NodeInfo n) { return n.Name; }, StringComparer.Ordinal)
                    .Select(delegate(IGrouping<string, NodeInfo> group) { return group.First(); })
                    .ToList();
                HashSet<string> activeNames = new HashSet<string>(
                    activeNodes.Select(delegate(NodeInfo n) { return n.Name; }),
                    StringComparer.Ordinal);

                DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
                _document.Entries = _document.Entries
                    .Where(delegate(PortEntry e)
                    {
                        DateTime lastSeen;
                        bool recent = DateTime.TryParse(e == null ? null : e.LastSeenUtc, out lastSeen) && lastSeen >= cutoff;
                        return e != null && (activeNames.Contains(e.Name) || recent) &&
                            e.Port >= basePort && e.Port < basePort + maxNodes;
                    })
                    .GroupBy(delegate(PortEntry e) { return e.Name; }, StringComparer.Ordinal)
                    .Select(delegate(IGrouping<string, PortEntry> group) { return group.First(); })
                    .ToList();

                HashSet<int> uniquePorts = new HashSet<int>();
                _document.Entries = _document.Entries
                    .Where(delegate(PortEntry e) { return uniquePorts.Add(e.Port); })
                    .ToList();
                HashSet<int> used = new HashSet<int>(uniquePorts);
                string now = DateTime.UtcNow.ToString("o");
                List<NodeInfo> result = new List<NodeInfo>();
                foreach (NodeInfo node in activeNodes)
                {
                    PortEntry entry = _document.Entries.FirstOrDefault(delegate(PortEntry e)
                    {
                        return String.Equals(e.Name, node.Name, StringComparison.Ordinal);
                    });
                    if (entry == null)
                    {
                        int port;
                        if (!TryFindFreePort(used, basePort, maxNodes, out port))
                        {
                            PortEntry reclaim = _document.Entries
                                .Where(delegate(PortEntry e) { return !activeNames.Contains(e.Name); })
                                .OrderBy(delegate(PortEntry e) { return ParseDate(e.LastSeenUtc); })
                                .FirstOrDefault();
                            if (reclaim == null) throw new InvalidOperationException("No free local node port is available.");
                            port = reclaim.Port;
                            _document.Entries.Remove(reclaim);
                            used.Remove(port);
                        }
                        entry = new PortEntry { Name = node.Name, Port = port, LastSeenUtc = now };
                        _document.Entries.Add(entry);
                        used.Add(port);
                    }
                    entry.LastSeenUtc = now;
                    result.Add(new NodeInfo { Name = node.Name, Type = node.Type, Port = entry.Port, Udp = node.Udp });
                }
                JsonFiles.Save(_path, _document);
                return result;
            }
        }

        private static bool TryFindFreePort(HashSet<int> used, int basePort, int maxNodes, out int result)
        {
            for (int port = basePort; port < basePort + maxNodes; port++)
            {
                if (port > 65535) break;
                if (!used.Contains(port))
                {
                    result = port;
                    return true;
                }
            }
            result = 0;
            return false;
        }

        private static DateTime ParseDate(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, out parsed) ? parsed : DateTime.MinValue;
        }
    }
}
