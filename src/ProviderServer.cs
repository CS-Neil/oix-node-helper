using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace OixNodeHelper
{
    public sealed class ProviderServer : IDisposable
    {
        private readonly Func<List<NodeInfo>> _getNodes;
        private readonly Func<HealthDocument> _getHealth;
        private readonly Action _refresh;
        private readonly Func<string> _getManagementSecret;
        private readonly SafeLogger _log;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _stopping;

        public ProviderServer(Func<List<NodeInfo>> getNodes, Func<HealthDocument> getHealth, Action refresh,
            Func<string> getManagementSecret, SafeLogger log)
        {
            _getNodes = getNodes;
            _getHealth = getHealth;
            _refresh = refresh;
            _getManagementSecret = getManagementSecret;
            _log = log;
        }

        public int Port { get; private set; }

        public void Start(int port)
        {
            Stop();
            Port = port;
            _stopping = false;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start(32);
            _thread = new Thread(AcceptLoop);
            _thread.IsBackground = true;
            _thread.Name = "OixNodeHelper HTTP";
            _thread.Start();
            _log.Info("Provider listening at http://127.0.0.1:" + port + "/.");
        }

        public void Stop()
        {
            _stopping = true;
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;
            if (_thread != null && _thread != Thread.CurrentThread)
            {
                try { _thread.Join(1000); } catch { }
            }
            _thread = null;
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
                }
                catch (SocketException)
                {
                    if (!_stopping) Thread.Sleep(250);
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    _log.Error("Provider accept failed: " + ex.Message);
                    Thread.Sleep(250);
                }
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    string requestLine;
                    string authorization = "";
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                    {
                        requestLine = reader.ReadLine();
                        int headerBytes = requestLine == null ? 0 : requestLine.Length;
                        string line;
                        while (!String.IsNullOrEmpty(line = reader.ReadLine()))
                        {
                            headerBytes += line.Length;
                            if (headerBytes > 16384) throw new InvalidDataException("HTTP headers are too large.");
                            int separator = line.IndexOf(':');
                            if (separator > 0 && String.Equals(line.Substring(0, separator).Trim(), "Authorization", StringComparison.OrdinalIgnoreCase))
                                authorization = line.Substring(separator + 1).Trim();
                        }
                    }
                    if (String.IsNullOrWhiteSpace(requestLine)) return;
                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 2) { Respond(stream, 400, "text/plain", "Bad Request\n"); return; }
                    string method = parts[0].ToUpperInvariant();
                    string path = parts[1].Split('?')[0];

                    if (method == "GET" && path == "/health")
                    {
                        Respond(stream, 200, "application/json; charset=utf-8", new JavaScriptSerializer().Serialize(_getHealth()));
                    }
                    else if (method == "GET" && path == "/clash")
                    {
                        Respond(stream, 200, "application/yaml; charset=utf-8", ProviderRenderer.RenderClash(_getNodes()));
                    }
                    else if (method == "GET" && path == "/list")
                    {
                        Respond(stream, 200, "text/plain; charset=utf-8", ProviderRenderer.RenderSurge(_getNodes()));
                    }
                    else if (method == "GET" && path == "/api/nodes")
                    {
                        Respond(stream, 200, "application/json; charset=utf-8", ProviderRenderer.RenderNodesJson(_getNodes()));
                    }
                    else if (method == "POST" && path == "/api/refresh")
                    {
                        string expected = "Bearer " + (_getManagementSecret() ?? "");
                        if (expected.Length <= 7 || !FixedTimeEquals(authorization, expected))
                            Respond(stream, 401, "application/json; charset=utf-8", "{\"error\":\"unauthorized\"}");
                        else
                        {
                            _refresh();
                            Respond(stream, 202, "application/json; charset=utf-8", "{\"accepted\":true}");
                        }
                    }
                    else
                    {
                        Respond(stream, 404, "application/json; charset=utf-8", "{\"error\":\"not found\"}");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Provider request failed: " + ex.Message);
                }
            }
        }

        private static void Respond(Stream stream, int status, string contentType, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            string reason = status == 200 ? "OK" : status == 202 ? "Accepted" : status == 400 ? "Bad Request" : status == 401 ? "Unauthorized" : "Not Found";
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null) left = "";
            if (right == null) right = "";
            int difference = left.Length ^ right.Length;
            int count = Math.Max(left.Length, right.Length);
            for (int i = 0; i < count; i++)
            {
                char a = i < left.Length ? left[i] : (char)0;
                char b = i < right.Length ? right[i] : (char)0;
                difference |= a ^ b;
            }
            return difference == 0;
        }

        public void Dispose() { Stop(); }
    }
}
