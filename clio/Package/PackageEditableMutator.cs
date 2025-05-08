using System;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

internal class PackageEditableMutator(
    IApplicationPackageListProvider applicationPackageListProvider,
    IApplicationClient applicationClient,
    IServiceUrlBuilder serviceUrlBuilder)
    : BasePackageOperation(applicationPackageListProvider, applicationClient, serviceUrlBuilder),
        IPackageEditableMutator
{
    /// <inheritdoc cref="IPackageEditableMutator.SetPackageHotfix" />
    public void SetPackageHotfix(string packageName, bool state)
    {
        packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
        Guid packageUId = GetPackageUId(packageName);
        if (state)
        {
            BaseResponse deactivateResponse =
                SendRequest<Guid, BaseResponse>(PackageServiceUrl, "StartPackageHotfix", packageUId);
            ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
        }
        else
        {
            BaseResponse deactivateResponse =
                SendRequest<Guid, BaseResponse>(PackageServiceUrl, "FinishPackageHotfix", packageUId);
            ThrowsErrorIfUnsuccessfulResponseReceived(deactivateResponse);
        }
    }

    protected override string CreateRequestData<TRequest>(TRequest request) => "{\"uId\": \"" + request + "\"}";
}
