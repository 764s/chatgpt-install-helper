using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal static class WindowActivation
    {
        public static readonly uint Message = RegisterWindowMessage("ChatGPTInstallHelper.ShowWindow.v1");

        public static bool NotifyExisting()
        {
            // 消息只唤起窗口，不携带安装指令、文件路径或待执行参数。
            IntPtr window = FindWindow(null, AppInfo.ProductName);
            return window != IntPtr.Zero && SendNotifyMessage(window, Message, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Show(Form form)
        {
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            SetForegroundWindow(form.Handle);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string title);

        [DllImport("user32.dll")]
        private static extern bool SendNotifyMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
    }
}
