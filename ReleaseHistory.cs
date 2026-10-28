using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：为 ReleaseResolver 提供本机曾从官方来源读到的版本候选。
    /// 示例：最新版安装包返回 404 时，参与按版本降序的可用性检查。
    /// 职责：合并并保存版本和官方包地址，兼容已有的纯版本记录；网络探测和包验证仍由原流程负责。
    /// 约束：记录不是完整发布历史，也不能凭记录认定安装包仍然可用。
    /// </summary>
    internal static class ReleaseHistory
    {
        private static string FilePath(string architecture)
        {
            if (architecture != "x64" && architecture != "arm64") throw new ArgumentException("不支持此处理器架构。");
            return Path.Combine(Paths.Root, "release-history-" + architecture + ".txt");
        }

        public static List<ReleaseInfo> Read(string architecture, RunLog log)
        {
            List<ReleaseInfo> versions;
            TryRead(architecture, log, out versions);
            return versions;
        }

        private static bool TryRead(string architecture, RunLog log, out List<ReleaseInfo> versions)
        {
            versions = new List<ReleaseInfo>();
            try
            {
                string path = FilePath(architecture);
                if (!File.Exists(path)) return true;
                if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("版本记录超过预期大小。");
                foreach (string line in File.ReadLines(path))
                {
                    string[] fields = line.Split(new[] { '\t' }, 2);
                    Version version;
                    if (!ReleaseManifest.TryVersion(fields[0], out version)) continue;
                    var release = ReleaseInfo.ForVersion(version, architecture);
                    if (fields.Length == 2)
                    {
                        Uri url;
                        if (!Uri.TryCreate(fields[1], UriKind.Absolute, out url) || !ReleaseFeed.IsPackageUrl(url)) continue;
                        release.PackageUrl = url;
                    }
                    versions.Add(release);
                }
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                log.Write("本机版本记录暂不可用，本次仍检查在线官方入口：" + ex.Message);
                return false;
            }
            return true;
        }

        public static void Remember(string architecture, Version version, RunLog log)
        {
            Remember(ReleaseInfo.ForVersion(version, architecture), log);
        }

        public static void Remember(ReleaseInfo release, RunLog log)
        {
            string temporary = null;
            try
            {
                List<ReleaseInfo> versions;
                // 无法完整读取时保留原文件，不用本次单条记录覆盖此前候选。
                if (!TryRead(release.Architecture, log, out versions)) return;
                if (versions.Any(v => v.Version == release.Version && v.PackageUrl == release.PackageUrl)) return;
                versions.Add(release);
                Directory.CreateDirectory(Paths.Root);
                string path = FilePath(release.Architecture);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllLines(temporary, versions.GroupBy(v => v.Version + "\t" + v.PackageUrl.AbsoluteUri).Select(g => g.First())
                    .OrderByDescending(v => v.Version).Select(v => v.Version + "\t" + v.PackageUrl.AbsoluteUri));
                CacheFiles.Publish(temporary, path);
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                log.Write("未能保存本机版本候选，不影响本次在线检查：" + ex.Message);
            }
            finally { if (temporary != null) Paths.DeleteFile(temporary); }
        }

        private static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is SecurityException;
        }
    }
}
