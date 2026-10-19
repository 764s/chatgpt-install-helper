using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：独立更新进程的桌面启动执行方。
    /// 示例：LocalUpdateClient 启动隐藏助手，使其在 ChatGPT 关闭后继续完成更新。
    /// 职责：取得 Explorer 桌面中的 Shell 对象，由 Explorer 创建助手进程。
    /// 约束：不使用 MCP 进程内新建的 Shell 对象，避免继承宿主的进程生命周期。
    /// </summary>
    internal static class ExplorerLauncher
    {
        public static void Start(string executable)
        {
            object windows = null, desktop = null, browser = null, view = null, folder = null, shell = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true));
                // 桌面始终由主 Explorer 托管，不依赖用户是否打开文件夹窗口。
                object[] arguments = { 0, null, 8, 0, 1 };
                var modifier = new ParameterModifier(5);
                modifier[0] = modifier[1] = modifier[3] = true;
                desktop = windows.GetType().InvokeMember("FindWindowSW", BindingFlags.InvokeMethod, null, windows, arguments, new[] { modifier }, null, null);
                if (desktop == null) throw new InvalidOperationException("未找到 Windows 桌面。请先登录桌面，再打开更新器。");
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), browserId = typeof(ShellBrowser).GUID;
                browser = ((ShellServices)desktop).QueryService(ref service, ref browserId);
                view = ((ShellBrowser)browser).QueryActiveShellView();
                Guid dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                folder = ((ShellView)view).GetItemObject(0, ref dispatch);
                shell = folder.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folder, null);
                shell.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, shell, new object[] { executable, "--background", Path.GetDirectoryName(executable), "open", 0 });
            }
            finally
            {
                foreach (object item in new[] { shell, folder, view, browser, desktop, windows })
                    if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            }
        }

        // 下列私有互操作接口按 Windows SDK 的 vtable 顺序声明，仅调用所需成员。
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ShellServices
        {
            [return: MarshalAs(UnmanagedType.Interface)] object QueryService(ref Guid service, ref Guid interfaceId);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ShellBrowser
        {
            void GetWindow();
            void ContextSensitiveHelp();
            void InsertMenusSB();
            void SetMenuSB();
            void RemoveMenusSB();
            void SetStatusTextSB();
            void EnableModelessSB();
            void TranslateAcceleratorSB();
            void BrowseObject();
            void GetViewStateStream();
            void GetControlWindow();
            void SendControlMsg();
            ShellView QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ShellView
        {
            void GetWindow();
            void ContextSensitiveHelp();
            void TranslateAccelerator();
            void EnableModeless();
            void UIActivate();
            void Refresh();
            void CreateViewWindow();
            void DestroyViewWindow();
            void GetCurrentInfo();
            void AddPropertySheetPages();
            void SaveViewState();
            void SelectItem();
            [return: MarshalAs(UnmanagedType.Interface)] object GetItemObject(uint aspect, ref Guid interfaceId);
        }
    }
}
