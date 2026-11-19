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

        public bool HasPreparedInput()
        {
            if (Release.WindowsOffer != null) return Release.WindowsOffer.Downloaded;
            return File.Exists(PackagePath) && File.Exists(LicensePath);
        }

        public void RestoreVerifiedStage(ReleaseInfo current)
        {
            if (Release.Version == null || Release.WindowsOffer == null || current == null || current.Version != null ||
                !Release.WindowsOffer.SameTask(current.WindowsOffer)) return;
            if (WindowsStagedPackage.Find(Release.Version, Release.Architecture) != null)
            {
                current.WindowsOffer.StagedVersion = Release.Version;
                current.Version = Release.Version;
            }
        }
    }
}
