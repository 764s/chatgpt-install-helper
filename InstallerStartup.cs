using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：双击启动与固定安装副本之间的交接执行方。
    /// 示例：下载目录中的 EXE 启动 App 目录副本后退出，MainForm 在后台确认 Codex 入口。
    /// 职责：释放原始文件、确认入口配置，并提供与真实完成状态一致的提示。
    /// 约束：后台 MCP、应用部署及普通更新操作不经过此流程；不结束其他更新器进程。
    /// </summary>
    internal sealed class InstallerStartup
    {
        private readonly int launcherId;
        private readonly long launcherStartedAt;
        public bool ConfiguredThisTime { get; private set; }
        public bool UpdatedThisTime { get; private set; }
        private string FileNotice { get { return launcherId > 0 ? "原 EXE 可安全删除，也可保留继续使用。" : "也可继续使用此窗口；请保留已安装目录中的文件。"; } }

        public InstallerStartup(int launcherId = 0, long launcherStartedAt = 0)
        {
            this.launcherId = launcherId;
            this.launcherStartedAt = launcherStartedAt;
        }

        public static bool ForwardToSavedCopy()
        {
            string target = SidebarIntegration.PrepareLaunch();
            if (string.Equals(Path.GetFullPath(Assembly.GetExecutingAssembly().Location), target, StringComparison.OrdinalIgnoreCase)) return false;
            using (var current = Process.GetCurrentProcess())
            {
                string arguments = "--startup " + current.Id + " " + current.StartTime.ToUniversalTime().Ticks;
                using (var launched = Process.Start(new ProcessStartInfo(target, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }))
                {
                    if (launched == null) throw new IOException("无法启动安装助手的独立副本，请保留下载的 EXE 后重试。");
                    WindowActivation.AllowForeground(launched.Id);
                }
            }
            return true;
        }

        public async Task<string> CompleteWithNotice(Form owner, Action<string> report, IntPtr existingWindow = default(IntPtr))
        {
            var notice = new StartupNotice(owner == null ? existingWindow : owner.Handle);
            bool installing = false;
            bool upgrading = false;
            var progress = new Progress<string>(message => { report(message); notice.Report(message); });
            var started = new Progress<bool>(value =>
            {
                installing = true;
                upgrading = value;
                notice.Present(value ? "正在更新 Codex 插件" : "正在安装 Codex 插件", "配置期间可继续使用更新器。\n关闭此提示不会中断操作。", false);
            });
            string result = null;
            Exception failure = null;
            try
            {
                result = await Task.Run(() => Complete(message => ((IProgress<string>)progress).Report(message), value => ((IProgress<bool>)started).Report(value)));
                if (ConfiguredThisTime) notice.Present(UpdatedThisTime ? "Codex 插件已更新" : "已作为 Codex 插件安装", (UpdatedThisTime ? "已更新至 " + SidebarIntegration.VersionString + "，启用 / 停用状态已保留。" : "今后可在 Codex 插件中操作。") + "\n" + FileNotice + "\n入口未刷新时，等任务结束再重新打开 ChatGPT。", true);
                else notice.Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
                report(ex.Message);
                notice.Present(installing ? (upgrading ? "Codex 插件更新未完成" : "Codex 插件安装未完成") : "Codex 插件检查未完成", ex.Message + "\n更新器仍可正常使用，可在“Codex 侧栏入口”中重试。", true);
            }
            // 已有更新器时，此独立进程只承载提示；等待提示关闭，不占用已有窗口的线程。
            if (owner == null && !notice.IsDisposed) await notice.Dismissed;
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }

        public string Complete(Action<string> report, Action<bool> installing)
        {
            WaitForLauncher();
            SidebarInstallStatus status = SidebarIntegration.EnsureInstalled(report, installing);
            UpdatedThisTime = status == SidebarInstallStatus.Updated;
            ConfiguredThisTime = status == SidebarInstallStatus.Installed || UpdatedThisTime;
            if (UpdatedThisTime)
                return "Codex 插件已更新至 " + SidebarIntegration.VersionString + "，启用 / 停用状态已保留。\n" + FileNotice;
            if (ConfiguredThisTime)
                return "已作为 Codex 插件安装，今后可在 Codex 插件中操作。\n" + FileNotice;
            if (status == SidebarInstallStatus.CodexNotFound)
                return "未检测到 Codex，可直接使用更新器。\n" + FileNotice;
            return "Codex 插件已是当前版本或更高版本，无需更新。\n" + FileNotice;
        }

        private void WaitForLauncher()
        {
            if (launcherId == 0) return;
            Process launcher;
            try { launcher = Process.GetProcessById(launcherId); }
            catch (ArgumentException) { return; }
            using (launcher)
            {
                // PID 可被复用；只等待启动我们的那次进程，不结束任何进程。
                try
                {
                    if (launcher.HasExited || launcher.StartTime.ToUniversalTime().Ticks != launcherStartedAt) return;
                    if (!launcher.WaitForExit(10000)) throw new IOException("原 EXE 尚未退出，请先保留该文件。可继续使用更新器，稍后重新打开以完成配置。");
                }
                catch (InvalidOperationException) { if (!launcher.HasExited) throw; }
            }
        }
    }
}
