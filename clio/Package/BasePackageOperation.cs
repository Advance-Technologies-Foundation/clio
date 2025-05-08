using System;
using System.Linq;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

internal abstract class BasePackageOperation
{

    #region Constants: Protected

    protected const string PackageServiceUrl = "PackageService.svc";

    #endregion

    #region Fields: Private

    private readonly IApplicationPackageListProvider _applicationPackageListProvider;
    private readonly IApplicationClient _applicationClient;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;

    #endregion

    #region Constructors: Protected

    protected BasePackageOperation(IApplicationPackageListProvider applicationPackageListProvider,
        IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
    {
        _applicationPackageListProvider = applicationPackageListProvider;
        _applicationClient = applicationClient;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    #endregion

    #region Methods: Protected

    protected static void ThrowsErrorIfUnsuccessfulResponseReceived(BaseResponse response)
    {
        if (response.Success)
        {
            return;
        }
        throw new Exception(response.ErrorInfo.Message);
    }

    protected abstract string CreateRequestData<TRequest>(TRequest request);

    protected Guid GetPackageUId(string packageName)
    {
        PackageInfo packageInfo =
            _applicationPackageListProvider.GetPackages("{}")
                                           .FirstOrDefault(package => package.Descriptor.Name == packageName);
        if (packageInfo is null)
        {
            throw new Exception($"Package with name {packageName} not found");
        }

        return packageInfo.Descriptor.UId;
    }

    protected TResponse SendRequest<TRequest, TResponse>(string serviceName, string methodName, TRequest request)
        where TResponse : BaseResponse, new()
    {
        string urlPart = $"/{string.Join("/", "ServiceModel", serviceName, methodName)}";
        string fullUrl = _serviceUrlBuilder.Build(urlPart);
        string requestData = CreateRequestData(request);
        return _applicationClient.ExecutePostRequest<TResponse>(fullUrl, requestData);
    }

    #endregion

}
