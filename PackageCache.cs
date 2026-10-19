using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Xml;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：为版本检查与下载流程提供统一的安装包缓存判断。
    /// 示例：侧栏和独立窗口均显示 prepared.msix 是否匹配目标，NetworkClient 据此跳过重复下载。
    /// 职责：按实时目标版本选择助手或官方缓存；引用官方完整包前校验并发布到助手路径。
    /// 约束：不写入或删除官方缓存及更新状态；失败时保留助手原有续传文件。
    /// </summary>
    internal static class PackageCache
    {
        public static string PackagePath(ReleaseInfo release)
        {
            return Path.Combine(Paths.Cache, "ChatGPT-" + release.Architecture + ".msix");
        }

        public static string Identity(ReleaseInfo release)
        {
            return release.PackageUrl.AbsoluteUri + "\n" + release.ETag + "\n" + release.Size;
        }

        public static PackageCacheInfo Inspect(ReleaseInfo release, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new PackageCacheInfo { Version = release.Version, Size = release.Size };
            string own = PackagePath(release);
            try
            {
                if (!string.IsNullOrEmpty(release.ETag) && File.Exists(own) && File.Exists(own + ".identity") && File.ReadAllText(own + ".identity") == Identity(release))
                {
                    long size = new FileInfo(own).Length;
                    if (size > 0 && size <= release.Size)
                    {
                        result.State = size == release.Size ? "complete" : "partial";
                        result.Source = "helper";
                        result.Bytes = size;
                    }
                }
            }
            catch (Exception ex) { if (!IsCacheReadError(ex)) throw; }
            if (result.Complete) return result;
            try
            {
                if (File.Exists(Paths.OfficialPackage))
                {
                    var info = PackageInfo.Read(Paths.OfficialPackage, release.Architecture);
                    if (info.Version != release.Version)
                        result.Notice = "官方已下载 " + info.Version + "，" + (info.Version < release.Version ? "不是最新版，将下载新版。" : "与当前发布版本不同，暂不使用。");
                    else if (new FileInfo(Paths.OfficialPackage).Length != release.Size)
                        result.Notice = "官方缓存不完整，将继续下载。";
                    else
                    {
                        result.State = "complete";
                        result.Source = "official";
                        result.Bytes = release.Size;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsCacheReadError(ex)) throw;
                result.Notice = "官方缓存暂不可用，仍可正常下载。";
            }
            token.ThrowIfCancellationRequested();
            return result;
        }

        private static bool IsCacheReadError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is InvalidOperationException || ex is ArgumentException || ex is FormatException || ex is XmlException;
        }

        public static bool TryReuse(ReleaseInfo release, string destination, CancellationToken token, IProgress<UiStatus> progress, RunLog log)
        {
            token.ThrowIfCancellationRequested();
            string source = Paths.OfficialPackage;
            if (!File.Exists(source))
            {
                log.Write("官方暂无可复用的完整安装包；未完成的下载不参与复用。");
                return false;
            }
            string temporary = null;
            try
            {
                progress.Report(new UiStatus("检查官方缓存", "核对官方已下载的完整包是否匹配最新版…", -1));
                // 允许官方移除或换名自己的路径，但复制期间不允许改写源文件内容。
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    var info = PackageInfo.Read(source, release.Architecture);
                    if (info.Version != release.Version)
                    {
                        log.Write("官方缓存版本 " + info.Version + " 与实时目标 " + release.Version + " 不同，跳过缓存，继续获取目标版本。");
                        return false;
                    }
                    if (input.Length != release.Size)
                    {
                        log.Write("官方缓存长度与目标安装包不符，跳过缓存，继续下载。");
                        return false;
                    }
                    token.ThrowIfCancellationRequested();
                    temporary = Path.Combine(Path.GetDirectoryName(destination), "official-" + Guid.NewGuid().ToString("N") + ".msix");
                    int linkError = CacheFiles.TryCreateHardLink(temporary, source);
                    bool linked = linkError == 0;
                    if (!linked)
                    {
                        log.Write("官方缓存无法建立硬链接（Windows 错误 " + linkError + "），尝试从本机复制，不重新下载。");
                        Paths.RequireSpace(Path.GetDirectoryName(destination), release.Size + 256L * 1024 * 1024, "缓存盘");
                        Copy(input, temporary, release.Size, token, progress);
                    }
                    progress.Report(new UiStatus("校验官方缓存", "核对安装包身份、版本与 Windows 数字签名…", -1));
                    using (var guard = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (guard.Length != release.Size) throw new InvalidDataException("官方缓存长度在复用期间发生变化。");
                        info = PackageInfo.Read(temporary, release.Architecture);
                        release.RequireVersion(info.Version, "官方缓存");
                        SignatureVerifier.Verify(temporary);
                        token.ThrowIfCancellationRequested();
                    }
                    // 验证通过后才替换助手缓存；官方路径与 staged-msix.json 始终不变。
                    CacheFiles.Publish(temporary, destination);
                    temporary = null;
                    log.Write("已复用官方完整安装包 " + info.Version + "（" + (linked ? "硬链接，共用文件数据" : "本机复制") + "），无需重复下载。");
                    progress.Report(new UiStatus("已复用官方缓存", "安装包已就绪，无需重复下载；继续准备许可证与安装前检查。", 100));
                    return true;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                if (!IsCacheReadError(ex)) throw;
                log.Write("未能复用官方缓存：" + ex.Message + " 将继续下载实时目标版本。");
                return false;
            }
            finally
            {
                if (temporary != null) Paths.DeleteFile(temporary);
            }
        }

        private static void Copy(FileStream input, string destination, long size, CancellationToken token, IProgress<UiStatus> progress)
        {
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[256 * 1024];
                long copied = 0;
                long lastReport = -1000;
                var clock = Stopwatch.StartNew();
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    copied += read;
                    if (copied > size) throw new InvalidDataException("官方缓存超过目标安装包大小。");
                    output.Write(buffer, 0, read);
                    if (clock.ElapsedMilliseconds - lastReport >= 160)
                    {
                        lastReport = clock.ElapsedMilliseconds;
                        progress.Report(new UiStatus("从本机缓存复制", NetworkClient.FormatBytes(copied) + " / " + NetworkClient.FormatBytes(size) + " · 不使用网络下载", (int)(copied * 100 / size)));
                    }
                }
                if (copied != size) throw new InvalidDataException("官方缓存未完整复制。");
            }
        }
    }
}
