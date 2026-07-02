using System;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public class CreatioRightsClientTests {

	private static (CreatioRightsClient client, IApplicationClient applicationClient) CreateClient(bool isNetCore = false) {
		// Real ServiceUrlBuilder so the route mapping + framework prefix are exercised; only the I/O boundary is faked.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = new ServiceUrlBuilder(new EnvironmentSettings {
			Uri = "http://localhost",
			IsNetCore = isNetCore
		});
		return (new CreatioRightsClient(applicationClient, urlBuilder), applicationClient);
	}

	[Test]
	[Category("Unit")]
	[Description("Posts the operation name to the WebApp-prefixed RightsService path and returns true when the result is true.")]
	public void GetCanExecuteOperation_PostsOperationAndReturnsTrue_WhenPermitted() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient(isNetCore: false);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetCanExecuteOperationResult\":true}");

		// Act
		bool granted = client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		granted.Should().BeTrue(because: "GetCanExecuteOperationResult=true means the operation is permitted");
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/RightsService.svc/GetCanExecuteOperation",
			"{\"operation\":\"CanManageThemes\"}", 100_000, 3, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when RightsService reports GetCanExecuteOperationResult=false.")]
	public void GetCanExecuteOperation_ReturnsFalse_WhenDenied() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetCanExecuteOperationResult\":false}");

		// Act
		bool granted = client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		granted.Should().BeFalse(because: "a false result means the operation is denied");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the response omits the result property (defensive default).")]
	public void GetCanExecuteOperation_ReturnsFalse_WhenResultMissing() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{}");

		// Act
		bool granted = client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		granted.Should().BeFalse(because: "a missing result property must default to not-permitted");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws a diagnostic InvalidOperationException when the RightsService response is a non-JSON body (e.g. an auth redirect).")]
	public void GetCanExecuteOperation_Throws_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		Action act = () => client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body signals the request never reached RightsService")
			.WithMessage("*RightsService*");
	}
}
