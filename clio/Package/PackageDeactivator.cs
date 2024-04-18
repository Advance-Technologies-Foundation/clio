using System;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

/// <inheritdoc cref="IPackageDeactivator"/>
internal class PackageDeactivator : BasePackageOperation, IPackageDeactivator
{
	#region Constructors: Public

	public PackageDeactivator(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) :
		base(applicationPackageListProvider, applicationClient, serviceUrlBuilder)
	{
	}

	#endregion

	#region Methods: Protected

	protected override string CreateRequestData<TRequest>(TRequest request)
	{
		return "\"" + request + "\"";
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc cref="IPackageDeactivator.Deactivate"/>
	public void Deactivate(string packageName)
	{
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		Guid packageUId = GetPackageUId(packageName);
		BaseResponse deactivateResponse = SendRequest<Guid, BaseResponse>(PackageServiceUrl, "DeactivatePackage", packageUId);
		ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
	}

	#endregion
}