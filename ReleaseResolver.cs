using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace ChatGPTInstaller
{
    internal sealed class ReleaseResolver
    {
        private readonly RunLog log;
        private readonly IProgress<UiStatus> progress;
        public NetworkClient Client { get; private set; }

        public ReleaseResolver(RunLog log, IProgress<UiStatus> progress) { this.log = log; this.progress = progress; }

        public ReleaseSelection Resolve(string architecture, string customProxy, Version installed, CancellationToken token, OfficialUpdateContext codexContext = null, bool useWindows = true)
        {
            Exception directError = null;
            var selection = new ReleaseSelection();
            WindowsUpdateOffer cached = null;
            if (useWindows && installed != null)
            {
                try { cached = new CurrentUserStore().Find(installed, null, architecture, false, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { selection.StoreIssue = "当前用户的本地更新索引暂不可读：" + ex.Message; log.Write(selection.StoreIssue); }
            }
            using (var direct = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                // 已取得系统任务时，额外的 CDN 请求不能让主操作长时间卡在“检查”。
                if (cached != null) direct.CancelAfter(6500);
                try { selection = ResolveCore(architecture, customProxy, installed, direct.Token, selection, cached); }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (!useWindows || installed == null) throw new UpdateRetrievalException(UpdateRetrievalStage.Information, ex.Message, ex);
                    directError = ex;
                    log.Write("直连来源未完成，保留已读取的公告并核对当前用户的官方任务。");
                }
            }
            // 仅检查操作传入会话。官方状态补充设备可用性，不能充当独立安装包。
            if (cached == null && codexContext != null && (directError != null || selection.IsOfflineCurrent(installed)))
            {
                progress.Report(new UiStatus("核对 Codex 更新", "正在读取 Codex 自带更新器的状态…", -1));
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(4000);
                    try
                    {
                        var native = OfficialUpdateCheck.Check(codexContext, null, timeout.Token);
                        timeout.Token.ThrowIfCancellationRequested();
                        selection.CodexStatus = native.Status;
                        log.Write("Codex 自带更新器状态：" + native.Status);
                    }
                    catch (Exception ex)
                    {
                        token.ThrowIfCancellationRequested();
                        log.Write("暂未取得 Codex 自带更新状态，保留本次清单结果：" + ex.Message);
                    }
                }
            }
            if (useWindows && installed != null) SelectWindowsOffer(selection, cached, installed, architecture, directError != null, token);
            if (directError != null && !selection.UsesWindows)
                throw new UpdateRetrievalException(UpdateRetrievalStage.Information, directError.Message + " " + selection.StoreIssue, directError);
            token.ThrowIfCancellationRequested();
            progress.Report(new UiStatus("版本检查完成", selection.Summary(installed), -1) { Selection = selection });
            return selection;
        }

        private void SelectWindowsOffer(ReleaseSelection selection, WindowsUpdateOffer cached, Version installed, string arch, bool directFailed, CancellationToken token)
        {
            Version direct = selection.AvailableVersion;
            bool publishedAhead = selection.HasNewerStoreVersion(installed) && (direct == null || selection.StoreVersion >= direct);
            bool nativeOffer = selection.CodexStatus == "available" || selection.CodexStatus == "restart_required";
            if (direct != null && direct > installed && !publishedAhead && !nativeOffer && cached == null) return;
            progress.Report(new UiStatus("检查本机官方更新", "读取当前用户的 ChatGPT 更新任务和系统缓存…", -1));
            try
            {
                var offer = cached;
                if (offer == null && (publishedAhead || nativeOffer || directFailed || selection.StoreIssue != null))
                    offer = new CurrentUserStore().Find(installed, selection.HighestPublishedVersion, arch, true, token);
                if (offer == null) { selection.StoreIssue = "本机尚未返回适用的 ChatGPT 更新任务。"; return; }
                Version advertised = selection.HighestPublishedVersion;
                if (offer.StagedVersion == null && advertised != null && advertised > installed && WindowsStagedPackage.Find(advertised, arch) != null)
                    offer.StagedVersion = advertised;
                if (direct != null && direct > installed && (offer.StagedVersion != null ? offer.StagedVersion < direct : !publishedAhead && !nativeOffer)) return;
                if (!offer.Downloaded && direct != null && direct > installed && (advertised == null || direct >= advertised) &&
                    (PackageCache.Inspect(selection.Package, token).State != "none" || OfficialDownload.ReadBytes().HasValue)) return;
                selection.Package = new ReleaseInfo { WindowsOffer = offer, Architecture = arch, Version = offer.StagedVersion, Size = offer.MaxBytes };
                selection.LatestVersion = offer.StagedVersion ?? (selection.HasNewerStoreVersion(installed) ? selection.StoreVersion : null);
                selection.VersionLabel = offer.StagedVersion == null ? "已公布版本" : "已核验暂存版本";
                selection.StoreIssue = null;
                selection.LatestPackageMissing = false;
                selection.SourceNotice = offer.Downloaded ? "当前用户的官方更新已下载，可以直接应用。" : "已取得当前用户适用的官方更新，下载进度和缓存由 Windows 管理。";
                log.Write("已绑定当前用户 ChatGPT 更新 " + offer.Id + " / " + offer.Revision + "，" +
                    (offer.Downloaded ? "系统缓存已就绪" : "等待下载") + (offer.StagedVersion == null ? "。" : "，已核验暂存版本 " + offer.StagedVersion + "。"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                selection.StoreIssue = "读取本机 ChatGPT 更新任务失败：" + ex.Message;
                log.Write(selection.StoreIssue);
            }
        }

        private ReleaseSelection ResolveCore(string architecture, string customProxy, Version installed, CancellationToken token, ReleaseSelection evidence, WindowsUpdateOffer cached)
        {
            Exception last = null;
            ReleaseInfo announced = null;
            ReleaseInfo publishedPackage = null;
            Version publishedVersion = null;
            foreach (var route in ProxyRoute.Candidates(customProxy))
            {
                token.ThrowIfCancellationRequested();
                log.Write("连接官方来源：" + route.Name);
                var client = new NetworkClient(route, log);
                try
                {
                    Action readMarker = () =>
                    {
                        if (announced != null) return;
                        ReadSource(() =>
                        {
                            announced = ReadManifest(client, architecture, token);
                            evidence.StoreVersion = announced.Version;
                            log.Write("Store 发布标记版本：" + announced.Version);
                            ReleaseHistory.Remember(announced, log);
                        }, "Store 发布标记", token);
                    };
                    if (cached != null) readMarker();
                    // 两个官方来源独立读取；Store 标记失败不阻断已取得的离线发布清单。
                    if (publishedVersion == null)
                    {
                        ReadSource(() =>
                        {
                            publishedPackage = ReleaseFeed.Parse(client.GetSmall(new Uri(AppInfo.ReleaseFeed), 64 * 1024, token), architecture, out publishedVersion);
                            evidence.LatestVersion = publishedVersion;
                            log.Write("官方离线发布版本：" + publishedVersion);
                            if (publishedPackage != null) ReleaseHistory.Remember(publishedPackage, log);
                            progress.Report(new UiStatus("已读取离线发布版本", "继续核对 Store 发布标记…", -1)
                            { LatestVersion = publishedVersion.ToString(), LatestVersionLabel = "离线发布最新版" });
                        }, "官方离线发布清单", token);
                    }
                    if (cached == null) readMarker();
                    if (announced == null && publishedVersion == null)
                        throw new IOException("本次连接未能读取任何官方版本来源。");
                    if (publishedPackage != null && announced != null && announced.ExplicitUrl &&
                        publishedPackage.Version == announced.Version && publishedPackage.PackageUrl == announced.PackageUrl)
                    {
                        publishedPackage.DeclaredLength = announced.DeclaredLength;
                        publishedPackage.Sha256 = announced.Sha256;
                    }
                    // 离线清单明确描述独立 MSIX 的发布范围；Store 标记不保证同版本离线包已发布。
                    Version latest = publishedVersion ?? announced.Version;
                    var selection = new ReleaseSelection
                    {
                        LatestVersion = latest,
                        StoreVersion = announced == null ? null : announced.Version,
                        VersionLabel = publishedVersion != null ? "离线发布最新版" : "Store 发布版本",
                        SourceNotice = announced == null ? "Store 版本标记暂不可用，以下仅代表官方离线发布版本。" :
                            publishedVersion == null ? "离线发布清单暂不可用，本次使用版本标记及兼容下载入口。" :
                            announced.Version > publishedVersion ? "Store 发布标记为 " + announced.Version + "；独立安装以官方离线清单为准，两条通道可能不同步。" : null
                    };
                    if (cached != null && cached.Downloaded && selection.HighestPublishedVersion > installed &&
                        WindowsStagedPackage.Find(selection.HighestPublishedVersion, architecture) != null)
                    {
                        cached.StagedVersion = selection.HighestPublishedVersion;
                        Client = client;
                        return selection;
                    }
                    progress.Report(new UiStatus("检查安装包", "核对官方发布的安装包地址…", -1) { LatestVersion = latest.ToString(), LatestVersionLabel = selection.VersionLabel });
                    if (selection.HighestPublishedVersion > latest && (installed == null || installed <= selection.HighestPublishedVersion))
                    {
                        // 发布标记本身不是包地址，但 Codex 的同版本签名完整包可以成为安装目标。
                        var newer = announced.ExplicitUrl ? ResolvePublishedPackage(client, announced, announced.Version, architecture, token)
                            : PackageCache.FindPublishedFile(announced.Version, architecture, token, log, announced);
                        if (newer != null)
                        {
                            selection.Package = newer;
                            selection.LatestVersion = newer.Version;
                            selection.VersionLabel = "Store 发布版本";
                            selection.SourceNotice = (newer.CacheOnly ? "已核验 Codex 完整包" : "已核对公告中的官方直连地址") + "，离线清单版本为 " + latest + "。";
                        }
                    }
                    if (selection.Package == null && (installed == null || installed <= latest))
                    {
                        var newest = publishedPackage != null && publishedPackage.Version == latest ? publishedPackage : announced != null && announced.Version == latest ? announced : null;
                        selection.Package = ResolvePublishedPackage(client, newest, latest, architecture, token);
                        if (selection.Package == null) SelectFallback(client, selection, architecture, installed, publishedPackage, announced, newest, token);
                    }
                    Client = client;
                    log.Write("已连接：" + route.Name);
                    if (selection.Package != null)
                    {
                        log.Write("独立安装包候选：" + selection.Package.Version);
                        log.Write("安装来源：" + (selection.Package.CacheOnly ? "已核验的 Codex 完整包" : selection.Package.PackageUrl.GetLeftPart(UriPartial.Path)));
                    }
                    return selection;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (ex is InvalidDataException) throw;
                    last = ex;
                    log.Write("此连接方式暂不可用：" + ex.Message);
                }
            }
            string reason = announced == null && publishedVersion == null
                ? "无法读取官方离线发布清单及 Store 版本标记，暂时无法判断最新版。"
                : "已读取官方版本信息，但安装包候选检查未完成。";
            throw new InvalidOperationException(reason + " 连接失败不能视为缺包；请检查网络或代理后重试。", last);
        }

        private void ReadSource(Action read, string name, CancellationToken token)
        {
            progress.Report(new UiStatus("读取版本信息", "正在读取" + name + "…", -1));
            try { read(); }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                if (ex is InvalidDataException || (!(ex is WebException) && !(ex is IOException))) throw;
                var web = ex as WebException;
                if (web != null && web.Response != null) web.Response.Dispose();
                log.Write(name + "暂不可用，将独立检查其他官方来源：" + ex.Message);
                log.WriteTechnical(ex.ToString());
            }
        }

        private void SelectFallback(NetworkClient client, ReleaseSelection selection, string architecture, Version installed, ReleaseInfo published, ReleaseInfo announced, ReleaseInfo attempted, CancellationToken token)
        {
            selection.LatestPackageMissing = true;
            progress.Report(new UiStatus("查找可用安装包", "最新版缺少安装包，正在查询其他官方候选…", -1));
            var candidates = new List<ReleaseInfo>();
            if (published != null) candidates.Add(published);
            if (announced != null) candidates.Add(announced);
            candidates.AddRange(ReleaseHistory.Read(architecture, log));
            ReleaseInfo available = null;
            try
            {
                available = client.ProbeAvailable(architecture, token);
                ReleaseHistory.Remember(architecture, available.Version, log);
                candidates.Add(available);
                log.Write("官方通用入口当前提供：" + available.Version);
            }
            catch (WebException ex)
            {
                token.ThrowIfCancellationRequested();
                bool missing = IsMissing(ex);
                if (!missing && published == null) throw;
                log.Write(missing ? "官方通用入口暂未找到安装包。" : "通用入口连接失败；继续核对离线发布清单中明确提供的候选，不将连接失败当作缺包。");
            }
            if (installed != null) candidates.Add(ReleaseInfo.ForVersion(installed, architecture));
            // 候选必须有明确来源；不枚举或猜测构建号，不越过当前安装版向下寻找。
            foreach (var candidate in candidates.Where(r => r.PackageUrl != null).GroupBy(r => r.Version + "\t" + r.PackageUrl.AbsoluteUri).Select(g => g.First())
                .Where(r => r.Version <= selection.LatestVersion && (installed == null || r.Version >= installed)).OrderByDescending(r => r.Version))
            {
                token.ThrowIfCancellationRequested();
                if (available != null && candidate == available)
                {
                    selection.Package = available;
                }
                else
                {
                    if (attempted != null && candidate.PackageUrl == attempted.PackageUrl) continue;
                    progress.Report(new UiStatus("查找可用安装包", "正在检查 " + candidate.Version + "…", -1));
                    if (Probe(client, candidate, token)) selection.Package = candidate;
                }
                if (selection.Package == null) continue;
                selection.LatestPackageMissing = selection.Package.Version < selection.LatestVersion;
                break;
            }
            log.Write(selection.Summary(installed));
        }

        private ReleaseInfo ResolvePublishedPackage(NetworkClient client, ReleaseInfo published, Version version, string architecture, CancellationToken token)
        {
            try
            {
                if (published != null && published.PackageUrl != null && Probe(client, published, token)) return published;
            }
            catch (WebException ex)
            {
                token.ThrowIfCancellationRequested();
                var cached = PackageCache.FindPublishedFile(version, architecture, token, log, published);
                if (cached == null) throw;
                if (ex.Response != null) ex.Response.Dispose();
                return cached;
            }
            return PackageCache.FindPublishedFile(version, architecture, token, log, published);
        }

        private bool Probe(NetworkClient client, ReleaseInfo release, CancellationToken token)
        {
            try { client.Probe(release, token); return true; }
            catch (WebException ex)
            {
                token.ThrowIfCancellationRequested();
                if (!IsMissing(ex)) throw;
                log.Write("官方安装包 " + release.Version + " 返回 404 / 410，继续检查候选。");
                return false;
            }
        }

        private static bool IsMissing(WebException error)
        {
            var response = error.Response as HttpWebResponse;
            if (response == null) return false;
            bool missing = response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone;
            response.Dispose();
            return missing;
        }

        private static ReleaseInfo ReadManifest(NetworkClient client, string architecture, CancellationToken token)
        {
            return ReleaseManifest.Parse(client.GetSmall(new Uri(AppInfo.UpdateManifest), 64 * 1024, token), architecture);
        }

        public bool ConfirmCurrent(ReleaseInfo selected, string proxy, Version installed, CancellationToken token, bool useWindows = true)
        {
            // 清单不变时，缺失的新包也可能刚刚上线；复核整段选择，而非仅比较清单版本。
            var current = Resolve(selected.Architecture, proxy, installed, token, useWindows: useWindows);
            bool unchanged = selected.Matches(current.Package);
            log.Write(unchanged ? "复核可安装目标通过，仍为 " + selected.Version + "。" : "可安装目标已变化或不可用，停止部署已准备的 " + selected.Version + "。");
            return unchanged;
        }
    }
}
