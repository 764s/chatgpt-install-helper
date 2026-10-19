using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChatGPTInstaller
{
    internal sealed class ProxyDialog : Form
    {
        private readonly TextBox address;
        public string ProxyAddress { get { return address.Text.Trim(); } }

        public ProxyDialog(string current)
        {
            Text = "网络设置";
            Font = new Font("Microsoft YaHei UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(490, 205);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Controls.Add(new Label { Text = "默认自动识别系统代理、TUN 和常见本机代理。\n如需指定 HTTP 代理，在下方填写；留空恢复自动。", AutoSize = false, Location = new Point(22, 20), Size = new Size(445, 53) });
            address = new TextBox { Text = current, Location = new Point(24, 88), Size = new Size(442, 28) };
            Controls.Add(address);
            var ok = new Button { Text = "确定", Location = new Point(366, 145), Size = new Size(100, 34) };
            ok.Click += delegate
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(address.Text)) ProxyRoute.Parse(address.Text.Trim(), "自定义代理");
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            Controls.Add(ok);
            AcceptButton = ok;
        }
    }
}
