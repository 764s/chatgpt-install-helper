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
        public bool NotesForStore
        {
            get
            {
                return Selection != null && Selection.HasNewerStoreVersion(InstalledVersion) && (AvailableVersion ?? LatestVersion) <= InstalledVersion;
            }
        }
        public Version NotesTarget { get { return NotesForStore ? Selection.StoreVersion : AvailableVersion ?? LatestVersion; } }
        public bool NeedsNotes
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalError) || NotesTarget == null) return false;
                return InstalledVersion == null || ChangelogParser.Batch(NotesTarget) > ChangelogParser.Batch(InstalledVersion);
            }
        }
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
                return LatestVersion > InstalledVersion || Selection.ExternalUpdateTitle(InstalledVersion) != null;
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
