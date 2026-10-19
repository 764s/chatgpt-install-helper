using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatGPTInstaller
{
    internal static class DeploymentHelper
    {
        public static int Execute(string package, string license, string userSid, string statusFile, bool repair)
        {
            // 提权入口只接受同一缓存目录内的固定输入，不接受任意输出路径。
            try { ValidatePaths(package, license, statusFile); }
            catch (Exception) { return 2; }
            using (var statusStream = new FileStream(statusFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var statusWriter = new StreamWriter(statusStream, Encoding.UTF8))
            {
                try
                {
                    using (var principal = WindowsIdentity.GetCurrent())
                    {
                        if (!new WindowsPrincipal(principal).IsInRole(WindowsBuiltInRole.Administrator))
                            throw new InvalidOperationException("Windows 安装此软件需要管理员批准。");
                        // 在权限较高的进程内再次检查，并禁止部署期间写入或替换输入文件。
                        using (var packageLock = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var licenseLock = new FileStream(license, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            Report(statusWriter, -1, "正在验证官方安装包");
                            var info = PackageInfo.Read(package, PlatformInfo.Architecture());
                            PackageInfo.CheckLicense(license);
                            SignatureVerifier.Verify(package);
                            using (var shell = new PowerShellBridge((value, detail) => Report(statusWriter, value, detail)))
                            {
                                Action<Action> run = operation => DeploymentRetry.Run(operation,
                                    message => Report(statusWriter, -3, message), error => Report(statusWriter, -4, error.ToString()));
                                var installed = shell.FindInstalled();
                                if (installed != null && installed.Version > info.Version)
                                    throw new InvalidOperationException("电脑上已有更高版本，已阻止降级安装。");
                                bool sameUser = principal.User.Value == userSid;
                                bool hasInstallation = sameUser && installed != null;
                                if (hasInstallation)
                                {
                                    Report(statusWriter, -1, repair ? "正在覆盖修复应用" : "正在更新应用");
                                    run(() => shell.Add(package, repair));
                                }
                                if (!hasInstallation || repair)
                                {
                                    Report(statusWriter, -1, "正在安装软件与官方离线许可证");
                                    run(() => shell.Provision(package, license));
                                }
                                if (principal.User.Value == userSid)
                                {
                                    Report(statusWriter, -1, "正在注册当前用户的应用");
                                    if (!hasInstallation) run(() => shell.Add(package, repair));
                                    run(() => shell.Register(info.FullName));
                                    var after = shell.FindInstalled();
                                    if (after == null || after.Version < info.Version)
                                        throw new InvalidOperationException("Windows 部署已结束，但未检测到当前用户的目标版本。");
                                }
                            }
                        }
                    }
                    Report(statusWriter, 100, "系统部署已完成");
                    return 0;
                }
                catch (Exception ex)
                {
                    try { Report(statusWriter, -2, ex.ToString()); } catch (IOException) { }
                    return 1;
                }
            }
        }

        private static void ValidatePaths(string package, string license, string statusFile)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(package));
            if (Path.GetFullPath(package).StartsWith("\\\\", StringComparison.Ordinal) ||
                !Regex.IsMatch(Path.GetFileName(package), @"^ChatGPT-(x64|arm64)\.msix$") ||
                Path.GetFileName(license) != "ChatGPT-License.xml" ||
                !Regex.IsMatch(Path.GetFileName(statusFile), @"^deploy-[0-9a-f]{32}\.progress$") ||
                !string.Equals(parent, Path.GetDirectoryName(Path.GetFullPath(license)), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(parent, Path.GetDirectoryName(Path.GetFullPath(statusFile)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("无效的部署文件路径。");
            for (var directory = new DirectoryInfo(parent); directory != null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("部署路径不能包含重解析点。");
            foreach (string file in new[] { package, license, statusFile })
                if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("部署输入不能是符号链接或重解析点。");
            if (File.Exists(statusFile)) throw new InvalidOperationException("部署状态文件必须是全新文件。");
        }

        private static void Report(StreamWriter writer, int percent, string detail)
        {
            string line = percent + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(detail ?? "")) + Environment.NewLine;
            lock (writer) { writer.Write(line); writer.Flush(); }
        }

        public static void Deploy(string package, string license, bool repair, IProgress<UiStatus> progress, RunLog log)
        {
            string status = Path.Combine(Paths.Cache, "deploy-" + Guid.NewGuid().ToString("N") + ".progress");
            try
            {
                string sid = WindowsIdentity.GetCurrent().User.Value;
                var start = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Arguments = "--deploy " + Quote(package) + " " + Quote(license) + " " + Quote(sid) + " " + Quote(status) + " " + (repair ? "repair" : "install"),
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                };
                progress.Report(new UiStatus("等待 Windows 授权", "请在 Windows 提示中批准安装。", -1) { Deployment = true });
                using (var process = Process.Start(start))
                {
                    int seen = 0;
                    string error = null;
                    while (true)
                    {
                        bool exited = process.WaitForExit(250);
                        if (File.Exists(status))
                        {
                            string content;
                            using (var file = new FileStream(status, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var reader = new StreamReader(file, Encoding.UTF8)) content = reader.ReadToEnd();
                            var lines = content.Split(new[] { '\n' });
                            // 只读取换行完成的记录，避免把正在写入的进度误判为错误。
                            while (seen < lines.Length - 1)
                            {
                                string line = lines[seen++].Trim('\r', '\uFEFF');
                                int separator = line.IndexOf('|');
                                if (separator < 1) continue;
                                int percent;
                                if (!int.TryParse(line.Substring(0, separator), out percent)) continue;
                                string detail = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(separator + 1)));
                                if (percent == -2) error = detail;
                                else if (percent == -4) log.WriteTechnical(detail);
                                else if (percent == -3)
                                {
                                    log.Write(detail);
                                    progress.Report(new UiStatus("等待应用退出后重试", detail, -1) { Deployment = true });
                                }
                                else progress.Report(new UiStatus("正在安装 ChatGPT", detail, percent) { Deployment = true });
                            }
                        }
                        if (exited)
                        {
                            if (process.ExitCode != 0) throw new InvalidOperationException(error ??
                                (process.ExitCode == 2 ? "安装辅助进程无法安全读取缓存或写入进度。请检查用户目录权限；不支持重定向到链接目录的缓存。" :
                                "Windows 安装进程未成功完成（退出码 " + process.ExitCode + "）。"));
                            break;
                        }
                    }
                }
                log.Write("Windows 部署进程已完成。");
            }
            finally { Paths.DeleteFile(status); }
        }

        private static string Quote(string value)
        {
            if (value.Contains("\"") || value.EndsWith("\\", StringComparison.Ordinal)) throw new ArgumentException("无效的内部参数。");
            return "\"" + value + "\"";
        }
    }
}
