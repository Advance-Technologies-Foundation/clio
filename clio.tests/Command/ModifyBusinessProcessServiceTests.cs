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
/// HTTP-layer tests for <see cref="ModifyBusinessProcessService"/>: the wrapped <c>{"request":{name|uid, operations}}</c>
/// body, the resolved ModifyProcess route, and each response branch. The command tests substitute the service, so
/// this is the only coverage of the actual clio→server contract for modify.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ModifyBusinessProcessServiceTests {

	private const string Env = "sandbox";
	private const string ModifyUrl = "http://sandbox/0/rest/ProcessDesignService/ModifyProcess";
	private const string Operations = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]";

	private static ModifyBusinessProcessService CreateService(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyProcess, env).Returns(ModifyUrl);
		return new ModifyBusinessProcessService(settings, factory, urlBuilder, Substitute.For<ILogger>());
	}

	[Test]
	[Description("Posts the process identity + operations array wrapped under 'request' to the ModifyProcess route and returns the applied-operation count on success.")]
	public void ModifyProcess_ShouldPostWrappedRequestToModifyRoute_AndReturnResult_OnSuccess() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":true,\"schemaName\":\"UsrProc\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"appliedOperations\":1}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		ModifyBusinessProcessResult result = service.ModifyProcess(Env,
			new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		result.AppliedOperations.Should().Be(1, because: "the applied-operation count is read from the server result");
		result.SchemaName.Should().Be("UsrProc", because: "the edited schema name is returned");
		client.Received(1).ExecutePostRequest(ModifyUrl, Arg.Is<string>(body =>
			Wrapped(body)["name"].GetValue<string>() == "UsrProc" && Wrapped(body)["operations"] is JsonArray));
	}

	[Test]
	[Description("Surfaces the server's errorMessage when the ModifyProcess result reports success=false (an aborted edit).")]
	public void ModifyProcess_ShouldThrowWithServerMessage_WhenSuccessFalse() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":false,\"errorMessage\":\"Element 'X' was not found.\"}}");
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		act.Should().Throw<InvalidOperationException>(because: "an aborted edit must surface the server message")
			.WithMessage("*Element 'X' was not found*");
	}

	[Test]
	[Description("Throws a clear error when the response envelope has no ModifyProcessResult payload.")]
	public void ModifyProcess_ShouldThrow_WhenResponseShapeUnexpected() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns("{}");
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		act.Should().Throw<InvalidOperationException>(because: "a missing result payload is an unexpected server response")
			.WithMessage("*unexpected response shape*");
	}

	[Test]
	[Description("Rejects a request with neither a process name nor a uid before any server call.")]
	public void ModifyProcess_ShouldThrow_WhenNeitherNameNorUid() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest(null, null, Operations));

		act.Should().Throw<ArgumentException>(because: "a modify target (name or uid) is required");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("Rejects operations content that is not a JSON array.")]
	public void ModifyProcess_ShouldThrow_WhenOperationsNotArray() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, "{}"));

		act.Should().Throw<InvalidOperationException>(because: "operations must be a JSON array of operations")
			.WithMessage("*array*");
	}

	// The service wraps the request under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
