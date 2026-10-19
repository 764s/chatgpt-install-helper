using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    /// <summary>
    /// 定位：插件自动安装流程的浮层提示展示方。
    /// 示例：InstallerStartup 开始添加插件时显示进度，完成后说明两种使用入口。
    /// 职责：展示开始、结果和错误，不占用更新器布局，不阻塞窗口操作。
    /// 约束：关闭进行中的提示只隐藏浮层，不取消安装；结束后再次显示结果。
    /// </summary>
    internal sealed class StartupNotice : Form
    {
        private readonly Label heading;
        private readonly Label message;
        private readonly ProgressBar progress;
        private readonly Timer dismissTimer;
        private readonly TaskCompletionSource<bool> dismissed = new TaskCompletionSource<bool>();
        private bool completed;

        public StartupNotice()
        {
            Text = "Codex 插件安装";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(240, 249, 245);
            ForeColor = Color.FromArgb(24, 65, 50);
            ClientSize = new Size(480, 162);
            Padding = new Padding(18, 14, 18, 14);
            var close = new Button { Text = "×", FlatStyle = FlatStyle.Flat, Size = new Size(28, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(ClientSize.Width - 36, 8), TabStop = false, AccessibleName = "关闭插件安装提示" };
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate { if (completed) Close(); else Hide(); };
            heading = new Label { Font = new Font(Font, FontStyle.Bold), AutoSize = false, Location = new Point(18, 16), Size = new Size(410, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            message = new Label { AutoSize = false, Location = new Point(18, 48), Size = new Size(444, 86), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "插件安装进度与结果" };
            progress = new ProgressBar { Location = new Point(18, 144), Size = new Size(444, 4), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Style = ProgressBarStyle.Marquee };
            Controls.AddRange(new Control[] { heading, message, progress, close });
            dismissTimer = new Timer { Interval = 15000 };
            dismissTimer.Tick += delegate { Close(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (!completed && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
            FormClosed += delegate { dismissTimer.Dispose(); dismissed.TrySetResult(true); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        public Task Dismissed { get { return dismissed.Task; } }

        public void Present(Form owner, string title, string detail, bool finished)
        {
            if (IsDisposed) return;
            completed = finished;
            heading.Text = title;
            SetMessage(detail);
            progress.Visible = !finished;
            var area = owner == null ? Screen.FromPoint(Cursor.Position).WorkingArea : Screen.FromControl(owner).WorkingArea;
            var bounds = owner == null ? area : owner.Bounds;
            Location = new Point(Math.Max(area.Left + 12, Math.Min(bounds.Right - Width - 16, area.Right - Width - 12)),
                Math.Max(area.Top + 12, Math.Min(bounds.Bottom - Height - 48, area.Bottom - Height - 12)));
            if (!Visible)
            {
                if (owner == null) Show();
                else Show(owner);
            }
            if (finished) dismissTimer.Start();
        }

        public void Report(string detail)
        {
            if (!IsDisposed && !completed) SetMessage(detail + "\n可继续使用更新器，关闭此提示不会中断安装。");
        }

        private void SetMessage(string detail)
        {
            int height = Math.Min(180, Math.Max(86, TextRenderer.MeasureText(detail, Font, new Size(message.Width, 0), TextFormatFlags.WordBreak).Height + 4));
            ClientSize = new Size(ClientSize.Width, message.Top + height + 28);
            message.Height = height;
            message.AutoEllipsis = true;
            message.Text = detail;
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            using (var pen = new Pen(Color.FromArgb(157, 198, 177))) args.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { dismissTimer.Dispose(); dismissed.TrySetResult(true); }
            base.Dispose(disposing);
        }
    }
}
