using System;

namespace ChatGPTInstaller
{
    internal sealed class ReleaseSelection
    {
        public Version LatestVersion;
        public ReleaseInfo Package;
        public bool LatestPackageMissing;
        public string SourceNotice;
        public string VersionLabel = "官方最新版";

        public Version AvailableVersion { get { return Package == null ? null : Package.Version; } }
        public bool IsFallback { get { return AvailableVersion != null && AvailableVersion < LatestVersion; } }
        public string CandidateNotice
        {
            get { return LatestPackageMissing ? "按官方离线清单、通用入口及本机记录检查，可能不包含全部历史版本。" : null; }
        }
        public string Notice
        {
            get
            {
                if (!LatestPackageMissing) return SourceNotice;
                return (string.IsNullOrEmpty(SourceNotice) ? "" : SourceNotice + " ") + VersionLabel + " " + LatestVersion + " 未找到独立安装包。" +
                    (Package == null ? "暂未找到可用的更新包。" : "本次找到的最新可安装版本为 " + Package.Version + "。");
            }
        }

        public bool IsCurrent(Version installed)
        {
            return installed != null && (installed >= LatestVersion || (AvailableVersion != null && installed >= AvailableVersion));
        }

        public bool CanUpdate(Version installed)
        {
            return AvailableVersion != null && (installed == null || AvailableVersion > installed);
        }

        public string Summary(Version installed)
        {
            if (installed != null && installed > LatestVersion) return "本机版本高于本次查到的官方发布版本，无需降级";
            if (installed == LatestVersion) return VersionLabel == "离线发布最新版" ? "已是官方离线发布最新版" : "已是官方生产渠道最新版";
            if (IsCurrent(installed)) return "已更新到本次找到的最新可安装版本";
            if (Package == null) return installed == null ? "暂未找到独立安装包" : "有新版，未找到独立安装包";
            if (IsFallback) return "较新的独立安装包暂未找到，可更新到 " + Package.Version;
            return installed == null ? "尚未安装新版，可安装 " + Package.Version : "有更新：" + installed + " → " + Package.Version;
        }
    }
}
