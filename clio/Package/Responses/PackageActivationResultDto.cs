using System.Runtime.Serialization;

namespace Clio.Package.Responses;

public class PackageActivationResultDto
{

    /// <summary>
    /// Gets or sets a value indicating whether is activation successful.
    /// </summary>
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets package name.
    /// </summary>
    [DataMember(Name = "packageName")]
    public string PackageName { get; set; }

    /// <summary>
    /// Gets or sets activation error message.
    /// </summary>
    [DataMember(Name = "message")]
    public string Message { get; set; }
}
