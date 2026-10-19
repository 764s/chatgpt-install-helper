using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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

        public InstalledSidebarPlugin GetInstalledPlugin()
        {
            // 一次只读查询确认真实安装状态；不依赖可能过期的完成记录，也不要求来源仍已注册。
            var result = JsonData.Parse(Run(5000, "plugin", "list", "--json"));
            var installed = JsonData.Get(result, "installed") as IEnumerable;
            if (installed == null) throw new InvalidDataException("Codex 返回的插件安装状态格式已变化，请更新安装助手。");
            foreach (var value in installed)
            {
                var plugin = value as Dictionary<string, object>;
                if (JsonData.Get(plugin, "pluginId") as string != SidebarIntegration.Selector || !object.Equals(JsonData.Get(plugin, "installed"), true)) continue;
                Version version;
                object enabled = JsonData.Get(plugin, "enabled");
                if (!Version.TryParse(JsonData.Get(plugin, "version") as string, out version) || !(enabled is bool))
                    throw new InvalidDataException("无法确认已有插件的版本或启用状态，本次未覆盖插件。可稍后重试。");
                return new InstalledSidebarPlugin(version, (bool)enabled);
            }
            return null;
        }

        public void RefreshInstalledPlugin(Version targetVersion, Func<bool> cacheReady)
        {
            // plugin add 会重新启用插件；已有插件交给官方刷新流程，不写用户的启用开关。
            var start = new ProcessStartInfo(executable, "app-server --stdio")
            {
                WorkingDirectory = Paths.Sidebar, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                var error = process.StandardError.ReadToEndAsync();
                var elapsed = Stopwatch.StartNew();
                try
                {
                    var writer = process.StandardInput;
                    writer.AutoFlush = true;
                    writer.WriteLine(JsonData.Serialize(new { id = 1, method = "initialize", @params = new
                    {
                        clientInfo = new { name = "chatgpt-install-helper", version = SidebarIntegration.VersionString },
                        capabilities = new { experimentalApi = true }
                    } }));
                    ReadReply(process, 1, elapsed);
                    writer.WriteLine(JsonData.Serialize(new { method = "initialized", @params = new { } }));
                    // 本地目录刷新会更新已配置插件的缓存；单独读取详情不会执行缓存刷新。
                    writer.WriteLine(JsonData.Serialize(new { id = 2, method = "plugin/list", @params = new
                    {
                        forceRefetch = false, marketplaceKinds = new[] { "local" }
                    } }));
                    ReadReply(process, 2, elapsed);
                    // 目录列表返回时缓存落盘可能仍在进行；保留辅助进程，直到真实安装记录就绪。
                    while (elapsed.ElapsedMilliseconds < 15000)
                    {
                        var installed = GetInstalledPlugin();
                        if (installed != null && installed.Version == targetVersion && cacheReady()) return;
                        Thread.Sleep(100);
                    }
                    throw new TimeoutException("Codex 尚未完成新版插件缓存准备，可稍后重试；更新器仍可使用。");
                }
                catch (IOException ex)
                {
                    string detail = error.IsCompleted ? error.GetAwaiter().GetResult().Trim() : "";
                    throw new IOException("Codex 插件刷新未完成。" + (detail.Length == 0 ? ex.Message : detail), ex);
                }
                finally
                {
                    // 只关闭本次配置使用的辅助进程，不连接或结束桌面端的任务进程。
                    try { process.StandardInput.Close(); } catch (IOException) { }
                    if (!process.WaitForExit(1000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                    }
                }
            }
        }

        private static void ReadReply(Process process, int id, Stopwatch elapsed)
        {
            while (true)
            {
                int remaining = 15000 - (int)elapsed.ElapsedMilliseconds;
                if (remaining <= 0) throw new TimeoutException("Codex 插件刷新等待超时，可稍后重试；更新器仍可使用。");
                var line = process.StandardOutput.ReadLineAsync();
                if (!line.Wait(remaining)) throw new TimeoutException("Codex 插件刷新等待超时，可稍后重试；更新器仍可使用。");
                if (line.Result == null) throw new IOException("Codex 提前结束了插件刷新。");
                var reply = JsonData.Parse(line.Result);
                if (!object.Equals(JsonData.Get(reply, "id"), id)) continue;
                var error = JsonData.Get(reply, "error") as Dictionary<string, object>;
                if (error != null) throw new InvalidOperationException("Codex 插件刷新失败：" + (JsonData.Get(error, "message") as string ?? "未知错误"));
                if (!(JsonData.Get(reply, "result") is Dictionary<string, object>)) throw new InvalidDataException("Codex 未返回插件刷新确认。");
                return;
            }
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
