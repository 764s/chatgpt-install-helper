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
        public long? OfficialTransferBytes;
        public readonly List<ReleaseNote> Notes = new List<ReleaseNote>();
        public PreparedInstallation ReconcilePrepared(PreparedInstallation previous)
        {
            if (LocalError != null || Selection == null || Selection.Package == null) return null;
            var target = Selection.Package;
            if (previous != null) previous.RestoreVerifiedStage(target);
            if (Selection.UsesWindows)
                return target.WindowsOffer.Downloaded && Selection.CanUpdate(InstalledVersion) ? new PreparedInstallation(target, null, null, true) : null;
            if (previous == null || !previous.Release.Matches(target)) return null;
            if (InstalledVersion != null && InstallerWorkflow.ShouldSkip(new InstalledPackage { Version = InstalledVersion }, previous.Release.Version, previous.UpdateOnly)) return null;
            return previous;
        }
        public bool? UpdateAvailable
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalError) || InstalledVersion == null) return null;
                if (Selection != null && Selection.UsesWindows) return Selection.CanUpdate(InstalledVersion);
                if (LatestVersion == null) return null;
                return LatestVersion > InstalledVersion || Selection.ExternalUpdateTitle(InstalledVersion) != null;
            }
        }
        public string Summary
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalError)) return "本机版本读取失败，无法判断是否有更新";
                if (Selection != null && Selection.UsesWindows) return Selection.Summary(InstalledVersion);
                if (LatestVersion == null) return "官方版本号不可用，暂时无法判断";
                if (OfficialTransferBytes.HasValue) return "发现 Codex 直连下载，已写入 " + NetworkClient.FormatBytes(OfficialTransferBytes.Value) + "，可等待完成后核验";
                return Selection.Summary(InstalledVersion);
            }
        }
    }
}
