using System.Runtime.Serialization;

using Clio.Common.Responses;

namespace Clio.Package.Responses;

[DataContract]
public class PackageActivationResponse : BaseResponse
{
    [DataMember(Name = "packagesActivationResults")]
    public PackageActivationResultDto[] PackagesActivationResults { get; set; }
}
