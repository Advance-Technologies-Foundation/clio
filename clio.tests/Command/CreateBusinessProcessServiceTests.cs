using System;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// HTTP-layer tests for <see cref="CreateBusinessProcessService"/>: the wrapped <c>{"request":…}</c> body, the
/// resolved BuildProcess route, and every response branch (success / server-failure / unexpected shape). The
/// command tests substitute the service, so this is the only coverage of the actual clio→server contract.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreateBusinessProcessServiceTests {

	private const string Env = "sandbox";
	private const string BuildUrl = "http://sandbox/0/rest/ProcessDesignService/BuildProcess";
	private const string SampleDescriptor =
		"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	private static CreateBusinessProcessService CreateService(IApplicationClient client, out EnvironmentSettings env) {
		env = new EnvironmentSettings { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildProcess, env).Returns(BuildUrl);
		return new CreateBusinessProcessService(settings, factory, urlBuilder, Substitute.For<ILogger>());
	}

	[Test]
	[Description("Reads the server's warnings[] off a SUCCESSFUL build. The create side shipped this member with no test at all while the modify side had four, which is the asymmetry a review found: an undeclared or misspelled member deserializes to null with nothing red anywhere, so the channel would look plumbed and carry nothing. The classes it carries are connection notices and pre-configured-page sync notices - NOT the two formula cases the doc used to name, which went with the package's own validator.")]
	public void BuildProcess_ShouldReadWarnings_WhenServerReportsThemOnASuccessfulBuild() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(BuildUrl, Arg.Any<string>()).Returns(
			"{\"BuildProcessResult\":{\"success\":true,\"schemaName\":\"UsrSampleProcess\","
			+ "\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\","
			+ "\"warnings\":[\"Connection 'OmniChat' is not registered\"]}}");
		CreateBusinessProcessService service = CreateService(client, out _);

		// Act
		CreateBusinessProcessResult result = service.BuildProcess(Env,
			new CreateBusinessProcessRequest(SampleDescriptor, "MyApp"));

		// Assert
		result.Warnings.Should().ContainSingle(
			because: "a caveat the server raised about a build that SUCCEEDED must reach the caller - dropping "
				+ "it leaves code that looks like it warned and did not");
		result.Warnings[0].Should().Contain("OmniChat",
			because: "the warning names WHICH connection is affected, which is the only actionable part of it");
	}

	[Test]
	[Description("Leaves Warnings null when the server reports none, so an absent list cannot be mistaken for an empty-but-present one by a caller that enumerates it - and so the case of a server predating the member behaves the same way.")]
	public void BuildProcess_ShouldLeaveWarningsNull_WhenServerReportsNone() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(BuildUrl, Arg.Any<string>()).Returns(
			"{\"BuildProcessResult\":{\"success\":true,\"schemaName\":\"UsrSampleProcess\","
			+ "\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\"}}");
		CreateBusinessProcessService service = CreateService(client, out _);

		// Act
		CreateBusinessProcessResult result = service.BuildProcess(Env,
			new CreateBusinessProcessRequest(SampleDescriptor, "MyApp"));

		// Assert
		result.Warnings.Should().BeNull(
			because: "nothing was reported, and an empty list would tell a caller the server answered the "
				+ "question when it did not");
	}

	[Test]
	[Description("Posts the descriptor wrapped under 'request' to the resolved BuildProcess route (with the package override applied) and returns the created schema identity on success.")]
	public void BuildProcess_ShouldPostWrappedRequestToBuildRoute_AndReturnResult_OnSuccess() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(BuildUrl, Arg.Any<string>()).Returns(
			"{\"BuildProcessResult\":{\"success\":true,\"schemaName\":\"UsrSampleProcess\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\"}}");
		CreateBusinessProcessService service = CreateService(client, out _);

		// Act
		CreateBusinessProcessResult result = service.BuildProcess(Env,
			new CreateBusinessProcessRequest(SampleDescriptor, "MyApp"));

		// Assert
		result.SchemaName.Should().Be("UsrSampleProcess", because: "the schema name is read from the server result");
		result.SchemaUId.Should().Be("5c58c4c4-134b-4744-9c67-96d9c69c9d55", because: "the schema UId is read from the server result");
		client.Received(1).ExecutePostRequest(BuildUrl, Arg.Is<string>(body =>
			RequestName(body) == "UsrSampleProcess" && RequestPackage(body) == "MyApp"));
	}

	[Test]
	[Description("Surfaces the server's errorMessage as an exception when the BuildProcess result reports success=false.")]
	public void BuildProcess_ShouldThrowWithServerMessage_WhenSuccessFalse() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(BuildUrl, Arg.Any<string>()).Returns(
			"{\"BuildProcessResult\":{\"success\":false,\"errorMessage\":\"Package 'Custom' was not found.\"}}");
		CreateBusinessProcessService service = CreateService(client, out _);

		Action act = () => service.BuildProcess(Env, new CreateBusinessProcessRequest(SampleDescriptor));

		act.Should().Throw<InvalidOperationException>(because: "a server-reported failure must not be swallowed")
			.WithMessage("*Package 'Custom' was not found*");
	}

	[Test]
	[Description("Throws a clear error when the response envelope has no BuildProcessResult payload.")]
	public void BuildProcess_ShouldThrow_WhenResponseShapeUnexpected() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(BuildUrl, Arg.Any<string>()).Returns("{}");
		CreateBusinessProcessService service = CreateService(client, out _);

		Action act = () => service.BuildProcess(Env, new CreateBusinessProcessRequest(SampleDescriptor));

		act.Should().Throw<InvalidOperationException>(because: "a missing result payload is an unexpected server response")
			.WithMessage("*unexpected response shape*");
	}

	[Test]
	[Description("Throws (without calling the server) when the target environment is not registered.")]
	public void BuildProcess_ShouldThrow_WhenEnvironmentNotFound() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns((EnvironmentSettings)null);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		var service = new CreateBusinessProcessService(settings,
			Substitute.For<IApplicationClientFactory>(), urlBuilder, Substitute.For<ILogger>());

		Action act = () => service.BuildProcess(Env, new CreateBusinessProcessRequest(SampleDescriptor));

		act.Should().Throw<InvalidOperationException>(because: "an unknown environment cannot be targeted");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("Rejects a blank descriptor before any environment lookup or server call.")]
	public void BuildProcess_ShouldThrow_WhenDescriptorBlank() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		CreateBusinessProcessService service = CreateService(client, out _);

		Action act = () => service.BuildProcess(Env, new CreateBusinessProcessRequest("  "));

		act.Should().Throw<ArgumentException>(because: "a process descriptor is required");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	private static string RequestName(string body) => Wrapped(body)?["name"]?.GetValue<string>();

	private static string RequestPackage(string body) => Wrapped(body)?["packageName"]?.GetValue<string>();

	// The service wraps the descriptor under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)?["request"];
}
