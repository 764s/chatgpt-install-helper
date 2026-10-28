using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：侧栏页面访问本机更新流程的客户端。
    /// 示例：SidebarMcpServer 通过它读取进度或请求准备更新。
    /// 职责：连接唯一的助手进程，仅传递固定操作及自动安装选择。
    /// 约束：通过桌面 Shell 启动独立进程，使其不随 ChatGPT 退出而终止。
    /// </summary>
    internal static class LocalUpdateClient
    {
        internal static string PipeName
        {
            get { return "ChatGPTInstallHelper-v2-" + WindowsIdentity.GetCurrent().User.Value + "-" + Process.GetCurrentProcess().SessionId; }
        }

        public static object Request(string command, bool autoInstall = false, OfficialUpdateContext officialContext = null)
        {
            try { return SendWithContext(command, autoInstall, 250, officialContext); }
            catch (TimeoutException) { }
            using (var gate = new Mutex(false, "Local\\" + PipeName + "-Start"))
            {
                bool acquired;
                try { acquired = gate.WaitOne(15000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException("更新器正在启动，请稍后重试。");
                try
                {
                    try { return SendWithContext(command, autoInstall, 250, officialContext); }
                    catch (TimeoutException) { }
                    StartBackground();
                    return SendWithContext(command, autoInstall, 15000, officialContext);
                }
                finally { gate.ReleaseMutex(); }
            }
        }

        private static object SendWithContext(string command, bool autoInstall, int timeout, OfficialUpdateContext context)
        {
            if (context == null) return Send(command, autoInstall, timeout);
            // 先只读协商，旧后台仍接收原来的两字段请求，不重发已执行的写操作。
            var state = Send("status", false, timeout);
            bool supported = object.Equals(JsonData.Get(state as Dictionary<string, object>, "officialCheckSupported"), true);
            if (command == "status" && !supported) return state;
            return Send(command, autoInstall, timeout, officialContext: supported ? context : null);
        }

        public static bool TryOpenExisting(out IntPtr window)
        {
            window = IntPtr.Zero;
            try
            {
                int serverProcessId = 0;
                Send("open", false, 1000, 1500, processId => { serverProcessId = processId; WindowActivation.AllowForeground(processId); });
                return serverProcessId != 0 && WindowActivation.ShowExisting(serverProcessId, out window);
            }
            catch (TimeoutException) { return false; }
            catch (IOException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static object Send(string command, bool autoInstall, int timeout, int responseTimeout = 15000, Action<int> connected = null, OfficialUpdateContext officialContext = null)
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(timeout);
                if (connected != null)
                {
                    uint processId;
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out processId)) throw new IOException("无法确认正在运行的更新器进程。");
                    connected(checked((int)processId));
                }
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                var request = new Dictionary<string, object> { { "command", command }, { "autoInstall", autoInstall } };
                if (officialContext != null) request.Add("officialContext", officialContext);
                writer.WriteLine(JsonData.Serialize(request));
                var read = reader.ReadLineAsync();
                if (!read.Wait(responseTimeout)) throw new IOException("更新器暂时没有响应，请稍后重试。");
                string line = read.GetAwaiter().GetResult();
                if (line == null || line.Length > 128 * 1024) throw new IOException("更新器返回了无效状态。");
                var reply = JsonData.Parse(line);
                string error = JsonData.Get(reply, "error") as string;
                if (error != null) throw new InvalidOperationException(error);
                return JsonData.Get(reply, "state");
            }
        }

        private static void StartBackground()
        {
            string executable = SidebarIntegration.ExecutablePath();
            // 由 Explorer 创建进程，而非作为 MCP 子进程；安装关闭宿主时仍能核对结果并重启。
            ExplorerLauncher.Start(executable);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    }
}
