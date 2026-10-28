using System;

namespace ChatGPTInstaller
{
    internal static class AppInfo
    {
        public const string ProductName = "ChatGPT 安装助手 1.6.10";
        public const string PackageName = "OpenAI.Codex";
        public const string Publisher = "CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B";
        public const string FamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
        public const string StoreId = "9PLM9XGG6VKS";
        public const string UpdateManifest = "https://persistent.oaistatic.com/codex-app-prod/windows-store-update.json";
        public const string ReleaseFeed = "https://persistent.oaistatic.com/codex-app-prod/windows-release.json";
        public const string Changelog = "https://learn.chatgpt.com/docs/changelog";
        public const string DownloadRoot = "https://persistent.oaistatic.com/codex-app-prod/";

        public static bool IsOfficialDownload(Uri uri)
        {
            return uri != null && uri.Scheme == "https" && uri.Port == 443 &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                string.Equals(uri.Host, "persistent.oaistatic.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
