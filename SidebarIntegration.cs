using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：安装助手的用户级侧栏集成执行方。
    /// 示例：MainForm 的添加 / 修复入口将程序副本和插件注册交给这里处理。
    /// 职责：部署独立副本、调用官方插件管理入口，并记录可修复的集成状态。
    /// 约束：与 ChatGPT 的包部署生命周期分离，不清理下载缓存或其他插件。
    /// </summary>
    internal static class SidebarIntegration
    {
        public const string PluginName = "chatgpt-updater";
        public const string MarketplaceName = "chatgpt-install-helper";
        public const string Selector = PluginName + "@" + MarketplaceName;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string VersionString { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
        private static string Receipt { get { return Path.Combine(Paths.Sidebar, "integration.json"); } }
        private static string Current { get { return Path.Combine(Paths.Applications, "current.txt"); } }
        private static string SavedExecutable { get { return Path.Combine(Paths.Applications, VersionString, "ChatGPTInstaller.exe"); } }

        public static void Install(Action<string> report)
        {
            Configure(report, true);
        }

        public static SidebarInstallStatus EnsureInstalled(Action<string> report, Action installing)
        {
            return Configure(report, false, installing);
        }

        private static SidebarInstallStatus Configure(Action<string> report, bool force, Action installing = null)
        {
            using (var mutex = new Mutex(false, "Local\\ChatGPTInstallHelper-Sidebar"))
            {
                bool locked;
                try { locked = mutex.WaitOne(force ? 0 : 5000); } catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new InvalidOperationException("另一个窗口正在配置侧栏入口，请等待完成。");
                try
                {
                    Directory.CreateDirectory(Paths.Sidebar);
                    var client = new CodexPluginClient();
                    if (!force && !client.IsAvailable) return SidebarInstallStatus.CodexNotFound;
                    report("正在检查 Codex 侧栏入口…");
                    if (!force && client.IsPluginInstalled()) return SidebarInstallStatus.AlreadyInstalled;
                    if (installing != null) installing();
                    report("正在确认 Codex 插件来源…");
                    client.HasOwnedMarketplace();
                    report("正在保存安装助手的独立副本…");
                    string target = DeployExecutable();
                    report("正在准备侧栏插件…");
                    WritePlugin(target);
                    report("正在注册本地插件来源…");
                    client.Run("plugin", "marketplace", "add", Paths.Sidebar, "--json");
                    report("正在安装并启用侧栏插件…");
                    var installed = JsonData.Parse(client.Run("plugin", "add", Selector, "--json"));
                    string installedPath = JsonData.Get(installed, "installedPath") as string;
                    if (JsonData.Get(installed, "pluginId") as string != Selector || !HasSavedCommand(installedPath))
                        throw new InvalidDataException("Codex 未返回此插件的安装确认，请稍后重新添加 / 修复入口。");
                    // 手动修复也保存启动目标；自动启动已先保存副本，与插件是否安装无关。
                    WriteText(Current, VersionString);
                    WriteText(Receipt, JsonData.Serialize(new { version = VersionString, codexHome = CodexHome(), installedPath = installedPath, installedAt = DateTime.UtcNow.ToString("o") }));
                    report("已作为 Codex 插件安装，今后可在 Codex 插件中操作，也可保留原 EXE 继续使用。");
                    return SidebarInstallStatus.Installed;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static void Remove(Action<string> report)
        {
            using (var mutex = new Mutex(false, "Local\\ChatGPTInstallHelper-Sidebar"))
            {
                bool locked;
                try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new InvalidOperationException("另一个窗口正在配置侧栏入口，请等待完成。");
                try
                {
                    Directory.CreateDirectory(Paths.Sidebar);
                    var client = new CodexPluginClient();
                    bool hasMarketplace = client.HasOwnedMarketplace();
                    report("正在移除侧栏插件…");
                    client.Run("plugin", "remove", Selector, "--json");
                    report("正在移除本工具的插件来源…");
                    if (hasMarketplace) client.Run("plugin", "marketplace", "remove", MarketplaceName, "--json");
                    if (File.Exists(Receipt)) File.Delete(Receipt);
                    report("侧栏入口已移除，重新打开 ChatGPT 后生效。安装助手和已下载的安装包已保留。");
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static string Status()
        {
            if (!File.Exists(Receipt)) return "双击 EXE 会在后台检查：已有 Codex 且插件未安装时自动添加。也可点击“添加 / 修复入口”。";
            try
            {
                var receipt = JsonData.Parse(File.ReadAllText(Receipt, Utf8));
                if (!string.Equals(JsonData.Get(receipt, "codexHome") as string, CodexHome(), StringComparison.OrdinalIgnoreCase))
                    return "当前 Codex 配置目录与上次不同，可点击添加 / 修复入口为当前配置启用。";
                return "已完成过侧栏安装。若入口未显示，请重新打开 ChatGPT，或点击添加 / 修复入口。";
            }
            catch (Exception) { return "侧栏安装记录无法读取，可以重新添加 / 修复入口。"; }
        }

        public static void OpenFolder()
        {
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(ExecutablePath())) { UseShellExecute = true });
        }

        public static string ExecutablePath()
        {
            string executable = Assembly.GetExecutingAssembly().Location;
            if (File.Exists(Current))
            {
                Version version;
                string value = File.ReadAllText(Current, Utf8).Trim();
                if (!Version.TryParse(value, out version) || version.ToString() != value)
                    throw new InvalidDataException("安装助手入口版本无效，请运行下载的 EXE 修复侧栏入口。");
                if (version >= new Version(VersionString)) executable = Path.Combine(Paths.Applications, value, "ChatGPTInstaller.exe");
            }
            if (!File.Exists(executable)) throw new FileNotFoundException("安装助手副本已被移除，请运行下载的 EXE 修复侧栏入口。");
            return executable;
        }

        public static string PrepareLaunch()
        {
            string target = DeployExecutable();
            // 旧下载文件优先唤起已有新版，不能将侧栏降级到旧助手。
            if (File.Exists(Current))
            {
                Version version;
                string value = File.ReadAllText(Current, Utf8).Trim();
                if (Version.TryParse(value, out version) && version.ToString() == value && version > new Version(VersionString))
                {
                    string newer = Path.Combine(Paths.Applications, value, "ChatGPTInstaller.exe");
                    Version fileVersion;
                    if (File.Exists(newer) && Version.TryParse(FileVersionInfo.GetVersionInfo(newer).FileVersion, out fileVersion) &&
                        fileVersion == new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision))) return newer;
                }
            }
            // 固定副本是独立窗口和插件共同的启动目标；没有 Codex 时也可以保留或删除原下载文件。
            if (!File.Exists(Current) || File.ReadAllText(Current, Utf8).Trim() != VersionString) WriteText(Current, VersionString);
            return target;
        }

        private static bool HasSavedCommand(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath)) return false;
            foreach (string name in new[] { "mcp.json", ".mcp.json" })
            {
                string path = Path.Combine(pluginPath, name);
                if (!File.Exists(path)) return false;
                var configuration = JsonData.Parse(File.ReadAllText(path, Utf8));
                var servers = JsonData.Get(configuration, "mcpServers") as Dictionary<string, object>;
                var server = JsonData.Get(servers, "chatgpt-updater") as Dictionary<string, object>;
                if (!string.Equals(JsonData.Get(server, "command") as string, SavedExecutable, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static string DeployExecutable()
        {
            string source = Assembly.GetExecutingAssembly().Location;
            string folder = Path.Combine(Paths.Applications, VersionString);
            string target = Path.Combine(folder, "ChatGPTInstaller.exe");
            Directory.CreateDirectory(folder);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                byte[] expected = File.ReadAllBytes(source);
                if (File.Exists(target))
                {
                    if (!EqualBytes(expected, File.ReadAllBytes(target)))
                        throw new IOException("同版本的独立副本内容不同。请先关闭旧助手后使用新的版本号发布，避免替换正在运行的程序。");
                }
                else
                {
                    try { WriteBytes(target, expected, false); }
                    catch (IOException)
                    {
                        // 同时双击时另一进程可能已发布相同副本；只接受内容完全一致的结果。
                        if (!File.Exists(target) || !EqualBytes(expected, File.ReadAllBytes(target))) throw;
                    }
                }
            }
            return target;
        }

        private static void WritePlugin(string executable)
        {
            string root = Path.Combine(Paths.Sidebar, "plugins", PluginName);
            var presentation = new
            {
                displayName = "ChatGPT 更新器", shortDescription = "直接查看版本与进度，下载更新，应用后自动重启。",
                longDescription = "默认下载后等待应用；可选择本次下载完成后自动安装并重启。完整安装助手作为可选入口。",
                developerName = "Personal utility", category = "Productivity", composerIcon = "./assets/icon.svg", logo = "./assets/icon.svg"
            };
            var manifest = new
            {
                name = PluginName, version = VersionString, description = "在全局侧栏检查、下载和应用 ChatGPT 更新。",
                author = new { name = "Personal utility - not affiliated with OpenAI" },
                extensions = new Dictionary<string, object> { { "com.openai", new { @interface = presentation } } }
            };
            // 当前桌面版仍使用兼容清单；两种清单由同一份字段生成。
            var legacyManifest = new
            {
                name = manifest.name, version = manifest.version, description = manifest.description,
                author = manifest.author, @interface = presentation
            };
            // 侧栏进程沿用注册时的用户配置，避免宿主的临时配置目录影响后续修复或移除。
            var server = new { command = executable, args = new[] { "--mcp" }, env = new Dictionary<string, string> { { "CODEX_HOME", CodexHome() } } };
            var servers = new Dictionary<string, object> { { "chatgpt-updater", server } };
            WriteText(Path.Combine(root, "plugin.json"), JsonData.Serialize(manifest));
            WriteText(Path.Combine(root, ".codex-plugin", "plugin.json"), JsonData.Serialize(legacyManifest));
            WriteText(Path.Combine(root, "mcp.json"), JsonData.Serialize(new { mcpServers = servers }));
            WriteText(Path.Combine(root, ".mcp.json"), JsonData.Serialize(new { mcpServers = servers }));
            WriteText(Path.Combine(root, "assets", "icon.svg"), EmbeddedFiles.Read("icon.svg"));
            WriteText(Path.Combine(Paths.Sidebar, ".agents", "plugins", "marketplace.json"), JsonData.Serialize(new
            {
                name = MarketplaceName,
                @interface = new { displayName = "ChatGPT 安装助手" },
                plugins = new[] { new { name = PluginName, source = new { source = "local", path = "./plugins/" + PluginName },
                    policy = new { installation = "AVAILABLE", authentication = "ON_INSTALL" }, category = "Productivity" } }
            }));
        }

        private static string CodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : configured);
        }

        private static bool EqualBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static void WriteText(string path, string value)
        {
            WriteBytes(path, Utf8.GetBytes(value));
        }

        private static void WriteBytes(string path, byte[] value, bool replaceExisting = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, value);
                if (replaceExisting && File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
