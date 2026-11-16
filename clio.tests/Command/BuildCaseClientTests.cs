using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class BuildCaseClientTests {

	private const string Env = "sandbox";
	private const string BuildCaseUrl = "http://sandbox/0/rest/ProcessDesignService/BuildCase";

	private IOwnedApplicationClient _client;
	private BuildCaseClient _sut;

	[SetUp]
	public void Setup() {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		_client = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(_client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildCase, env).Returns(BuildCaseUrl);
		_sut = new BuildCaseClient(settings, factory, urlBuilder);
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_client.Dispose();
	}

	private static BuildCaseRequest Request() => new() {
		EntitySchemaName = "UsrOrder",
		StageColumnName = "UsrStatus",
		PackageName = "UsrOrders",
		Stages = [new BuildCaseStageRequest("New"), new BuildCaseStageRequest("Done")],
		AllowMissingDependencies = true,
		ValidateOnly = true
	};

	[Test]
	[Description("The pass is posted to the BuildCase route wrapped under 'request', with the server's camelCase keys, and an absent optional member is omitted rather than sent as null.")]
	public void Send_ShouldPostTheWrappedCamelCaseBody() {
		// Arrange
		string sent = null;
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Do<string>(body => sent = body))
			.Returns("{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");

		// Act
		_sut.Send(Env, Request());

		// Assert
		JsonObject request = JsonNode.Parse(sent)!["request"]!.AsObject();
		request["entitySchemaName"]!.GetValue<string>().Should().Be("UsrOrder", because: "the server reads camelCase keys");
		request["stageColumnName"]!.GetValue<string>().Should().Be("UsrStatus", because: "the server reads camelCase keys");
		request["packageName"]!.GetValue<string>().Should().Be("UsrOrders", because: "the server reads camelCase keys");
		request["stages"]!.AsArray()[1]!["value"]!.GetValue<string>().Should().Be("Done",
			because: "a stage travels as an object so a later member is additive");
		request["allowMissingDependencies"]!.GetValue<bool>().Should().BeTrue(because: "the flag is forwarded");
		request["validateOnly"]!.GetValue<bool>().Should().BeTrue(because: "the flag is forwarded");
		request.ContainsKey("name").Should().BeFalse(
			because: "an absent name lets the server derive one, and the server refuses nothing it declares");
	}

	[Test]
	[Description("The BuildCaseResult envelope is parsed into the response, nested blocks included.")]
	public void Send_ShouldParseTheEnvelope() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>()).Returns(
			"{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"created\",\"case\":{\"schemaName\":\"UsrOrderCase\","
			+ "\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"connectionCount\":2,"
			+ "\"stages\":[{\"name\":\"Stage1\",\"color\":\"#0C8701\"}]},\"missingDependencies\":[\"CrtBase\"],"
			+ "\"obligations\":{\"compileRequired\":false,\"artifacts\":[{\"kind\":\"dcm\"}]}}}");

		// Act
		BuildCaseReply reply = _sut.Send(Env, Request());

		// Assert
		reply.Kind.Should().Be(BuildCaseReplyKind.Envelope, because: "the body is the envelope");
		reply.Response.Outcome.Should().Be("created", because: "the outcome is read");
		reply.Response.Case.SchemaName.Should().Be("UsrOrderCase", because: "the case block is read");
		reply.Response.Case.Stages.Should().ContainSingle(stage => stage.Color == "#0C8701", because: "stages are read");
		reply.Response.MissingDependencies.Should().Equal(["CrtBase"], because: "missing dependencies drive the add");
		reply.Response.Obligations.Artifacts.Should().ContainSingle(artifact => artifact.Kind == "dcm",
			because: "per-artifact obligations are read");
	}

	[Test]
	[Description("A 404 on the route is RouteMissing: the installed package has no BuildCase.")]
	public void Send_ShouldClassifyA404_AsRouteMissing() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>())
			.Throws(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));

		// Act
		BuildCaseReply reply = _sut.Send(Env, Request());

		// Assert
		reply.Kind.Should().Be(BuildCaseReplyKind.RouteMissing, because: "a 404 means the operation does not exist");
	}

	[Test]
	[Description("An HTML page or a bare fault string is NotAnEnvelope, and the server's text is kept for the caller.")]
	public void Send_ShouldClassifyAnHtmlOrFaultBody_AsNotAnEnvelope() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>())
			.Returns("<html><body>Endpoint not found.</body></html>", "\"Object reference not set\"");

		// Act
		BuildCaseReply html = _sut.Send(Env, Request());
		BuildCaseReply fault = _sut.Send(Env, Request());

		// Assert
		html.Kind.Should().Be(BuildCaseReplyKind.NotAnEnvelope, because: "an HTML page is not the result");
		html.Detail.Should().Contain("Endpoint not found", because: "the server's text is relayed");
		fault.Kind.Should().Be(BuildCaseReplyKind.NotAnEnvelope, because: "a WCF fault string is not the result");
		fault.Detail.Should().Be("Object reference not set", because: "the fault text is unwrapped for the caller");
	}

	[Test]
	[Description("Any other transport failure is TransportFailure, never an exception, because what it means depends on the pass.")]
	public void Send_ShouldClassifyAnyOtherFailure_AsTransportFailure() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>())
			.Throws(new HttpRequestException("Gateway Timeout", null, HttpStatusCode.GatewayTimeout));

		// Act
		BuildCaseReply reply = _sut.Send(Env, Request());

		// Assert
		reply.Kind.Should().Be(BuildCaseReplyKind.TransportFailure, because: "a timeout says nothing about the route");
		reply.Detail.Should().Contain("Gateway Timeout", because: "the transport's text is relayed");
	}
}
