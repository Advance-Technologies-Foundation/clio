using System.Collections.Generic;

using Clio.Common;
using Clio.Package.Responses;

namespace Clio.Package;

internal class PackageActivator(IApplicationPackageListProvider applicationPackageListProvider,
    IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder): BasePackageOperation(applicationPackageListProvider, applicationClient, serviceUrlBuilder), IPackageActivator
{
    protected override string CreateRequestData<TRequest>(TRequest request) => "\"" + request + "\"";

    public IEnumerable<PackageActivationResultDto> Activate(string packageName)
    {
        packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
        PackageActivationResponse activationResponse =
            SendRequest<string, PackageActivationResponse>(PackageServiceUrl, "ActivatePackage", packageName);
        ThrowsErrorIfUnsuccessfulResponseReceived(activationResponse);
        return activationResponse.PackagesActivationResults;
    }
}
