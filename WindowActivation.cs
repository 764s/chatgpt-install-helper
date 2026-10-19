using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal static class WindowActivation
    {
        public static readonly uint Message = RegisterWindowMessage("ChatGPTInstallHelper.ShowWindow.v1");

        public static bool ShowExisting(int processId)
        {
            IntPtr window = IntPtr.Zero;
            // 管道确定进程归属，窗口标题只筛选主窗体；不受旧版标题中的版本号影响。
            EnumWindows(delegate(IntPtr candidate, IntPtr unused)
            {
                uint owner;
                GetWindowThreadProcessId(candidate, out owner);
                if (owner != processId || GetWindow(candidate, 4) != IntPtr.Zero) return true;
                var title = new StringBuilder(256);
                GetWindowText(candidate, title, title.Capacity);
                if (!title.ToString().StartsWith("ChatGPT 安装助手 ", StringComparison.Ordinal)) return true;
                window = candidate;
                return false;
            }, IntPtr.Zero);
            if (window == IntPtr.Zero) return false;
            // 兼容旧后台：托管 Visible 可能为 true，但首次 Show 被启动参数中的 SW_HIDE 覆盖。
            ShowWindowAsync(window, IsIconic(window) ? 9 : 5);
            for (int attempt = 0; attempt < 25 && (!IsWindowVisible(window) || IsIconic(window)); attempt++) Thread.Sleep(20);
            bool visible = IsWindowVisible(window) && !IsIconic(window);
            if (visible) SetForegroundWindow(window);
            return visible;
        }

        public static void AllowForeground(int processId)
        {
            if (processId > 0) AllowSetForegroundWindow((uint)processId);
        }

        public static void Show(Form form)
        {
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            // 再显式显示一次原生窗口，消除隐藏启动标志对第一次 Show 的覆盖。
            ShowWindow(form.Handle, 5);
            form.Activate();
            SetForegroundWindow(form.Handle);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);

        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint processId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
    }
}
