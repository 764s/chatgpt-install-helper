using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：全局侧栏页面与本机安装助手之间的 MCP 执行方。
    /// 示例：更新器页面读取进度或请求下载，同一 MainForm 流程执行实际工作。
    /// 职责：提供页面、状态及固定操作，将请求交给本机唯一的助手进程。
    /// 约束：写操作只面向页面；不接受路径、命令或下载地址。
    /// </summary>
    internal sealed class SidebarMcpServer
    {
        private static readonly string ResourceUri = SidebarUiResource.Uri;
        private const int MaximumMessageLength = 1024 * 1024;
        private bool initialized;

        public static int Run()
        {
            using (var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true)))
            using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true })
                return new SidebarMcpServer().Serve(input, output);
        }

        private int Serve(TextReader input, TextWriter output)
        {
            while (true)
            {
                string line;
                try { line = ReadLine(input); }
                catch (InvalidDataException) { output.WriteLine(JsonData.Serialize(Error(null, -32700, "Request exceeds the message limit."))); return 1; }
                if (line == null) return 0;
                if (string.IsNullOrWhiteSpace(line)) continue;
                object response = Handle(line);
                if (response != null) output.WriteLine(JsonData.Serialize(response));
            }
        }

        private object Handle(string line)
        {
            Dictionary<string, object> request;
            try { request = JsonData.Parse(line); }
            catch (ArgumentException) { return Error(null, -32700, "Invalid JSON."); }
            catch (InvalidOperationException) { return Error(null, -32700, "Invalid JSON."); }
            if (request == null || JsonData.Get(request, "jsonrpc") as string != "2.0") return Error(null, -32600, "Invalid JSON-RPC request.");
            object id = JsonData.Get(request, "id");
            string method = JsonData.Get(request, "method") as string;
            if (method == null) return Error(id, -32600, "Missing method.");
            if (!request.ContainsKey("id")) return null;
            var parameters = JsonData.Get(request, "params") as Dictionary<string, object>;
            if (method == "initialize")
            {
                initialized = true;
                string requested = JsonData.Get(parameters, "protocolVersion") as string;
                string protocol = requested == "2024-11-05" || requested == "2025-03-26" || requested == "2025-11-25" ? requested : "2025-06-18";
                return Result(id, new
                {
                    protocolVersion = protocol,
                    capabilities = new { tools = new { listChanged = false }, resources = new { subscribe = false, listChanged = false } },
                    serverInfo = new { name = "chatgpt-updater", title = "ChatGPT 更新器", version = SidebarIntegration.VersionString, icons = Icons() },
                    instructions = "在侧栏直接查看版本与进度。下载、应用和自动安装选项仅由用户在页面中操作。"
                });
            }
            if (method == "ping") return Result(id, new { });
            if (!initialized) return Error(id, -32002, "Initialize the connection first.");
            switch (method)
            {
                case "tools/list": return Result(id, new { tools = Tools() });
                case "resources/list": return Result(id, new { resources = new[] { new { uri = ResourceUri, name = "ChatGPT 更新器", mimeType = "text/html;profile=mcp-app" } } });
                case "resources/templates/list": return Result(id, new { resourceTemplates = new object[0] });
                case "resources/read":
                    if (JsonData.Get(parameters, "uri") as string != ResourceUri) return Error(id, -32602, "Unknown resource.");
                    return Result(id, new { contents = new[] { new
                    {
                        uri = ResourceUri, mimeType = "text/html;profile=mcp-app", text = SidebarUiResource.Html,
                        _meta = new { ui = new { prefersBorder = false, csp = new { connectDomains = new string[0], resourceDomains = new string[0] } } }
                    } } });
                case "tools/call": return CallTool(id, parameters);
                default: return Error(id, -32601, "Method not found.");
            }
        }

        private object CallTool(object id, Dictionary<string, object> parameters)
        {
            string name = JsonData.Get(parameters, "name") as string;
            string command;
            switch (name)
            {
                case "show_updater": case "get_update_state": command = "status"; break;
                case "check_updates": command = "check"; break;
                case "download_update": command = "download"; break;
                case "apply_update": command = "apply"; break;
                case "set_auto_install": command = "auto"; break;
                case "cancel_update": command = "cancel"; break;
                case "open_updater_folder": command = "folder"; break;
                default: return Error(id, -32602, "Unknown tool.");
            }
            object arguments = JsonData.Get(parameters, "arguments");
            var values = arguments as Dictionary<string, object>;
            bool hasChoice = command == "download" || command == "auto";
            if (arguments != null && values == null) return Error(id, -32602, "Arguments must be an object.");
            if (hasChoice ? values == null || values.Count != 1 || !(JsonData.Get(values, "autoInstall") is bool) : values != null && values.Count != 0)
                return Error(id, -32602, "Unexpected tool arguments.");
            try
            {
                if (command == "folder")
                {
                    SidebarIntegration.OpenFolder();
                    command = "status";
                }
                object state = LocalUpdateClient.Request(command, hasChoice && (bool)JsonData.Get(values, "autoInstall"));
                return Result(id, new
                {
                    content = new[] { new { type = "text", text = "更新器状态已刷新。下载完成后默认等待应用；仅勾选自动安装时自动继续。" } },
                    structuredContent = state
                });
            }
            catch (Exception ex)
            {
                return Result(id, new { isError = true, content = new[] { new { type = "text", text = ex.Message } } });
            }
        }

        private static object[] Tools()
        {
            return new object[]
            {
                Tool("show_updater", "ChatGPT 更新器", "显示版本、下载进度和更新操作。", true, false, true),
                Tool("get_update_state", "读取更新进度", "读取助手当前状态，不启动下载或安装。", true),
                Tool("check_updates", "检查官方更新", "重新读取本机与官方生产渠道版本。", true),
                Tool("download_update", "下载更新", "准备官方包。autoInstall 为 true 时，校验完成后立即安装并重启；默认应为 false。", false, true),
                Tool("apply_update", "应用更新并重启", "用户确认后复核准备包、申请 Windows 授权并安装；核对成功后重新打开 ChatGPT。", false),
                Tool("set_auto_install", "设置本次自动安装", "仅更改当前下载的自动安装选择，不检测任务是否结束。", false, true),
                Tool("cancel_update", "取消准备", "取消下载或安装前检查，保留有效缓存。Windows 部署开始后不能取消。", false),
                Tool("open_updater_folder", "打开所在文件夹", "在资源管理器中打开更新器程序所在文件夹。", false)
            };
        }

        private static object Tool(string name, string title, string description, bool readOnly, bool choice = false, bool entry = false)
        {
            var metadata = new Dictionary<string, object> { { "ui", entry ? (object)new { resourceUri = ResourceUri, visibility = new[] { "model", "app" } } : new { visibility = new[] { "app" } } } };
            if (entry) metadata.Add("openai/ui", new { entrypoints = new[] { new { type = "global" } } });
            var properties = new Dictionary<string, object>();
            if (choice) properties.Add("autoInstall", new { type = "boolean" });
            return new
            {
                name = name, title = title, description = description, icons = entry ? Icons() : new object[0],
                inputSchema = new { type = "object", properties = properties, required = choice ? new[] { "autoInstall" } : new string[0], additionalProperties = false },
                outputSchema = new { type = "object", additionalProperties = true },
                annotations = new { readOnlyHint = readOnly, destructiveHint = name == "apply_update" || name == "download_update", idempotentHint = true, openWorldHint = !readOnly },
                _meta = metadata
            };
        }

        private static object[] Icons()
        {
            return new object[] { new { src = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(EmbeddedFiles.Read("icon.svg"))), mimeType = "image/svg+xml" } };
        }

        private static object Result(object id, object result) { return new { jsonrpc = "2.0", id = id, result = result }; }
        private static object Error(object id, int code, string message) { return new { jsonrpc = "2.0", id = id, error = new { code = code, message = message } }; }

        private static string ReadLine(TextReader reader)
        {
            var text = new StringBuilder();
            int value;
            while ((value = reader.Read()) >= 0)
            {
                if (value == '\n') return text.ToString().TrimEnd('\r');
                if (text.Length >= MaximumMessageLength) throw new InvalidDataException();
                text.Append((char)value);
            }
            return text.Length == 0 ? null : text.ToString();
        }
    }
}
