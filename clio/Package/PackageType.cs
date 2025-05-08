using System.Runtime.Serialization;

namespace Clio.Package;

[DataContract]
public enum PackageType
{
    /// <summary>
    ///     Default package type
    /// </summary>
    [EnumMember] General,

    /// <summary>
    ///     Package won't be compiled during installation and can be compiled without configuration build
    /// </summary>
    [EnumMember] Assembly
}
