using System.Text.Json.Nodes;
using ATF.Repository.Providers;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// HTTP-layer tests for <see cref="ServerProcessDescriber"/>: the wrapped <c>{"request":{name|uid}}</c> body, the
/// resolved DescribeProcess route, and each <see cref="ErrorOr{T}"/> branch (success / server-failure / empty /
/// unexpected shape / no identity). These exercise the actual clio→server contract, which the tool tests fake.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ServerProcessDescriberTests {

	private const string DescribeUrl = "http://sandbox/0/rest/ProcessDesignService/DescribeProcess";

	private static ServerProcessDescriber CreateDescriber(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcess).Returns(DescribeUrl);
		return new ServerProcessDescriber(client,
			Substitute.For<IDataProvider>(), urlBuilder);
	}

	private static IApplicationClient ClientReturning(string response) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(DescribeUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);
		return client;
	}

	[Test]
	[Description("Posts the process code wrapped under 'request.name' to the DescribeProcess route and returns the parsed graph on success.")]
	public void Describe_ShouldPostWrappedNameToDescribeRoute_AndReturnResult_OnSuccess() {
		// Arrange — the element carries its name (= local handle/Name) and uid (= element UId); both must survive.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "a successful describe returns the graph, not an error");
		result.Value.Name.Should().Be("UsrProc", because: "the process name is read from the server result");
		result.Value.Elements[0].Name.Should().Be("task1", because: "the element local handle (Name) is read back");
		result.Value.Elements[0].Uid.Should().Be("a1b2c3d4-0000-0000-0000-000000000001",
			because: "the element UId must be surfaced, not dropped (PR #715 review)");
		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["name"].GetValue<string>() == "UsrProc"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Deserializes an element parameter's direction and isResult from the server response into the DescribedParameter DTO (so callers can tell an element's outputs, mappable as a source, from its inputs).")]
	public void Describe_ShouldReadParameterDirectionAndIsResult_WhenServerReportsThem() {
		// Arrange — a user task whose parameter is an output (isResult true) while its direction is Variable
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"PResult\",\"uid\":\"p1\",\"type\":\"Guid\",\"direction\":\"Variable\",\"isResult\":true,\"source\":\"None\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Direction.Should().Be("Variable",
			because: "the parameter's direction must be read from the server, not dropped by the clio DTO");
		parameter.IsResult.Should().BeTrue(
			because: "isResult marks an element output usable as a mapping source and must be deserialized");
	}

	[Test]
	[Description("Leaves direction/isResult unset (null) when an older server omits them, so the absent fields serialize away cleanly.")]
	public void Describe_ShouldLeaveDirectionAndIsResultNull_WhenServerOmitsThem() {
		// Arrange — an older clioprocessbuilder that does not report direction/isResult on parameters
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"PResult\",\"uid\":\"p1\",\"type\":\"Guid\",\"source\":\"None\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Direction.Should().BeNull(
			because: "an omitted direction stays null so it serializes away (WhenWritingNull) for older servers");
		parameter.IsResult.Should().BeNull(
			because: "an omitted isResult stays null rather than defaulting to false, avoiding a misleading output");
	}

	[Test]
	[Description("Posts the uid (not the name) when the identity is a uid.")]
	public void Describe_ShouldPostWrappedUid_WhenIdentityIsUid() {
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\"}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		describer.Describe(new ProcessIdentity(null, "5c58c4c4-134b-4744-9c67-96d9c69c9d55", null), null);

		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["uid"].GetValue<string>() == "5c58c4c4-134b-4744-9c67-96d9c69c9d55"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns an error (not a throw) carrying the server's message when the result reports success=false.")]
	public void Describe_ShouldReturnError_WhenSuccessFalse() {
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":false,\"errorMessage\":\"Process 'UsrProc' was not found.\"}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		result.IsError.Should().BeTrue(because: "a server-reported failure becomes an ErrorOr error");
		result.FirstError.Description.Should().Contain("was not found",
			because: "the server message is surfaced to the caller");
	}

	[Test]
	[Description("Returns an error when the server response body is empty.")]
	public void Describe_ShouldReturnError_WhenResponseEmpty() {
		ServerProcessDescriber describer = CreateDescriber(ClientReturning(""));

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		result.IsError.Should().BeTrue(because: "an empty server response is a failure, not a graph");
	}

	[Test]
	[Description("Returns an error (without calling the server) when no identity is provided.")]
	public void Describe_ShouldReturnError_WhenNoIdentity() {
		IApplicationClient client = ClientReturning("{}");
		ServerProcessDescriber describer = CreateDescriber(client);

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity(null, null, null), null);

		result.IsError.Should().BeTrue(because: "a describe needs a code, uid, or caption");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	// The describer wraps the identity under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
