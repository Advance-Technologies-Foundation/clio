using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package.Responses;

namespace Clio.Package;

public class PackageActivator: IPackageActivator
{
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	public PackageActivator(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	{
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	private static string CreateRequestData(string packageName) {
		return "\"" + packageName + "\"";
	}
	private PackageActivationResponse SendActivationRequest(string packageName) {
		return _applicationClient.ExecutePostRequest<PackageActivationResponse>(
			_serviceUrlBuilder.Build("/ServiceModel/PackageService.svc/ActivatePackage"),
			CreateRequestData(packageName));
	}

	public IEnumerable<PackageActivationResultDto> Activate(string packageName)
	{
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		PackageActivationResponse activationResponse = SendActivationRequest(packageName);
		if (!activationResponse.Success)
		{
			throw new Exception(activationResponse.ErrorInfo?.Message);
		}
		return activationResponse.PackagesActivationResults;
	}
}