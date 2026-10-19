using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：插件升级完成后，回收安装助手自有旧副本的执行方。
    /// 示例：SidebarIntegration 核验新版插件后，回收此前由本规则创建的 App 与 Sidebar/versions 目录。
    /// 职责：标记新建目录，只保留当前版本，并报告暂时无法回收的旧版本。
    /// 约束：不接管已有目录、不结束进程；占用或清理失败留待下一次插件升级重试。
    /// </summary>
    internal static class SidebarVersionRetention
    {
        private const string Marker = ".helper-retention-v1";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly string[] ApplicationFiles = { "ChatGPTInstaller.exe" };
        private static readonly string[] PluginFiles =
        {
            "chatgpt-updater/plugin.json", "chatgpt-updater/.codex-plugin/plugin.json",
            "chatgpt-updater/mcp.json", "chatgpt-updater/.mcp.json", "chatgpt-updater/assets/icon.svg"
        };

        public static string CreateVersionDirectory(string parent, string version)
        {
            string folder = VersionDirectory(parent, version);
            if (Directory.Exists(folder)) return folder;
            Directory.CreateDirectory(folder);
            // 不给既有目录补标记；未标记的历史副本永久排除在自动清理范围之外。
            string marker = Path.Combine(folder, Marker);
            try
            {
                using (var file = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(file, Utf8)) writer.Write(MarkerContent(version));
            }
            catch (IOException)
            {
                if (!IsManaged(folder, version)) throw;
            }
            return folder;
        }

        public static void Cleanup(string applications, string sources, string currentVersion, Action<string> report)
        {
            try
            {
                Version current = ParseVersion(currentVersion);
                var versions = VersionNames(applications).Concat(VersionNames(sources)).Distinct(StringComparer.OrdinalIgnoreCase);
                HashSet<string> running = null;
                foreach (string name in versions)
                {
                    Version version;
                    if (!TryVersion(name, out version) || version >= current) continue;
                    string application = VersionDirectory(applications, name);
                    string source = VersionDirectory(sources, name);
                    try
                    {
                        bool managedApplication = IsManaged(application, name);
                        bool managedSource = IsManaged(source, name);
                        if (!managedApplication && !managedSource) continue;
                        if (running == null) running = RunningExecutables();
                        if (running.Contains(Path.Combine(application, ApplicationFiles[0])))
                        {
                            report("旧副本 " + name + " 仍在运行，暂留到下一次插件更新时清理。");
                            continue;
                        }
                        // 两处均检查通过后才开始回收；发现外加文件或链接时保留整个版本。
                        if (managedApplication) ValidateContents(application, ApplicationFiles);
                        if (managedSource) ValidateContents(source, PluginFiles);
                        // 先删 EXE；Windows 拒绝删除占用文件时，插件来源也保持原样。
                        if (managedApplication) DeleteManagedDirectory(application, ApplicationFiles, name);
                        if (managedSource) DeleteManagedDirectory(source, PluginFiles, name);
                        report("已清理旧副本 " + name + "，当前版本 " + currentVersion + " 已保留。");
                    }
                    catch (Exception ex)
                    {
                        if (!IsCleanupError(ex)) throw;
                        report("旧副本 " + name + " 本次未能清理，留待下一次插件更新重试：" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsCleanupError(ex)) throw;
                // 清理发生在安装确认之后；清理问题不能把已成功的插件升级改报为失败。
                report("插件已更新；旧副本清理暂未完成，将在下一次插件更新时重试：" + ex.Message);
            }
        }

        private static IEnumerable<string> VersionNames(string parent)
        {
            if (!Directory.Exists(parent)) return new string[0];
            RequireRealPath(parent);
            return Directory.GetDirectories(parent).Select(Path.GetFileName).ToArray();
        }

        private static string VersionDirectory(string parent, string version)
        {
            ParseVersion(version);
            string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string folder = Path.GetFullPath(Path.Combine(root, version));
            if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(folder) + Path.DirectorySeparatorChar != root)
                throw new InvalidDataException("副本目录不在指定的版本目录内。");
            return folder;
        }

        private static Version ParseVersion(string value)
        {
            Version result;
            if (!TryVersion(value, out result)) throw new InvalidDataException("副本版本号无效。");
            return result;
        }

        private static bool TryVersion(string value, out Version result)
        {
            return Version.TryParse(value, out result) && result.Build >= 0 && result.Revision == -1 && result.ToString(3) == value;
        }

        private static string MarkerContent(string version)
        {
            return "ChatGPTInstallHelper owned version directory v1\n" + version;
        }

        private static bool IsManaged(string folder, string version)
        {
            if (!Directory.Exists(folder)) return false;
            string marker = Path.Combine(folder, Marker);
            if (!File.Exists(marker)) return false;
            RequireRealPath(marker);
            return new FileInfo(marker).Length <= 128 && File.ReadAllText(marker, Utf8) == MarkerContent(version);
        }

        private static HashSet<string> RunningExecutables()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processes = Process.GetProcessesByName("ChatGPTInstaller");
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        var module = process.MainModule;
                        if (module == null) throw new IOException("无法确认更新器进程的运行路径。");
                        result.Add(Path.GetFullPath(module.FileName));
                    }
                    catch (InvalidOperationException ex)
                    {
                        if (!process.HasExited) throw new IOException("无法确认更新器进程是否仍在运行。", ex);
                    }
                    // 权限不足时无法确认是否占用，交给上层保留副本，不猜测为空闲。
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            return result;
        }

        private static HashSet<string> AllowedDirectories(string[] files)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
                for (string folder = Path.GetDirectoryName(file.Replace('/', Path.DirectorySeparatorChar)); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder)) result.Add(folder);
            return result;
        }

        private static void ValidateContents(string folder, string[] files)
        {
            RequireRealPath(folder);
            var allowedFiles = new HashSet<string>(files.Select(file => file.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase) { Marker };
            var allowedDirectories = AllowedDirectories(files);
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                foreach (string entry in Directory.GetFileSystemEntries(pending.Pop()))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("目录包含链接，已保留。");
                    string relative = entry.Substring(folder.Length + 1);
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    if (!(directory ? allowedDirectories : allowedFiles).Contains(relative)) throw new IOException("目录包含非本工具管理的文件，已保留。");
                    if (directory) pending.Push(entry);
                }
            }
        }

        private static void RequireRealPath(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("副本路径包含链接，已保留。");
        }

        private static void DeleteManagedDirectory(string folder, string[] files, string version)
        {
            foreach (string file in files)
            {
                string path = Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) File.Delete(path);
            }
            foreach (string directory in AllowedDirectories(files).OrderByDescending(value => value.Length))
            {
                string child = Path.Combine(folder, directory);
                if (Directory.Exists(child)) Directory.Delete(child, false);
            }
            string marker = Path.Combine(folder, Marker);
            File.Delete(marker);
            try { Directory.Delete(folder, false); }
            catch (IOException)
            {
                // 并发新增文件导致目录不能删除时保留管理标记，下次仍可检查和重试。
                if (Directory.Exists(folder) && !File.Exists(marker)) File.WriteAllText(marker, MarkerContent(version), Utf8);
                throw;
            }
        }

        private static bool IsCleanupError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException || ex is Win32Exception;
        }
    }
}
