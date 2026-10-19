using System;
using System.Runtime.InteropServices;

namespace ChatGPTInstaller
{
    internal static class PlatformInfo
    {
        [DllImport("kernel32.dll")]
        private static extern void GetNativeSystemInfo(IntPtr info);

        public static string Architecture()
        {
            IntPtr info = Marshal.AllocHGlobal(64);
            try
            {
                GetNativeSystemInfo(info);
                short architecture = Marshal.ReadInt16(info);
                if (architecture == 9) return "x64";
                if (architecture == 12) return "arm64";
                throw new InvalidOperationException("ChatGPT 需要 64 位 Windows（x64 或 ARM64）。");
            }
            finally { Marshal.FreeHGlobal(info); }
        }

        public static void Check()
        {
            Architecture();
            if (Environment.OSVersion.Version < new Version(10, 0, 19041))
                throw new InvalidOperationException("需要 Windows 10 版本 2004（内部版本 19041）或更高版本。");
        }
    }
}
