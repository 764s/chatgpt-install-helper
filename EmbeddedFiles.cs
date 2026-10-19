using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace ChatGPTInstaller
{
    internal static class EmbeddedFiles
    {
        public static string Read(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Sidebar." + name))
            {
                if (stream == null) throw new InvalidOperationException("安装助手缺少侧栏资源：" + name);
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
    }
}
