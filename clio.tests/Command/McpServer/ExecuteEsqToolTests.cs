using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
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
	[Description("Redacts the target host/URI out of a raw IApplicationClient exception before it reaches the typed ExecuteEsqResponse failure envelope, since that POCO error path bypasses the throw-path and IsError redaction gates.")]
	public void Execute_Should_Redact_Host_And_Uri_In_Failure_Envelope_When_Client_Throws_Connection_Failure() {
		// Arrange
		const string sensitiveHost = "http://secret-host:88/0/DataService/json/SyncReply/SelectQuery";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns(sensitiveHost);
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new InvalidOperationException($"Failed to connect to {sensitiveHost}"));
		ExecuteEsqTool tool = new(commandResolver);

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\",\"operationType\":0,\"allColumns\":false,\"rowCount\":1}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a connection failure must produce a structured failure envelope, not an exception");
		response.Error.Should().NotContain(sensitiveHost,
			because: "the raw target host/URI from the IApplicationClient failure must never reach the transcript");
		response.Error.Should().NotContain("secret-host",
			because: "no fragment of the redacted host should survive in the surfaced error");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the redactor replaces the URI with a stable placeholder rather than dropping the whole message");
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
		validation.Success.Should().BeFalse(
			because: "a query without rootSchemaName cannot run");
		validation.Hint.Should().Contain("get-guidance",
			because: "a caller that guessed the format should be told to read the esq guidance");
		validation.Hint.Should().Contain("esq-filters",
			because: "the hint should name both guidance articles");
		server.Success.Should().BeFalse(
			because: "a server-side ESQ failure envelope must not be reported as success");
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
		response.Success.Should().BeTrue(
			because: "a SelectQuery success envelope should map to a successful response");
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
	[Description("Surfaces a truncated raw body when a failure envelope carries no extractable error field.")]
	public void Execute_Should_Surface_Truncated_Body_When_No_Error_Field() {
		// Arrange — a success:false envelope with no responseStatus/errorInfo/Message to extract
		(ExecuteEsqTool tool, _, _) = BuildTool("{\"success\":false,\"rowsAffected\":-1}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a success:false envelope must not be reported as success");
		response.Error.Should().Contain("rowsAffected",
			because: "when no error field can be extracted, the raw response body should be surfaced to the caller");
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

	[Test]
	[Category("Unit")]
	[Description("Fails fast without a network call when environment-name is blank.")]
	public void Execute_Should_Fail_When_Environment_Missing() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ExecuteEsqTool tool = new(resolver);

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = " ",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank environment-name cannot resolve a Creatio instance");
		response.Error.Should().Contain("environment-name is required",
			because: "the failure should name the missing required argument");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a query that is not a JSON object.")]
	public void Execute_Should_Fail_When_Query_Is_Not_Object() {
		// Arrange
		ExecuteEsqTool tool = new(Substitute.For<IToolCommandResolver>());

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("123")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a non-object query cannot be a SelectQuery");
		response.Error.Should().Contain("query must be a JSON SelectQuery object",
			because: "the failure should explain the expected query shape");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a query string that is not valid JSON.")]
	public void Execute_Should_Fail_When_Query_String_Is_Invalid_Json() {
		// Arrange
		ExecuteEsqTool tool = new(Substitute.For<IToolCommandResolver>());

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("\"{ not json\"")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a query string that is not valid JSON cannot be parsed into a SelectQuery");
		response.Error.Should().Contain("query is not valid JSON",
			because: "the failure should explain that the supplied query string was not valid JSON");
	}

	[Test]
	[Category("Unit")]
	[Description("Clamps an out-of-range timeout into the supported window.")]
	public void Execute_Should_Clamp_Timeout() {
		// Arrange
		(ExecuteEsqTool lowTool, IApplicationClient lowClient, _) = BuildTool("{\"rows\":[],\"success\":true}");
		(ExecuteEsqTool highTool, IApplicationClient highClient, _) = BuildTool("{\"rows\":[],\"success\":true}");

		// Act
		lowTool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev", Query = Json("{\"rootSchemaName\":\"Contact\"}"), Timeout = 10
		});
		highTool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev", Query = Json("{\"rootSchemaName\":\"Contact\"}"), Timeout = 999_999
		});

		// Assert
		lowClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), 1_000, Arg.Any<int>(), Arg.Any<int>());
		highClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), 120_000, Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an empty SelectQuery response as a failure.")]
	public void Execute_Should_Fail_On_Empty_Response() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool("   ");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "an empty SelectQuery response cannot be treated as a successful query");
		response.Error.Should().Contain("empty response",
			because: "the failure should explain that the server returned nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the whole response body when a successful response has no rows array.")]
	public void Execute_Should_Return_Whole_Body_When_No_Rows_Array() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool("{\"rowsAffected\":-1,\"success\":true}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "a success envelope without a rows array is still a successful query");
		response.Count.Should().BeNull(
			because: "row count is unknown when there is no rows array to measure");
		response.Rows.Should().NotBeNull(
			because: "the whole response body should be returned when there is no rows array");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a transport exception as a failure carrying the guidance hint.")]
	public void Execute_Should_Return_Failure_When_Client_Throws() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://creatio/select");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new Exception("network down"));
		ExecuteEsqTool tool = new(resolver);

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a transport exception must be reported as a failed query");
		response.Error.Should().Contain("network down",
			because: "the transport exception message should be surfaced to the caller");
		response.Hint.Should().Contain("get-guidance",
			because: "a failure should still point the caller at the esq guidance");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats an explicit success:true envelope as success even when it also carries a responseStatus block and no rows array.")]
	public void Execute_Should_Treat_Explicit_Success_With_ResponseStatus_And_No_Rows_As_Success() {
		// Arrange — a success envelope that still carries a (benign) responseStatus and no rows array
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"responseStatus\":{\"ErrorCode\":\"\",\"Message\":\"\"},\"rowsAffected\":-1,\"success\":true}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "an explicit success:true response must not be reclassified as a failure just because it carries a responseStatus block");
		response.Error.Should().BeNull(
			because: "a successful response should not surface an error message");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a query string whose decoded content is not a JSON object.")]
	public void Execute_Should_Fail_When_Query_String_Is_Not_Object() {
		// Arrange
		ExecuteEsqTool tool = new(Substitute.For<IToolCommandResolver>());

		// Act — the outer JSON is a string, but its decoded content is a bare number
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("\"123\"")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a query string that decodes to a non-object cannot be a SelectQuery");
		response.Error.Should().Contain("query string must contain a JSON SelectQuery object",
			because: "the failure should explain that the decoded query string was not a JSON object");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a responseStatus failure that carries only a Message (no ErrorCode) without an ErrorCode prefix.")]
	public void Execute_Should_Surface_ResponseStatus_Error_Without_ErrorCode() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"responseStatus\":{\"Message\":\"server boom\"},\"success\":false}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a responseStatus failure must not be reported as success");
		response.Error.Should().Be("server boom",
			because: "a responseStatus without an ErrorCode should surface the bare message with no prefix");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the whole body as success when a success response carries a non-array rows value.")]
	public void Execute_Should_Return_Whole_Body_When_Rows_Is_Not_Array() {
		// Arrange — success:true with a null (non-array) rows value
		(ExecuteEsqTool tool, _, _) = BuildTool("{\"rows\":null,\"success\":true}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "a success response with a non-array rows value is still successful");
		response.Count.Should().BeNull(
			because: "row count is unknown when rows is not an array");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails with a parse error when the server returns a non-empty but malformed JSON body.")]
	public void Execute_Should_Fail_When_Response_Is_Malformed_Json() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool("{ broken json");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a malformed SelectQuery response cannot be reported as success");
		response.Error.Should().Contain("Failed to parse SelectQuery response",
			because: "the failure should explain that the server response could not be parsed");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails when a requested column alias is absent from the returned rows instead of silently dropping the unknown column.")]
	public void Execute_Should_Fail_When_Requested_Column_Is_Dropped_From_Rows() {
		// Arrange — the server returns success:true rows that omit the requested NoSuchColumnZZZ alias
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"rows\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"John\"}],\"success\":true}");
		JsonElement query = Json(
			"{\"rootSchemaName\":\"Contact\",\"columns\":{\"items\":{" +
			"\"Id\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"Id\"}}," +
			"\"Name\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"Name\"}}," +
			"\"NoSuchColumnZZZ\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"NoSuchColumnZZZ\"}}}}}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = query
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a requested column that the SelectQuery silently dropped must be surfaced as a failure, not hidden");
		response.Error.Should().Contain("NoSuchColumnZZZ",
			because: "the failure should name the unresolved column so the caller can fix the columnPath");
		response.Error.Should().Contain("Contact",
			because: "the failure should name the schema the column was requested on");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns success unchanged when every requested column alias is present in the returned rows.")]
	public void Execute_Should_Return_Success_When_All_Requested_Columns_Present() {
		// Arrange
		(ExecuteEsqTool tool, _, _) = BuildTool(
			"{\"rows\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"John\"}],\"success\":true}");
		JsonElement query = Json(
			"{\"rootSchemaName\":\"Contact\",\"columns\":{\"items\":{" +
			"\"Id\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"Id\"}}," +
			"\"Name\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"Name\"}}}}}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = query
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "when every requested alias is present in the rows the query resolved correctly");
		response.Count.Should().Be(1,
			because: "the single returned row should still be reported");
		response.Error.Should().BeNull(
			because: "a fully-resolved query must not surface an error");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not flag a dropped column when the result set is empty, since absence cannot be distinguished from a legitimately empty result.")]
	public void Execute_Should_Not_Flag_Unknown_Column_When_Rows_Are_Empty() {
		// Arrange — no rows to inspect, so a missing alias cannot be detected and must not false-positive
		(ExecuteEsqTool tool, _, _) = BuildTool("{\"rows\":[],\"success\":true}");
		JsonElement query = Json(
			"{\"rootSchemaName\":\"Contact\",\"columns\":{\"items\":{" +
			"\"NoSuchColumnZZZ\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"NoSuchColumnZZZ\"}}}}}");

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = query
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "an empty result set cannot prove a column was dropped, so the call must not be failed on a false positive");
		response.Count.Should().Be(0,
			because: "the empty rows array should be reported as zero rows");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a timeout/cancellation as a failure without the misleading ESQ-format guidance hint.")]
	public void Execute_Should_Report_Timeout_Without_Guidance_Hint() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://creatio/select");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new TaskCanceledException("A task was canceled."));
		ExecuteEsqTool tool = new(resolver);

		// Act
		ExecuteEsqResponse response = tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev",
			Query = Json("{\"rootSchemaName\":\"Contact\"}")
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a timed-out SelectQuery must be reported as a failure");
		response.Error.Should().Contain("timed out",
			because: "a cancellation/timeout should be reported as a timeout, not a generic error");
		response.Hint.Should().BeNull(
			because: "a timeout is not a query-format problem, so the ESQ-format guidance hint must not be attached");
	}
}
