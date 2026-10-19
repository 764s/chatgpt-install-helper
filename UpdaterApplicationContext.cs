using System;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal sealed class UpdaterApplicationContext : ApplicationContext
    {
        private readonly LocalUpdateServer server;
        private readonly Timer idleTimer;
        private readonly MainForm form;

        public UpdaterApplicationContext(bool background, InstallerStartup startup = null)
        {
            form = new MainForm(background, startup);
            // 不赋给 ApplicationContext.MainForm，避免消息循环强制显示后台窗口。
            form.FormClosed += delegate { ExitThread(); };
            IntPtr handle = form.Handle;
            server = new LocalUpdateServer(form);
            idleTimer = new Timer { Interval = 15000 };
            idleTimer.Tick += delegate { if (form.CanExitBackground()) form.ExitBackground(); };
            idleTimer.Start();
            if (!background) form.Show();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { idleTimer.Dispose(); server.Dispose(); form.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
