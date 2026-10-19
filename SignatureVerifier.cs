using System;
using System.Runtime.InteropServices;

namespace ChatGPTInstaller
{
    internal static class SignatureVerifier
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr File;
            public IntPtr Subject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustData
        {
            public uint Size;
            public IntPtr Policy;
            public IntPtr Sip;
            public uint Ui;
            public uint Revocation;
            public uint Choice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr State;
            public IntPtr Url;
            public uint Flags;
            public uint Context;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, [In] ref Guid action, ref TrustData data);

        public static void Verify(string path)
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = path };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf(typeof(TrustData)), Ui = 2, Choice = 1,
                File = pointer, StateAction = 1, Flags = 0x1000
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                // 使用本机信任链，避免企业代理阻断吊销服务器导致无限等待；部署服务仍会再次验证包。
                int status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                if (status != 0) throw new InvalidOperationException("Windows 未通过安装包签名校验（0x" +
                    unchecked((uint)status).ToString("X8") + "）。请检查系统时间、证书和网络后重试。");
            }
            finally
            {
                data.StateAction = 2;
                WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                Marshal.DestroyStructure(pointer, typeof(TrustFile));
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
