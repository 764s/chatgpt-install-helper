using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：将 Codex 提供的本机会话上下文传递给官方只读更新检查。
    /// 示例：SidebarMcpServer 将当前会话上下文交给独立 MainForm，供获取失败后核对。
    /// 职责：限制为本机管道与真实会话标识，不接收工具名称、任意命令或网络地址。
    /// 约束：仅保留在进程内，缺少上下文时不猜测管道或冒用其他会话。
    /// </summary>
    internal sealed class OfficialUpdateContext
    {
        public string PipePath;
        public string ThreadId;
        public string HostId;

        public static OfficialUpdateContext Capture(Dictionary<string, object> metadata = null)
        {
            string thread = null;
            foreach (string key in new[] { "openai/threadId", "openai/thread_id", "codexThreadId", "codex_thread_id", "threadId", "thread_id" })
            {
                thread = JsonData.Get(metadata, key) as string;
                if (!string.IsNullOrWhiteSpace(thread)) break;
            }
            if (string.IsNullOrWhiteSpace(thread))
            {
                object turn = JsonData.Get(metadata, "x-codex-turn-metadata");
                var decoded = turn as Dictionary<string, object>;
                if (turn is string)
                {
                    try { decoded = JsonData.Parse((string)turn); }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                thread = JsonData.Get(decoded, "thread_id") as string;
            }
            if (string.IsNullOrWhiteSpace(thread)) thread = Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
            var context = new OfficialUpdateContext
            {
                PipePath = Environment.GetEnvironmentVariable("CODEX_APP_TOOLS_PIPE_PATH"),
                ThreadId = thread,
                HostId = Environment.GetEnvironmentVariable("CODEX_APP_TOOLS_CALLER_HOST_ID")
            };
            return context.IsValid() ? context : null;
        }

        public static OfficialUpdateContext Parse(object value)
        {
            var data = value as Dictionary<string, object>;
            if (data == null || data.Count != 3) return null;
            var context = new OfficialUpdateContext
            {
                PipePath = JsonData.Get(data, "PipePath") as string,
                ThreadId = JsonData.Get(data, "ThreadId") as string,
                HostId = JsonData.Get(data, "HostId") as string
            };
            return context.IsValid() ? context : null;
        }

        private bool IsValid()
        {
            Guid thread;
            const string prefix = @"\\.\pipe\";
            return PipePath != null && PipePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                PipePath.Length <= 256 && Regex.IsMatch(PipePath.Substring(prefix.Length), @"^[A-Za-z0-9_.-]+$") &&
                Guid.TryParse(ThreadId, out thread) && (string.IsNullOrWhiteSpace(HostId) || HostId == "local");
        }

        internal string PipeName { get { return PipePath.Substring(@"\\.\pipe\".Length); } }
    }
}
