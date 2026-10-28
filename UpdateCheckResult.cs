using System;
using System.Collections.Generic;

namespace ChatGPTInstaller
{
    internal sealed class UpdateCheckResult
    {
        public Version InstalledVersion;
        public ReleaseSelection Selection;
        public Version LatestVersion { get { return Selection == null ? null : Selection.LatestVersion; } }
        public Version AvailableVersion { get { return Selection == null ? null : Selection.AvailableVersion; } }
        public string LocalError;
        public string NotesError;
        public bool NotesFetched;
        public string Report;
        public PackageCacheInfo Cache;
        public readonly List<ReleaseNote> Notes = new List<ReleaseNote>();
        public bool? UpdateAvailable
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalError) || LatestVersion == null || InstalledVersion == null) return null;
                return LatestVersion > InstalledVersion;
            }
        }
        public string Summary
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalError)) return "本机版本读取失败，无法判断是否有更新";
                if (LatestVersion == null) return "官方版本号不可用，暂时无法判断";
                return Selection.Summary(InstalledVersion);
            }
        }
    }
}
