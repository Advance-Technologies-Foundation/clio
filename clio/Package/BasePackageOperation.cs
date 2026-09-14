using System;
using System.Linq;
using System.Threading;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

internal abstract class BasePackageOperation
{
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	protected const string PackageServiceUrl = "PackageService.svc";
	protected BasePackageOperation(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	{
		_applicationPackageListProvider = applicationPackageListProvider;
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	protected static void ThrowsErrorIfUnsuccessfulResponseReceived(BaseResponse response)
	{
		if (response.Success)
		{
			return;
		}
		throw new Exception(response.ErrorInfo.Message);
	}

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

	/// <summary>
	/// Posts <paramref name="request"/> to a package service method.
	/// </summary>
	/// <param name="serviceName">Service the method belongs to (for example <c>PackageService.svc</c>).</param>
	/// <param name="methodName">Method to invoke.</param>
	/// <param name="request">Request payload, serialized by <see cref="CreateRequestData"/>.</param>
	/// <param name="requestTimeoutMs">
	/// Per-request timeout in milliseconds. Defaults to <see cref="Timeout.Infinite"/>, which keeps the
	/// historical behavior; pass a bound when the call runs inside an already-failing operation, where an
	/// environment that stops answering must cost a bounded wait rather than block the caller forever.
	/// </param>
	/// <returns>The deserialized response.</returns>
	protected TResponse SendRequest<TRequest, TResponse>(string serviceName, string methodName, TRequest request,
		int requestTimeoutMs = Timeout.Infinite)
		where TResponse : BaseResponse, new()
	{
		string urlPart = $"/{string.Join("/", "ServiceModel", serviceName, methodName)}";
		string fullUrl = _serviceUrlBuilder.Build(urlPart);
		string requestData = CreateRequestData(request);
		return _applicationClient.ExecutePostRequest<TResponse>(fullUrl, requestData, requestTimeoutMs);
	}

	protected abstract string CreateRequestData<TRequest>(TRequest request);
}