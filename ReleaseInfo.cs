using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

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
        public bool CacheOnly;
        public bool ExplicitUrl;
        public long? DeclaredLength;
        public string Sha256;
        public WindowsUpdateOffer WindowsOffer;

        public bool Matches(ReleaseInfo current)
        {
            if (WindowsOffer != null || (current != null && current.WindowsOffer != null))
                return current != null && WindowsOffer != null && WindowsOffer.SameTask(current.WindowsOffer) && (Version == null || Version == current.Version);
            return current != null && Version == current.Version && Architecture == current.Architecture &&
                (Size <= 0 || current.Size <= 0 || Size == current.Size) &&
                (!current.DeclaredLength.HasValue || Size == current.DeclaredLength.Value) &&
                (current.Sha256 == null || string.Equals(Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase));
        }

        public void VerifyDeclaredContent(string path, CancellationToken token)
        {
            if (DeclaredLength.HasValue && new FileInfo(path).Length != DeclaredLength.Value)
                throw new InvalidDataException("安装包长度与官方清单不符。");
            if (Sha256 == null) return;
            token.ThrowIfCancellationRequested();
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var hash = SHA256.Create())
            {
                var buffer = new byte[256 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                hash.TransformFinalBlock(buffer, 0, 0);
                if (!string.Equals(BitConverter.ToString(hash.Hash).Replace("-", ""), Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包 SHA-256 与官方清单不符。");
            }
        }

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
