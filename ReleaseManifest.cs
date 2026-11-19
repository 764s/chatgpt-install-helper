using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace ChatGPTInstaller
{
    [DataContract]
    internal sealed class ReleaseManifest
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "buildVersion", IsRequired = true)]
        public string BuildVersion { get; set; }

        [DataMember(Name = "storeProductId", IsRequired = true)]
        public string StoreProductId { get; set; }

        [DataMember(Name = "packageIdentity", IsRequired = true)]
        public string PackageIdentity { get; set; }

        [DataMember(Name = "packagePublisher")] public string PackagePublisher { get; set; }
        [DataMember(Name = "packageUrl")] public string PackageUrl { get; set; }
        [DataMember(Name = "length")] public long? Length { get; set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; set; }
        [DataMember(Name = "packages")] public ManifestPackage[] Packages { get; set; }

        public static ReleaseInfo Parse(byte[] json, string architecture)
        {
            if (architecture != "x64" && architecture != "arm64")
                throw new InvalidDataException("不支持此处理器架构。");
            ReleaseManifest manifest;
            try
            {
                using (var input = new MemoryStream(json))
                    manifest = (ReleaseManifest)new DataContractJsonSerializer(typeof(ReleaseManifest)).ReadObject(input);
            }
            catch (Exception ex)
            {
                if (!(ex is SerializationException) && !(ex is XmlException)) throw;
                throw new InvalidDataException("官方更新清单无法解析，无法确认候选范围。请稍后重试或更新安装助手。", ex);
            }
            if (manifest == null || manifest.SchemaVersion != 1)
                throw new InvalidDataException("官方更新清单格式已变化，需要更新安装助手；尚未选择安装包。");
            if (manifest.StoreProductId != AppInfo.StoreId || manifest.PackageIdentity != AppInfo.PackageName ||
                (manifest.PackagePublisher != null && manifest.PackagePublisher != AppInfo.Publisher))
                throw new InvalidDataException("官方更新清单中的产品身份不匹配，已停止检查与安装。");
            Version version;
            if (!TryVersion(manifest.BuildVersion, out version))
                throw new InvalidDataException("官方更新清单未提供有效的 Windows 包版本，无法确认最新版。");

            var release = ReleaseInfo.ForVersion(version, architecture);
            string url = manifest.PackageUrl;
            release.DeclaredLength = manifest.Length;
            release.Sha256 = manifest.Sha256;
            if (manifest.Packages != null)
            {
                var matches = manifest.Packages.Where(p => p != null && p.Architecture == architecture).ToArray();
                if (matches.Length == 0)
                {
                    // 清单明确按架构列包，没有本架构时只保留公告版本。
                    release.PackageUrl = null;
                    release.Sha256 = null;
                    release.DeclaredLength = null;
                    return release;
                }
                if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].PackageUrl) || string.IsNullOrWhiteSpace(matches[0].Sha256))
                    throw new InvalidDataException("无法确定本架构的官方直连安装包。");
                url = matches[0].PackageUrl;
                release.DeclaredLength = matches[0].Length;
                release.Sha256 = matches[0].Sha256;
            }
            if (!ValidIntegrity(release.DeclaredLength, release.Sha256)) throw new InvalidDataException("官方直连包的长度或 SHA-256 无效。");
            if (url != null)
            {
                Uri explicitUrl;
                if (!Uri.TryCreate(url, UriKind.Absolute, out explicitUrl) || !ReleaseFeed.IsPackageUrl(explicitUrl))
                    throw new InvalidDataException("官方直连包地址不在允许的产品路径内。");
                release.PackageUrl = explicitUrl;
                release.ExplicitUrl = true;
            }
            return release;
        }

        internal static bool ValidIntegrity(long? length, string sha256)
        {
            return (!length.HasValue || (length > 0 && length <= 16L * 1024 * 1024 * 1024)) &&
                (sha256 == null || Regex.IsMatch(sha256, @"\A[0-9a-fA-F]{64}\z"));
        }

        public static bool TryVersion(string text, out Version version)
        {
            return Version.TryParse(text, out version) && version.Revision >= 0 &&
                version.Major >= 1 && version.Major <= 65535 && version.Minor <= 65535 &&
                version.Build <= 65535 && version.Revision <= 65535 && version.ToString() == text;
        }
    }
}
