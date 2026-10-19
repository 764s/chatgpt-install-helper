using System;
using System.Security.Cryptography;
using System.Text;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：侧栏页面正文及资源身份的提供方。
    /// 示例：SidebarMcpServer 的工具声明和资源读取共用它生成的地址。
    /// 职责：填入当前助手版本，并按最终 HTML 内容生成稳定的缓存标识。
    /// 约束：页面内容变化时地址自动变化，避免宿主持久缓存旧界面。
    /// </summary>
    internal static class SidebarUiResource
    {
        public static readonly string Html = EmbeddedFiles.Read("launcher.html").Replace("__HELPER_VERSION__", SidebarIntegration.VersionString);
        public static readonly string Uri = CreateUri(Html);

        private static string CreateUri(string html)
        {
            using (var hash = SHA256.Create())
                return "ui://chatgpt-updater/launcher-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(html))).Replace("-", "").ToLowerInvariant() + ".html";
        }
    }
}
