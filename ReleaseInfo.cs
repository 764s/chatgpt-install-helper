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

        public void RequireVersion(Version actual, string source)
        {
            if (Version == null || actual == null || actual != Version)
                throw new InvalidDataException(source + "的版本与官方更新清单不一致（目标 " + Version + "，实际 " + actual + "），已停止，未使用旧包替代。");
        }
    }
}
