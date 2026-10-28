using System;
using System.IO;

namespace ChatGPTInstaller
{
    internal sealed class ReleaseInfo
    {
        public Uri PackageUrl;
        public Uri LicenseUrl;
        public Version Version;
        public string ETag;
        public long Size;
        public string Architecture;

        public static ReleaseInfo ForVersion(Version version, string architecture)
        {
            return new ReleaseInfo
            {
                Version = version, Architecture = architecture,
                PackageUrl = new Uri(AppInfo.DownloadRoot + "releases/" + version + "/ChatGPT-" + architecture + ".msix"),
                LicenseUrl = new Uri(AppInfo.DownloadRoot + "ChatGPT-License.xml")
            };
        }

        public void RequireVersion(Version actual, string source)
        {
            if (Version == null || actual == null || actual != Version)
                throw new InvalidDataException(source + "的版本与本次选定的官方安装目标不一致（目标 " + Version + "，实际 " + actual + "），已停止。");
        }
    }
}
