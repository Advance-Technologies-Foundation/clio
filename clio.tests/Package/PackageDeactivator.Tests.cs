using System;

using Clio.Common.Responses;
using Clio.Package;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
public class PackageDeactivatorTestCase : BasePackageOperationTestCase
{
    private PackageDeactivator _packageDeactivator;

    public override void Init()
    {
        base.Init();
        _packageDeactivator = new PackageDeactivator(applicationPackageListProvider, applicationClient,
            serviceUrlBuilder);
    }

    [Test]
    [Category("Unit")]
    public void Deactivate_DeactivatesPackageByName()
    {
        const string packageName = "TestPackageName";
        Guid packageUId = Guid.NewGuid();
        SetupGetPackagesResponse(
            CreatePackageInfo("SomePackage"),
            CreatePackageInfo(packageName, packageUId),
            CreatePackageInfo("SomePackage1"));
        const string fullUrl = "TestUrl";
        SetupBuildUrl("/ServiceModel/PackageService.svc/DeactivatePackage", fullUrl);
        applicationClient.ExecutePostRequest<BaseResponse>(
            fullUrl,
                Arg.Is<string>(data => data.Contains(packageUId.ToString())))
            .Returns(new BaseResponse { Success = true });
        Assert.DoesNotThrow(() => _packageDeactivator.Deactivate(packageName));
    }

    [Test]
    [Category("Unit")]
    [TestCase("")]
    [TestCase(null)]
    public void Deactivate_ThrowsException_WhenPackageByNameIsNotValid(string packageName) =>
        Assert.Throws<ArgumentNullException>(() => _packageDeactivator.Deactivate(packageName));

    [Test]
    [Category("Unit")]
    public void Deactivate_ThrowsException_WhenPackageWasNotBeenDeactivated()
    {
        const string packageName = "TestPackageName";
        Guid packageUId = Guid.NewGuid();
        const string errorMessage = "Some error";
        SetupGetPackagesResponse(CreatePackageInfo(packageName, packageUId));
        applicationClient.ExecutePostRequest<BaseResponse>(
            Arg.Any<string>(),
                Arg.Is<string>(data => data.Contains(packageUId.ToString())))
            .Returns(new BaseResponse { Success = false, ErrorInfo = new ErrorInfo { Message = errorMessage } });

        Assert.Throws<Exception>(() => _packageDeactivator.Deactivate(packageName), errorMessage);
    }
}
