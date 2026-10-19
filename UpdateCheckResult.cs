using System;
using System.Collections.Generic;

namespace ChatGPTInstaller
{
    internal sealed class UpdateCheckResult
    {
        public Version InstalledVersion;
        public Version LatestVersion;
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
                if (InstalledVersion == null) return "尚未安装新版，可安装官方最新版";
                if (UpdateAvailable == true) return "有更新：" + InstalledVersion + " → " + LatestVersion;
                if (InstalledVersion > LatestVersion) return "本机版本高于官方生产渠道，无需降级";
                return "已是官方生产渠道最新版";
            }
        }
    }
}
