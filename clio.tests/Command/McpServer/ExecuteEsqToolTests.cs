using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ExecuteEsqToolTests {

	private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

	private static (ExecuteEsqTool tool, IApplicationClient client, IServiceUrlBuilder urlBuilder) BuildTool(string responseJson) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://creatio/DataService/json/SyncReply/SelectQuery");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseJson);
		return (new ExecuteEsqTool(commandResolver), client, urlBuilder);
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable read-only MCP tool name for execute-esq.")]
	public void Execute_Should_Advertise_Stable_Tool_Name() {
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ExecuteEsqTool)
			.GetMethod(nameof(ExecuteEsqTool.Execute))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ExecuteEsqTool.ToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.ReadOnly.Should().BeTrue(
			because: "execute-esq only reads Creatio records via SelectQuery");
		attribute.Destructive.Should().BeFalse(
			because: "execute-esq must not mutate remote Creatio state");
	}

	[Test]
	[Category("Unit")]
	[Description("Posts the raw SelectQuery body to the DataService Select route and returns the rows array.")]
	public void Execute_Should_Post_Query_And_Return_Rows() {
		// Arrange
		(ExecuteEsqTool tool, IApplicationClient client, IServiceUrlBuilder urlBuilder) =
			BuildTool("{\"rows\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\"}],\"success\":true}");
		JsonElement query = Json("{\"rootSchemaName\":\"Contact\",\"operationType\":0,\"allColumns\":false,\"rowCount\":1}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = query
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "a SelectQuery success envelope with rows should map to a successful response");
		response.Count.Should().Be(1,
			because: "the response should report the number of returned rows");
		response.Rows!.Value.ValueKind.Should().Be(JsonValueKind.Array,
			because: "the rows array should be preserved");
		urlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.Select);
		client.Received(1).ExecutePostRequest(
			"http://creatio/DataService/json/SyncReply/SelectQuery",
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Contact\"")),
			30_000,
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves environment-scoped client dependencies from the environment name.")]
	public void Execute_Should_Resolve_Environment_Scoped_Client() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://env/select");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rows\":[],\"success\":true}");
		ExecuteEsqTool tool = new(commandResolver);

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "an empty rows array is still a successful query");
		response.Count.Should().Be(0,
			because: "zero rows should be reported as a count of zero");
		commandResolver.Received(1).Resolve<IApplicationClient>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		commandResolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a validation failure when the query has no rootSchemaName, without calling the environment.")]
	public void Execute_Should_Fail_When_RootSchemaName_Missing() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ExecuteEsqTool tool = new(commandResolver);

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"operationType\":0}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a SelectQuery without rootSchemaName cannot run");
		response.Error.Should().Contain("rootSchemaName",
			because: "the failure should name the missing required property");
		commandResolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Every failure response carries a recovery hint pointing the caller at the esq/esq-filters guidance.")]
	public void Execute_Should_Attach_Guidance_Hint_On_Failure() {
		// Arrange — a validation failure (missing rootSchemaName) and a server-error failure
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ExecuteEsqTool validationTool = new(resolver);
		(ExecuteEsqTool serverTool, _, _) = BuildTool(
			"{\"responseStatus\":{\"ErrorCode\":\"NullReferenceException\",\"Message\":\"Object reference not set to an instance of an object.\"},\"success\":false}");

		// Act
		ExecuteEsqResponse validation = validationTool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"operationType\":0}")
		});
		ExecuteEsqResponse server = serverTool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		validation.Success.Should().BeFalse();
		validation.Hint.Should().Contain("get-guidance",
			because: "a caller that guessed the format should be told to read the esq guidance");
		validation.Hint.Should().Contain("esq-filters",
			because: "the hint should name both guidance articles");
		server.Success.Should().BeFalse();
		server.Hint.Should().Contain("get-guidance",
			because: "even a server-side ESQ error should nudge the caller toward the guidance");
	}

	[Test]
	[Category("Unit")]
	[Description("A successful response does not carry the failure recovery hint.")]
	public void Execute_Should_Not_Attach_Hint_On_Success() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool("{\"rows\":[],\"success\":true}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeTrue();
		response.Hint.Should().BeNull(
			because: "the guidance recovery hint is only for failures");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a query passed as a JSON-encoded string and posts the parsed object.")]
	public void Execute_Should_Accept_Query_As_Json_String() {
		// Arrange
		(ExecuteEsqTool tool, IApplicationClient client, _) =
			BuildTool("{\"rows\":[],\"success\":true}");
		JsonElement queryAsString = Json("\"{\\\"rootSchemaName\\\":\\\"Account\\\"}\"");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = queryAsString
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "a query supplied as a JSON-encoded string should be parsed and executed");
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Account\"")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a DataService failure envelope as a failure with the server message.")]
	public void Execute_Should_Surface_DataService_Failure() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"success\":false,\"errorInfo\":{\"errorCode\":\"GeneralError\",\"message\":\"Unknown column Foo\"}}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a SelectQuery failure envelope must not be reported as success");
		response.Error.Should().Be("Unknown column Foo",
			because: "the server errorInfo.message should be surfaced");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a DataService responseStatus error (the SelectQuery failure shape) with a clean ErrorCode-prefixed message.")]
	public void Execute_Should_Surface_ResponseStatus_Error() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"responseStatus\":{\"ErrorCode\":\"ItemNotFoundException\",\"Message\":\"Column by path Foo not found in schema Contact.\",\"Errors\":[]},\"rowsAffected\":-1,\"success\":false}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a responseStatus failure must not be reported as success");
		response.Error.Should().Be("ItemNotFoundException: Column by path Foo not found in schema Contact.",
			because: "the responseStatus ErrorCode and Message should be surfaced as a clean message");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces an ASP.NET server error body as a failure.")]
	public void Execute_Should_Surface_Server_Error_Body() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"Object reference not set to an instance of an object.\",\"ExceptionType\":\"System.NullReferenceException\"}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "an ASP.NET error body must not be reported as a successful query");
		response.Error.Should().Contain("Object reference",
			because: "the ExceptionMessage should be surfaced to the caller");
	}
}
