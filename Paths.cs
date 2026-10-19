using System;
using System.IO;

namespace ChatGPTInstaller
{
    internal static class Paths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPTInstallHelper");
        public static readonly string Cache = Path.Combine(Root, "Cache");
        public static readonly string Logs = Path.Combine(Root, "Logs");
        public static readonly string Applications = Path.Combine(Root, "App");
        public static readonly string Sidebar = Path.Combine(Root, "Sidebar");
        public static readonly string OfficialPackage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", AppInfo.FamilyName, "LocalCache", "windows-updates", "prepared.msix");

        public static void Ensure()
        {
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(Logs);
        }

        public static void DeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void RequireSpace(string path, long bytes, string label)
        {
            var drive = new DriveInfo(Path.GetPathRoot(path));
            if (drive.AvailableFreeSpace < bytes)
                throw new IOException(label + "剩余空间不足，当前阶段需要至少 " + NetworkClient.FormatBytes(bytes) + " 可用空间。");
        }
    }
}
