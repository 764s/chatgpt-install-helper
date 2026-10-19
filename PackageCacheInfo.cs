using System;

namespace ChatGPTInstaller
{
    internal sealed class PackageCacheInfo
    {
        public Version Version;
        public string State = "none";
        public string Source;
        public string Notice;
        public long Bytes;
        public long Size;
        public bool Complete { get { return State == "complete"; } }
        public string Summary
        {
            get
            {
                if (Complete) return "已下载，待校验 · " + (Source == "official" ? "官方缓存" : "更新器缓存");
                if (State == "partial") return "下载未完成 · " + NetworkClient.FormatBytes(Bytes) + " / " + NetworkClient.FormatBytes(Size);
                return "未下载";
            }
        }
        public string ActionText { get { return Complete ? "校验并准备" : State == "partial" ? "继续下载" : "下载更新"; } }
    }
}
