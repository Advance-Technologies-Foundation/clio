using System;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

/// <inheritdoc cref="IPackageDeactivator" />
internal class PackageDeactivator(
    IApplicationPackageListProvider applicationPackageListProvider,
    IApplicationClient applicationClient,
    IServiceUrlBuilder serviceUrlBuilder)
    : BasePackageOperation(applicationPackageListProvider, applicationClient, serviceUrlBuilder), IPackageDeactivator
{
    /// <inheritdoc cref="IPackageDeactivator.Deactivate" />
    public void Deactivate(string packageName)
    {
        packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
        Guid packageUId = GetPackageUId(packageName);
        BaseResponse deactivateResponse =
            SendRequest<Guid, BaseResponse>(PackageServiceUrl, "DeactivatePackage", packageUId);
        ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
    }

    protected override string CreateRequestData<TRequest>(TRequest request) => "\"" + request + "\"";
}
