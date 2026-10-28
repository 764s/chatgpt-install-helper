using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：官方离线发布清单的读取方，为 ReleaseResolver 提供明确的包地址。
    /// 示例：官方客户端在 Store 不可用时读取 windows-release.json 的 packages。
    /// 职责：核对清单版本和架构，输出本架构的官方 MSIX 下载目标。
    /// 约束：不从 Store 提示猜测包地址；下载后仍核对包身份、版本和签名。
    /// </summary>
    [DataContract]
    internal sealed class ReleaseFeed
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "packageVersion", IsRequired = true)]
        public string PackageVersion { get; set; }

        [DataMember(Name = "packages", IsRequired = true)]
        public ReleaseFeedPackage[] Packages { get; set; }

        public static ReleaseInfo Parse(byte[] json, string architecture, out Version publishedVersion)
        {
            if (architecture != "x64" && architecture != "arm64") throw new InvalidDataException("不支持此处理器架构。");
            ReleaseFeed feed;
            try
            {
                using (var input = new MemoryStream(json))
                    feed = (ReleaseFeed)new DataContractJsonSerializer(typeof(ReleaseFeed)).ReadObject(input);
            }
            catch (Exception ex)
            {
                if (!(ex is SerializationException) && !(ex is XmlException)) throw;
                throw new InvalidDataException("官方离线发布清单无法解析，请稍后重试或更新安装助手。", ex);
            }
            if (feed == null || feed.SchemaVersion != 1 || feed.Packages == null || feed.Packages.Length == 0)
                throw new InvalidDataException("官方离线发布清单格式已变化，尚未选择安装包。");
            if (!ReleaseManifest.TryVersion(feed.PackageVersion, out publishedVersion))
                throw new InvalidDataException("官方离线发布清单未提供有效的 Windows 包版本。");
            var matches = feed.Packages.Where(p => p != null && (p.Architecture == "x86_64" ? "x64" : p.Architecture) == architecture).ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length != 1) throw new InvalidDataException("官方离线发布清单包含重复的架构条目，无法确定安装包。");
            Uri url;
            if (!Uri.TryCreate(matches[0].DownloadUrl, UriKind.Absolute, out url) || !IsPackageUrl(url))
                throw new InvalidDataException("官方离线发布清单的安装包地址不在允许的官方产品路径内。");
            var release = ReleaseInfo.ForVersion(publishedVersion, architecture);
            release.PackageUrl = url;
            return release;
        }

        internal static bool IsPackageUrl(Uri url)
        {
            return AppInfo.IsOfficialDownload(url) &&
                url.AbsolutePath.StartsWith(new Uri(AppInfo.DownloadRoot).AbsolutePath, StringComparison.Ordinal) &&
                url.AbsolutePath.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(url.Fragment);
        }
    }
}
