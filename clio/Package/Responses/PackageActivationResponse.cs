namespace Clio.Package.Responses;

using System.Runtime.Serialization;
using Clio.Common.Responses;

#region Class: PackageActivationResponse

[DataContract]
public class PackageActivationResponse : BaseResponse
{

	#region Properties: Public

	[DataMember(Name = "packagesActivationResults")]
	public PackageActivationResultDto[] PackagesActivationResults { get; set; }

	#endregion

}

#endregion
