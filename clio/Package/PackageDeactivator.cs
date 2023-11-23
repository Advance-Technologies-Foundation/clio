using System;
using System.Linq;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

/// <inheritdoc cref="IPackageDeactivator"/>
internal class PackageDeactivator: IPackageDeactivator
{
	#region Fields: Private

	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Constructors: Public

	public PackageDeactivator(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationPackageListProvider = applicationPackageListProvider;
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Methods: Private

	private static string CreateRequestData(Guid packageUId) {
		return "\"" + packageUId + "\"";
	}
	private static void ThrowsErrorIfUnsuccessfulResponseReceived(BaseResponse deactivateResponse) {
		if (deactivateResponse.Success) {
			return;
		}
		throw new Exception(deactivateResponse.ErrorInfo.Message);
	}

	private BaseResponse SendDeactivateRequest(Guid packageUId) {
		return _applicationClient.ExecutePostRequest<BaseResponse>(
			_serviceUrlBuilder.Build("/ServiceModel/PackageService.svc/DeactivatePackage"),
			CreateRequestData(packageUId));
	}

	private Guid GetPackageUId(string packageName) {
		PackageInfo packageInfo =
			_applicationPackageListProvider.GetPackages("{}")
				.FirstOrDefault(package => package.Descriptor.Name == packageName);
		if (packageInfo is null) {
			throw new Exception($"Package with name {packageName} not found");
		}

		return packageInfo.Descriptor.UId;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc cref="IPackageDeactivator.Deactivate"/>
	public void Deactivate(string packageName) {
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		Guid packageUId = GetPackageUId(packageName);
		BaseResponse deactivateResponse = SendDeactivateRequest(packageUId);
		ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
	}

	#endregion

}