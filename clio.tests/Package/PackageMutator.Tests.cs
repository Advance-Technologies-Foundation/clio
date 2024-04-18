using System;
using Clio.Common.Responses;
using Clio.Package;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
public class PackageMutatorTestCase : BasePackageOperationTestCase
{
	#region Fields: Private

	private PackageEditableMutator _packageEditableMutator;

	#endregion

	#region Methods: Private

	private void SetupSuccessfulPostRequest(string fullUrl, Guid packageUId)
	{
		ApplicationClient.ExecutePostRequest<BaseResponse>(fullUrl,
				Arg.Is<string>(data => data.Contains(packageUId.ToString())))
			.Returns(new BaseResponse { Success = true });
	}

	private void SetupPackagesServiceBuildUrl(string methodName, string resultUrl)
	{
		SetupBuildUrl($"/ServiceModel/PackageService.svc/{methodName}", resultUrl);
	}

	private void SetupUnsuccessfulPostRequest(Guid packageUId, string errorMessage)
	{
		ApplicationClient.ExecutePostRequest<BaseResponse>(Arg.Any<string>(),
				Arg.Is<string>(data => data.Contains(packageUId.ToString())))
			.Returns(new BaseResponse { Success = false, ErrorInfo = new ErrorInfo {Message = errorMessage}});
	}

	#endregion

	#region Methods: Public

	public override void Init()
	{
		base.Init();
		_packageEditableMutator = new PackageEditableMutator(ApplicationPackageListProvider, ApplicationClient,
			ServiceUrlBuilder);
	}

	[Test, Category("UnitTests")]
	public void StartPackageHotfix_EnableHotFixMode()
	{
		string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		SetupGetPackagesResponse(
			CreatePackageInfo("SomePackage"),
			CreatePackageInfo(packageName, packageUId),
			CreatePackageInfo("SomePackage1")
		);
		const string fullUrl = "TestUrl";
		SetupPackagesServiceBuildUrl("StartPackageHotfix", fullUrl);
		SetupSuccessfulPostRequest(fullUrl, packageUId);
		Assert.DoesNotThrow(() => _packageEditableMutator.SetPackageHotfix(packageName, true));
	}

	[Test, Category("Unit")]
	[TestCase("")]
	[TestCase(null)]
	public void StartPackageHotfix_ThrowsException_WhenPackageByNameIsNotValid(string packageName)
	{
		Assert.Throws<ArgumentNullException>(() => _packageEditableMutator.SetPackageHotfix(packageName, true));
	}

	[Test, Category("Unit")]
	public void StartPackageHotfix_ThrowsException_WhenPackageNotFoundByName()
	{
		const string packageName = "TestPackageName";
		SetupGetPackagesResponse(CreatePackageInfo("SomePackage"));
		Assert.Throws<Exception>(() => _packageEditableMutator.SetPackageHotfix(packageName, false),
			$"Package with name {packageName} not found");
	}

	[Test, Category("Unit")]
	public void StartPackageHotfix_ThrowsException_WhenPackageModeWasNotBeenChanged()
	{
		const string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		const string errorMessage = "Some error";
		SetupGetPackagesResponse(CreatePackageInfo(packageName, packageUId));
		SetupUnsuccessfulPostRequest(packageUId, errorMessage);
		Assert.Throws<Exception>(() => _packageEditableMutator.SetPackageHotfix(packageName, false), errorMessage);
	}

	[Test, Category("UnitTests")]
	public void FinishPackageHotfix_DisableHotFixMode()
	{
		string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		SetupGetPackagesResponse(
			CreatePackageInfo("SomePackage"),
			CreatePackageInfo(packageName, packageUId),
			CreatePackageInfo("SomePackage1")
		);
		const string fullUrl = "TestUrl";
		SetupPackagesServiceBuildUrl("FinishPackageHotfix", fullUrl);
		SetupSuccessfulPostRequest(fullUrl, packageUId);
		Assert.DoesNotThrow(() => _packageEditableMutator.SetPackageHotfix(packageName, false));
	}

	[Test, Category("Unit")]
	[TestCase("")]
	[TestCase(null)]
	public void FinishPackageHotfix_ThrowsException_WhenPackageByNameIsNotValid(string packageName)
	{
		Assert.Throws<ArgumentNullException>(() => _packageEditableMutator.SetPackageHotfix(packageName, false));
	}

	[Test, Category("Unit")]
	public void FinishPackageHotfix_ThrowsException_WhenPackageNotFoundByName()
	{
		const string packageName = "TestPackageName";
		SetupGetPackagesResponse(CreatePackageInfo("SomePackage"));
		Assert.Throws<Exception>(() => _packageEditableMutator.SetPackageHotfix(packageName, false),
			$"Package with name {packageName} not found");
	}

	[Test, Category("Unit")]
	public void FinishPackageHotfix_ThrowsException_WhenPackageModeWasNotBeenChanged()
	{
		const string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		const string errorMessage = "Some error";
		SetupGetPackagesResponse(CreatePackageInfo(packageName, packageUId));
		SetupUnsuccessfulPostRequest(packageUId, errorMessage);
		Assert.Throws<Exception>(() => _packageEditableMutator.SetPackageHotfix(packageName, false), errorMessage);
	}

	#endregion
}