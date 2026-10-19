using System.IO;

namespace ChatGPTInstaller
{
    internal sealed class PreparedInstallation
    {
        public readonly ReleaseInfo Release;
        public readonly string PackagePath;
        public readonly string LicensePath;
        public readonly bool UpdateOnly;

        public PreparedInstallation(ReleaseInfo release, string packagePath, string licensePath, bool updateOnly)
        {
            Release = release;
            PackagePath = packagePath;
            LicensePath = licensePath;
            UpdateOnly = updateOnly;
        }

        public bool FilesExist()
        {
            return File.Exists(PackagePath) && File.Exists(LicensePath);
        }
    }
}
