using System;

namespace ChatGPTInstaller
{
    internal sealed class WindowsUpdateOffer
    {
        public Guid Id;
        public Guid CategoryId;
        public int Revision;
        public string UserSid;
        public Version Baseline;
        public Version StagedVersion;
        public bool Downloaded;
        public long MaxBytes;

        public bool SameTask(WindowsUpdateOffer other)
        {
            return other != null && Id == other.Id && Revision == other.Revision && UserSid == other.UserSid && Baseline == other.Baseline;
        }
    }
}
