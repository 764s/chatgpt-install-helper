using System;
using System.Threading;

namespace ChatGPTInstaller
{
    internal static class DeploymentRetry
    {
        public static void Run(Action operation, Action<string> notice, Action<Exception> technical)
        {
            Run(operation, notice, technical, Thread.Sleep);
        }

        internal static void Run(Action operation, Action<string> notice, Action<Exception> technical, Action<int> wait)
        {
            try { operation(); }
            catch (Exception ex)
            {
                if (!ErrorMessages.IsApplicationBusy(ex)) throw;
                technical(ex);
                notice("Windows 暂未能关闭 ChatGPT，等待 5 秒后自动重试一次；不会重新下载安装包。");
                wait(5000);
                // 仅对明确的应用占用重试一次；其他错误和再次失败直接交给界面处理。
                operation();
            }
        }
    }
}
