using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ChatGPTInstaller
{
    internal sealed class RunLog
    {
        private readonly object gate = new object();
        private bool retainedLogs;
        public readonly string FilePath;
        public event Action<string> Added;

        public RunLog()
        {
            Paths.Ensure();
            FilePath = Path.Combine(Paths.Logs, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 6) + ".log");
        }

        public void Write(string message)
        {
            Append(message, true);
        }

        public void WriteTechnical(string detail)
        {
            Append("[技术详情] " + detail, false);
        }

        public string ReadAll()
        {
            lock (gate) return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : "暂无运行记录";
        }

        private void Append(string message, bool showInWindow)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            lock (gate)
            {
                try
                {
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                    // 仅产生新记录时清理旧日志；后台状态查询不删除既有记录。
                    if (!retainedLogs)
                    {
                        foreach (string old in Directory.GetFiles(Paths.Logs, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(10)) Paths.DeleteFile(old);
                        retainedLogs = true;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var handler = Added;
            if (showInWindow && handler != null) handler(line);
        }
    }
}
