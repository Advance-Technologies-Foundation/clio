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

    protected override string CreateRequestData<TRequest>(TRequest request) => "{\"uId\": \"" + request + "\"}";

    #endregion

    #region Methods: Public

    /// <inheritdoc cref="IPackageEditableMutator.SetPackageHotfix"/>
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

    #endregion
}
