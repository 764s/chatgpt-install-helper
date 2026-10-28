using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：获取失败保护中的官方只读状态核对方，向 MainForm 返回核对结论。
    /// 示例：安装包下载失败后，调用当前 Codex 的 check_app_update，区分已准备更新与未知状态。
    /// 职责：沿 Codex 注入的本机工具通道发现并调用固定的只读检查工具，严格解析结果。
    /// 约束：不调用下载、安装或重启，不扫描日志推断成功，不保存会话上下文。
    /// </summary>
    internal static class OfficialUpdateCheck
    {
        private const int MaximumFrameBytes = 1024 * 1024;

        public static OfficialUpdateResult Check(OfficialUpdateContext context, UpdateRetrievalStage stage, CancellationToken token)
        {
            if (context == null)
                return OfficialUpdateResult.Unknown("当前没有 Codex 提供的官方检查连接，未能交叉核对。可从 Codex 插件页面重试；原失败原因仍保留。");
            token.ThrowIfCancellationRequested();
            using (var pipe = new NamedPipeClientStream(".", context.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (token.Register(pipe.Dispose))
            {
                pipe.Connect(1500);
                var catalog = Request(pipe, 1, "tools/list", new { threadStartKind = "all" }, token) as Dictionary<string, object>;
                var tools = JsonData.Get(catalog, "tools") as IEnumerable;
                Dictionary<string, object> selected = null;
                if (tools != null)
                {
                    foreach (var item in tools)
                    {
                        var tool = item as Dictionary<string, object>;
                        if (JsonData.Get(tool, "name") as string != "check_app_update") continue;
                        if (selected != null) throw new InvalidDataException("官方只读检查工具存在重复定义。");
                        selected = tool;
                    }
                }
                string toolNamespace = JsonData.Get(selected, "namespace") as string;
                if (string.IsNullOrWhiteSpace(toolNamespace))
                    return OfficialUpdateResult.Unknown("当前 Codex 没有提供可调用的只读更新检查工具，未能交叉核对。");
                var annotations = JsonData.Get(selected, "annotations") as Dictionary<string, object>;
                if (object.Equals(JsonData.Get(annotations, "destructiveHint"), true))
                    return OfficialUpdateResult.Unknown("官方检查工具的行为声明已变化，已停止调用，未执行任何更新操作。");
                var arguments = new Dictionary<string, object>
                {
                    { "arguments", new Dictionary<string, object>() },
                    { "callerSource", "codex" },
                    { "callId", "updater-guard-" + Guid.NewGuid().ToString("N") },
                    { "namespace", toolNamespace },
                    { "threadId", context.ThreadId },
                    { "tool", "check_app_update" },
                    { "turnId", "mcp-turn-guard-" + Guid.NewGuid().ToString("N") }
                };
                if (!string.IsNullOrWhiteSpace(context.HostId)) arguments.Add("hostId", context.HostId);
                var reply = Request(pipe, 2, "tools/call", arguments, token) as Dictionary<string, object>;
                return ParseResult(reply, stage);
            }
        }

        internal static OfficialUpdateResult ParseResult(Dictionary<string, object> reply, UpdateRetrievalStage stage)
        {
            if (!object.Equals(JsonData.Get(reply, "success"), true))
                return OfficialUpdateResult.Unknown("官方只读更新检查未成功，本次不能确认更新途径是否变化。");
            var content = JsonData.Get(reply, "contentItems") as IEnumerable;
            string reportedStatus = null;
            if (content != null)
            {
                foreach (var value in content)
                {
                    var item = value as Dictionary<string, object>;
                    if (JsonData.Get(item, "type") as string != "inputText") continue;
                    string text = JsonData.Get(item, "text") as string;
                    if (string.IsNullOrWhiteSpace(text) || text.Length > 16 * 1024) continue;
                    Dictionary<string, object> state;
                    try { state = JsonData.Parse(text); }
                    catch (ArgumentException) { continue; }
                    catch (InvalidOperationException) { continue; }
                    string status = JsonData.Get(state, "status") as string;
                    if (status == null) continue;
                    string channel = JsonData.Get(state, "installedReleaseChannel") as string;
                    if (!string.IsNullOrWhiteSpace(channel) && channel != "prod")
                        return OfficialUpdateResult.Unknown("官方检查来自其他发布通道，不能据此判断当前生产通道的更新途径变化。", "other_channel");
                    if (reportedStatus != null) return OfficialUpdateResult.Unknown("官方检查返回多个状态，本次未确认更新途径变化。");
                    reportedStatus = status;
                }
            }
            if (reportedStatus != null) return OfficialUpdateResult.FromStatus(reportedStatus, stage);
            return OfficialUpdateResult.Unknown("官方检查返回了无法识别的结果，本次未确认更新途径变化。");
        }

        private static object Request(NamedPipeClientStream pipe, int id, string method, object parameters, CancellationToken token)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonData.Serialize(new { jsonrpc = "2.0", id = id, method = method, @params = parameters }));
            byte[] header = BitConverter.GetBytes(data.Length);
            pipe.WriteAsync(header, 0, header.Length, token).GetAwaiter().GetResult();
            pipe.WriteAsync(data, 0, data.Length, token).GetAwaiter().GetResult();
            ReadExactly(pipe, header, token);
            int length = BitConverter.ToInt32(header, 0);
            if (length < 1 || length > MaximumFrameBytes) throw new InvalidDataException("官方工具返回的数据长度无效。");
            var body = new byte[length];
            ReadExactly(pipe, body, token);
            var reply = JsonData.Parse(new UTF8Encoding(false, true).GetString(body));
            if (JsonData.Get(reply, "jsonrpc") as string != "2.0" || Convert.ToString(JsonData.Get(reply, "id"), CultureInfo.InvariantCulture) != id.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("官方工具返回了不匹配的响应。");
            if (JsonData.Get(reply, "error") != null) throw new IOException("官方只读更新检查返回错误。");
            return JsonData.Get(reply, "result");
        }

        private static void ReadExactly(Stream stream, byte[] buffer, CancellationToken token)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = stream.ReadAsync(buffer, offset, buffer.Length - offset, token).GetAwaiter().GetResult();
                if (count == 0) throw new EndOfStreamException("官方检查连接已关闭。");
                offset += count;
            }
        }
    }
}
