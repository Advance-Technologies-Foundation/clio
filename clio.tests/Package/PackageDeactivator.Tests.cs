using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
public class PackageDeactivatorTestCase
{
	#region Properties: Private

	private IApplicationPackageListProvider _applicationPackageListProvider;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private PackageDeactivator _packageDeactivator;

	#endregion

	#region Methods: Private

	private static PackageInfo CreatePackageInfo(string packageName, Guid? packageUId = null)
	{
		return new PackageInfo(new PackageDescriptor { Name = packageName, UId = packageUId ?? Guid.NewGuid() },
			string.Empty,
			Enumerable.Empty<string>());
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void Init()
	{
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_packageDeactivator = new PackageDeactivator(_applicationPackageListProvider, _applicationClient,
			_serviceUrlBuilder);
	}

	[Test, Category("Unit")]
	public void Deactivate_DeactivatesPackageByName()
	{
		const string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		_applicationPackageListProvider.GetPackages("{}").Returns(new List<PackageInfo>
		{
			CreatePackageInfo("SomePackage"),
			CreatePackageInfo(packageName, packageUId),
			CreatePackageInfo("SomePackage1")
		});
		const string fullUrl = "TestUrl";
		_serviceUrlBuilder.Build("/ServiceModel/PackageService.svc/DeactivatePackage").Returns(fullUrl);
		_applicationClient.ExecutePostRequest<BaseResponse>(fullUrl,
				Arg.Is<string>(data => data.Contains(packageUId.ToString())))
			.Returns(new BaseResponse { Success = true });
		Assert.DoesNotThrow(() => _packageDeactivator.Deactivate(packageName));
	}

	[Test, Category("Unit")]
	[TestCase("")]
	[TestCase(null)]
	public void Deactivate_ThrowsException_WhenPackageByNameIsNotValid(string packageName)
	{
		Assert.Throws<ArgumentNullException>(() => _packageDeactivator.Deactivate(packageName));
	}

	[Test, Category("Unit")]
	public void Deactivate_ThrowsException_WhenPackageNotFoundByName()
	{
		const string packageName = "TestPackageName";
		_applicationPackageListProvider.GetPackages("{}").Returns(new List<PackageInfo>
		{
			CreatePackageInfo("SomePackage")
		});
		Assert.Throws<Exception>(() => _packageDeactivator.Deactivate(packageName),
			$"Package with name {packageName} not found");
	}

	[Test, Category("Unit")]
	public void Deactivate_ThrowsException_WhenPackageWasNotBeenDeactivated()
	{
		const string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		const string errorMessage = "Some error";
		_applicationPackageListProvider.GetPackages("{}").Returns(new List<PackageInfo>
		{
			CreatePackageInfo(packageName, packageUId),
		});
		_applicationClient.ExecutePostRequest<BaseResponse>(Arg.Any<string>(),
				Arg.Is<string>(data => data.Contains(packageUId.ToString())))
			.Returns(new BaseResponse { Success = false, ErrorInfo = new ErrorInfo {Message = errorMessage}});

		Assert.Throws<Exception>(() => _packageDeactivator.Deactivate(packageName), errorMessage);
	}

	#endregion

}