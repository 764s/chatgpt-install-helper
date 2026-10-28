using System.Runtime.Serialization;

namespace ChatGPTInstaller
{
    [DataContract]
    internal sealed class ReleaseFeedPackage
    {
        [DataMember(Name = "architecture", IsRequired = true)]
        public string Architecture { get; set; }

        [DataMember(Name = "downloadUrl", IsRequired = true)]
        public string DownloadUrl { get; set; }
    }
}
