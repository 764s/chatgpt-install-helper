using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ChatGPTInstaller
{
    internal static class OfficialDownload
    {
        private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(60);

        public static long? ReadBytes()
        {
            var file = new FileInfo(Path.ChangeExtension(Paths.OfficialPackage, ".download"));
            try
            {
                if (!file.Exists) return null;
                long length = file.Length;
                try
                {
                    using (var reader = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        length = reader.Length;
                }
                catch (IOException ex)
                {
                    if (!IsSharingViolation(ex)) return null;
                }
                // 过期的、已没有写入者的临时文件不能永久挡住正常下载。
                if (DateTime.UtcNow - file.LastWriteTimeUtc > IdleLimit)
                {
                    try
                    {
                        using (File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)) { }
                        return null;
                    }
                    catch (IOException ex)
                    {
                        if (!IsSharingViolation(ex)) return null;
                    }
                }
                return length;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static bool IsSharingViolation(IOException error)
        {
            int code = error.HResult & 0xffff;
            return code == 32 || code == 33;
        }

        public static void WaitForPublish(CancellationToken token, IProgress<UiStatus> progress, RunLog log)
        {
            long? bytes = ReadBytes();
            if (!bytes.HasValue) return;
            log.Write("发现 Codex 的直连下载，等待它发布完整包。");
            long previous = -1;
            var idle = Stopwatch.StartNew();
            while (bytes.HasValue)
            {
                token.ThrowIfCancellationRequested();
                if (bytes.Value != previous)
                {
                    previous = bytes.Value;
                    idle.Restart();
                    // 临时文件不含可核实的目标与总长度，只报告实际字节数。
                    progress.Report(new UiStatus("跟随 Codex 下载", "Codex 已写入 " + NetworkClient.FormatBytes(previous), -1));
                }
                if (idle.Elapsed >= IdleLimit)
                    throw new IOException("Codex 下载连续 60 秒没有增长，已停止等待。可在 Codex 中查看下载状态。");
                if (token.WaitHandle.WaitOne(400)) token.ThrowIfCancellationRequested();
                bytes = ReadBytes();
            }
            token.ThrowIfCancellationRequested();
            progress.Report(new UiStatus("核对 Codex 下载结果", "重新核对发布版本和完整安装包…", -1));
        }
    }
}
