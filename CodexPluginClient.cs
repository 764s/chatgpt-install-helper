using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：侧栏安装流程调用 Codex 插件管理命令的执行方。
    /// 示例：SidebarIntegration 通过它安装 chatgpt-updater 插件。
    /// 职责：定位本机 CLI，传递固定操作参数，收集结果和失败信息。
    /// 约束：不启动终端窗口，不直接编辑用户的 Codex 配置。
    /// </summary>
    internal sealed class CodexPluginClient
    {
        private readonly string executable;

        public CodexPluginClient()
        {
            executable = Locate();
        }

        public bool IsAvailable { get { return executable != null; } }

        public string Run(params string[] arguments)
        {
            return Run(60000, arguments);
        }

        private string Run(int timeout, params string[] arguments)
        {
            if (!IsAvailable) throw new FileNotFoundException("未找到 ChatGPT 随附的 Codex 组件。请先安装并打开一次新版 ChatGPT，再添加侧栏入口。");
            var start = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
            {
                WorkingDirectory = Paths.Sidebar,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout))
                {
                    // 只结束本次插件命令，不影响正在执行任务的 Codex 进程。
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("Codex 插件配置等待超时。可稍后重试添加 / 修复入口。");
                }
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Codex 插件命令未完成：" + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim());
                return stdout;
            }
        }

        public bool HasOwnedMarketplace()
        {
            var result = JsonData.Parse(Run(5000, "plugin", "marketplace", "list", "--json"));
            var marketplaces = JsonData.Get(result, "marketplaces") as IEnumerable;
            if (marketplaces == null) throw new InvalidDataException("Codex 返回的插件来源格式已变化，请更新安装助手。");
            foreach (var value in marketplaces)
            {
                var marketplace = value as Dictionary<string, object>;
                if (JsonData.Get(marketplace, "name") as string != SidebarIntegration.MarketplaceName) continue;
                string root = JsonData.Get(marketplace, "root") as string;
                if (string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("无法确认侧栏插件来源路径，未修改现有来源。");
                if (root.StartsWith("\\\\?\\", StringComparison.Ordinal)) root = root.Substring(4);
                if (!string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Path.GetFullPath(Paths.Sidebar).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("已有同名插件来源指向其他目录。为保留现有配置，本次未覆盖或移除该来源。");
                return true;
            }
            return false;
        }

        public bool IsPluginInstalled()
        {
            // 一次只读查询确认真实安装状态；不依赖可能过期的完成记录，也不要求来源仍已注册。
            var result = JsonData.Parse(Run(5000, "plugin", "list", "--json"));
            var installed = JsonData.Get(result, "installed") as IEnumerable;
            if (installed == null) throw new InvalidDataException("Codex 返回的插件安装状态格式已变化，请更新安装助手。");
            foreach (var value in installed)
            {
                var plugin = value as Dictionary<string, object>;
                // 已安装的旧版或被用户停用的插件不因双击而重装、升级或重新启用。
                if (JsonData.Get(plugin, "pluginId") as string == SidebarIntegration.Selector &&
                    object.Equals(JsonData.Get(plugin, "installed"), true)) return true;
            }
            return false;
        }

        private static string Locate()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                string found = Directory.GetDirectories(root).Select(folder => Path.Combine(folder, "codex.exe"))
                    .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (found != null) return found;
            }
            using (var shell = new PowerShellBridge(null))
            {
                var installed = shell.FindInstalled();
                if (installed != null)
                {
                    string bundled = Path.Combine(installed.Location, "app", "resources", "codex.exe");
                    if (File.Exists(bundled)) return bundled;
                }
            }
            return null;
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }
    }
}
