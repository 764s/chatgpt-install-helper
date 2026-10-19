using System;

namespace ChatGPTInstaller
{
    internal sealed class InstalledSidebarPlugin
    {
        public Version Version { get; private set; }
        public bool Enabled { get; private set; }

        public InstalledSidebarPlugin(Version version, bool enabled)
        {
            Version = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
            Enabled = enabled;
        }
    }
}
