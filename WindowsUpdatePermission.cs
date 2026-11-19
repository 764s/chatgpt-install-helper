using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ChatGPTInstaller
{
    internal static class WindowsUpdatePermission
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);

        public static void Run(WindowsUpdateOffer offer, string arch, bool apply, CancellationToken token, Action<int, string> report)
        {
            try
            {
                var store = new CurrentUserStore();
                if (apply) store.Apply(offer, arch, report);
                else store.Download(offer, token, report);
                return;
            }
            catch (Exception ex)
            {
                if (!AccessDenied(ex)) throw;
                using (var who = WindowsIdentity.GetCurrent())
                    if (new WindowsPrincipal(who).IsInRole(WindowsBuiltInRole.Administrator)) throw;
            }
            token.ThrowIfCancellationRequested();
            report(-1, apply ? "Windows 需要授权以应用已下载的更新。" : "Windows 需要授权以下载此更新。");
            string ticket = Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(offer.UserSid), PipeAccessRights.FullControl, AccessControlType.Allow));
            using (var pipe = new NamedPipeServerStream(PipeName(ticket), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
            {
                var connect = pipe.BeginWaitForConnection(null, null);
                var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                    "--windows-update " + (apply ? "apply" : "download") + " " + offer.Id.ToString("D") + " " + offer.Revision.ToString(CultureInfo.InvariantCulture) +
                    " " + offer.UserSid + " " + offer.Baseline + " " + ticket) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var child = Process.Start(start))
                {
                    using (var waiting = connect.AsyncWaitHandle)
                    {
                        while (!waiting.WaitOne(150))
                        {
                            token.ThrowIfCancellationRequested();
                            if (child.HasExited) throw new InvalidOperationException("Windows 授权助手未能连接。请使用原 Windows 用户授权。");
                        }
                        pipe.EndWaitForConnection(connect);
                    }
                    uint pid;
                    if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid) || pid != child.Id) throw new IOException("Windows 授权助手连接不匹配。");
                    string error = null;
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                    {
                        while (true)
                        {
                            var read = reader.ReadLineAsync();
                            while (!read.Wait(200)) token.ThrowIfCancellationRequested();
                            string line = read.GetAwaiter().GetResult();
                            if (line == null) break;
                            int sep = line.IndexOf('|'), percent;
                            if (line.Length > 65536 || sep < 1 || !int.TryParse(line.Substring(0, sep), out percent)) throw new IOException("更新进度格式不符。");
                            string detail = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(sep + 1)));
                            if (percent == -2) error = detail;
                            else report(percent, detail);
                        }
                    }
                    child.WaitForExit();
                    if (child.ExitCode != 0 || error != null) throw new InvalidOperationException(error ?? "Windows 授权更新未完成。");
                }
            }
        }

        private static bool AccessDenied(Exception ex)
        {
            for (; ex != null; ex = ex.InnerException)
                if (ex.HResult == unchecked((int)0x80070005) || ex is UnauthorizedAccessException ||
                    ex.Message.IndexOf("0x80070005", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string PipeName(string ticket) { return "ChatGPTInstallHelper-WindowsUpdate-" + ticket; }

        public static int Execute(string[] args)
        {
            Guid id, ticket;
            Version baseline;
            int revision;
            if (args.Length != 7 || (args[1] != "apply" && args[1] != "download") || !Guid.TryParseExact(args[2], "D", out id) || id == Guid.Empty ||
                !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision < 1 ||
                !ReleaseManifest.TryVersion(args[5], out baseline) || !Guid.TryParseExact(args[6], "N", out ticket)) return 2;
            bool apply = args[1] == "apply";
            using (var who = WindowsIdentity.GetCurrent())
            {
                if (who.User.Value != args[4] || !new WindowsPrincipal(who).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
            }
            // 提权进程仅输出进度，不接受管道命令，也不向管道服务端授予模拟令牌。
            using (var pipe = new NamedPipeClientStream(".", PipeName(ticket.ToString("N")), PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.Anonymous))
            using (var cancel = new CancellationTokenSource())
            {
                pipe.Connect(5000);
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                {
                    Action<int, string> report = (percent, detail) =>
                    {
                        try { writer.WriteLine(percent + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(detail ?? ""))); }
                        catch (IOException) { if (!apply) cancel.Cancel(); }
                    };
                    try
                    {
                        var expected = new WindowsUpdateOffer { Id = id, Revision = revision, UserSid = args[4], Baseline = baseline };
                        var store = new CurrentUserStore();
                        // 在提权端按原 SID 与产品重新查找这个任务。安装目标由 Windows 返回，不采用调用方提供的路径或包名。
                        if (apply) store.Apply(expected, PlatformInfo.Architecture(), report);
                        else store.Download(expected, cancel.Token, report);
                        return 0;
                    }
                    catch (Exception ex) { report(-2, ex.Message); return 1; }
                }
            }
        }
    }
}
