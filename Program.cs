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
            bool created;
            using (var mutex = new Mutex(true, "Local\\ChatGPTInstallHelper-UI", out created))
            {
                if (!created)
                {
                    if (args.Length == 1 && args[0] == "--background") return 0;
                    if (!WindowActivation.NotifyExisting()) MessageBox.Show("另一个版本的安装助手正在运行或正在启动。请先关闭该窗口，再打开当前版本。", AppInfo.ProductName);
                    return 0;
                }
                try
                {
                    using (var context = new UpdaterApplicationContext(args.Length == 1 && args[0] == "--background")) Application.Run(context);
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
