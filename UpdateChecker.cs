using System;
using System.Text;
using System.Threading;

namespace ChatGPTInstaller
{
    internal sealed class UpdateChecker
    {
        private readonly RunLog log;
        private readonly IProgress<UiStatus> progress;
        private readonly Func<InstalledPackage> readInstalled;

        public UpdateChecker(RunLog log, IProgress<UiStatus> progress)
            : this(log, progress, () => { using (var shell = new PowerShellBridge(null)) return shell.FindInstalled(); }) { }

        internal UpdateChecker(RunLog log, IProgress<UiStatus> progress, Func<InstalledPackage> readInstalled)
        {
            this.log = log;
            this.progress = progress;
            this.readInstalled = readInstalled;
        }

        public UpdateCheckResult Check(string proxy, CancellationToken token)
        {
            PlatformInfo.Check();
            var result = new UpdateCheckResult();
            progress.Report(new UiStatus("仅检查更新", "读取本机版本…", -1) { LatestVersion = "查询中…" });
            try
            {
                var installed = readInstalled();
                result.InstalledVersion = installed == null ? null : installed.Version;
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                result.LocalError = ErrorMessages.Explain(ex);
                log.Write("本机版本读取失败：" + result.LocalError);
            }
            token.ThrowIfCancellationRequested();
            progress.Report(new UiStatus("检查官方版本", "只读取小型版本元数据…", -1)
            {
                CurrentVersion = result.LocalError != null ? "读取失败" : result.InstalledVersion == null ? "尚未安装新版" : result.InstalledVersion.ToString()
            });
            var resolver = new ReleaseResolver(log, progress);
            result.Selection = resolver.Resolve(PlatformInfo.Architecture(), proxy, result.InstalledVersion, token);
            if (result.Selection.Package != null) result.Cache = PackageCache.Inspect(result.Selection.Package, token);
            progress.Report(new UiStatus("读取累计更新内容", result.Summary, -1)
            {
                Selection = result.Selection, Cache = result.Cache
            });
            try
            {
                string html = Encoding.UTF8.GetString(resolver.Client.GetSmall(new Uri(AppInfo.Changelog), 4 * 1024 * 1024, token));
                result.Notes.AddRange(ChangelogParser.Parse(html));
                result.NotesFetched = true;
                log.Write("已读取官方桌面发布说明；按明确标注的版本批次筛选。");
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                result.NotesError = ErrorMessages.Explain(ex);
                log.Write("更新内容获取失败，版本检查结果仍保留：" + result.NotesError);
            }
            token.ThrowIfCancellationRequested();
            result.Report = UpdateReport.Format(result);
            log.Write(result.Summary + "。仅检查操作结束。");
            progress.Report(new UiStatus("检查完成", result.Summary, 100));
            return result;
        }
    }
}
