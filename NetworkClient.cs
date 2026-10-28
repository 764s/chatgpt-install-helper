using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ChatGPTInstaller
{
    internal sealed class NetworkClient
    {
        private readonly ProxyRoute route;
        private readonly RunLog log;

        public NetworkClient(ProxyRoute route, RunLog log)
        {
            this.route = route;
            this.log = log;
        }

        private HttpWebRequest Request(Uri uri, long? start, long? end)
        {
            if (!AppInfo.IsOfficialDownload(uri) && uri.AbsoluteUri != AppInfo.Changelog)
                throw new InvalidOperationException("下载地址不是允许的官方 HTTPS 地址。");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = route.Proxy;
            request.UserAgent = "Mozilla/5.0 ChatGPTInstallHelper/" + SidebarIntegration.VersionString;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = false;
            request.KeepAlive = false;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            if (start.HasValue)
            {
                if (end.HasValue) request.AddRange(start.Value, end.Value);
                else request.AddRange(start.Value);
            }
            return request;
        }

        private static HttpWebResponse Response(HttpWebRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var response = (HttpWebResponse)request.GetResponse();
                int status = (int)response.StatusCode;
                if (status != 200 && status != 206)
                {
                    response.Dispose();
                    throw new IOException("官方服务器返回了意外的 HTTP 状态：" + status);
                }
                return response;
            }
            catch (WebException)
            {
                token.ThrowIfCancellationRequested();
                throw;
            }
        }

        public byte[] GetSmall(Uri uri, int limit, CancellationToken token)
        {
            return ReadMetadataWithRetry(() => GetSmallOnce(uri, limit, token), token);
        }

        private byte[] GetSmallOnce(Uri uri, int limit, CancellationToken token)
        {
            var request = Request(uri, null, null);
            using (token.Register(request.Abort))
            using (var response = Response(request, token))
            using (var stream = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (output.Length + read > limit) throw new IOException("官方元数据超过预期大小。");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        public void Probe(ReleaseInfo release, CancellationToken token)
        {
            ReadMetadataWithRetry(() => { ProbeOnce(release, token); return true; }, token);
        }

        private void ProbeOnce(ReleaseInfo release, CancellationToken token)
        {
            // 某些 CDN 拒绝 HEAD；范围 GET 同时提供版本、ETag 和完整文件大小。
            var request = Request(release.PackageUrl, 0, 0);
            using (token.Register(request.Abort))
            using (var response = Response(request, token))
            {
                release.ETag = response.Headers[HttpResponseHeader.ETag];
                release.Size = response.StatusCode == HttpStatusCode.PartialContent
                    ? RangeTotal(response.Headers[HttpResponseHeader.ContentRange], 0) : response.ContentLength;
                bool discoverVersion = release.Version == null;
                string declaredVersion = response.Headers["x-ms-meta-package_version"];
                if (discoverVersion && string.IsNullOrEmpty(declaredVersion))
                    throw new InvalidDataException("官方通用下载入口未提供包版本，不能将其作为历史候选。");
                if (!string.IsNullOrEmpty(declaredVersion))
                {
                    Version version;
                    if (!ReleaseManifest.TryVersion(declaredVersion, out version))
                        throw new InvalidDataException("官方安装包的版本元数据无效。");
                    if (discoverVersion) release.Version = version;
                    release.RequireVersion(version, "服务器包元数据");
                }
                string identity = response.Headers["x-ms-meta-package_identity"];
                if ((discoverVersion || !string.IsNullOrEmpty(identity)) && identity != AppInfo.PackageName)
                    throw new InvalidDataException("官方包标识发生变化，需要更新安装助手后再继续。");
                string architecture = response.Headers["x-ms-meta-architecture"];
                if ((discoverVersion || !string.IsNullOrEmpty(architecture)) && architecture != release.Architecture)
                    throw new InvalidDataException("官方安装包的处理器架构不匹配。");
                if (release.Size < 1024 || release.Size > 16L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("无法确认官方安装包大小。");
            }
        }

        private T ReadMetadataWithRetry<T>(Func<T> read, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return read(); }
                catch (WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    var response = ex.Response as HttpWebResponse;
                    int code = response == null ? 0 : (int)response.StatusCode;
                    bool transient = code == 408 || code == 429 || code >= 500 ||
                        ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.ConnectFailure ||
                        ex.Status == WebExceptionStatus.NameResolutionFailure || ex.Status == WebExceptionStatus.ConnectionClosed ||
                        ex.Status == WebExceptionStatus.ReceiveFailure || ex.Status == WebExceptionStatus.SendFailure;
                    if (attempt != 0 || !transient) throw;
                    if (response != null) response.Dispose();
                    log.Write("官方元数据连接暂时失败，重试一次：" + ex.Message);
                    if (token.WaitHandle.WaitOne(500)) token.ThrowIfCancellationRequested();
                }
            }
        }

        public ReleaseInfo ProbeAvailable(string architecture, CancellationToken token)
        {
            var release = new ReleaseInfo
            {
                Architecture = architecture,
                PackageUrl = new Uri(AppInfo.DownloadRoot + "ChatGPT-" + architecture + ".msix"),
                LicenseUrl = new Uri(AppInfo.DownloadRoot + "ChatGPT-License.xml")
            };
            Probe(release, token);
            return release;
        }

        public static long RangeTotal(string header, long expectedStart)
        {
            var match = Regex.Match(header ?? "", @"^bytes (\d+)-(\d+)/(\d+)$");
            long start, end, total;
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out start) ||
                !long.TryParse(match.Groups[2].Value, out end) || !long.TryParse(match.Groups[3].Value, out total) ||
                start != expectedStart || end < start || end >= total)
                throw new InvalidDataException("服务器返回的续传范围无效。");
            return total;
        }

        public string Download(ReleaseInfo release, CancellationToken token, IProgress<UiStatus> progress)
        {
            token.ThrowIfCancellationRequested();
            var cache = PackageCache.Inspect(release, token);
            progress.Report(new UiStatus("选择安装包", cache.Summary, -1) { Cache = cache });
            if (!string.IsNullOrEmpty(cache.Notice)) log.Write(cache.Notice);
            string part = PackageCache.PackagePath(release);
            string meta = part + ".identity";
            string identity = PackageCache.Identity(release);
            string previousIdentity = File.Exists(meta) ? File.ReadAllText(meta) : null;
            if (string.IsNullOrEmpty(release.ETag) || previousIdentity != identity ||
                (File.Exists(part) && new FileInfo(part).Length > release.Size))
            {
                if (File.Exists(part))
                    log.Write("官方包标识已变化或缓存记录不完整，移除助手旧缓存并重新选择安装包；不会拼接不同的包。");
                File.Delete(part);
            }
            File.WriteAllText(meta, identity, Encoding.UTF8);
            long cachedSize = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (cachedSize != release.Size && cache.Source == "official" && PackageCache.TryReuse(release, part, token, progress, log)) return part;
            if (cachedSize != release.Size)
                Paths.RequireSpace(Paths.Cache, Math.Max(0, release.Size - cachedSize) + 256L * 1024 * 1024, "下载盘");
            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (offset == release.Size)
                {
                    log.Write("复用已下载的完整安装包，将重新校验后等待你确认安装。");
                    progress.Report(new UiStatus("复用下载缓存", "安装包已完整下载，无需重复下载。", 100));
                    return part;
                }
                if (offset > 0) log.Write("继续下载，已保存 " + FormatBytes(offset) + "。");
                var request = Request(release.PackageUrl, offset, null);
                if (!string.IsNullOrEmpty(release.ETag)) request.Headers[HttpRequestHeader.IfMatch] = release.ETag;
                try
                {
                    using (token.Register(request.Abort))
                    using (var response = Response(request, token))
                    {
                        if (!string.IsNullOrEmpty(release.ETag) && response.Headers[HttpResponseHeader.ETag] != release.ETag)
                            throw new InvalidDataException("下载期间官方版本已变化，请重新点击安装或更新。");
                        if (response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            if (RangeTotal(response.Headers[HttpResponseHeader.ContentRange], offset) != release.Size)
                                throw new InvalidDataException("下载期间安装包大小发生变化。");
                        }
                        else
                        {
                            offset = 0;
                            if (response.ContentLength != release.Size) throw new InvalidDataException("安装包长度不匹配。");
                        }
                        using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                        using (var input = response.GetResponseStream())
                        {
                            CacheFiles.RequirePrivateWriter(file);
                            file.SetLength(offset);
                            file.Position = offset;
                            long received = offset;
                            var clock = Stopwatch.StartNew();
                            long lastReport = -1000;
                            byte[] buffer = new byte[256 * 1024];
                            int read;
                            while ((read = ReadPackageBytes(input, buffer)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                received += read;
                                if (received > release.Size) throw new InvalidDataException("下载内容超过官方声明的大小。");
                                file.Write(buffer, 0, read);
                                if (clock.ElapsedMilliseconds - lastReport >= 160)
                                {
                                    lastReport = clock.ElapsedMilliseconds;
                                    double speed = (received - offset) / Math.Max(0.1, clock.Elapsed.TotalSeconds);
                                    progress.Report(new UiStatus("下载官方安装包", FormatBytes(received) + " / " +
                                        FormatBytes(release.Size) + "  ·  " + FormatBytes((long)speed) + "/s",
                                        (int)(received * 100 / release.Size))
                                    {
                                        Cache = new PackageCacheInfo { Version = release.Version, Size = release.Size, Bytes = received,
                                            State = received == release.Size ? "complete" : "partial", Source = "helper", Notice = cache.Notice }
                                    });
                                }
                            }
                            if (received != release.Size) throw new UpdateRetrievalException(UpdateRetrievalStage.Package, "连接提前结束，将继续下载。");
                        }
                    }
                    return part;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (ex is InvalidDataException)
                    {
                        Paths.DeleteFile(part);
                        throw new UpdateRetrievalException(UpdateRetrievalStage.Package, ex.Message, ex, true);
                    }
                    if (!(ex is IOException) && !(ex is WebException)) throw;
                    var web = ex as WebException;
                    var errorResponse = web == null ? null : web.Response as HttpWebResponse;
                    if (errorResponse != null)
                    {
                        int status = (int)errorResponse.StatusCode;
                        errorResponse.Dispose();
                        if (status == 412 || status == 416)
                        {
                            Paths.DeleteFile(part);
                            throw new UpdateRetrievalException(UpdateRetrievalStage.Package, "官方包已更新，请重新点击按钮获取最新版。", ex);
                        }
                    }
                    last = ex;
                    log.Write("下载连接中断（" + attempt + "/3），自动重试。");
                    if (attempt < 3 && token.WaitHandle.WaitOne(attempt * 1000)) token.ThrowIfCancellationRequested();
                }
            }
            const string failure = "下载未完成，已保留进度。检查代理后再次点击即可续传。";
            if (last is WebException || last is UpdateRetrievalException)
                throw new UpdateRetrievalException(UpdateRetrievalStage.Package, failure, last);
            throw new IOException(failure, last);
        }

        private static int ReadPackageBytes(Stream input, byte[] buffer)
        {
            try { return input.Read(buffer, 0, buffer.Length); }
            catch (IOException ex) { throw new UpdateRetrievalException(UpdateRetrievalStage.Package, ex.Message, ex); }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GB";
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0.0") + " KB";
        }
    }
}
