using System;
using System.Linq;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
public class BasePackageOperationTestCase
{
	#region Region: NestedClass

	private class TestPackageOperation(
		IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder)
		: BasePackageOperation(applicationPackageListProvider, applicationClient, serviceUrlBuilder)
	{
		protected override string CreateRequestData<TRequest>(TRequest request) => request.ToString();

		internal void ProcessUnsuccessfulResponse(BaseResponse response)
		{
			ThrowsErrorIfUnsuccessfulResponseReceived(response);
		}

		internal Guid GetPackageByName(string packageName)
		{
			return GetPackageUId(packageName);
		}

		internal BaseResponse ExecuteRequest(string serviceName, string methodName, string packageName)
		{
			return SendRequest<string, BaseResponse>(serviceName, methodName, packageName);
		}
	}

	#endregion

	#region Properties: Private

	private TestPackageOperation _testPackageOperation;

	#endregion

	#region Properties: Protected

	protected IApplicationClient ApplicationClient;
	protected IServiceUrlBuilder ServiceUrlBuilder;
	protected IApplicationPackageListProvider ApplicationPackageListProvider;

	#endregion

	#region Methods: Private

	private void InitTestPackageOperation()
	{
		_testPackageOperation = new TestPackageOperation(ApplicationPackageListProvider, ApplicationClient,
			ServiceUrlBuilder);
	}

	#endregion

	#region Methods: Protected

	protected static PackageInfo CreatePackageInfo(string packageName, Guid? packageUId = null)
	{
		return new PackageInfo(new PackageDescriptor { Name = packageName, UId = packageUId ?? Guid.NewGuid() },
			string.Empty,
			Enumerable.Empty<string>());
	}


	protected void SetupBuildUrl(string requestUrl, string responseUrl)
	{
		ServiceUrlBuilder.Build(requestUrl).Returns(responseUrl);
	}

	protected void SetupGetPackagesResponse(params PackageInfo[] packagesInfos)
	{
		ApplicationPackageListProvider.GetPackages("{}").Returns(packagesInfos);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public virtual void Init()
	{
		ApplicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		ApplicationClient = Substitute.For<IApplicationClient>();
		ServiceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	}

	[Test, Category("Unit")]
	public void GetPackageByName_ReturnsUIdName()
	{
		InitTestPackageOperation();
		const string packageName = "TestPackageName";
		Guid packageUId = Guid.NewGuid();
		var packagesInfos = new[]
		{
			CreatePackageInfo("TestPackage1"),
			CreatePackageInfo(packageName, packageUId),
			CreatePackageInfo("TestPackage2"),
		};
		SetupGetPackagesResponse(packagesInfos);
		var actualUId = _testPackageOperation.GetPackageByName(packageName);
		packageUId.Should().Be(actualUId);
	}

	[Test, Category("Unit")]
	public void GetPackageByName_ThrowsException_WhenPackageNotFoundByName()
	{
		InitTestPackageOperation();
		var packagesInfos = new[]
		{
			CreatePackageInfo("TestPackage1"),
		};
		SetupGetPackagesResponse(packagesInfos);
		string packageName = "TestPackage2";
		
		Action act = ()=> _testPackageOperation.GetPackageByName(packageName);
		act.Should().Throw<Exception>().WithMessage($"Package with name {packageName} not found");
	}

	[Test, Category("Unit")]
	public void GetPackageByName_ThrowsException_WhenPackageProviderReturnsEmptyCollection()
	{
		InitTestPackageOperation();
		SetupGetPackagesResponse();
		string packageName = "TestPackage";
		Action act = ()=> _testPackageOperation.GetPackageByName(packageName);
		act.Should().Throw<Exception>().WithMessage($"Package with name {packageName} not found");
	}

	[Test, Category("Unit")]
	public void BaseClass_ThrowsError_WhenReceivedUnsuccessfulResponse()
	{
		InitTestPackageOperation();
		const string errorMessage = "Some error";
		var response = new BaseResponse
		{
			Success = false,
			ErrorInfo = new ErrorInfo
			{
				Message = errorMessage
			}
		};
		Action act = () => _testPackageOperation.ProcessUnsuccessfulResponse(response);
		act.Should().Throw<Exception>().WithMessage(errorMessage);
	}

	[Test, Category("Unit")]
	public void BaseClass_DoesNotThrowError_WhenReceivedSuccessfulResponse()
	{
		InitTestPackageOperation();
		var response = new BaseResponse
		{
			Success = true,
		};
		Action act = ()=> _testPackageOperation.ProcessUnsuccessfulResponse(response);
		act.Should().NotThrow<Exception>();
	}

	[Test, Category("Unit")]
	[TestCase(true)]
	[TestCase(false)]
	public void SendRequest_ReturnsExpectedResponse(bool isSuccess)
	{
		InitTestPackageOperation();
		string testData = "TestData";
		string serviceName = "TestServiceName";
		string methodName = "TestMethodName";
		string fullUrl = $"/ServiceModel/{serviceName}/{methodName}";
		ServiceUrlBuilder.Build(fullUrl).Returns(fullUrl);
		var response = new BaseResponse
		{
			Success = isSuccess
		};
		ApplicationClient.ExecutePostRequest<BaseResponse>(fullUrl, testData).Returns(response);
		var actualResponse = _testPackageOperation.ExecuteRequest(serviceName, methodName, testData);
		actualResponse.Success.Should().Be(isSuccess);
	}

	#endregion
}