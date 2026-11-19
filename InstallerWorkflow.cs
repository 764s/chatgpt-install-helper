using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Xml;

namespace ChatGPTInstaller
{
    internal sealed class InstallerWorkflow
    {
        private readonly RunLog log;
        private readonly IProgress<UiStatus> progress;
        private readonly Action<PreparedInstallation> deploy;

        public InstallerWorkflow(RunLog log, IProgress<UiStatus> progress)
            : this(log, progress, item =>
            {
                if (item.Release.WindowsOffer != null)
                    WindowsUpdatePermission.Run(item.Release.WindowsOffer, item.Release.Architecture, true, CancellationToken.None,
                        (percent, detail) => progress.Report(new UiStatus("应用官方更新", detail, percent) { Deployment = true }));
                else DeploymentHelper.Deploy(item.PackagePath, item.LicensePath, !item.UpdateOnly, progress, log);
            }) { }

        internal InstallerWorkflow(RunLog log, IProgress<UiStatus> progress, Action<PreparedInstallation> deploy)
        {
            this.log = log;
            this.progress = progress;
            this.deploy = deploy;
        }

        public PreparedInstallation Prepare(bool updateOnly, string proxy, CancellationToken token, bool followObserved = false)
        {
            token.ThrowIfCancellationRequested();
            PlatformInfo.Check();
            Paths.Ensure();
            progress.Report(new UiStatus("检查本机", "读取当前安装版本…", -1));
            InstalledPackage installed;
            using (var shell = new PowerShellBridge(null)) installed = shell.FindInstalled();
            progress.Report(new UiStatus("检查官方最新版", "正在连接官方更新来源…", -1)
            {
                CurrentVersion = installed == null ? "尚未安装新版" : installed.Version.ToString(),
                LatestVersion = "查询中…"
            });
            var resolver = new ReleaseResolver(log, progress);
            Version installedVersion = installed == null ? null : installed.Version;
            bool hadOfficialTransfer = OfficialDownload.ReadBytes().HasValue;
            // 用户已选择跟随时先等待同一文件发布，随后一次性复核最新目标。
            if (followObserved) OfficialDownload.WaitForPublish(token, progress, log);
            string arch = PlatformInfo.Architecture();
            var selection = resolver.Resolve(arch, proxy, installedVersion, token, useWindows: updateOnly);
            if (!followObserved && (hadOfficialTransfer || OfficialDownload.ReadBytes().HasValue))
            {
                var cache = selection.Package == null ? null : PackageCache.Inspect(selection.Package, token);
                if (selection.CanFollowOfficial(installedVersion, cache, updateOnly))
                {
                    OfficialDownload.WaitForPublish(token, progress, log);
                    // 直连下载在两次观察间也可能已经换名。重新选择目标后才能复用或发起自己的下载。
                    selection = resolver.Resolve(arch, proxy, installedVersion, token, useWindows: updateOnly);
                }
            }
            var release = selection.Package;
            if (selection.UsesWindows)
            {
                if (!release.WindowsOffer.Downloaded)
                {
                    progress.Report(new UiStatus("准备官方更新", "使用当前用户的同一项 Windows 下载任务…", -1));
                    WindowsUpdatePermission.Run(release.WindowsOffer, arch, false, token, (percent, detail) => progress.Report(new UiStatus("下载官方更新", detail, percent)));
                    var completed = resolver.Resolve(arch, proxy, installedVersion, token);
                    if (!release.Matches(completed.Package) || !completed.Package.WindowsOffer.Downloaded)
                        throw new InvalidDataException("下载期间官方更新任务或系统缓存已变化，请重新准备。");
                    release = completed.Package;
                }
                token.ThrowIfCancellationRequested();
                log.Write("当前用户的官方更新已下载，使用系统缓存等待应用。");
                progress.Report(new UiStatus("官方更新已就绪", "已下载，等待应用。", 100));
                return new PreparedInstallation(release, null, null, true);
            }
            if (ShouldSkip(installed, selection.LatestVersion, updateOnly) || (release != null && ShouldSkip(installed, release.Version, updateOnly)))
            {
                string summary = selection.Summary(installed.Version) + "。";
                if (selection.ExternalUpdateTitle(installedVersion) != null)
                {
                    log.Write(summary);
                    progress.Report(new UiStatus("暂无可用离线更新", summary, 100));
                }
                else Complete(installed.Version, summary);
                return null;
            }
            if (release == null)
            {
                progress.Report(new UiStatus("暂无可用安装包", "暂未找到可用于" + (updateOnly ? "更新" : "安装 / 重装") + "的官方包。", 100));
                return null;
            }
            if (installed == null && updateOnly) log.Write("尚未安装新版 ChatGPT，将先准备已选定的官方安装包。");
            string package = resolver.Client.Download(release, token, progress);
            token.ThrowIfCancellationRequested();
            var info = VerifyPackage(release, package, token);
            progress.Report(new UiStatus("安装包验证通过", "正在获取官方离线许可证…", -1));
            string license = Path.Combine(Paths.Cache, "ChatGPT-License.xml");
            byte[] licenseData;
            try { licenseData = resolver.Client.GetSmall(release.LicenseUrl, 1024 * 1024, token); }
            catch (WebException ex) { throw new UpdateRetrievalException(UpdateRetrievalStage.Information, "获取官方离线许可证失败：" + ex.Message, ex); }
            catch (IOException ex) { throw new UpdateRetrievalException(UpdateRetrievalStage.Information, "读取官方离线许可证失败：" + ex.Message, ex); }
            File.WriteAllBytes(license, licenseData);
            CheckRetrievedLicense(license);
            Paths.RequireSpace(Environment.GetFolderPath(Environment.SpecialFolder.Windows), info.ExpandedSize + 512L * 1024 * 1024, "系统盘");
            progress.Report(new UiStatus("复核可安装版本", "确认下载期间没有更高的可用安装包…", -1));
            if (!resolver.ConfirmCurrent(release, proxy, installedVersion, token, updateOnly))
                throw new InvalidOperationException("可安装目标在准备期间已变化。尚未部署，请重新点击安装 / 更新以获取当前可用版本。");
            token.ThrowIfCancellationRequested();
            // 准备阶段不部署；调用方依据用户手动应用或本次自动安装选择决定下一步。
            log.Write("安装包和许可证已准备就绪。下载校验阶段未申请管理员权限，未关闭或修改 ChatGPT。");
            progress.Report(new UiStatus("准备就绪，尚未安装", "下载与校验已完成，尚未执行 Windows 安装。", 100));
            return new PreparedInstallation(release, package, license, updateOnly);
        }

        public bool InstallPrepared(PreparedInstallation prepared, string proxy, CancellationToken token)
        {
            if (prepared == null) throw new ArgumentNullException("prepared");
            token.ThrowIfCancellationRequested();
            PlatformInfo.Check();
            progress.Report(new UiStatus("安装前复核", "重新检查官方版本、本机版本和下载缓存…", -1));
            InstalledPackage installed;
            using (var shell = new PowerShellBridge(null)) installed = shell.FindInstalled();
            var resolver = new ReleaseResolver(log, progress);
            Version installedVersion = installed == null ? null : installed.Version;
            var selection = resolver.Resolve(PlatformInfo.Architecture(), proxy, installedVersion, token, useWindows: prepared.UpdateOnly);
            progress.Report(new UiStatus("安装前复核", "官方版本已确认，正在核对准备好的安装包…", -1)
            {
                CurrentVersion = installed == null ? "尚未安装新版" : installed.Version.ToString(),
                Selection = selection
            });
            if (prepared.Release.WindowsOffer != null) return ApplyWindows(prepared, selection, installedVersion, token);
            // 应用等待期间可能已由官方完成更新；先处理无需安装的情况，避免要求重复准备。
            if (ShouldSkip(installed, prepared.Release.Version, prepared.UpdateOnly) && selection.IsOfflineCurrent(installedVersion))
            {
                Complete(installed.Version, selection.Summary(installed.Version) + "。");
                ClearPreparedFiles(prepared);
                return true;
            }
            var current = selection.Package;
            if (!prepared.Release.Matches(current))
            {
                string message = "可安装目标已变化或不可用，请重新检查并准备。尚未执行部署。";
                log.Write(message);
                progress.Report(new UiStatus("需要重新检查安装包", message, 0));
                return false;
            }
            if (ShouldSkip(installed, current.Version, prepared.UpdateOnly))
            {
                Complete(installed.Version, "本机已安装此版本或更高版本，无需更新。");
                ClearPreparedFiles(prepared);
                return true;
            }
            if (!prepared.HasPreparedInput()) throw new FileNotFoundException("下载缓存已被移除，请点击上方安装 / 更新重新准备。");
            var info = VerifyPackage(prepared.Release, prepared.PackagePath, token);
            CheckRetrievedLicense(prepared.LicensePath);
            Paths.RequireSpace(Environment.GetFolderPath(Environment.SpecialFolder.Windows), info.ExpandedSize + 512L * 1024 * 1024, "系统盘");
            if (!resolver.ConfirmCurrent(prepared.Release, proxy, installedVersion, token, prepared.UpdateOnly))
            {
                progress.Report(new UiStatus("需要重新检查安装包", "可安装目标已变化，请重新准备。尚未执行部署。", 0));
                return false;
            }
            token.ThrowIfCancellationRequested();
            deploy(prepared);
            progress.Report(new UiStatus("核对安装结果", "确认当前用户可以访问安装的版本…", -1) { Deployment = true });
            using (var shell = new PowerShellBridge((percent, detail) =>
                progress.Report(new UiStatus("注册当前用户", detail, percent) { Deployment = true })))
            {
                var after = shell.FindInstalled();
                // 使用其他管理员账户授权时，仍为发起安装的用户完成注册。
                if (after == null || after.Version < info.Version)
                {
                    shell.Register(info.FullName);
                    after = shell.FindInstalled();
                }
                if (after == null || after.Version < info.Version)
                    throw new InvalidOperationException("系统已预配安装包，但当前用户注册尚未完成。请注销并重新登录 Windows 后检查应用。日志中保留了结果。");
                Complete(after.Version, prepared.UpdateOnly && installed != null ? "更新完成，可以打开 ChatGPT。" : "安装 / 覆盖修复完成，可以打开 ChatGPT。");
            }
            ClearPreparedFiles(prepared);
            return true;
        }

        private bool ApplyWindows(PreparedInstallation prepared, ReleaseSelection fresh, Version installed, CancellationToken token)
        {
            var original = prepared.Release.WindowsOffer;
            if (installed != null && installed > original.Baseline && prepared.Release.Version != null && installed >= prepared.Release.Version)
            {
                Complete(installed, "本机已经注册了目标版本或更高版本。");
                return true;
            }
            prepared.RestoreVerifiedStage(fresh.Package);
            if (installed != original.Baseline || !prepared.Release.Matches(fresh.Package) || !fresh.Package.WindowsOffer.Downloaded) return false;
            token.ThrowIfCancellationRequested();
            progress.Report(new UiStatus("应用官方更新", "已复核同一用户、更新标识和缓存，正在应用…", -1) { Deployment = true });
            deploy(new PreparedInstallation(fresh.Package, null, null, true));
            progress.Report(new UiStatus("核对更新结果", "正在核对当前用户注册的 ChatGPT 包版本…", -1) { Deployment = true });
            using (var shell = new PowerShellBridge(null))
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    var after = shell.FindInstalled();
                    if (after != null && after.Version > original.Baseline && (fresh.AvailableVersion == null || after.Version >= fresh.AvailableVersion))
                    {
                        Complete(after.Version, "更新完成，当前版本 " + after.Version + "。");
                        return true;
                    }
                    if (attempt < 7) Thread.Sleep(500);
                }
            }
            throw new InvalidOperationException("系统操作已返回，但尚未核实当前用户已注册新版 ChatGPT。请重新检查安装状态。");
        }

        private PackageInfo VerifyPackage(ReleaseInfo release, string package, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new UiStatus("检查安装包", "核对官方包身份、系统要求和数字签名…", -1));
            PackageInfo info;
            try
            {
                info = PackageInfo.Read(package, release.Architecture);
                release.RequireVersion(info.Version, "下载的安装包");
                release.VerifyDeclaredContent(package, token);
            }
            catch (InvalidDataException ex)
            {
                Paths.DeleteFile(package);
                Paths.DeleteFile(package + ".identity");
                throw new UpdateRetrievalException(UpdateRetrievalStage.Package, ex.Message, ex, true);
            }
            try { SignatureVerifier.Verify(package); }
            catch (Exception ex)
            {
                Paths.DeleteFile(package);
                Paths.DeleteFile(package + ".identity");
                log.Write("签名验证失败，已清除下载缓存，下次将重新下载。");
                throw new UpdateRetrievalException(UpdateRetrievalStage.Package, ex.Message, ex, true);
            }
            token.ThrowIfCancellationRequested();
            log.Write("Windows 数字签名验证通过；包版本 " + info.Version + "，架构 " + info.Architecture + "。");
            return info;
        }

        private static void ClearPreparedFiles(PreparedInstallation prepared)
        {
            Paths.DeleteFile(prepared.PackagePath);
            Paths.DeleteFile(prepared.PackagePath + ".identity");
            Paths.DeleteFile(prepared.LicensePath);
        }

        private static void CheckRetrievedLicense(string path)
        {
            try { PackageInfo.CheckLicense(path); }
            catch (Exception ex)
            {
                if (!(ex is InvalidDataException) && !(ex is XmlException)) throw;
                throw new UpdateRetrievalException(UpdateRetrievalStage.Information, ex.Message, ex, true);
            }
        }

        public static bool ShouldSkip(InstalledPackage installed, Version latest, bool updateOnly)
        {
            return installed != null && latest != null &&
                (installed.Version > latest || (updateOnly && installed.Version == latest));
        }

        private void Complete(Version version, string message)
        {
            log.Write(message + " 版本：" + version);
            progress.Report(new UiStatus("完成", message, 100) { CurrentVersion = version.ToString(), Finished = true });
        }
    }
}
