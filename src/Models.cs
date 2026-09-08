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
                PollSeconds = 60,
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

    public sealed class NodeInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Port { get; set; }
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
    }
}
