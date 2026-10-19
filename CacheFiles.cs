using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace ChatGPTInstaller
{
    internal static class CacheFiles
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            public uint Attributes;
            public FileTime CreationTime;
            public FileTime LastAccessTime;
            public FileTime LastWriteTime;
            public uint VolumeSerialNumber;
            public uint SizeHigh;
            public uint SizeLow;
            public uint NumberOfLinks;
            public uint IndexHigh;
            public uint IndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string newName, string existingName, IntPtr securityAttributes);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string source, string destination, uint flags);

        public static int TryCreateHardLink(string destination, string source)
        {
            return CreateHardLink(destination, source, IntPtr.Zero) ? 0 : Marshal.GetLastWin32Error();
        }

        public static void Publish(string temporary, string destination)
        {
            // 同目录原子换名；不用 File.Replace 合并旧文件元数据，避免影响硬链接指向的官方文件。
            if (!MoveFileEx(temporary, destination, 1))
                throw new IOException("无法保存已验证的官方缓存，原有下载进度已保留。", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        public static void RequirePrivateWriter(FileStream file)
        {
            FileInformation information;
            if (!GetFileInformationByHandle(file.SafeFileHandle, out information))
                throw new IOException("无法确认下载文件是否独占，已停止写入。", new Win32Exception(Marshal.GetLastWin32Error()));
            // 在截断或续写之前检查同一文件句柄，防止任何共享缓存被下载器改写。
            if (information.NumberOfLinks > 1)
                throw new InvalidDataException("共享安装包不能作为未完成的下载继续写入；已移除助手自己的引用，请重新下载。");
        }
    }
}
