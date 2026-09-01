using Clio.Common;
using Clio.Query;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Query;

/// <summary>
/// call-service routes the response body to the classifier that matches the endpoint it called.
/// Every response used to be classified as a custom-service body, which misread the documented
/// <c>odata/...</c> route in both directions.
/// </summary>
[TestFixture]
[Property("Module", "Query")]
public class CallServiceCommandResponseContextTests : BaseCommandTests<CallServiceCommandOptions> {

	private const string ODataServicePath = "odata/UsrLog";
	private const string ODataUrl = "http://host/odata/UsrLog";

	private static int ExecutePost(string servicePath, string url, string response,
		out IApplicationClient applicationClient) {
		applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		//The URL is built from the NORMALIZED path, which is also the path the response context is
		//derived from - stubbing the raw option value would miss a case carrying a 0/ prefix.
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(url);
		applicationClient.ExecutePostRequest(url, Arg.Any<string>()).Returns(response);
		CallServiceCommand command = new(applicationClient, new EnvironmentSettings(), serviceUrlBuilder,
			Substitute.For<IFileSystem>());
		return command.Execute(new CallServiceCommandOptions {
			ServicePath = servicePath,
			HttpMethodName = "POST",
			RequestBody = "{}",
			IsSilent = true
		});
	}

	[Test]
	[Category("Unit")]
	[Description("A successful OData POST echo carrying a business column named Success is not a failed BaseResponse envelope.")]
	public void Execute_Should_Succeed_When_An_ODataPost_Echo_Carries_A_Business_Success_Column() {
		// Act - the record was created; Success here is an ordinary entity column, not a service envelope.
		int exitCode = ExecutePost(ODataServicePath, ODataUrl,
			"""{"@odata.context":"http://host/odata/$metadata#UsrLog/$entity","Id":"1","Success":false}""",
			out IApplicationClient _);

		// Assert
		exitCode.Should().Be(0,
			because: "classifying an odata/ response as a custom-service body made BaseResponse detection "
				+ "report a created record as failed, and a retry on that exit 1 creates a duplicate");
	}

	[Test]
	[Category("Unit")]
	[Description("An OData body whose identity is proven by @odata.context is not a server error just because it carries a column named ExceptionMessage.")]
	public void Execute_Should_Succeed_When_An_ODataPost_Echo_Carries_A_Business_ExceptionMessage_Column() {
		// Act
		int exitCode = ExecutePost(ODataServicePath, ODataUrl,
			"""{"@odata.context":"http://host/odata/$metadata#UsrLog/$entity","Id":"1","ExceptionMessage":"ordinary business value"}""",
			out IApplicationClient _);

		// Assert
		exitCode.Should().Be(0,
			because: "ExceptionMessage/ExceptionType/StackTrace are legal persisted column names, and the "
				+ "annotation already proves this payload is an entity rather than an error body");
	}

	[Test]
	[Category("Unit")]
	[Description("A bare OData Message error fails the call even though its wording is not the narrow routing-miss hint.")]
	public void Execute_Should_Fail_When_An_ODataPost_Returns_A_Bare_Message_Error() {
		// Act
		int exitCode = ExecutePost(ODataServicePath, ODataUrl,
			"""{"Message":"An error has occurred."}""",
			out IApplicationClient _);

		// Assert
		exitCode.Should().Be(1,
			because: "the OData protocol fixes the payload shape, so a bare Message body is an error there - "
				+ "classifying it as a custom-service payload saved it as a successful response");
	}

	[Test]
	[Category("Unit")]
	[Description("A custom endpoint still owns its own contract: a bare Message body outside odata/ stays a successful payload.")]
	public void Execute_Should_Succeed_When_A_Custom_Service_Returns_A_Bare_Message_Body() {
		// Act
		int exitCode = ExecutePost("ServiceModel/CustomService.svc/Ping",
			"http://host/ServiceModel/CustomService.svc/Ping",
			"""{"Message":"OK"}""",
			out IApplicationClient _);

		// Assert
		exitCode.Should().Be(0,
			because: "outside OData only the routing-miss wording counts, so a custom endpoint answering "
				+ "{\"Message\":\"OK\"} must still be saved");
	}

	[Test]
	[Category("Unit")]
	[Description("The odata/ segment is recognized after the numeric prefixes NormalizeServicePath already strips.")]
	public void Execute_Should_Fail_When_A_Prefixed_ODataPath_Returns_A_Bare_Message_Error() {
		// Act - "/0/odata/UsrLog" normalizes to "odata/UsrLog", which is the path the URL is built from.
		int exitCode = ExecutePost("/0/odata/UsrLog", ODataUrl,
			"""{"Message":"An error has occurred."}""",
			out IApplicationClient _);

		// Assert
		exitCode.Should().Be(1,
			because: "the context is derived from the same normalized path the URL is built from, so a "
				+ "0/ prefix cannot change which classifier the body is routed to");
	}
}
