using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ChatGPTInstaller
{
    internal static class WindowsStagedPackage
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int GetStagedPackagePathByFullName(string fullName, ref uint length, StringBuilder path);
        [DllImport("kernelbase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int GetStagedPackageOrigin(string fullName, out int origin);

        public static string Find(Version version, string architecture)
        {
            Version checkedVersion;
            if (version == null || !ReleaseManifest.TryVersion(version.ToString(), out checkedVersion) || (architecture != "x64" && architecture != "arm64")) return null;
            string fullName = AppInfo.PackageName + "_" + version + "_" + architecture + "__2p2nqsd0c76g0";
            try
            {
                int origin;
                if (GetStagedPackageOrigin(fullName, out origin) != 0 || origin != 3) return null; // PackageOrigin_Store
                uint length = 0;
                if (GetStagedPackagePathByFullName(fullName, ref length, null) != 122 || length == 0 || length > 32768) return null;
                var buffer = new StringBuilder((int)length);
                if (GetStagedPackagePathByFullName(fullName, ref length, buffer) != 0) return null;
                string root = buffer.ToString();
                if (!File.Exists(Path.Combine(root, "AppxSignature.p7x"))) return null;
                using (var stream = File.OpenRead(Path.Combine(root, "AppxManifest.xml")))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 }))
                {
                    XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                    var identity = XDocument.Load(reader).Root.Element(ns + "Identity");
                    if (identity == null || (string)identity.Attribute("Name") != AppInfo.PackageName || (string)identity.Attribute("Publisher") != AppInfo.Publisher ||
                        (string)identity.Attribute("Version") != version.ToString() || (string)identity.Attribute("ProcessorArchitecture") != architecture) return null;
                }
                return fullName;
            }
            catch (EntryPointNotFoundException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (XmlException) { return null; }
        }
    }
}
