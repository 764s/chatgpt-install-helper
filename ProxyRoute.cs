using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ChatGPTInstaller
{
    internal sealed class ProxyRoute
    {
        public string Name;
        public IWebProxy Proxy;

        public static IEnumerable<ProxyRoute> Candidates(string custom)
        {
            if (!string.IsNullOrWhiteSpace(custom))
            {
                yield return Parse(custom, "自定义 HTTP 代理");
                yield break;
            }
            foreach (string key in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" })
            {
                string value = Environment.GetEnvironmentVariable(key);
                Uri uri;
                if (!string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                    uri.Scheme == "http") yield return Parse(value, key + " 代理");
            }
            IWebProxy system = WebRequest.GetSystemWebProxy();
            system.Credentials = CredentialCache.DefaultNetworkCredentials;
            yield return new ProxyRoute { Name = "Windows 系统代理 / 自动配置", Proxy = system };
            yield return new ProxyRoute { Name = "直接连接 / TUN", Proxy = null };
            // 仅在常见本机端口有监听时尝试，且始终保留正常的 HTTPS 证书校验。
            foreach (int port in new[] { 7897, 7890, 10809, 2080, 1080, 20171 })
            {
                bool listening = false;
                using (var client = new TcpClient())
                {
                    try
                    {
                        var attempt = client.BeginConnect(IPAddress.Loopback, port, null, null);
                        using (attempt.AsyncWaitHandle)
                        {
                            if (attempt.AsyncWaitHandle.WaitOne(150))
                            {
                                client.EndConnect(attempt);
                                listening = true;
                            }
                        }
                    }
                    catch (SocketException) { }
                }
                if (listening) yield return Parse("http://127.0.0.1:" + port, "本机代理端口 " + port);
            }
        }

        public static ProxyRoute Parse(string value, string name)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "http")
                throw new InvalidOperationException("代理地址应为 http://127.0.0.1:端口；SOCKS 代理请使用系统 TUN 或 HTTP 混合端口。");
            var proxy = new WebProxy(new UriBuilder(uri) { UserName = "", Password = "" }.Uri);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string[] parts = uri.UserInfo.Split(new[] { ':' }, 2);
                proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
            }
            else proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
            return new ProxyRoute { Name = name, Proxy = proxy };
        }
    }
}
