using System;
using System.Collections.Generic;

namespace ChatGPTInstaller
{
    internal sealed class ReleaseNote
    {
        public string Title;
        public string Content;
        public Uri Source;
        public DateTime Published;
        public readonly List<Version> Versions = new List<Version>();
    }
}
