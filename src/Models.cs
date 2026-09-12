using System;
using System.Collections.Generic;

namespace OixNodeHelper
{
    public sealed class AppSettings
    {
        public string CorePath { get; set; }
        public string ControllerUrl { get; set; }
        public int ProviderPort { get; set; }
        public int BaseNodePort { get; set; }
        public int MaxNodes { get; set; }
        public int PollSeconds { get; set; }
        public string IncludeRegex { get; set; }
        public string ExcludeRegex { get; set; }
        public string OixParams { get; set; }
        public int PortRetentionDays { get; set; }
        public int EmptyRefreshThreshold { get; set; }
        public bool StartWithWindows { get; set; }
        public string FrontendPath { get; set; }

        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                CorePath = "",
                ControllerUrl = "http://127.0.0.1:6173",
                ProviderPort = 6172,
                BaseNodePort = 7200,
                MaxNodes = 100,
                PollSeconds = 300,
                IncludeRegex = "",
                ExcludeRegex = "",
                OixParams = "",
                PortRetentionDays = 14,
                EmptyRefreshThreshold = 3,
                StartWithWindows = false,
                FrontendPath = ""
            };
        }
    }

    public sealed class CredentialBundle
    {
        public string AccessToken { get; set; }
        public string ControllerSecret { get; set; }

        public CredentialBundle()
        {
            AccessToken = "";
            ControllerSecret = "";
        }
    }

    // Mirrors GET/PUT /oix/options on the official core. Params is the query
    // fragment the core appends to the managed subscription URL, for example
    // "&mode=premium&tfo=true". DefaultParams is what the account plan supplies
    // on its own, and Source reports where the core took the value from.
    //
    // The core re-injects its own reserved keys (tfo) after every write, so the
    // effective Params is always a superset of what the helper submits.
    public sealed class OixOptions
    {
        public string Params { get; set; }
        public string DefaultParams { get; set; }
        public string Source { get; set; }

        public OixOptions()
        {
            Params = "";
            DefaultParams = "";
            Source = "";
        }
    }

    public sealed class NodeInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Port { get; set; }

        // Mirrors what the upstream node actually advertises. Claiming UDP a node
        // does not have makes FlClash send UDP that dies upstream, which shows up
        // as QUIC and proxied-DNS stalls rather than a clean failure.
        public bool Udp { get; set; }

        public NodeInfo()
        {
            Udp = true;
        }
    }

    public sealed class PortEntry
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public string LastSeenUtc { get; set; }
    }

    public sealed class PortMapDocument
    {
        public List<PortEntry> Entries { get; set; }

        public PortMapDocument()
        {
            Entries = new List<PortEntry>();
        }
    }

    public sealed class HealthDocument
    {
        public string Status { get; set; }
        public bool CoreRunning { get; set; }
        public int NodeCount { get; set; }
        public string LastRefreshUtc { get; set; }
        public string LastError { get; set; }
        public string Version { get; set; }
        public string Stage { get; set; }
        public int ConsecutiveEmptyRefreshes { get; set; }
        public string CoreHealth { get; set; }
        public int CoreConnections { get; set; }
        public int CoreHandles { get; set; }
        public int CoreMemoryMb { get; set; }
        public int EphemeralPortsInUse { get; set; }
        public string OixParamsEffective { get; set; }
        public string OixParamsDefault { get; set; }
        public string OixParamsSource { get; set; }

        public HealthDocument()
        {
            OixParamsEffective = "";
            OixParamsDefault = "";
            OixParamsSource = "";
        }
    }
}
