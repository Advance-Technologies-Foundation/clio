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
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcessSchema).Returns(DescribeUrl);
		return new ServerProcessDescriber(Substitute.For<ILogger>(), client,
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
		// Arrange
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"elements\":[],\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "a successful describe returns the graph, not an error");
		result.Value.Name.Should().Be("UsrProc", because: "the process name is read from the server result");
		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["name"].GetValue<string>() == "UsrProc"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
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
