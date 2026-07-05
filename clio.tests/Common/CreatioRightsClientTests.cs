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
	[Description("Posts the operation name to the WebApp-prefixed RightsService REST path and returns true when the result is true.")]
	public void GetCanExecuteOperation_ShouldPostOperationAndReturnTrue_WhenPermitted() {
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
			"http://localhost/0/rest/RightsService/GetCanExecuteOperation",
			"{\"operation\":\"CanManageThemes\"}", 100_000, 3, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when RightsService reports GetCanExecuteOperationResult=false.")]
	public void GetCanExecuteOperation_ShouldReturnFalse_WhenDenied() {
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
	public void GetCanExecuteOperation_ShouldReturnFalse_WhenResultMissing() {
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
	[Description("Caps the response-body excerpt echoed into the diagnostic, so a multi-kilobyte HTML error page does not flood the error message.")]
	public void GetCanExecuteOperation_ShouldTruncateEchoedBody_WhenNonJsonResponseIsLarge() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		string largeBody = new string('x', 5000);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(largeBody);

		// Act
		Action act = () => client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body is a transport-level failure")
			.Which.Message.Length.Should().BeLessThan(700,
				because: "the echoed body excerpt is capped at 500 characters plus the diagnostic prefix");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws a diagnostic InvalidOperationException when the RightsService response is a non-JSON body (e.g. an auth redirect).")]
	public void GetCanExecuteOperation_ShouldThrow_WhenResponseBodyIsNotParseableJson() {
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
