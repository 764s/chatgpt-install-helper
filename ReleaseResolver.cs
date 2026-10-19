using System;
using System.IO;
using System.Threading;

namespace ChatGPTInstaller
{
    internal sealed class ReleaseResolver
    {
        private readonly RunLog log;
        public NetworkClient Client { get; private set; }

        public ReleaseResolver(RunLog log) { this.log = log; }

        public ReleaseInfo Resolve(string architecture, string customProxy, CancellationToken token)
        {
            Exception last = null;
            ReleaseInfo announced = null;
            foreach (var route in ProxyRoute.Candidates(customProxy))
            {
                token.ThrowIfCancellationRequested();
                log.Write("连接官方来源：" + route.Name);
                var client = new NetworkClient(route, log);
                try
                {
                    var release = ReadManifest(client, architecture, token);
                    announced = release;
                    log.Write("官方生产渠道清单版本：" + release.Version);
                    client.Probe(release, token);
                    Client = client;
                    log.Write("已连接：" + route.Name);
                    log.Write("安装来源：" + release.PackageUrl.GetLeftPart(UriPartial.Path));
                    log.Write("官方版本：" + release.Version);
                    return release;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (ex is InvalidDataException) throw;
                    last = ex;
                    log.Write("此连接方式暂不可用：" + ex.Message);
                }
            }
            string reason = announced == null
                ? "无法读取官方生产渠道更新清单，暂时无法判断最新版。"
                : "已确认官方最新版 " + announced.Version + "，但无法访问其对应安装包。";
            throw new InvalidOperationException(reason + " 未回退到旧版。请确认代理已运行，稍后重试；若持续失败，官方发布接口可能需要适配。", last);
        }

        private static ReleaseInfo ReadManifest(NetworkClient client, string architecture, CancellationToken token)
        {
            return ReleaseManifest.Parse(client.GetSmall(new Uri(AppInfo.UpdateManifest), 64 * 1024, token), architecture);
        }

        public void ConfirmCurrent(ReleaseInfo selected, CancellationToken token)
        {
            var current = ReadManifest(Client, selected.Architecture, token);
            if (current.Version != selected.Version)
                throw new InvalidOperationException("官方发布版本在下载期间已变化：" + selected.Version + " → " + current.Version + "。尚未部署，请重新点击操作以获取最新版本。");
            log.Write("部署前复核官方清单通过，目标版本仍为 " + selected.Version + "。");
        }
    }
}
