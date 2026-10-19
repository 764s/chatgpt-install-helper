using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ChatGPTInstaller
{
    internal sealed class PackageInfo
    {
        public Version Version;
        public string Architecture;
        public long ExpandedSize;
        public string FullName;

        public static PackageInfo Read(string path, string architecture)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("AppxManifest.xml");
                if (entry == null || entry.Length > 4 * 1024 * 1024 || archive.GetEntry("AppxSignature.p7x") == null)
                    throw new InvalidDataException("安装包缺少有效的清单或签名。");
                XDocument document;
                using (var input = entry.Open()) document = ReadXml(input);
                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                var identity = document.Root.Element(ns + "Identity");
                if (identity == null || (string)identity.Attribute("Name") != AppInfo.PackageName ||
                    (string)identity.Attribute("Publisher") != AppInfo.Publisher)
                    throw new InvalidDataException("安装包不是预期的 OpenAI ChatGPT 官方产品。");
                var result = new PackageInfo
                {
                    Version = Version.Parse((string)identity.Attribute("Version")),
                    Architecture = (string)identity.Attribute("ProcessorArchitecture"),
                    ExpandedSize = archive.Entries.Sum(e => e.Length)
                };
                if (result.Architecture != architecture) throw new InvalidDataException("安装包架构不匹配。");
                result.FullName = AppInfo.PackageName + "_" + result.Version + "_" + result.Architecture + "__2p2nqsd0c76g0";
                foreach (var family in document.Descendants(ns + "TargetDeviceFamily"))
                {
                    Version minimum;
                    if (Version.TryParse((string)family.Attribute("MinVersion"), out minimum) && Environment.OSVersion.Version < minimum)
                        throw new InvalidOperationException("当前最新版需要 Windows 内部版本 " + minimum + " 或更高版本。");
                }
                return result;
            }
        }

        public static void CheckLicense(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("许可证文件过大。");
            XDocument document;
            using (var file = File.OpenRead(path)) document = ReadXml(file);
            XNamespace ns = "urn:schemas-microsoft-com:windows:store:licensing:ls";
            if (document.Root.Name != ns + "License" ||
                !document.Descendants(ns + "ProductID").Any(e => e.Value == AppInfo.StoreId) ||
                !document.Descendants(ns + "PFM").Any(e => string.Equals(e.Value, AppInfo.FamilyName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("离线许可证与 ChatGPT 安装包不匹配。");
        }

        private static XDocument ReadXml(Stream stream)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
            using (var reader = XmlReader.Create(stream, settings)) return XDocument.Load(reader);
        }
    }
}
