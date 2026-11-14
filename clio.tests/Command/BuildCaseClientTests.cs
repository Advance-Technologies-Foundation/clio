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
	private const string DescribeCaseUrl = "http://sandbox/0/rest/ProcessDesignService/DescribeCase";
	private const string ModifyCaseUrl = "http://sandbox/0/rest/ProcessDesignService/ModifyCase";

	private EnvironmentSettings _env;
	private IOwnedApplicationClient _client;
	private IApplicationClientFactory _factory;
	private BuildCaseClient _sut;

	[SetUp]
	public void Setup() {
		_env = new EnvironmentSettings { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(_env);
		_client = Substitute.For<IOwnedApplicationClient>();
		_factory = Substitute.For<IApplicationClientFactory>();
		_factory.CreateEnvironmentClient(_env).Returns(_client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildCase, _env).Returns(BuildCaseUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeCase, _env).Returns(DescribeCaseUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyCase, _env).Returns(ModifyCaseUrl);
		_sut = new BuildCaseClient(settings, _factory, urlBuilder);
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_factory.ClearReceivedCalls();
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

	private DynamicCaseReply<BuildCaseResponse> Build(BuildCaseRequest request) {
		using IDynamicCaseSession session = _sut.OpenSession(Env);
		return session.Build(request);
	}

	[Test]
	[Description("The pass is posted to the BuildCase route wrapped under 'request', with the server's camelCase keys, and an absent optional member is omitted rather than sent as null.")]
	public void Build_ShouldPostTheWrappedCamelCaseBody() {
		// Arrange
		string sent = null;
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Do<string>(body => sent = body))
			.Returns("{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");

		// Act
		Build(Request());

		// Assert
		JsonObject request = JsonNode.Parse(sent)!["request"]!.AsObject();
		request["entitySchemaName"]!.GetValue<string>().Should().Be("UsrOrder", because: "the server reads camelCase keys");
		request["stageColumnName"]!.GetValue<string>().Should().Be("UsrStatus", because: "the server reads camelCase keys");
		request["packageName"]!.GetValue<string>().Should().Be("UsrOrders", because: "the server reads camelCase keys");
		request["stages"]!.AsArray()[1]!["value"]!.GetValue<string>().Should().Be("Done",
			because: "a stage travels as an object so a later member is additive");
		request["stages"]!.AsArray()[1]!.AsObject().ContainsKey("group").Should().BeFalse(
			because: "a stage that names no group sends no group key");
		request["allowMissingDependencies"]!.GetValue<bool>().Should().BeTrue(because: "the flag is forwarded");
		request["validateOnly"]!.GetValue<bool>().Should().BeTrue(because: "the flag is forwarded");
		request.ContainsKey("name").Should().BeFalse(
			because: "an absent name lets the server derive one, and the server refuses nothing it declares");
	}

	[Test]
	[Description("A build stage that names a group and successful sends both, so the server builds the stage group described.")]
	public void Build_ShouldSendTheStageGroupAndSuccessful_WhenAStageNamesThem() {
		// Arrange
		string sent = null;
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Do<string>(body => sent = body))
			.Returns("{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");
		BuildCaseRequest request = Request() with {
			Stages = [new BuildCaseStageRequest("New"), new BuildCaseStageRequest("Rejected") {
				Group = "New", Successful = false
			}]
		};

		// Act
		Build(request);

		// Assert
		JsonObject stage = JsonNode.Parse(sent)!["request"]!["stages"]!.AsArray()[1]!.AsObject();
		stage["group"]!.GetValue<string>().Should().Be("New", because: "the parent stage is named by its value");
		stage["successful"]!.GetValue<bool>().Should().BeFalse(because: "an unsuccessful final stage is sent as false");
	}

	[Test]
	[Description("The BuildCaseResult envelope is parsed into the response, nested blocks included, and the case-change members of a stage and a case are read when the server sends them.")]
	public void Build_ShouldParseTheEnvelope() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>()).Returns(
			"{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"created\",\"case\":{\"schemaName\":\"UsrOrderCase\","
			+ "\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"connectionCount\":2,\"finalStages\":[\"Done\"],"
			+ "\"stages\":[{\"name\":\"Stage1\",\"color\":\"#0C8701\",\"group\":\"g\",\"successful\":false,"
			+ "\"isFinal\":true}]},\"missingDependencies\":[\"CrtBase\"],"
			+ "\"obligations\":{\"compileRequired\":false,\"artifacts\":[{\"kind\":\"dcm\"}]}}}");

		// Act
		DynamicCaseReply<BuildCaseResponse> reply = Build(Request());

		// Assert
		reply.Kind.Should().Be(DynamicCaseReplyKind.Envelope, because: "the body is the envelope");
		reply.Response.Outcome.Should().Be("created", because: "the outcome is read");
		reply.Response.Case.SchemaName.Should().Be("UsrOrderCase", because: "the case block is read");
		reply.Response.Case.Stages.Should().ContainSingle(stage => stage.Color == "#0C8701", because: "stages are read");
		reply.Response.Case.Stages[0].Group.Should().Be("g", because: "the stage group is read");
		reply.Response.Case.Stages[0].Successful.Should().BeFalse(because: "the successful flag is read");
		reply.Response.Case.Stages[0].IsFinal.Should().BeTrue(because: "the final flag is read");
		reply.Response.Case.FinalStages.Should().Equal(["Done"], because: "the final stages are read");
		reply.Response.MissingDependencies.Should().Equal(["CrtBase"], because: "missing dependencies drive the add");
		reply.Response.Obligations.Artifacts.Should().ContainSingle(artifact => artifact.Kind == "dcm",
			because: "per-artifact obligations are read");
	}

	[Test]
	[Description("A 404 on the route is RouteMissing: the installed package has no BuildCase.")]
	public void Build_ShouldClassifyA404_AsRouteMissing() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>())
			.Throws(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));

		// Act
		DynamicCaseReply<BuildCaseResponse> reply = Build(Request());

		// Assert
		reply.Kind.Should().Be(DynamicCaseReplyKind.RouteMissing, because: "a 404 means the operation does not exist");
	}

	[Test]
	[Description("An HTML page or a bare fault string is NotAnEnvelope, and the server's text is kept for the caller.")]
	public void Build_ShouldClassifyAnHtmlOrFaultBody_AsNotAnEnvelope() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>())
			.Returns("<html><body>Endpoint not found.</body></html>", "\"Object reference not set\"");

		// Act
		DynamicCaseReply<BuildCaseResponse> html = Build(Request());
		DynamicCaseReply<BuildCaseResponse> fault = Build(Request());

		// Assert
		html.Kind.Should().Be(DynamicCaseReplyKind.NotAnEnvelope, because: "an HTML page is not the result");
		html.Detail.Should().Contain("Endpoint not found", because: "the server's text is relayed");
		fault.Kind.Should().Be(DynamicCaseReplyKind.NotAnEnvelope, because: "a WCF fault string is not the result");
		fault.Detail.Should().Be("Object reference not set", because: "the fault text is unwrapped for the caller");
	}

	[Test]
	[Description("Any other transport failure is TransportFailure, never an exception, because what it means depends on the call.")]
	public void Build_ShouldClassifyAnyOtherFailure_AsTransportFailure() {
		// Arrange
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Any<string>())
			.Throws(new HttpRequestException("Gateway Timeout", null, HttpStatusCode.GatewayTimeout));

		// Act
		DynamicCaseReply<BuildCaseResponse> reply = Build(Request());

		// Assert
		reply.Kind.Should().Be(DynamicCaseReplyKind.TransportFailure, because: "a timeout says nothing about the route");
		reply.Detail.Should().Contain("Gateway Timeout", because: "the transport's text is relayed");
	}

	[Test]
	[Description("Every call of one session goes through ONE application client, so a tool call that sends several passes logs in once; disposing the session releases that client.")]
	public void OpenSession_ShouldShareOneClientAcrossItsCalls_AndReleaseItOnDispose() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");

		// Act
		using (IDynamicCaseSession session = _sut.OpenSession(Env)) {
			session.Build(Request());
			session.Build(Request() with { ValidateOnly = false });
			session.Describe(new DescribeCaseRequest { EntitySchemaName = "UsrOrder" });
		}

		// Assert
		_factory.Received(1).CreateEnvironmentClient(_env);
		_client.Received(3).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
		_client.Received(1).Dispose();
	}

	[Test]
	[Description("A session opened for an unregistered environment is refused up front, naming the environment, before any client exists.")]
	public void OpenSession_ShouldThrow_WhenTheEnvironmentIsNotRegistered() {
		// Arrange
		Action open = () => _sut.OpenSession("missing");

		// Act & Assert
		open.Should().Throw<InvalidOperationException>(because: "there is no environment to log in to")
			.WithMessage("*missing*");
		_factory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Description("A describe is posted to the DescribeCase route with the identity in camelCase and only the identity named, and the DescribeCaseResult envelope is parsed with its cases and settings rows.")]
	public void Describe_ShouldPostTheIdentity_AndParseTheEnvelope() {
		// Arrange
		string sent = null;
		_client.ExecutePostRequest(DescribeCaseUrl, Arg.Do<string>(body => sent = body)).Returns(
			"{\"DescribeCaseResult\":{\"success\":true,\"cases\":[{\"schemaName\":\"UsrOrderCase\",\"status\":\"existing\","
			+ "\"runningInstances\":2,\"packageReadOnly\":true,\"stages\":[{\"value\":\"v1\",\"runningInstances\":2,"
			+ "\"hasSteps\":true}]}],\"settings\":[{\"sysDcmSettingsId\":\"s1\",\"stageColumnName\":\"UsrStatus\","
			+ "\"hasFilterColumn\":false}],\"notes\":[\"n\"]}}");

		// Act
		DynamicCaseReply<DescribeCaseResponse> reply;
		using (IDynamicCaseSession session = _sut.OpenSession(Env)) {
			reply = session.Describe(new DescribeCaseRequest { EntitySchemaName = "UsrOrder" });
		}

		// Assert
		JsonObject request = JsonNode.Parse(sent)!["request"]!.AsObject();
		request["entitySchemaName"]!.GetValue<string>().Should().Be("UsrOrder", because: "the server reads camelCase keys");
		request.ContainsKey("caseName").Should().BeFalse(because: "only the identity the call named is sent");
		reply.Kind.Should().Be(DynamicCaseReplyKind.Envelope, because: "the body is the DescribeCaseResult envelope");
		reply.Response.Cases.Should().ContainSingle(because: "the object has one case");
		reply.Response.Cases[0].RunningInstances.Should().Be(2, because: "the running cases of the case are read");
		reply.Response.Cases[0].PackageReadOnly.Should().BeTrue(because: "a read-only package is reported");
		reply.Response.Cases[0].Stages[0].HasSteps.Should().BeTrue(because: "a stage with steps is reported");
		reply.Response.Settings.Should().ContainSingle(row => row.StageColumnName == "UsrStatus",
			because: "the settings rows are read");
	}

	[Test]
	[Description("A modify is posted to the ModifyCase route: an explicit null group travels as JSON null, a group left out stays absent, members left out are omitted, and the flags are sent.")]
	public void Modify_ShouldKeepAnExplicitNullGroupApartFromAnAbsentOne() {
		// Arrange
		string sent = null;
		_client.ExecutePostRequest(ModifyCaseUrl, Arg.Do<string>(body => sent = body))
			.Returns("{\"ModifyCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");
		ModifyCaseRequest request = new() {
			CaseName = "UsrOrderCase",
			Stages = [
				new CaseStageEditRequest { Value = "New", GroupSent = true, Group = null },
				new CaseStageEditRequest { Value = "Done", Caption = "Closed", Color = "#FF0000", Successful = false }
			],
			ConfirmRunningInstances = true,
			ValidateOnly = true
		};

		// Act
		using (IDynamicCaseSession session = _sut.OpenSession(Env)) {
			session.Modify(request);
		}

		// Assert
		JsonObject body = JsonNode.Parse(sent)!["request"]!.AsObject();
		JsonArray stages = body["stages"]!.AsArray();
		stages[0]!.AsObject().ContainsKey("group").Should().BeTrue(because: "an explicit null group ungroups the stage");
		stages[0]!["group"].Should().BeNull(because: "the explicit null is sent as JSON null");
		stages[0]!.AsObject().ContainsKey("caption").Should().BeFalse(because: "a member left out keeps the stored value");
		stages[1]!.AsObject().ContainsKey("group").Should().BeFalse(
			because: "a group left out keeps the stored group, so the key stays absent");
		stages[1]!["caption"]!.GetValue<string>().Should().Be("Closed", because: "a named caption is sent");
		stages[1]!["color"]!.GetValue<string>().Should().Be("#FF0000", because: "a named colour is sent");
		stages[1]!["successful"]!.GetValue<bool>().Should().BeFalse(because: "a named successful flag is sent");
		body.ContainsKey("enabled").Should().BeFalse(because: "enabled is sent only when the call names it");
		body.ContainsKey("caption").Should().BeFalse(because: "a case caption left out keeps the stored one");
		body["confirmRunningInstances"]!.GetValue<bool>().Should().BeTrue(because: "the user's yes is forwarded");
		body["validateOnly"]!.GetValue<bool>().Should().BeTrue(because: "a dry run is forwarded as validateOnly");
	}

	[Test]
	[Description("The ModifyCaseResult envelope is parsed with its diff and running instances, and a 404 on the ModifyCase route is RouteMissing.")]
	public void Modify_ShouldParseTheEnvelope_AndClassifyA404() {
		// Arrange
		_client.ExecutePostRequest(ModifyCaseUrl, Arg.Any<string>()).Returns(
			"{\"ModifyCaseResult\":{\"success\":true,\"outcome\":\"planned\",\"diff\":{\"moved\":[{\"value\":\"v1\","
			+ "\"caption\":\"New\",\"from\":\"1\",\"to\":\"2\"}],\"enabled\":{\"from\":\"true\",\"to\":\"false\"}},"
			+ "\"runningInstances\":{\"total\":3,\"perStage\":[{\"value\":\"v1\",\"count\":3}]}}}");
		_client.ExecutePostRequest(ModifyCaseUrl, Arg.Is<string>(body => body.Contains("UsrMissing")))
			.Throws(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));

		// Act
		DynamicCaseReply<ModifyCaseResponse> planned;
		DynamicCaseReply<ModifyCaseResponse> missing;
		using (IDynamicCaseSession session = _sut.OpenSession(Env)) {
			planned = session.Modify(new ModifyCaseRequest { CaseName = "UsrOrderCase", ValidateOnly = true });
			missing = session.Modify(new ModifyCaseRequest { CaseName = "UsrMissing" });
		}

		// Assert
		planned.Kind.Should().Be(DynamicCaseReplyKind.Envelope, because: "the body is the ModifyCaseResult envelope");
		planned.Response.Diff.Moved.Should().ContainSingle(change => change.To == "2", because: "the diff is read");
		planned.Response.Diff.Enabled.To.Should().Be("false", because: "the enabled change is read");
		planned.Response.RunningInstances.Total.Should().Be(3, because: "the running cases are read");
		missing.Kind.Should().Be(DynamicCaseReplyKind.RouteMissing, because: "a 404 means the operation does not exist");
	}

	[Test]
	[Description("A filtered build sends the filter with the server's keys and deactivateUnfiltered only when set; a build without them sends neither key, so it reads the same to every package that carries BuildCase.")]
	public void Build_ShouldSendTheFilter_AndOmitItWhenAbsent() {
		// Arrange
		var sentBodies = new System.Collections.Generic.List<string>();
		_client.ExecutePostRequest(BuildCaseUrl, Arg.Do<string>(body => sentBodies.Add(body)))
			.Returns("{\"BuildCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");
		BuildCaseRequest filtered = Request() with {
			Filter = BuildCaseFilterRequest.From(new DynamicCaseFilterArgument("UsrType", ["B2B"], false)),
			DeactivateUnfiltered = true
		};
		BuildCaseRequest empty = Request() with {
			Filter = BuildCaseFilterRequest.From(new DynamicCaseFilterArgument("UsrType", [], true))
		};

		// Act
		Build(filtered);
		Build(empty);
		Build(Request());

		// Assert
		JsonObject first = JsonNode.Parse(sentBodies[0])!["request"]!.AsObject();
		first["filter"]!["column"]!.GetValue<string>().Should().Be("UsrType", because: "the filter column is sent");
		first["filter"]!["values"]!.AsArray()[0]!.GetValue<string>().Should().Be("B2B", because: "the values are sent");
		first["filter"]!.AsObject().ContainsKey("empty").Should().BeFalse(because: "an IN filter sends no empty key");
		first["deactivateUnfiltered"]!.GetValue<bool>().Should().BeTrue(because: "the user's yes is forwarded");
		JsonObject second = JsonNode.Parse(sentBodies[1])!["request"]!.AsObject();
		second["filter"]!["empty"]!.GetValue<bool>().Should().BeTrue(because: "an empty-value case is sent as empty");
		second["filter"]!.AsObject().ContainsKey("values").Should().BeFalse(because: "an empty-value case has no values");
		JsonObject third = JsonNode.Parse(sentBodies[2])!["request"]!.AsObject();
		third.ContainsKey("filter").Should().BeFalse(because: "a case without a filter sends no filter key");
		third.ContainsKey("deactivateUnfiltered").Should().BeFalse(because: "the flag is sent only when set");
	}

	[Test]
	[Description("A modify sends a new filter as an object and a removal as JSON null, and leaves the key out when the call names neither.")]
	public void Modify_ShouldSendTheFilterChange_AndANullForARemoval() {
		// Arrange
		var sentBodies = new System.Collections.Generic.List<string>();
		_client.ExecutePostRequest(ModifyCaseUrl, Arg.Do<string>(body => sentBodies.Add(body)))
			.Returns("{\"ModifyCaseResult\":{\"success\":true,\"outcome\":\"planned\"}}");

		// Act
		using (IDynamicCaseSession session = _sut.OpenSession(Env)) {
			session.Modify(new ModifyCaseRequest {
				CaseName = "UsrOrderCase",
				Filter = new CaseFilterPatch(BuildCaseFilterRequest.From(new DynamicCaseFilterArgument("UsrType", ["B2B"], false)))
			});
			session.Modify(new ModifyCaseRequest { CaseName = "UsrOrderCase", Filter = new CaseFilterPatch(null) });
			session.Modify(new ModifyCaseRequest { CaseName = "UsrOrderCase", Caption = "X" });
		}

		// Assert
		JsonNode.Parse(sentBodies[0])!["request"]!["filter"]!["values"]!.AsArray().Should().HaveCount(1,
			because: "the new filter travels as an object");
		JsonObject removal = JsonNode.Parse(sentBodies[1])!["request"]!.AsObject();
		removal.ContainsKey("filter").Should().BeTrue(because: "a removal is an explicit filter key");
		removal["filter"].Should().BeNull(because: "the removal is JSON null");
		JsonNode.Parse(sentBodies[2])!["request"]!.AsObject().ContainsKey("filter").Should().BeFalse(
			because: "a call that names no filter keeps the stored one");
	}
}
