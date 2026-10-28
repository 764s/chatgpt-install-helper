using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
            if (manifest.StoreProductId != AppInfo.StoreId || manifest.PackageIdentity != AppInfo.PackageName)
                throw new InvalidDataException("官方更新清单中的产品身份不匹配，已停止检查与安装。");
            Version version;
            if (!TryVersion(manifest.BuildVersion, out version))
                throw new InvalidDataException("官方更新清单未提供有效的 Windows 包版本，无法确认最新版。");

            return ReleaseInfo.ForVersion(version, architecture);
        }

        public static bool TryVersion(string text, out Version version)
        {
            return Version.TryParse(text, out version) && version.Revision >= 0 &&
                version.Major >= 1 && version.Major <= 65535 && version.Minor <= 65535 &&
                version.Build <= 65535 && version.Revision <= 65535 && version.ToString() == text;
        }
    }
}
