using System;
using System.Linq;
using Clio.Common.Responses;
using Clio.Package;
using Clio.Package.Responses;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
public class PackageActivatorTestCase : BasePackageOperationTestCase
{
	#region Properties: Private

	private PackageActivator _packageActivator;

	#endregion

	#region Methods: Public

	public override void Init()
	{
		base.Init();
		_packageActivator = new PackageActivator(ApplicationPackageListProvider, ApplicationClient, ServiceUrlBuilder);
	}

	[Test, Category("Unit")]
	public void Activate_ActivatesPackageByName()
	{
		const string packageName = "TestPackageName";
		const string packageName1 = "TestPackageName1";
		const string packageName2 = "TestPackageName2";
		const string fullUrl = "TestUrl";
		SetupBuildUrl("/ServiceModel/PackageService.svc/ActivatePackage", fullUrl);
		var activationResponse = new PackageActivationResponse
		{
			Success = true,
			PackagesActivationResults =
			[
				new PackageActivationResultDto
				{
					PackageName = packageName,
					Success = true
				},

				new PackageActivationResultDto
				{
					PackageName = packageName1,
					Success = true,
					Message = "SomeError"
				},

				new PackageActivationResultDto
				{
					PackageName = packageName2,
					Success = false
				}
			]
		};
		ApplicationClient.ExecutePostRequest<PackageActivationResponse>(fullUrl,
				Arg.Is<string>(data => data.Contains(packageName)))
			.Returns(activationResponse);
		var packageActivationResults = _packageActivator.Activate(packageName).ToArray();
		Assert.AreEqual(activationResponse.PackagesActivationResults.Length, packageActivationResults.Length);
		Assert.IsNotNull(packageActivationResults.First(result =>
			result.PackageName == packageName && result.Success && string.IsNullOrEmpty(result.Message)));
		Assert.IsNotNull(packageActivationResults.First(result =>
			result.PackageName == packageName1 && result.Success && !string.IsNullOrEmpty(result.Message)));
		Assert.IsNotNull(packageActivationResults.First(result =>
			result.PackageName == packageName2 && !result.Success));
	}

	[Test, Category("Unit")]
	public void Activate_ThrowsException_WhenPackageWasNotBeenActivated()
	{
		const string packageName = "TestPackageName";
		const string errorMessage = "Some error";
		ApplicationClient.ExecutePostRequest<PackageActivationResponse>(Arg.Any<string>(),
				Arg.Is<string>(data => data.Contains(packageName)))
			.Returns(new PackageActivationResponse
			{
				Success = false,
				ErrorInfo = new ErrorInfo
				{
					Message = errorMessage
				}
			});
		Assert.Throws<Exception>(() => _packageActivator.Activate(packageName), errorMessage);
	}

	#endregion
}