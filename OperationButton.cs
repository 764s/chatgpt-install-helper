using System.Drawing;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal sealed class OperationButton : Button
    {
        private readonly string idleText;
        private readonly string runningText;

        public OperationButton(string idleText, string runningText)
        {
            this.idleText = idleText;
            this.runningText = runningText;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            DoubleBuffered = true;
            SetRunningState(false, false);
        }

        public void SetRunningState(bool busy, bool current, string currentText = null)
        {
            bool running = busy && current;
            Color green = Color.FromArgb(21, 111, 87);
            Text = running ? currentText ?? runningText : idleText;
            Enabled = !busy;
            BackColor = running ? green : busy ? Color.FromArgb(237, 240, 238) : Color.White;
            ForeColor = running ? Color.White : busy ? Color.FromArgb(117, 128, 123) : green;
            FlatAppearance.BorderColor = busy && !running ? Color.FromArgb(216, 223, 219) : green;
            FlatAppearance.BorderSize = 1;
            Cursor = busy ? Cursors.Default : Cursors.Hand;
            AccessibleDescription = running ? "正在执行此操作。" : busy ? "其他操作进行中，暂不可用。" : "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            if (Enabled)
            {
                base.OnPaint(args);
                return;
            }

            // 原生禁用按钮会强制使用灰色文字，单独绘制以保持当前操作可读。
            using (var background = new SolidBrush(BackColor)) args.Graphics.FillRectangle(background, ClientRectangle);
            ControlPaint.DrawBorder(args.Graphics, ClientRectangle, FlatAppearance.BorderColor, ButtonBorderStyle.Solid);
            TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
