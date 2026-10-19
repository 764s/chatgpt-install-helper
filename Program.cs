using System;
using System.Net;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--mcp") return SidebarMcpServer.Run();
            if (args.Length == 1 && (args[0] == "--sidebar-install" || args[0] == "--sidebar-remove"))
            {
                using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true })
                {
                    try
                    {
                        Action<string> report = message => output.WriteLine(message);
                        if (args[0] == "--sidebar-install") SidebarIntegration.Install(report);
                        else SidebarIntegration.Remove(report);
                        return 0;
                    }
                    catch (Exception ex) { output.WriteLine(ex.Message); return 1; }
                }
            }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 8;
            if (args.Length == 6 && args[0] == "--deploy")
            {
                try { return DeploymentHelper.Execute(args[1], args[2], args[3], args[4], args[5] == "repair"); }
                catch { return 2; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            InstallerStartup startup = null;
            if (args.Length == 0)
            {
                try
                {
                    if (InstallerStartup.ForwardToSavedCopy()) return 0;
                    startup = new InstallerStartup();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("未能保存或启动独立副本，请保留当前 EXE。\n\n" + ex.Message + "\n\n仍可继续使用独立更新器。", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else if (args.Length == 3 && args[0] == "--startup")
            {
                int launcherId;
                long launcherStartedAt;
                if (!int.TryParse(args[1], out launcherId) || launcherId <= 0 || !long.TryParse(args[2], out launcherStartedAt) || launcherStartedAt <= 0) return 1;
                startup = new InstallerStartup(launcherId, launcherStartedAt);
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\ChatGPTInstallHelper-UI", out created))
            {
                if (!created)
                {
                    if (args.Length == 1 && args[0] == "--background") return 0;
                    bool opened = WindowActivation.NotifyExisting() || LocalUpdateClient.TryOpenExisting();
                    // 已有窗口先立即唤起；入口配置由这个独立进程完成，不阻塞已有下载或部署。
                    if (startup != null)
                    {
                        using (var context = new ApplicationContext())
                        {
                            var log = new RunLog();
                            EventHandler run = null;
                            run = async delegate
                            {
                                Application.Idle -= run;
                                try { log.Write(await startup.CompleteWithNotice(null, log.Write)); }
                                catch (Exception ex) { log.WriteTechnical(ex.ToString()); }
                                finally { context.ExitThread(); }
                            };
                            Application.Idle += run;
                            Application.Run(context);
                        }
                    }
                    if (!opened) MessageBox.Show("安装助手正在运行或正在启动。请稍后再打开窗口。", AppInfo.ProductName);
                    return 0;
                }
                try
                {
                    using (var context = new UpdaterApplicationContext(args.Length == 1 && args[0] == "--background", startup)) Application.Run(context);
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
