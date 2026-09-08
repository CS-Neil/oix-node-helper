using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OixNodeHelper
{
    public static class SelfTest
    {
        public static int Run()
        {
            List<string> failures = new List<string>();
            TestYaml(failures);
            TestProvider(failures);
            TestCoreProviderParsing(failures);
            TestMapper(failures);
            TestDpapi(failures);
            TestServer(failures);
            TestCoreValidation(failures);
            if (failures.Count == 0)
            {
                Console.WriteLine("PASS: all self-tests passed");
                return 0;
            }
            foreach (string failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            return 1;
        }

        private static void TestYaml(List<string> failures)
        {
            string path = Path.Combine(Path.GetTempPath(), "OixNodeHelper-config-" + Guid.NewGuid().ToString("N") + ".yaml");
            AppSettings settings = AppSettings.CreateDefault();
            CredentialBundle credentials = new CredentialBundle { ControllerSecret = "secret" };
            new RuntimeConfigBuilder().Build(path, settings, credentials, new List<NodeInfo>
            {
                new NodeInfo { Name = "Node 'A'", Port = 7200, Type = "VLESS" }
            });
            string clean = File.ReadAllText(path);
            Assert(new RuntimeConfigBuilder().IsCurrent(path), "runtime schema marker was not recognized", failures);
            Assert(clean.Contains("external-controller: '127.0.0.1:6173'"), "controller was not generated", failures);
            Assert(clean.Contains("name: 'oix-route-7200'"), "per-node route group was not generated", failures);
            Assert(clean.Contains("use:\r\n      - oixCloud") || clean.Contains("use:\n      - oixCloud"),
                "route group does not use the oixCloud provider", failures);
            Assert(clean.Contains("default-selected: 'Node ''A'''"), "route group node selection escaping is incorrect", failures);
            Assert(clean.Contains("proxy: 'oix-route-7200'"), "listener does not reference its route group", failures);
            Assert(RuntimeConfigBuilder.QuoteYaml("a'b") == "'a''b'", "YAML quoting is incorrect", failures);
            try { File.Delete(path); } catch { }
        }

        private static void TestProvider(List<string> failures)
        {
            List<NodeInfo> nodes = new List<NodeInfo>
            {
                new NodeInfo { Name = "Hong Kong '01'", Type = "VLESS", Port = 7200 }
            };
            string yaml = ProviderRenderer.RenderClash(nodes);
            Assert(yaml.Contains("name: 'Hong Kong ''01'''"), "provider name escaping is incorrect", failures);
            Assert(yaml.Contains("port: 7200"), "provider port is missing", failures);
        }

        private static void TestCoreProviderParsing(List<string> failures)
        {
            string json = "{\"providers\":{\"default\":{\"proxies\":[{\"name\":\"DIRECT\",\"type\":\"Direct\"}]}," +
                "\"oixCloud\":{\"proxies\":[{\"name\":\"Node B\",\"type\":\"Snell\"}," +
                "{\"name\":\"PASS-RULE\",\"type\":\"PassRule\"},{\"name\":\"Node A\",\"type\":\"Snell\"}]}}}";
            try
            {
                List<NodeInfo> nodes = new CoreClient().ParseProviderNodes(json, AppSettings.CreateDefault());
                Assert(nodes.Count == 2, "provider parsing did not return only real oixCloud nodes", failures);
                Assert(nodes.Count == 2 && nodes[0].Name == "Node A" && nodes[1].Name == "Node B",
                    "provider nodes were not sorted correctly", failures);
            }
            catch (Exception ex) { failures.Add("provider parsing threw: " + ex.Message); }
        }

        private static void TestMapper(List<string> failures)
        {
            string dir = Path.Combine(Path.GetTempPath(), "OixNodeHelper-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "ports.json");
            try
            {
                NodeMapper mapper = new NodeMapper(path);
                List<NodeInfo> first = mapper.Assign(new List<NodeInfo>
                {
                    new NodeInfo { Name = "A", Type = "VLESS" },
                    new NodeInfo { Name = "B", Type = "Trojan" }
                }, 7200, 10, 14);
                List<NodeInfo> second = mapper.Assign(new List<NodeInfo>
                {
                    new NodeInfo { Name = "B", Type = "Trojan" },
                    new NodeInfo { Name = "A", Type = "VLESS" }
                }, 7200, 10, 14);
                int firstA = first.Find(delegate(NodeInfo n) { return n.Name == "A"; }).Port;
                int secondA = second.Find(delegate(NodeInfo n) { return n.Name == "A"; }).Port;
                Assert(firstA == secondA, "node port was not stable", failures);
                Assert(first[0].Port != first[1].Port, "node ports are not unique", failures);

                List<NodeInfo> shifted = mapper.Assign(new List<NodeInfo>
                {
                    new NodeInfo { Name = "A", Type = "VLESS" },
                    new NodeInfo { Name = "C", Type = "VLESS" }
                }, 7300, 2, 14);
                Assert(shifted.TrueForAll(delegate(NodeInfo n) { return n.Port >= 7300 && n.Port < 7302; }),
                    "ports were not remapped into the configured range", failures);
                Assert(shifted[0].Port != shifted[1].Port, "remapped ports are not unique", failures);
            }
            catch (Exception ex) { failures.Add("mapper threw: " + ex.Message); }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
                try { Directory.Delete(dir, false); } catch { }
            }
        }

        private static void TestDpapi(List<string> failures)
        {
            string dir = Path.Combine(Path.GetTempPath(), "OixNodeHelper-DpapiTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                AppPaths paths = new AppPaths(dir);
                CredentialStore store = new CredentialStore(paths);
                CredentialBundle test = new CredentialBundle { AccessToken = "self-test-token", ControllerSecret = "self-test-secret" };
                store.Save(test);
                CredentialBundle loaded = store.Load();
                Assert(loaded.AccessToken == test.AccessToken && loaded.ControllerSecret == test.ControllerSecret, "DPAPI round-trip failed", failures);
            }
            catch (CryptographicException ex)
            {
                // CI/sandbox identities may not have a loaded Windows user profile.
                // A real encryption/decryption mismatch still reaches Assert above.
                Console.WriteLine("SKIP: DPAPI is unavailable for this process identity: " + ex.Message);
            }
            catch (Exception ex) { failures.Add("DPAPI test threw: " + ex.Message); }
            finally
            {
                try { if (File.Exists(Path.Combine(dir, "credentials.dat"))) File.Delete(Path.Combine(dir, "credentials.dat")); } catch { }
                try { if (Directory.Exists(Path.Combine(dir, "core-data"))) Directory.Delete(Path.Combine(dir, "core-data"), false); } catch { }
                try { if (Directory.Exists(dir)) Directory.Delete(dir, false); } catch { }
            }
        }

        private static void TestServer(List<string> failures)
        {
            int port = FindTestPort();
            CredentialBundle credentials = new CredentialBundle();
            int refreshCount = 0;
            string logPath = Path.Combine(Path.GetTempPath(), "OixNodeHelper-server-test-" + Guid.NewGuid().ToString("N") + ".log");
            ProviderServer server = new ProviderServer(
                delegate { return new List<NodeInfo>(); },
                delegate { return new HealthDocument { Status = "ok", Version = "test" }; },
                delegate { refreshCount++; },
                delegate { return "test-management-secret"; },
                new SafeLogger(logPath, delegate { return credentials; }));
            try
            {
                server.Start(port);
                using (WebClient client = new WebClient())
                {
                    client.Proxy = null;
                    string health = client.DownloadString("http://127.0.0.1:" + port + "/health");
                    string clash = client.DownloadString("http://127.0.0.1:" + port + "/clash");
                    Assert(health.Contains("\"Status\":\"ok\""), "health endpoint response is invalid", failures);
                    Assert(clash == "proxies: []\n", "empty provider response is invalid", failures);
                    bool unauthorized = false;
                    try { client.UploadString("http://127.0.0.1:" + port + "/api/refresh", "POST", ""); }
                    catch (WebException ex)
                    {
                        HttpWebResponse response = ex.Response as HttpWebResponse;
                        unauthorized = response != null && response.StatusCode == HttpStatusCode.Unauthorized;
                    }
                    Assert(unauthorized, "management endpoint accepted an unauthenticated request", failures);
                    client.Headers[HttpRequestHeader.Authorization] = "Bearer test-management-secret";
                    client.UploadString("http://127.0.0.1:" + port + "/api/refresh", "POST", "");
                    Assert(refreshCount == 1, "authenticated refresh was not accepted", failures);
                }
            }
            catch (Exception ex) { failures.Add("HTTP server test threw: " + ex.Message); }
            finally
            {
                server.Dispose();
                try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
            }
        }

        private static void TestCoreValidation(List<string> failures)
        {
            string projectCore = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "core", "mihomo-oix.exe"));
            if (!File.Exists(projectCore))
            {
                Console.WriteLine("SKIP: bundled mihomo-oix core was not found for validation");
                return;
            }
            string dir = Path.Combine(Path.GetTempPath(), "OixNodeHelper-CoreTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string config = Path.Combine(dir, "config.yaml");
            string log = Path.Combine(dir, "test.log");
            CredentialBundle credentials = new CredentialBundle { ControllerSecret = "test-secret" };
            AppSettings settings = AppSettings.CreateDefault();
            settings.CorePath = projectCore;
            try
            {
                new RuntimeConfigBuilder().BuildBootstrap(config, settings, credentials);
                using (CoreSupervisor supervisor = new CoreSupervisor(new SafeLogger(log, delegate { return credentials; })))
                    Assert(supervisor.ValidateConfig(settings, credentials, config, dir), "mihomo rejected generated bootstrap config", failures);
            }
            catch (Exception ex) { failures.Add("core validation threw: " + ex.Message); }
            finally
            {
                try { if (File.Exists(config)) File.Delete(config); } catch { }
                try { if (File.Exists(log)) File.Delete(log); } catch { }
                try { Directory.Delete(dir, false); } catch { }
            }
        }

        private static int FindTestPort()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void Assert(bool condition, string message, List<string> failures)
        {
            if (!condition) failures.Add(message);
        }
    }
}
