using System.Runtime.Serialization;

namespace ChatGPTInstaller
{
    [DataContract]
    internal sealed class ManifestPackage
    {
        [DataMember(Name = "arch", IsRequired = true)] public string Architecture { get; set; }
        [DataMember(Name = "packageUrl", IsRequired = true)] public string PackageUrl { get; set; }
        [DataMember(Name = "length", IsRequired = true)] public long Length { get; set; }
        [DataMember(Name = "sha256", IsRequired = true)] public string Sha256 { get; set; }
    }
}
