using System;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

internal class PackageEditableMutator : BasePackageOperation, IPackageEditableMutator
{
	#region Constructors: Public

	public PackageEditableMutator(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) :
		base(applicationPackageListProvider, applicationClient, serviceUrlBuilder)
	{
	}

	#endregion

	#region Methods: Protected

	protected override string CreateRequestData<TRequest>(TRequest request)
	{
		return "{\"uId\": \"" + request + "\"}";
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc cref="IPackageEditableMutator.StartPackageHotfix"/>
	public void StartPackageHotfix(string packageName)
	{
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		Guid packageUId = GetPackageUId(packageName);
		BaseResponse deactivateResponse = SendRequest<Guid, BaseResponse>(PackageServiceUrl, "StartPackageHotfix", packageUId);
		ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
	}

	/// <inheritdoc cref="IPackageEditableMutator.FinishPackageHotfix"/>
	public void FinishPackageHotfix(string packageName)
	{
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		Guid packageUId = GetPackageUId(packageName);
		BaseResponse deactivateResponse = SendRequest<Guid, BaseResponse>(PackageServiceUrl, "FinishPackageHotfix", packageUId);
		ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
	}

	#endregion
}