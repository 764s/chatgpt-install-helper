using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace ChatGPTInstaller
{
    internal sealed class CurrentUserStore
    {
        // 已注册的 Windows Store (Prod) 服务。默认 SearchScope 只查整机，会漏掉当前用户的应用更新。
        private const string ServiceId = "855e8a7c-ecb4-4ca3-b045-1dfa50104289";
        private const string Product = AppInfo.StoreId + "-" + AppInfo.PackageName;
        private readonly dynamic session;
        private readonly string sid;

        public CurrentUserStore()
        {
            using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
            session = Create("Microsoft.Update.Session");
            session.ClientApplicationID = "ChatGPT Install Helper";
        }

        private static object Create(string registeredClass)
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID(registeredClass, true));
        }

        public WindowsUpdateOffer Find(Version baseline, Version advertised, string arch, bool online, CancellationToken token)
        {
            object root = Search(online, token);
            return root == null ? null : Describe(root, baseline, advertised, arch);
        }

        private object Search(bool online, CancellationToken token, Guid categoryId = default(Guid))
        {
            token.ThrowIfCancellationRequested();
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = 3; // ssOthers
            searcher.ServiceID = ServiceId;
            searcher.SearchScope = 2; // searchScopeCurrentUserOnly
            searcher.IncludePotentiallySupersededUpdates = false;
            searcher.Online = online;
            var callback = new UpdateJobCallback();
            string criteria = "IsInstalled=0 and IsHidden=0 and Type='Software'";
            if (categoryId != Guid.Empty) criteria += " and CategoryIDs contains '" + categoryId.ToString("D") + "'";
            dynamic job = searcher.BeginSearch(criteria, callback, null);
            try
            {
                var clock = Stopwatch.StartNew();
                while (!(bool)job.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    if (clock.ElapsedMilliseconds > (online ? 30000 : 8000)) throw new TimeoutException("读取当前用户的官方更新任务超时。");
                    token.WaitHandle.WaitOne(100);
                }
                token.ThrowIfCancellationRequested();
                dynamic result = searcher.EndSearch(job);
                if ((int)result.ResultCode != 2) throw new IOException("Windows 未完整返回当前用户的更新列表。");
                object match = null;
                dynamic updates = result.Updates;
                for (int i = 0; i < (int)updates.Count; i++)
                {
                    dynamic update = updates.Item(i);
                    if ((string)update.Title != Product || (int)update.Type != 1 || (int)update.DeploymentAction != 1 || (bool)update.IsInstalled || (bool)update.IsHidden) continue;
                    Guid actualCategory = ApplicationCategory((object)update);
                    if (actualCategory == Guid.Empty || (categoryId != Guid.Empty && actualCategory != categoryId)) continue;
                    if (match != null) throw new InvalidDataException("Windows 返回多个适用的 ChatGPT 更新，未选定安装目标。");
                    match = (object)update;
                }
                return match;
            }
            finally { FinishJob((object)job, callback); }
        }

        private static Guid ApplicationCategory(object root)
        {
            dynamic update = root;
            dynamic categories = update.Categories;
            for (int i = 0; i < (int)categories.Count; i++)
            {
                dynamic item = categories.Item(i);
                if ((string)item.Type == "Application" && (string)item.Name == Product)
                    return Guid.Parse((string)item.CategoryID);
            }
            return Guid.Empty;
        }

        private WindowsUpdateOffer Describe(object root, Version baseline, Version advertised, string arch)
        {
            dynamic update = root;
            if (!(bool)update.EulaAccepted) throw new InvalidOperationException("本机官方任务的许可条款尚未获得同意。");
            Guid id = Guid.Parse((string)update.Identity.UpdateID);
            int revision = (int)update.Identity.RevisionNumber;
            if (id == Guid.Empty || revision < 1) throw new InvalidDataException("Windows 返回的 ChatGPT 更新标识无效。");
            bool staged = baseline != null && advertised != null && advertised > baseline && WindowsStagedPackage.Find(advertised, arch) != null;
            return new WindowsUpdateOffer { Id = id, CategoryId = ApplicationCategory(root), Revision = revision, UserSid = sid, Baseline = baseline, StagedVersion = staged ? advertised : null,
                Downloaded = (bool)update.IsDownloaded, MaxBytes = Convert.ToInt64((object)update.MaxDownloadSize, CultureInfo.InvariantCulture) };
        }

        private object Require(WindowsUpdateOffer expected, bool online, CancellationToken token)
        {
            if (expected == null || expected.UserSid != sid) throw new InvalidOperationException("请使用原 Windows 用户应用这项更新。");
            object root = Search(online, token, expected.CategoryId);
            dynamic update = root;
            if (root == null || Guid.Parse((string)update.Identity.UpdateID) != expected.Id || (int)update.Identity.RevisionNumber != expected.Revision)
                throw new InvalidDataException("当前用户的 ChatGPT 更新任务已变化，请重新检查。");
            if (!(bool)update.EulaAccepted) throw new InvalidOperationException("此更新的许可条款尚未获得用户同意，本次操作已停止。");
            return root;
        }

        private static object SingleUpdate(object root)
        {
            dynamic updates = Create("Microsoft.Update.UpdateColl");
            updates.Add((dynamic)root);
            return updates;
        }

        public void Download(WindowsUpdateOffer expected, CancellationToken token, Action<int, string> progress)
        {
            using (var shell = new PowerShellBridge(null))
            {
                var installed = shell.FindInstalled();
                if (installed == null || installed.Version != expected.Baseline) throw new InvalidDataException("下载前本机版本已变化，请重新检查。");
            }
            dynamic update = Require(expected, false, token);
            if ((bool)update.IsDownloaded) { progress(100, "官方更新已下载，复用 Windows 缓存。"); return; }
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.IsForced = false;
            downloader.Updates = (dynamic)SingleUpdate((object)update);
            var callback = new UpdateJobCallback();
            token.ThrowIfCancellationRequested();
            object started;
            try { started = (object)downloader.BeginDownload(callback, callback, null); }
            catch (COMException ex)
            {
                if (ex.ErrorCode != unchecked((int)0x80240009)) throw; // WU_E_OPERATIONINPROGRESS
                WaitForSharedDownload(expected, token, progress);
                return;
            }
            dynamic job = started;
            try
            {
                while (!(bool)job.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    dynamic p = job.GetProgress();
                    long done = Convert.ToInt64((object)p.TotalBytesDownloaded, CultureInfo.InvariantCulture);
                    long total = Convert.ToInt64((object)p.TotalBytesToDownload, CultureInfo.InvariantCulture);
                    progress(total > 0 ? (int)p.PercentComplete : -1, NetworkClient.FormatBytes(done) + (total > 0 ? " / " + NetworkClient.FormatBytes(total) : ""));
                    token.WaitHandle.WaitOne(400);
                }
                token.ThrowIfCancellationRequested();
                CheckResult((object)downloader.EndDownload(job), "下载");
            }
            finally { FinishJob((object)job, callback); }
            dynamic after = Require(expected, false, token);
            if (!(bool)after.IsDownloaded) throw new IOException("Windows 尚未确认同一项 ChatGPT 更新下载完成。");
            progress(100, "Windows 已准备好更新。");
        }

        private void WaitForSharedDownload(WindowsUpdateOffer expected, CancellationToken token, Action<int, string> progress)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromMinutes(5))
            {
                token.ThrowIfCancellationRequested();
                dynamic current = Require(expected, false, token);
                if ((bool)current.IsDownloaded) { progress(100, "已复用完成的官方更新。"); return; }
                progress(-1, "系统已有下载操作，正在等待本项更新就绪…");
                token.WaitHandle.WaitOne(2500);
            }
            throw new IOException("系统下载仍未确认完成，已停止等待；可以稍后继续使用已有缓存。");
        }

        public void Apply(WindowsUpdateOffer expected, string arch, Action<int, string> progress)
        {
            object root;
            string fullName = BindForApply(expected, arch, out root);
            if (fullName != null)
            {
                progress(-1, "正在为当前用户应用已暂存的 " + expected.StagedVersion + "…");
                using (var shell = new PowerShellBridge(progress))
                    DeploymentRetry.Run(() => shell.Register(fullName), detail => progress(-1, detail), detail => Debug.WriteLine(detail));
                return;
            }
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = (dynamic)SingleUpdate(root);
            installer.AllowSourcePrompts = false;
            var callback = new UpdateJobCallback();
            dynamic job = installer.BeginInstall(callback, callback, null);
            try
            {
                while (!(bool)job.IsCompleted)
                {
                    progress((int)job.GetProgress().PercentComplete, "Windows 正在应用已下载的 ChatGPT 更新…");
                    Thread.Sleep(400);
                }
                dynamic result = installer.EndInstall(job);
                CheckResult((object)result, "应用");
                if ((bool)result.RebootRequired) progress(100, "Windows 报告此更新还需要重启系统。");
            }
            finally { if ((bool)job.IsCompleted) job.CleanUp(); GC.KeepAlive(callback); }
        }

        internal string BindForApply(WindowsUpdateOffer expected, string arch, out object root)
        {
            using (var shell = new PowerShellBridge(null))
            {
                var before = shell.FindInstalled();
                if (before == null || before.Version != expected.Baseline) throw new InvalidDataException("本机 ChatGPT 版本已变化，请重新检查。");
                root = Require(expected, true, CancellationToken.None);
                dynamic update = root;
                if (!(bool)update.IsDownloaded) throw new InvalidDataException("Windows 缓存已变化，请重新准备这项更新。");
                if (expected.StagedVersion == null) return null;
                string fullName = WindowsStagedPackage.Find(expected.StagedVersion, arch);
                if (fullName == null || expected.StagedVersion <= before.Version) throw new InvalidDataException("官方暂存包已变化，请重新检查。");
                return fullName;
            }
        }

        private static void CheckResult(object resultObject, string operation)
        {
            dynamic result = resultObject;
            dynamic item = result.GetUpdateResult(0);
            int operationResult = (int)result.ResultCode, itemResult = (int)item.ResultCode;
            int hr = (int)item.HResult;
            if (operationResult == 2 && itemResult == 2 && hr >= 0) return;
            if (hr >= 0) hr = (int)result.HResult;
            if (hr >= 0) throw new IOException("Windows " + operation + "尚未确认成功。任务结果 " + operationResult + "，包结果 " + itemResult + "。");
            throw new COMException("Windows " + operation + " ChatGPT 更新未完成（0x" + unchecked((uint)hr).ToString("X8") + "）。", hr);
        }

        private static void FinishJob(object jobObject, UpdateJobCallback callback)
        {
            dynamic job = jobObject;
            try
            {
                // 取消仅针对本次发起的搜索或下载请求，不清理系统缓存或操作其他作业。
                if (!(bool)job.IsCompleted) job.RequestAbort();
                if ((bool)job.IsCompleted) job.CleanUp();
            }
            catch (COMException) { }
            finally { GC.KeepAlive(callback); }
        }
    }
}
