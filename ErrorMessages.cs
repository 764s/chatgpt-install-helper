using System;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace ChatGPTInstaller
{
    internal static class ErrorMessages
    {
        public static bool IsApplicationBusy(Exception error)
        {
            string text = error.ToString().ToUpperInvariant();
            // 800700E9 也可能出现在其他错误中，必须同时确认是本应用的占用提示。
            return text.Contains("80073D02") || (text.Contains("800700E9") && text.Contains(AppInfo.FamilyName.ToUpperInvariant()));
        }

        public static string Explain(Exception error)
        {
            var native = error as Win32Exception;
            if (native != null && native.NativeErrorCode == 1223) return "已取消 Windows 管理员授权。下载包已保留，可再次点击继续。";
            string text = error.ToString().ToUpperInvariant();
            if (IsApplicationBusy(error)) return "ChatGPT / Codex 仍被占用，Windows 未能完成关闭。请结束任务、保存工作并完全退出应用（包括托盘），再点击重试；安装包已保留。";
            if (text.Contains("80073CF3")) return "Windows 报告依赖或包冲突。可复制下方日志中的具体依赖名称，供公司 IT 检查。";
            if (text.Contains("80073D01") || text.Contains("800704EC") || text.Contains("ACCESS IS DENIED") || text.Contains("拒绝访问"))
                return "Windows 或公司策略拒绝了安装。需要管理员允许应用部署；本工具不会更改公司策略。";
            if (text.Contains("80070070")) return "磁盘剩余空间不足，请清理系统盘后重试。";
            if (text.Contains("80073CFD")) return "当前 Windows 版本不满足最新版 ChatGPT 的要求。";
            if (text.Contains("80073D06")) return "系统已经存在更高版本，已停止安装以避免降级。";
            if (text.Contains("80073CF9")) return "Windows 未能完成安装（0x80073CF9）。具体原因已保留，可通过“打开完整日志”或“复制完整日志”查看。";
            string message = Regex.Replace(error.Message ?? "操作未完成，请查看日志。", @"^(?:[\w.]+Exception:\s*)+", "");
            int newline = message.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0) message = message.Substring(0, newline);
            return message.Length > 240 ? message.Substring(0, 240) + "…完整信息请查看日志。" : message;
        }
    }
}
