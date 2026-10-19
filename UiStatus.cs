namespace ChatGPTInstaller
{
    internal sealed class UiStatus
    {
        public string Phase;
        public string Detail;
        public int Percent;
        public string CurrentVersion;
        public string LatestVersion;
        public PackageCacheInfo Cache;
        public bool Deployment;
        public bool Finished;

        public UiStatus(string phase, string detail, int percent)
        {
            Phase = phase;
            Detail = detail;
            Percent = percent;
        }
    }
}
