using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：侧栏请求与唯一 MainForm 更新流程之间的接收方。
    /// 示例：页面下载按钮提交 download，由已有窗口的同一流程执行。
    /// 职责：限制本机同用户访问，并将固定请求调度到窗口线程。
    /// 约束：不接收命令行、路径或安装包地址，不自行维护第二份更新状态。
    /// </summary>
    internal sealed class LocalUpdateServer : IDisposable
    {
        private readonly MainForm form;
        private readonly object gate = new object();
        private bool stopped;
        private NamedPipeServerStream listening;

        public LocalUpdateServer(MainForm form)
        {
            this.form = form;
            new Thread(Serve) { IsBackground = true, Name = "Updater sidebar connection" }.Start();
        }

        private void Serve()
        {
            while (!stopped)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(LocalUpdateClient.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        lock (gate) { if (stopped) return; listening = pipe; }
                        pipe.WaitForConnection();
                        var reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 1024, true);
                        var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                        var read = reader.ReadLineAsync();
                        if (!read.Wait(5000)) continue;
                        string line = read.GetAwaiter().GetResult();
                        if (line == null || line.Length > 4096) continue;
                        object response;
                        try
                        {
                            var request = JsonData.Parse(line);
                            string command = JsonData.Get(request, "command") as string;
                            object choice = JsonData.Get(request, "autoInstall");
                            if (request == null || request.Count != 2 || !(choice is bool)) throw new InvalidDataException("无效的更新器请求。");
                            var completion = new TaskCompletionSource<object>();
                            form.BeginInvoke(new Action(() =>
                            {
                                try { completion.SetResult(form.HandleSidebarCommand(command, (bool)choice)); }
                                catch (Exception ex) { completion.SetException(ex); }
                            }));
                            if (!completion.Task.Wait(10000)) throw new IOException("更新器界面暂时没有响应。");
                            response = new { state = completion.Task.GetAwaiter().GetResult() };
                        }
                        catch (Exception ex) { response = new { error = ex.GetBaseException().Message }; }
                        writer.WriteLine(JsonData.Serialize(response));
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                lock (gate) { listening = null; }
            }
        }

        public void Dispose()
        {
            lock (gate) { stopped = true; if (listening != null) listening.Dispose(); }
        }
    }
}
