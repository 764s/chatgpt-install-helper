using System;
using System.IO;
using System.Threading;

namespace ChatGPTInstaller
{
    internal sealed class InstallerWorkflow
    {
        private readonly RunLog log;
        private readonly IProgress<UiStatus> progress;
        private readonly Action<PreparedInstallation> deploy;

        public InstallerWorkflow(RunLog log, IProgress<UiStatus> progress)
            : this(log, progress, item => DeploymentHelper.Deploy(item.PackagePath, item.LicensePath, !item.UpdateOnly, progress, log)) { }

        internal InstallerWorkflow(RunLog log, IProgress<UiStatus> progress, Action<PreparedInstallation> deploy)
        {
            this.log = log;
            this.progress = progress;
            this.deploy = deploy;
        }

        public PreparedInstallation Prepare(bool updateOnly, string proxy, CancellationToken token)
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
            var resolver = new ReleaseResolver(log);
            var release = resolver.Resolve(PlatformInfo.Architecture(), proxy, token);
            progress.Report(new UiStatus("官方版本已确认", "准备下载…", -1)
            {
                LatestVersion = release.Version.ToString()
            });
            if (ShouldSkip(installed, release.Version, updateOnly))
            {
                Complete(installed.Version, installed.Version == release.Version ? "已是官方生产渠道最新版，无需更新。" : "本机版本高于官方生产渠道，已保留当前版本。");
                return null;
            }
            if (installed == null && updateOnly) log.Write("尚未安装新版 ChatGPT，将先准备官方最新版安装包。");
            string package = resolver.Client.Download(release, token, progress);
            token.ThrowIfCancellationRequested();
            var info = VerifyPackage(release, package, token);
            progress.Report(new UiStatus("安装包验证通过", "正在获取官方离线许可证…", -1) { LatestVersion = info.Version.ToString() });
            string license = Path.Combine(Paths.Cache, "ChatGPT-License.xml");
            File.WriteAllBytes(license, resolver.Client.GetSmall(release.LicenseUrl, 1024 * 1024, token));
            PackageInfo.CheckLicense(license);
            Paths.RequireSpace(Environment.GetFolderPath(Environment.SpecialFolder.Windows), info.ExpandedSize + 512L * 1024 * 1024, "系统盘");
            progress.Report(new UiStatus("复核官方最新版", "确认下载期间没有新的官方发布…", -1));
            resolver.ConfirmCurrent(release, token);
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
            var resolver = new ReleaseResolver(log);
            var current = resolver.Resolve(PlatformInfo.Architecture(), proxy, token);
            progress.Report(new UiStatus("安装前复核", "官方版本已确认，正在核对准备好的安装包…", -1)
            {
                CurrentVersion = installed == null ? "尚未安装新版" : installed.Version.ToString(),
                LatestVersion = current.Version.ToString()
            });
            if (current.Version != prepared.Release.Version)
            {
                string message = "官方版本已从 " + prepared.Release.Version + " 变为 " + current.Version + "，请点击上方安装 / 更新重新准备。尚未执行部署。";
                log.Write(message);
                progress.Report(new UiStatus("准备包已过期，需要重新下载", message, 0));
                return false;
            }
            if (ShouldSkip(installed, current.Version, prepared.UpdateOnly))
            {
                Complete(installed.Version, "本机已安装此版本或更高版本，无需更新。");
                ClearPreparedFiles(prepared);
                return true;
            }
            if (!prepared.FilesExist()) throw new FileNotFoundException("下载缓存已被移除，请点击上方安装 / 更新重新准备。");
            var info = VerifyPackage(prepared.Release, prepared.PackagePath, token);
            PackageInfo.CheckLicense(prepared.LicensePath);
            Paths.RequireSpace(Environment.GetFolderPath(Environment.SpecialFolder.Windows), info.ExpandedSize + 512L * 1024 * 1024, "系统盘");
            resolver.ConfirmCurrent(current, token);
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

        private PackageInfo VerifyPackage(ReleaseInfo release, string package, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new UiStatus("检查安装包", "核对官方包身份、系统要求和数字签名…", -1));
            PackageInfo info;
            try
            {
                info = PackageInfo.Read(package, release.Architecture);
                release.RequireVersion(info.Version, "下载的安装包");
            }
            catch (InvalidDataException)
            {
                Paths.DeleteFile(package);
                Paths.DeleteFile(package + ".identity");
                throw;
            }
            try { SignatureVerifier.Verify(package); }
            catch
            {
                Paths.DeleteFile(package);
                Paths.DeleteFile(package + ".identity");
                log.Write("签名验证失败，已清除下载缓存，下次将重新下载。");
                throw;
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
