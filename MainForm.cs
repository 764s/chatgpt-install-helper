using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal sealed class MainForm : Form
    {
        private readonly RunLog log;
        private readonly Label current;
        private readonly Label latest;
        private readonly Label phase;
        private readonly Label detail;
        private readonly Label percent;
        private readonly ProgressBar bar;
        private readonly TextBox history;
        private readonly TextBox notes;
        private readonly TabControl information;
        private readonly OperationButton install;
        private readonly OperationButton update;
        private readonly OperationButton check;
        private readonly OperationButton commit;
        private readonly Label preparation;
        private readonly Label cacheNotice;
        private readonly Label officialNotice;
        private readonly TableLayoutPanel preparedRow;
        private readonly Button cancel;
        private readonly Button launch;
        private readonly LinkLabel network;
        private Button addSidebar;
        private Button removeSidebar;
        private Label sidebarStatus;
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool deploying;
        private bool closeAfterCancel;
        private bool preparing;
        private bool confirming;
        private bool integrating;
        private bool startupConfiguring;
        private bool closeAfterStartup;
        private PreparedInstallation prepared;
        private PackageCacheInfo cacheStatus;
        private ReleaseSelection releaseSelection;
        private OfficialUpdateContext officialContext;
        private OfficialUpdateContext operationOfficialContext;
        private DateTime officialContextReceivedAt;
        private OfficialUpdateResult officialResult;
        private bool checkingOfficial;
        private bool installedAvailable;
        private bool retryPrepared;
        private string finishedMessage;
        private string preparationNotice;
        private string proxy = "";
        private readonly bool backgroundMode;
        private bool sidebarConnected;
        private bool exitBackground;
        private bool autoInstallSelected;
        private bool restartAfterUpdate;
        private bool checkCompleted;
        private DateTime lastSidebarRequest = DateTime.UtcNow;
        private string currentVersionText;
        private string latestVersionText;
        private string latestVersionLabel = "官方最新版";
        private string lastFailure;
        private UiStatus lastStatus = new UiStatus("准备检查更新", "", 0);
        private readonly string sidebarSessionId = Guid.NewGuid().ToString("N");

        public MainForm(bool background = false, InstallerStartup startup = null)
        {
            backgroundMode = background;
            Text = AppInfo.ProductName;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(247, 249, 248);
            ForeColor = Color.FromArgb(24, 41, 37);
            ClientSize = new Size(820, 730);
            MinimumSize = new Size(760, 690);
            StartPosition = FormStartPosition.CenterScreen;
            var grid = new TableLayoutPanel { MaximumSize = new Size(1040, 0), Padding = new Padding(28, 22, 28, 18), ColumnCount = 1, RowCount = 13 };
            Resize += delegate { FitContent(grid); };
            foreach (float height in new[] { 44F, 31F, 65F, 80F, 96F, 39F, 22F, 36F }) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            Controls.Add(grid);
            grid.Controls.Add(new Label { Text = "ChatGPT 安装助手", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
            grid.Controls.Add(new Label { Text = "先下载准备 · 自选安装时机 · 检查更新及累计内容", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(90, 108, 101) }, 0, 1);

            var versions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.White, Padding = new Padding(14, 8, 14, 6), Margin = new Padding(0, 0, 0, 6) };
            versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            current = new Label { Text = "本机安装包\n正在检测…", AutoSize = false, Dock = DockStyle.Fill };
            latest = new Label { Text = "官方最新版\n点击按钮后实时获取", AutoSize = false, Dock = DockStyle.Fill };
            versions.Controls.Add(current, 0, 0);
            versions.Controls.Add(latest, 1, 0);
            grid.Controls.Add(versions, 0, 2);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            install = CreateActionButton("1  安装 / 重装", "1  下载 / 校验…");
            update = CreateActionButton("2  更新", "2  下载 / 校验…");
            check = CreateActionButton("3  仅检查更新", "3  正在检查…");
            install.Margin = new Padding(0, 8, 6, 8);
            update.Margin = new Padding(6, 8, 6, 8);
            check.Margin = new Padding(6, 8, 0, 8);
            actions.Controls.Add(install, 0, 0);
            actions.Controls.Add(update, 1, 0);
            actions.Controls.Add(check, 2, 0);
            grid.Controls.Add(actions, 0, 3);
            preparedRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 0, 5) };
            preparedRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preparedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            preparedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            preparation = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(65, 85, 77), Font = new Font(Font.FontFamily, 9F) };
            commit = new OperationButton("先选择操作", "正在执行…") { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), Margin = new Padding(8, 0, 0, 0) };
            var preparationPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            preparationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            preparationPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preparationPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            cacheNotice = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(117, 128, 123), Font = new Font(Font.FontFamily, 8F), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            preparationPanel.Controls.Add(preparation, 0, 0);
            preparationPanel.Controls.Add(cacheNotice, 0, 1);
            preparedRow.Controls.Add(preparationPanel, 0, 0);
            preparedRow.Controls.Add(commit, 1, 0);
            grid.Controls.Add(preparedRow, 0, 4);
            var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            phase = new Label { Text = "等待操作", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) };
            percent = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight };
            statusRow.Controls.Add(phase, 0, 0);
            statusRow.Controls.Add(percent, 1, 0);
            grid.Controls.Add(statusRow, 0, 5);
            bar = new ProgressBar { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 5), Maximum = 100 };
            grid.Controls.Add(bar, 0, 6);
            detail = new Label { Text = "可先仅检查更新，查看累计更新内容。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(90, 108, 101), AutoEllipsis = true };
            grid.Controls.Add(detail, 0, 7);
            officialNotice = new Label { AutoSize = true, Dock = DockStyle.Fill, Visible = false, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 0, 8), Font = new Font(Font.FontFamily, 9F), AccessibleName = "官方更新方式核对" };
            grid.Controls.Add(officialNotice, 0, 8);
            grid.Controls.Add(new Label { Text = "更新内容与本次操作记录（完整技术详情保存在日志中）", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(90, 108, 101), Font = new Font(Font.FontFamily, 9F) }, 0, 9);
            history = new TextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9), Multiline = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0), WordWrap = true,
                AccessibleName = "安装运行记录"
            };
            notes = new TextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9), Multiline = true, ScrollBars = ScrollBars.Vertical,
                WordWrap = true, AccessibleName = "累计更新内容", Text = "点击“仅检查更新”，查看版本结论与累计官方更新内容。"
            };
            information = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var notesPage = new TabPage("更新内容") { BackColor = Color.White, Padding = new Padding(7) };
            var historyPage = new TabPage("本次记录") { BackColor = Color.White, Padding = new Padding(7) };
            var notesActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 29, WrapContents = false };
            var copyNotes = new LinkLabel { Text = "复制检查结果", AutoSize = true, Margin = new Padding(0, 6, 16, 0), LinkColor = Color.FromArgb(27, 106, 86) };
            var openNotes = new LinkLabel { Text = "官方更新日志", AutoSize = true, Margin = new Padding(0, 6, 16, 0), LinkColor = copyNotes.LinkColor };
            notesActions.Controls.AddRange(new Control[] { copyNotes, openNotes });
            notesPage.Controls.Add(notes);
            notesPage.Controls.Add(notesActions);
            historyPage.Controls.Add(history);
            information.TabPages.AddRange(new[] { notesPage, historyPage });
            information.TabPages.Add(CreateSidebarPage());
            grid.Controls.Add(information, 0, 10);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 7, 0, 0), WrapContents = false };
            network = new LinkLabel { Text = "网络设置", AutoSize = true, Margin = new Padding(0, 7, 16, 0), LinkColor = Color.FromArgb(27, 106, 86) };
            var copy = new LinkLabel { Text = "复制完整日志", AutoSize = true, Margin = new Padding(0, 7, 16, 0), LinkColor = network.LinkColor };
            var logs = new LinkLabel { Text = "打开完整日志", AutoSize = true, Margin = new Padding(0, 7, 16, 0), LinkColor = network.LinkColor };
            cancel = new Button { Text = "取消", Size = new Size(85, 29), Enabled = false, Visible = false, FlatStyle = FlatStyle.Flat };
            launch = new Button { Text = "打开 ChatGPT", Size = new Size(126, 29), Enabled = false, FlatStyle = FlatStyle.Flat };
            footer.Controls.AddRange(new Control[] { network, copy, logs, cancel, launch });
            grid.Controls.Add(footer, 0, 11);
            grid.Controls.Add(new Label { Text = "非官方辅助工具  ·  安装包与离线许可证由 OpenAI 官方提供", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(117, 128, 123), Font = new Font(Font.FontFamily, 8F), Padding = new Padding(0, 4, 0, 0) }, 0, 12);

            log = new RunLog();
            log.Added += AppendLog;
            install.Click += async delegate { await Start(false); };
            update.Click += async delegate { await Start(true); };
            check.Click += async delegate { await CheckOnly(); };
            commit.Click += async delegate
            {
                if (prepared != null) await InstallPrepared();
                else if (!busy && finishedMessage != null && installedAvailable) OpenChatGPT();
                else if (!busy && cacheStatus != null) await Start(true);
            };
            cancel.Click += delegate { Cancel(); };
            network.LinkClicked += delegate
            {
                using (var dialog = new ProxyDialog(proxy))
                    if (dialog.ShowDialog(this) == DialogResult.OK) proxy = dialog.ProxyAddress;
            };
            copy.LinkClicked += delegate { try { Clipboard.SetText(log.ReadAll()); } catch (Exception ex) { log.Write(ex.Message); } };
            copyNotes.LinkClicked += delegate { try { Clipboard.SetText(notes.Text); } catch (Exception ex) { log.Write(ex.Message); } };
            openNotes.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(AppInfo.Changelog) { UseShellExecute = true }); } catch (Exception ex) { log.Write(ex.Message); } };
            logs.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(log.FilePath) { UseShellExecute = true }); } catch (Exception ex) { log.Write(ex.Message); } };
            launch.Click += delegate { OpenChatGPT(); };
            Shown += async delegate
            {
                Task setup = startup == null ? Task.FromResult(0) : InitializeSidebar(startup);
                if (!busy && currentVersionText == null) await CheckOnly();
                await setup;
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (!exitBackground && args.CloseReason == CloseReason.UserClosing && (backgroundMode || sidebarConnected))
                {
                    args.Cancel = true;
                    Hide();
                    return;
                }
                if (!busy)
                {
                    if (startupConfiguring) { args.Cancel = true; closeAfterStartup = true; Hide(); }
                    return;
                }
                args.Cancel = true;
                if (integrating) { closeAfterCancel = true; detail.Text = "正在配置侧栏入口，完成后关闭窗口。"; }
                else if (deploying) detail.Text = "Windows 正在安装，请等待完成后关闭窗口。";
                else { closeAfterCancel = true; Cancel(); }
            };
            FitContent(grid);
            RefreshPreparedControls();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WindowActivation.Message) WindowActivation.Show(this);
            base.WndProc(ref message);
        }

        private TabPage CreateSidebarPage()
        {
            var page = new TabPage("Codex 侧栏入口") { BackColor = Color.White, Padding = new Padding(12), AutoScroll = true };
            var content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            content.Controls.Add(new Label { Text = "在全局侧栏下载和应用更新", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) });
            var explanation = new Label
            {
                AutoSize = true, Margin = new Padding(0, 0, 0, 10),
                Text = "双击 EXE 后，插件未安装时自动添加，旧版自动升级，同版或更高版直接使用；保留启用 / 停用状态。\n安装或升级后，等当前任务结束再重新打开 ChatGPT，以加载新入口。首次使用可在侧栏“更多”中找到“ChatGPT 更新器”并固定。\n页面直接显示版本和进度，默认下载后等待应用；可勾选本次自动安装并重启。正常更新和覆盖重装会保留入口。\n下方“添加 / 修复入口”会主动重新安装并启用插件。"
            };
            content.Controls.Add(explanation);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            addSidebar = new Button { Text = "添加 / 修复入口", AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 12, 0) };
            removeSidebar = new Button { Text = "移除侧栏入口", AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0) };
            buttons.Controls.AddRange(new Control[] { addSidebar, removeSidebar });
            content.Controls.Add(buttons);
            sidebarStatus = new Label { Text = SidebarIntegration.Status(), AutoSize = true, ForeColor = Color.FromArgb(65, 85, 77), Margin = new Padding(0, 0, 0, 12), AccessibleName = "侧栏配置状态" };
            content.Controls.Add(sidebarStatus);
            page.Controls.Add(content);
            page.SizeChanged += delegate
            {
                int width = Math.Max(200, page.ClientSize.Width - 44);
                explanation.MaximumSize = new Size(width, 0);
                sidebarStatus.MaximumSize = new Size(width, 0);
            };
            addSidebar.Click += async delegate { await ConfigureSidebar(false); };
            removeSidebar.Click += async delegate { await ConfigureSidebar(true); };
            return page;
        }

        private async Task ConfigureSidebar(bool remove)
        {
            if (busy || startupConfiguring) return;
            integrating = true;
            SetBusy(true);
            string result = "";
            var progress = new Progress<string>(message =>
            {
                result = message;
                sidebarStatus.Text = message;
                log.Write(message);
                ShowStatus(new UiStatus("正在配置侧栏入口", message, -1));
            });
            try
            {
                await Task.Run(() =>
                {
                    Action<string> report = message => ((IProgress<string>)progress).Report(message);
                    if (remove) SidebarIntegration.Remove(report);
                    else SidebarIntegration.Install(report);
                });
                ShowStatus(new UiStatus(remove ? "侧栏入口已移除" : "侧栏入口已配置", result, 100));
            }
            catch (Exception ex)
            {
                sidebarStatus.Text = "侧栏配置未完成：" + ex.Message;
                log.Write(sidebarStatus.Text);
                log.WriteTechnical(ex.ToString());
                ShowStatus(new UiStatus("侧栏配置未完成", "可查看侧栏页提示后重试。", 0));
            }
            finally
            {
                integrating = false;
                SetBusy(false);
                if (closeAfterCancel) Close();
            }
        }

        private async Task InitializeSidebar(InstallerStartup startup)
        {
            startupConfiguring = true;
            addSidebar.Enabled = removeSidebar.Enabled = false;
            try
            {
                string notice = await startup.CompleteWithNotice(this, message => { sidebarStatus.Text = message; log.Write(message); });
                sidebarStatus.Text = notice + (startup.ConfiguredThisTime ? "\n等当前任务结束后重新打开 ChatGPT，以加载侧栏入口。" : "");
                log.Write(notice);
            }
            catch (Exception ex)
            {
                sidebarStatus.Text = "侧栏配置未完成：" + ex.Message;
                log.Write(sidebarStatus.Text);
                log.WriteTechnical(ex.ToString());
            }
            finally
            {
                startupConfiguring = false;
                addSidebar.Enabled = removeSidebar.Enabled = !busy;
                if (closeAfterStartup) Close();
            }
        }

        private void FitContent(Control content)
        {
            int width = Math.Min(ClientSize.Width, content.MaximumSize.Width);
            if (officialNotice != null) officialNotice.MaximumSize = new Size(Math.Max(200, width - 56), 0);
            content.SetBounds(Math.Max(0, (ClientSize.Width - width) / 2), 0, width, ClientSize.Height);
        }

        private void OpenChatGPT()
        {
            try { Process.Start("explorer.exe", "shell:AppsFolder\\" + AppInfo.FamilyName + "!App"); }
            catch (Exception ex) { log.Write(ErrorMessages.Explain(ex)); log.WriteTechnical(ex.ToString()); }
        }

        private void BeginOperation()
        {
            // 界面只展示本次操作；日志文件仍保留此前失败及完整堆栈。
            history.Clear();
            finishedMessage = null;
            preparationNotice = null;
            retryPrepared = false;
            lastFailure = null;
            cacheStatus = null;
            officialResult = null;
            officialNotice.Visible = false;
            operationOfficialContext = officialContext != null && DateTime.UtcNow - officialContextReceivedAt < TimeSpan.FromMinutes(2)
                ? officialContext : OfficialUpdateContext.Capture();
        }

        private OperationButton CreateActionButton(string text, string runningText)
        {
            return new OperationButton(text, runningText)
            {
                Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 13F, FontStyle.Bold)
            };
        }

        private async Task Start(bool updateOnly, bool autoInstall = false, bool fromSidebar = false)
        {
            if (busy) return;
            autoInstallSelected = fromSidebar && autoInstall;
            restartAfterUpdate = fromSidebar;
            BeginOperation();
            cancellation = new CancellationTokenSource();
            deploying = false;
            preparing = true;
            prepared = null;
            SetBusy(true, updateOnly ? update : install);
            cancel.Enabled = true;
            information.SelectedIndex = 1;
            var progress = new Progress<UiStatus>(ShowStatus);
            log.Write(autoInstallSelected ? "开始准备更新包；你已选择下载完成后自动安装并重启。" : updateOnly ? "开始准备更新包，下载完成后等待手动安装。" : "开始准备安装 / 修复包，下载完成后等待手动安装。");
            UpdateRetrievalStage? officialStage = null;
            try
            {
                prepared = await Task.Run(() => new InstallerWorkflow(log, progress).Prepare(updateOnly, proxy, cancellation.Token));
                if (prepared == null && finishedMessage == null && releaseSelection != null)
                {
                    preparationNotice = lastStatus.Detail;
                    if (releaseSelection.Package == null) officialStage = UpdateRetrievalStage.Package;
                }
            }
            catch (OperationCanceledException)
            {
                autoInstallSelected = false;
                preparationNotice = "准备已取消，已保留下载进度。点击上方安装 / 更新可以继续。";
                ShowStatus(new UiStatus("准备已取消", "已保留下载进度；未关闭或修改 ChatGPT。", 0));
                log.Write("下载准备已取消，未执行部署。");
            }
            catch (Exception ex)
            {
                autoInstallSelected = false;
                var retrieval = ex as UpdateRetrievalException;
                if (retrieval != null) officialStage = retrieval.Stage;
                string message = ErrorMessages.Explain(ex);
                lastFailure = message;
                cacheStatus = null;
                preparationNotice = message;
                ShowStatus(new UiStatus("准备未完成", message, 0) { LatestVersion = latestVersionText == "查询中…" ? "未能读取" : null });
                log.Write(message);
                log.WriteTechnical(ex.ToString());
            }
            finally
            {
                deploying = false;
                preparing = false;
                SetBusy(false);
                cancellation.Dispose();
                cancellation = null;
                if (closeAfterCancel) Close();
            }
            if (officialStage.HasValue && !closeAfterCancel && !IsDisposed)
                await DiagnoseOfficial(officialStage.Value, updateOnly ? update : install);
            // 自动安装只属于用户本次下载选择；取消、失败和以后重新打开都不会继承该授权。
            bool applyAutomatically = autoInstallSelected && prepared != null && !closeAfterCancel;
            autoInstallSelected = false;
            if (applyAutomatically) await InstallPrepared();
        }

        private async Task InstallPrepared()
        {
            if (busy || prepared == null) return;
            var selection = prepared;
            BeginOperation();
            cancellation = new CancellationTokenSource();
            deploying = false;
            confirming = true;
            SetBusy(true, selection.UpdateOnly ? update : install, selection.UpdateOnly ? "2  安装前复核…" : "1  安装前复核…");
            cancel.Enabled = true;
            information.SelectedIndex = 1;
            var progress = new Progress<UiStatus>(ShowStatus);
            log.Write("已收到应用更新请求，开始部署前复核。");
            UpdateRetrievalStage? officialStage = null;
            try
            {
                bool complete = await Task.Run(() => new InstallerWorkflow(log, progress).InstallPrepared(selection, proxy, cancellation.Token));
                prepared = null;
                if (complete)
                {
                    installedAvailable = true;
                    if (restartAfterUpdate)
                    {
                        try
                        {
                            Process.Start("explorer.exe", "shell:AppsFolder\\" + AppInfo.FamilyName + "!App");
                            log.Write("已核对安装成功，并请求重新打开 ChatGPT。");
                            ShowStatus(new UiStatus("更新完成", "已请求重新打开 ChatGPT。", 100) { Finished = true });
                        }
                        catch (Exception ex)
                        {
                            lastFailure = "更新已成功，但自动打开未完成。请从开始菜单打开 ChatGPT。";
                            log.Write(lastFailure);
                            log.WriteTechnical(ex.ToString());
                        }
                    }
                }
                else
                {
                    preparationNotice = "可安装目标已变化或不可用，请重新检查并准备。尚未执行部署。";
                    if (releaseSelection != null && releaseSelection.Package == null && !releaseSelection.IsCurrent(ReadCurrentVersion())) officialStage = UpdateRetrievalStage.Package;
                }
            }
            catch (OperationCanceledException)
            {
                preparationNotice = "安装前检查已取消，准备包已保留。可以继续运行任务，准备好后再点击右侧按钮。";
                ShowStatus(new UiStatus("安装前检查已取消", "准备包已保留，尚未进入 Windows 部署。", 0));
                log.Write("安装前检查已取消。");
            }
            catch (Exception ex)
            {
                var retrieval = ex as UpdateRetrievalException;
                if (retrieval != null) officialStage = retrieval.Stage;
                if (ex is InvalidDataException || (retrieval != null && retrieval.InvalidatesPackage) || !selection.FilesExist()) prepared = null;
                string message = ErrorMessages.Explain(ex);
                lastFailure = message;
                preparationNotice = message;
                retryPrepared = prepared != null;
                ShowStatus(new UiStatus("安装未完成", message, 0));
                log.Write(message);
                log.WriteTechnical(ex.ToString());
            }
            finally
            {
                deploying = false;
                confirming = false;
                SetBusy(false);
                cancellation.Dispose();
                cancellation = null;
                if (closeAfterCancel) Close();
            }
            if (officialStage.HasValue && !closeAfterCancel && !IsDisposed) await DiagnoseOfficial(officialStage.Value, selection.UpdateOnly ? update : install);
        }

        private async Task CheckOnly()
        {
            if (busy) return;
            checkCompleted = false;
            BeginOperation();
            cancellation = new CancellationTokenSource();
            deploying = false;
            SetBusy(true, check);
            cancel.Enabled = true;
            information.SelectedIndex = 0;
            notes.Text = "正在读取本机版本、官方版本和累计更新内容…";
            var progress = new Progress<UiStatus>(ShowStatus);
            log.Write("开始仅检查更新，不下载安装包、不改动应用。");
            UpdateRetrievalStage? officialStage = null;
            try
            {
                var result = await Task.Run(() => new UpdateChecker(log, progress).Check(proxy, cancellation.Token));
                notes.Text = result.Report;
                notes.SelectionStart = 0;
                notes.ScrollToCaret();
                installedAvailable = result.InstalledVersion != null;
                checkCompleted = result.LocalError == null && result.LatestVersion != null;
                cacheStatus = result.Cache;
                releaseSelection = result.Selection;
                if (result.LocalError == null && result.Selection.Package == null && !result.Selection.IsCurrent(result.InstalledVersion)) officialStage = UpdateRetrievalStage.Package;
                if (result.Selection.CanUpdate(result.InstalledVersion) && cacheStatus != null)
                    ShowStatus(new UiStatus(cacheStatus.Summary, result.Summary, cacheStatus.Size > 0 ? (int)(cacheStatus.Bytes * 100 / cacheStatus.Size) : 0));
                if (prepared != null && (result.AvailableVersion != prepared.Release.Version ||
                    (result.InstalledVersion != null && InstallerWorkflow.ShouldSkip(new InstalledPackage { Version = result.InstalledVersion }, prepared.Release.Version, prepared.UpdateOnly))))
                {
                    prepared = null;
                    log.Write("版本状态已变化，已取消原准备状态；如仍需安装，请点击上方安装 / 更新重新准备。");
                }
                if (prepared == null)
                {
                    if (result.LocalError == null && result.Selection.IsCurrent(result.InstalledVersion)) finishedMessage = result.Summary;
                    else preparationNotice = result.Summary;
                }
            }
            catch (OperationCanceledException)
            {
                preparationNotice = "检查已取消，未下载或修改应用。可重新选择操作。";
                notes.Text = "检查已取消，尚未形成完整结果。";
                ShowStatus(new UiStatus("检查已取消", "未下载安装包，也未修改应用。", 0));
                log.Write("仅检查操作已取消。");
            }
            catch (Exception ex)
            {
                var retrieval = ex as UpdateRetrievalException;
                if (retrieval != null) officialStage = retrieval.Stage;
                string message = ErrorMessages.Explain(ex);
                lastFailure = message;
                preparationNotice = message;
                notes.Text = "检查未完成，暂时无法判断是否有更新。" + Environment.NewLine + message;
                ShowStatus(new UiStatus("检查未完成", message, 0) { LatestVersion = latestVersionText == "查询中…" ? "未能读取" : null });
                log.Write(message);
                log.WriteTechnical(ex.ToString());
            }
            finally
            {
                SetBusy(false);
                cancellation.Dispose();
                cancellation = null;
                if (closeAfterCancel) Close();
            }
            if (officialStage.HasValue && !closeAfterCancel && !IsDisposed)
            {
                await DiagnoseOfficial(officialStage.Value, check);
                if (!IsDisposed && officialResult != null) notes.AppendText(Environment.NewLine + Environment.NewLine + "官方更新方式核对：" + officialResult.Message);
            }
        }

        private async Task DiagnoseOfficial(UpdateRetrievalStage stage, OperationButton action)
        {
            if (checkingOfficial || officialResult != null || busy) return;
            checkingOfficial = true;
            autoInstallSelected = false;
            cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(12000);
            SetBusy(true, action, "核对官方更新方式…");
            officialNotice.BackColor = Color.FromArgb(239, 243, 241);
            officialNotice.ForeColor = Color.FromArgb(65, 85, 77);
            officialNotice.Text = "本次获取失败，正在只读核对 Codex 自带更新器…";
            officialNotice.Visible = true;
            log.Write("获取失败保护：开始只读核对官方更新方式，不下载、安装或重启。");
            try
            {
                var token = cancellation.Token;
                officialResult = await Task.Run(() => OfficialUpdateCheck.Check(operationOfficialContext, stage, token));
            }
            catch (Exception ex)
            {
                officialResult = OfficialUpdateResult.Unknown(cancellation.IsCancellationRequested
                    ? "官方核对已取消或超时，原失败原因仍保留，未判定更新途径变化。"
                    : "未能完成官方只读更新检查，原失败原因仍保留，未判定更新途径变化。");
                log.WriteTechnical(ex.ToString());
            }
            finally
            {
                checkingOfficial = false;
                if (officialResult != null)
                {
                    officialNotice.Text = officialResult.Message;
                    if (officialResult.SuggestRepair)
                    {
                        officialNotice.BackColor = Color.FromArgb(255, 246, 228);
                        officialNotice.ForeColor = Color.FromArgb(132, 76, 27);
                    }
                    log.Write("官方核对：" + officialResult.Message);
                }
                SetBusy(false);
                cancellation.Dispose();
                cancellation = null;
                if (closeAfterCancel) Close();
            }
        }

        internal void ShowStatus(UiStatus status)
        {
            lastStatus = status;
            if (status.CurrentVersion != null)
            {
                currentVersionText = status.CurrentVersion;
                current.Text = "本机安装包\n" + status.CurrentVersion;
                Version version;
                installedAvailable = Version.TryParse(status.CurrentVersion, out version);
            }
            if (status.Finished)
            {
                finishedMessage = status.Detail;
                preparationNotice = null;
                retryPrepared = false;
            }
            if (status.LatestVersion != null)
            {
                latestVersionText = status.LatestVersion;
                latestVersionLabel = status.LatestVersionLabel ?? "官方最新版";
                latest.Text = latestVersionLabel + "\n" + status.LatestVersion;
                // 清单已读取但候选尚未选定，旧缓存不能冒充本次目标。
                releaseSelection = null;
                cacheStatus = null;
                checkCompleted = false;
            }
            if (status.Selection != null)
            {
                releaseSelection = status.Selection;
                latestVersionText = releaseSelection.LatestVersion.ToString();
                latestVersionLabel = releaseSelection.VersionLabel;
                latest.Text = latestVersionLabel + "\n" + latestVersionText;
                if (releaseSelection.IsFallback) latest.Text += "\n可安装：" + releaseSelection.AvailableVersion;
                else if (releaseSelection.LatestPackageMissing) latest.Text += "\n暂未找到独立安装包";
                if (cacheStatus != null && cacheStatus.Version != releaseSelection.AvailableVersion) cacheStatus = null;
                checkCompleted = ReadCurrentVersion() != null || currentVersionText == "尚未安装新版";
            }
            if (status.Cache != null) cacheStatus = status.Cache;
            phase.Text = status.Phase;
            detail.Text = status.Detail;
            if (status.Deployment)
            {
                deploying = true;
                cancel.Enabled = false;
                if (confirming && prepared != null)
                    SetBusy(true, prepared.UpdateOnly ? update : install, prepared.UpdateOnly ? "2  正在更新…" : "1  正在安装…");
            }
            bar.Style = status.Percent < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (status.Percent >= 0) bar.Value = Math.Max(0, Math.Min(100, status.Percent));
            percent.Text = status.Percent < 0 ? "进行中" : status.Percent + "%";
            RefreshPreparedControls();
        }

        private void SetBusy(bool value, OperationButton activeAction = null, string actionText = null)
        {
            busy = value;
            foreach (var button in new[] { install, update, check }) button.SetRunningState(value, button == activeAction, actionText);
            network.Enabled = !value;
            addSidebar.Enabled = !value && !startupConfiguring;
            removeSidebar.Enabled = !value && !startupConfiguring;
            cancel.Visible = value && cancellation != null;
            cancel.Enabled = value && cancellation != null && !deploying && !cancellation.IsCancellationRequested;
            RefreshPreparedControls();
        }

        private void RefreshPreparedControls()
        {
            launch.Enabled = installedAvailable && !confirming && !deploying;
            bool ready = prepared != null && !busy;
            bool finished = prepared == null && finishedMessage != null && !busy;
            bool canOpen = finished && installedAvailable;
            bool canPrepare = !busy && prepared == null && !finished && cacheStatus != null && lastFailure == null &&
                checkCompleted && releaseSelection != null && releaseSelection.CanUpdate(ReadCurrentVersion());
            launch.Visible = !canOpen;
            commit.SetRunningState(!ready && !canOpen && !canPrepare, confirming);
            preparedRow.BackColor = Color.White;
            preparation.ForeColor = Color.FromArgb(65, 85, 77);
            cacheNotice.Text = !finished && cacheStatus != null ? cacheStatus.Notice ?? "" : "";
            if (releaseSelection != null && !string.IsNullOrEmpty(releaseSelection.SourceNotice))
                cacheNotice.Text += (cacheNotice.Text.Length == 0 ? "" : " ") + releaseSelection.SourceNotice;
            if (releaseSelection != null && releaseSelection.LatestPackageMissing)
                cacheNotice.Text += (cacheNotice.Text.Length == 0 ? "" : " ") + "候选可能不含全部历史版本，详见检查报告。";

            if (integrating)
            {
                commit.Text = "正在配置入口…";
                preparation.Text = "正在配置 Codex 侧栏入口。\n等当前任务结束后再重新打开 ChatGPT。";
            }
            else if (confirming)
            {
                commit.Text = deploying ? "正在安装…" : "安装前复核…";
                preparation.Text = deploying ? "正在执行安装，请等待 Windows 完成部署。\nChatGPT / Codex 可能会关闭。" : "正在复核版本和缓存，随后进入 Windows 安装。\n尚未进入部署时可以取消。";
            }
            else if (preparing)
            {
                commit.Text = "等待准备完成";
                preparation.Text = autoInstallSelected ? "你已勾选自动安装，准备完成后将安装并重启。\n请先结束正在进行的任务；可在侧栏页面取消勾选。" : "正在准备安装包，可继续运行 Codex 任务。\n已有完整包将直接复用，校验完成后等待你应用。";
            }
            else if (prepared != null)
            {
                commit.Text = retryPrepared ? (prepared.UpdateOnly ? "重试更新" : "重试安装") : prepared.UpdateOnly ? "立即更新" : "立即安装 / 重装";
                preparation.Text = preparationNotice ?? "已准备 " + prepared.Release.Version + "，可继续运行任务。\n" +
                    (releaseSelection != null && releaseSelection.IsFallback ? "官方最新版暂缺安装包；" : "") + "任务结束并保存后，点击右侧按钮安装。";
                preparedRow.BackColor = preparationNotice == null ? Color.FromArgb(236, 247, 241) : Color.FromArgb(255, 246, 228);
            }
            else if (busy)
            {
                commit.Text = "正在检查…";
                preparation.Text = "正在读取版本状态。\n此阶段不会关闭或修改 ChatGPT / Codex。";
            }
            else if (finished)
            {
                commit.Text = canOpen ? "打开 ChatGPT" : "已完成";
                preparation.Text = finishedMessage + "\n可以打开应用，也可以直接关闭安装助手。";
                preparedRow.BackColor = Color.FromArgb(236, 247, 241);
                preparation.ForeColor = Color.FromArgb(21, 111, 87);
            }
            else if (canPrepare)
            {
                commit.Text = cacheStatus.ActionText;
                preparation.Text = (releaseSelection.IsFallback ? releaseSelection.Summary(ReadCurrentVersion()) : cacheStatus.Summary) + "\n" +
                    (cacheStatus.Complete ? "可直接复用完整包；校验通过后由你决定安装时机。" : "点击下载更新，或在上方选择安装 / 重装。");
            }
            else if (preparationNotice != null)
            {
                commit.Text = "请重新选择操作";
                preparation.Text = preparationNotice;
            }
            else
            {
                commit.Text = "先选择操作";
                preparation.Text = "点击上方安装 / 更新，先下载并校验。\n准备好后由你决定安装时机。";
            }
            if (ready || canOpen || canPrepare)
            {
                commit.BackColor = Color.FromArgb(21, 111, 87);
                commit.ForeColor = Color.White;
            }
            commit.AccessibleDescription = ready ? "点击后进入实际安装，可能关闭 ChatGPT 和 Codex。请先保存工作。" :
                canOpen ? "安装已完成，点击打开 ChatGPT。" : preparation.Text;
        }

        private void Cancel()
        {
            if (deploying || cancellation == null) return;
            autoInstallSelected = false;
            cancellation.Cancel();
            cancel.Enabled = false;
            detail.Text = "正在取消当前操作…";
        }

        internal object HandleSidebarCommand(string command, bool autoInstall, OfficialUpdateContext context = null)
        {
            if (context != null) { officialContext = context; officialContextReceivedAt = DateTime.UtcNow; }
            sidebarConnected = true;
            lastSidebarRequest = DateTime.UtcNow;
            switch (command)
            {
                case "status": break;
                case "open": WindowActivation.Show(this); break;
                case "check":
                    if (!busy) RunSidebarOperation(CheckOnly());
                    break;
                case "download":
                    if (!busy && prepared == null) RunSidebarOperation(Start(true, autoInstall, true));
                    break;
                case "apply":
                    if (!busy)
                    {
                        if (prepared == null) throw new InvalidOperationException("请先下载并完成校验，再应用更新。");
                        restartAfterUpdate = true;
                        RunSidebarOperation(InstallPrepared());
                    }
                    break;
                case "auto":
                    if (!preparing || confirming || deploying) throw new InvalidOperationException("仅可在下载准备期间更改自动安装选项。");
                    autoInstallSelected = autoInstall;
                    if (autoInstall) restartAfterUpdate = true;
                    log.Write(autoInstall ? "你已选择本次下载完成后自动安装并重启。" : "你已取消本次自动安装；下载完成后等待手动应用。");
                    RefreshPreparedControls();
                    break;
                case "cancel": Cancel(); break;
                default: throw new InvalidDataException("未知的更新器操作。");
            }
            Version currentVersion = ReadCurrentVersion();
            bool upToDate = releaseSelection != null && releaseSelection.IsCurrent(currentVersion);
            bool canDownload = checkCompleted && releaseSelection != null && releaseSelection.CanUpdate(currentVersion);
            string state = busy ? checkingOfficial ? "diagnosing" : preparing ? "downloading" : confirming ? "installing" : "checking" : prepared != null ? "ready" : lastFailure != null ? "error" : upToDate ? "current" :
                canDownload ? "available" : checkCompleted ? "unavailable" : "idle";
            return new
            {
                helperVersion = SidebarIntegration.VersionString, sessionId = sidebarSessionId, state = state, busy = busy,
                officialCheckSupported = true,
                diagnosticStatus = checkingOfficial ? "checking" : officialResult == null ? null : officialResult.Status,
                diagnosticMessage = checkingOfficial ? "本次获取失败，正在只读核对 Codex 自带更新器…" : officialResult == null ? null : officialResult.Message,
                diagnosticSuggestRepair = officialResult != null && officialResult.SuggestRepair,
                currentVersion = currentVersionText, latestVersion = latestVersionText,
                preparedVersion = prepared == null ? null : prepared.Release.Version.ToString(),
                latestVersionLabel = latestVersionLabel,
                availableVersion = releaseSelection == null || releaseSelection.AvailableVersion == null ? null : releaseSelection.AvailableVersion.ToString(),
                isFallback = releaseSelection != null && releaseSelection.IsFallback,
                availabilityNotice = releaseSelection == null ? null : releaseSelection.Notice,
                candidateNotice = releaseSelection == null ? null : releaseSelection.CandidateNotice,
                canDownload = canDownload,
                phase = checkingOfficial ? "核对官方更新方式" : lastStatus.Phase,
                detail = checkingOfficial ? "只读取官方状态，不下载、安装或重启。" : lastFailure ?? lastStatus.Detail,
                percent = checkingOfficial ? -1 : lastStatus.Percent,
                ready = prepared != null && !busy, upToDate = upToDate, checkedVersion = checkCompleted,
                autoInstall = autoInstallSelected, canCancel = cancellation != null && !deploying && !cancellation.IsCancellationRequested,
                cacheState = cacheStatus == null ? "unknown" : cacheStatus.State,
                cacheSummary = cacheStatus == null ? null : cacheStatus.Summary,
                cacheNotice = cacheStatus == null ? null : cacheStatus.Notice,
                error = lastFailure
            };
        }

        private Version ReadCurrentVersion()
        {
            Version version;
            return Version.TryParse(currentVersionText, out version) ? version : null;
        }

        private async void RunSidebarOperation(Task operation)
        {
            try { await operation; }
            catch (Exception ex) { lastFailure = ErrorMessages.Explain(ex); log.WriteTechnical(ex.ToString()); }
        }

        internal bool CanExitBackground()
        {
            return !Visible && !busy && !startupConfiguring && DateTime.UtcNow - lastSidebarRequest > TimeSpan.FromMinutes(2);
        }

        internal void ExitBackground()
        {
            exitBackground = true;
            Close();
        }

        private void AppendLog(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(AppendLog), line); } catch (InvalidOperationException) { } return; }
            history.AppendText(line + Environment.NewLine);
            history.SelectionStart = history.TextLength;
            history.ScrollToCaret();
        }
    }
}
