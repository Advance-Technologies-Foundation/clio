using System.Collections.Generic;
using Clio.Common;
using Clio.Package.Responses;

namespace Clio.Package;

internal class PackageActivator : BasePackageOperation, IPackageActivator
{
	#region Constructors: Public

	public PackageActivator(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) :
		base(applicationPackageListProvider, applicationClient, serviceUrlBuilder)
	{
	}

	#endregion

	#region Methods: Protected

	protected override string CreateRequestData<TRequest>(TRequest request) => "\"" + request + "\"";

	#endregion

	#region Methods: Public

	public IEnumerable<PackageActivationResultDto> Activate(string packageName)
	{
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		PackageActivationResponse activationResponse =
			SendRequest<string, PackageActivationResponse>(PackageServiceUrl, "ActivatePackage", packageName);
		ThrowsErrorIfUnsuccessfulResponseReceived(activationResponse);
		return activationResponse.PackagesActivationResults;
	}

	#endregion
}