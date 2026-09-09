using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ODataCreateToolTests {
	private static JsonElement Arr(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, non-read-only, non-destructive, non-idempotent MCP tool name for odata-create.")]
	public void Create_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataCreateTool)
			.GetMethod(nameof(ODataCreateTool.Create))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ODataCreateTool.ToolName, because: "the tool name is part of the stable MCP contract");
		attribute.ReadOnly.Should().BeFalse(because: "odata-create mutates state by inserting records");
		attribute.Destructive.Should().BeFalse(because: "creating a record does not destroy existing state");
		attribute.Idempotent.Should().BeFalse(because: "repeating a create inserts another record");
	}

	[Test]
	[Category("Unit")]
	[Description("Posts each row to the entity set URL and returns the created records with their Ids.")]
	public void Create_Should_Post_Rows_And_Return_Created_Records() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Acme\"}",
				"{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Name\":\"Globex\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]")
		});

		// Assert
		response.Created.Should().Be(2, because: "both rows insert successfully");
		response.Failed.Should().Be(0, because: "no row failed");
		response.Results[0].Index.Should().Be(0, because: "per-row results preserve input order");
		response.Results[0].Id.Should().Be("11111111-1111-1111-1111-111111111111", because: "the first created record Id is reported");
		response.Results[1].Index.Should().Be(1, because: "per-row results preserve input order");
		response.Results[1].Id.Should().Be("22222222-2222-2222-2222-222222222222", because: "the second created record Id is reported");
		urlBuilder.Received(1).Build("odata/Account");
		client.Received(2).ExecutePostRequest("http://creatio/odata/Account", Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves environment-scoped dependencies for the provided environment name.")]
	public void Create_Should_Resolve_Environment_Scoped_Client() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://env/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		tool.Create(new ODataCreateArgs { EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"A\"}]") });

		// Assert
		resolver.Received(1).Resolve<IApplicationClient>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a request-level failure without any remote call when entity is missing.")]
	public void Create_Should_Fail_When_Entity_Missing() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = " ", Rows = Arr("[{\"Name\":\"A\"}]")
		});

		// Assert
		response.Error.Should().Be("entity is required.", because: "a blank entity is a request-level error");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a request-level failure without any remote call when rows is empty.")]
	public void Create_Should_Fail_When_Rows_Empty() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[]")
		});

		// Assert
		response.Error.Should().Contain("rows is required", because: "an empty rows array is a request-level error");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("An OData error body on a row is surfaced as a structured per-row failure.")]
	public void Create_Should_Surface_ODataError_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"Column Name is required\"}}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"X\":1}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "the single row failed with an OData error");
		response.Results.Single().Success.Should().BeFalse(because: "an OData error body is not a successful create");
		response.Results.Single().Error.Should().Be("Column Name is required", because: "the OData error message is surfaced verbatim");
	}

	[Test]
	[Category("Unit")]
	[Description("An ASP.NET server error body returned with a non-failing status is reported as a per-row failure, not a created record.")]
	public void Create_Should_Surface_AspNet_ServerError_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"Object reference not set to an instance of an object.\",\"ExceptionType\":\"System.NullReferenceException\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a server error body must never be reported as a successful create");
		response.Results.Single().Error.Should().Contain("Object reference", because: "the ASP.NET exception message is surfaced");
		response.Results.Single().Id.Should().BeNull(because: "no record was created");
	}

	[Test]
	[Category("Unit")]
	[Description("A Web API routing error body ({Message, MessageDetail}) for an unregistered controller is reported as a per-row failure, not a created record.")]
	public void Create_Should_Surface_Routing_Error_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/0/odata/UsrCustomerStatus");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"No HTTP resource was found that matches the request URI '.../0/odata/UsrCustomerStatus'.\",\"MessageDetail\":\"No type was found that matches the controller named 'UsrCustomerStatus'.\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "UsrCustomerStatus", Rows = Arr("[{\"Name\":\"Active\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "the single row failed with a routing error");
		response.Created.Should().Be(0, because: "no record was created against an unregistered entity set");
		response.Results.Single().Success.Should().BeFalse(because: "a {Message, MessageDetail} routing body must never be reported as a successful create");
		response.Results.Single().Error.Should().Contain("controller named 'UsrCustomerStatus'", because: "the MessageDetail identifies the unregistered controller");
		response.Results.Single().Error.Should().Contain(CreatioResponseError.UnregisteredEntityHint, because: "the create path funnels through the same shared TryDetect and must surface the identical hint (asserted via the constant to avoid drift)");
		response.Results.Single().Id.Should().BeNull(because: "no record was created against an unregistered entity set");
	}

	[Test]
	[Category("Unit")]
	[Description("A bare {Message} body without MessageDetail is a per-row failure without the unregistered-entity hint, mirroring the read-side boundary on the shared detector.")]
	public void Create_Should_Surface_Bare_Message_Body_As_Failure_Without_Registration_Hint() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"Authorization has been denied for this request.\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "a bare {Message} body is an error, not a created record");
		response.Results.Single().Success.Should().BeFalse(because: "a bare {Message} body with no entity members is not a successful create");
		response.Results.Single().Error.Should().Contain("Authorization has been denied", because: "the Message text is surfaced verbatim");
		response.Results.Single().Error.Should().NotContain(CreatioResponseError.UnregisteredEntityHint, because: "without MessageDetail the failure is not identifiable as a routing error, so the registration hint must not be appended");
	}

	[Test]
	[Category("Unit")]
	[Description("A created-record echo that legitimately carries a Message column is reported as a successful create, proving the routing-error heuristic does not misfire on genuine create responses.")]
	public void Create_Should_Not_Misclassify_Created_Entity_With_Message_Column() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/EmailMessageData");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"@odata.context\":\"http://creatio/odata/$metadata#EmailMessageData/$entity\",\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Message\":\"Hello there\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "EmailMessageData", Rows = Arr("[{\"Message\":\"Hello there\"}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "a create echo carrying @odata.context + Id is a real created record, even with a Message column");
		response.Results.Single().Success.Should().BeTrue(because: "the routing-error detection must not swallow a genuine created-record echo");
		response.Results.Single().Id.Should().Be("22222222-2222-2222-2222-222222222222", because: "the created record's Id must be surfaced");
	}

	[Test]
	[Category("Unit")]
	[Description("A created-record echo carrying a business Success=false column is still a successful create: the BaseResponse detector belongs to Creatio's service envelopes and must not run against an OData body, where Success is an ordinary entity column and the write has already happened.")]
	public void Create_Should_Not_Misclassify_Created_Entity_With_Success_Column() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/UsrIntegrationLog");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"@odata.context\":\"http://creatio/odata/$metadata#UsrIntegrationLog/$entity\","
				+ "\"Id\":\"33333333-3333-3333-3333-333333333333\",\"Success\":false,"
				+ "\"errorInfo\":null}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "UsrIntegrationLog", Rows = Arr("[{\"Success\":false}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "the record was created; Success here is a column of the entity, not a service envelope flag");
		response.Failed.Should().Be(0, because: "reporting this write as failed invites a duplicate retry of a record that already exists");
		response.Results.Single().Success.Should().BeTrue(because: "the BaseResponse envelope detector must not run against an OData payload");
		response.Results.Single().Id.Should().Be("33333333-3333-3333-3333-333333333333", because: "the created record's Id must be surfaced");
	}

	[Test]
	[Category("Unit")]
	[Description("An explicit error envelope that also carries an Id is a failure: Id is not proof of a payload, so a non-zero Code with an Exception must win and the row must not be reported as created.")]
	public void Create_Should_Surface_Id_Bearing_Error_Envelope_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Code\":-1,\"Exception\":\"Access to the entity is denied.\","
				+ "\"Id\":\"44444444-4444-4444-4444-444444444444\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "an explicit non-zero Code with an Exception is an error even when the body carries an Id");
		response.Created.Should().Be(0, because: "no record was created, so nothing may be counted as created");
		response.Results.Single().Success.Should().BeFalse(because: "an Id member is carried by any envelope and is not structural proof of an OData payload");
		response.Results.Single().Error.Should().Contain("Access to the entity is denied", because: "the rejection reason must reach the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("A created-record echo carrying a numeric Code plus a Message is still a successful create: the DataService/AuthService envelope detector must not claim a body that also holds OData payload members, because the write has already happened and a reported failure invites a duplicate retry.")]
	public void Create_Should_Not_Misclassify_Created_Entity_With_Code_And_Message_Columns() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/UsrThing");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"@odata.context\":\"http://creatio/odata/$metadata#UsrThing/$entity\",\"Id\":\"33333333-3333-3333-3333-333333333333\",\"Code\":200,\"Message\":\"Created\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "UsrThing", Rows = Arr("[{\"Code\":200}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "a non-zero Code column on a body that also carries @odata.context and Id is data, not a DataService error envelope");
		response.Results.Single().Success.Should().BeTrue(because: "reporting a completed create as a failure invites a duplicate retry");
		response.Results.Single().Id.Should().Be("33333333-3333-3333-3333-333333333333", because: "the created record's Id must be surfaced");
	}

	[Test]
	[Category("Unit")]
	[Description("The absolute request URI carried by a bare-Message routing body is redacted on the create path too, mirroring the read-side guard so a silent removal of the Redact call would fail a test.")]
	public void Create_Should_Redact_Server_Uri_In_Routing_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://secret-host:88/prod-app/0/odata/UsrCustomerStatus");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"No HTTP resource was found that matches the request URI 'http://secret-host:88/prod-app/0/odata/UsrCustomerStatus'.\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "UsrCustomerStatus", Rows = Arr("[{\"Name\":\"Active\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a bare-Message routing body is still a per-row failure on the create path");
		response.Results.Single().Error.Should().NotContain("secret-host", because: "the environment host embedded in the routing Message must be redacted on the create path exactly as on the read path");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty {Message} (or empty {Message, MessageDetail}) body is surfaced as a per-row failure with an explicit contentless-response message and no registration hint, verifying the empty-body fallback branch.")]
	public void Create_Should_Surface_Empty_Message_Body_As_Failure_With_Explicit_Text() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"\",\"MessageDetail\":\"\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a body whose only members are empty Message/MessageDetail is an error, not a created record");
		response.Results.Single().Error.Should().Be("Creatio returned an empty error response.", because: "an empty error body must degrade to an explicit contentless message rather than an empty or leading-space string");
		response.Results.Single().Error.Should().NotContain(CreatioResponseError.UnregisteredEntityHint, because: "an empty body carries no MessageDetail, so it is not identifiable as a routing error and must not get the registration hint");
	}

	[Test]
	[Category("Unit")]
	[Description("An unrecognized error body (not one of TryDetect's shapes) that reaches the id-missing fallback is redacted, so response host detail cannot leak through the create path's catch-all failure branch.")]
	public void Create_Should_Redact_Unrecognized_Error_Body_In_Id_Missing_Fallback() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		// A ModelState validation body carries a member beyond Message/MessageDetail, so TryDetect does
		// not recognize it; with no Id it falls through to the id-missing fallback branch.
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"The request is invalid.\",\"ModelState\":{\"row\":[\"failed calling http://secret-host:88/prod-app/0/odata/Account\"]}}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a body with no created-record Id is a per-row failure");
		response.Results.Single().Error.Should().NotContain("secret-host", because: "the id-missing fallback embeds raw response text and must redact host detail to keep parity with the other error branches");
	}

	[Test]
	[Category("Unit")]
	[Description("A numeric primary key in the response body is accepted as a created record rather than reported as a missing Id.")]
	public void Create_Should_Accept_Numeric_Id() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/NumberKeyed");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":42,\"Name\":\"Office\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "NumberKeyed", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "a numeric Id still identifies a created record");
		response.Results.Single().Success.Should().BeTrue(because: "a non-string key must not be misreported as a failure");
		response.Results.Single().Id.Should().Be("42", because: "the numeric key is surfaced as its raw value");
	}

	[Test]
	[Category("Unit")]
	[Description("A success-status body without an Id is treated as a per-row failure, since a real OData create always echoes the record Id.")]
	public void Create_Should_Fail_When_Response_Has_No_Id() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Name\":\"Office\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a body without an Id is not a created record");
		response.Results.Single().Error.Should().Contain("did not return a record Id", because: "the missing-Id reason is reported");
	}

	[Test]
	[Category("Unit")]
	[Description("By default a failed row does not abort the batch: remaining rows are still inserted and reported.")]
	public void Create_Should_Continue_After_Row_Failure_By_Default() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"{\"error\":{\"code\":\"\",\"message\":\"bad row\"}}",
				"{\"Id\":\"22222222-2222-2222-2222-222222222222\"}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Bad\"},{\"Name\":\"Good\"}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "the second row inserts even though the first failed");
		response.Failed.Should().Be(1, because: "the first row failed");
		response.Results.Should().HaveCount(2, because: "continue-on-error attempts every row");
		client.Received(2).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("With stop-on-error the batch aborts after the first failed row and does not attempt later rows.")]
	public void Create_Should_Stop_After_Row_Failure_When_Requested() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"bad row\"}}");
		ODataCreateTool tool = new(resolver);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", StopOnError = true,
			Rows = Arr("[{\"Name\":\"Bad\"},{\"Name\":\"NeverTried\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "the first row failed and aborted the batch");
		response.Results.Should().HaveCount(1, because: "stop-on-error aborts before the second row");
		client.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000, 1, 1);
	}

	#region record-created side-effect state

	private static ODataCreateTool BuildTool(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		return new ODataCreateTool(resolver);
	}

	[Test]
	[Category("Unit")]
	[Description("The advertised odata-create description documents the record-created side-effect state and the no-blind-retry rule, so a consumer learns the contract from the tool surface rather than from clio source.")]
	public void Create_Should_Advertise_The_RecordCreated_Contract() {
		// Arrange
		// Act
		// Fully qualified: NUnit ships its own DescriptionAttribute, and this assertion is about the
		// System.ComponentModel one the MCP surface actually advertises.
		System.ComponentModel.DescriptionAttribute description =
			(System.ComponentModel.DescriptionAttribute)typeof(ODataCreateTool)
				.GetMethod(nameof(ODataCreateTool.Create))!
				.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
				.Single();

		// Assert
		// Asserts the FIELD NAME only. It is a contract identifier and cannot change without a consumer
		// noticing; the surrounding prose can be reworded freely, and pinning it here would fail CI for a
		// rewrite rather than for a defect.
		description.Description.Should().Contain("record-created",
			because: "the side-effect state is only actionable if the advertised contract names it");
	}

	[Test]
	[Category("Unit")]
	[Description("A row echoed back with its Id reports record-created true, so a caller can act on the side effect without inferring it from the success flag.")]
	public void Create_Should_Report_RecordCreated_True_On_Success() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		response.Results[0].RecordCreated.Should().BeTrue(because: "the server echoed the created record");
		response.Results[0].RetryGuidance.Should().BeNull(because: "a verified insert needs no retry advice");
		response.Unverified.Should().Be(0, because: "nothing is in an unknown state");
	}

	[Test]
	[Category("Unit")]
	[Description("A server error payload leaves record-created unknown rather than false, because Creatio can fail a POST after the row is already written - reporting not-inserted would invite a duplicating retry.")]
	public void Create_Should_Report_RecordCreated_Unknown_On_Server_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"An error has occurred.\"}}");
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "MailboxSyncSettings", Rows = Arr("[{\"UserName\":\"probe\"}]")
		});

		// Assert
		response.Results[0].Success.Should().BeFalse(because: "the server reported the call as failed");
		response.Results[0].RecordCreated.Should().BeNull(
			because: "a post-insert handler can throw after the record persists, so not-inserted is NOT known");
		response.Results[0].RetryGuidance.Should().NotBeNullOrWhiteSpace(
			because: "an unknown side effect must tell the caller to verify instead of retrying");
		response.Unverified.Should().Be(1, because: "the batch must surface how many rows are unverified");
	}

	[Test]
	[Description("A row rejected locally for its shape reports record-created false, because no request ever left clio - the caller can fix and re-send safely.")]
	[Category("Unit")]
	public void Create_Should_Report_RecordCreated_False_When_Row_Rejected_Locally() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{}]")
		});

		// Assert
		response.Results[0].RecordCreated.Should().BeFalse(
			because: "the row never reached the server, so not-inserted is verified");
		response.Unverified.Should().Be(0, because: "a locally rejected row is not an unknown outcome");
		client.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A non-JSON response body (an IIS/proxy error page instead of Creatio's OData pipeline) leaves record-created unknown rather than reporting a successful create — the request never reached Creatio intact, so the row's side effect cannot be assumed.")]
	public void Create_Should_Report_RecordCreated_Unknown_On_Non_Json_Response() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>404 - File or directory not found.</title></head></html>");
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		response.Results[0].Success.Should().BeFalse(because: "an HTML error page proves the request never reached Creatio's OData pipeline");
		response.Results[0].RecordCreated.Should().BeNull(
			because: "the request did not reach Creatio intact, so whether a post-insert handler already wrote the row is unknown");
		response.Results[0].RetryGuidance.Should().NotBeNullOrWhiteSpace(
			because: "an unknown side effect must tell the caller to verify instead of retrying");
		response.Results[0].Error.Should().Contain("was not JSON",
			because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
		response.Unverified.Should().Be(1, because: "the batch must surface how many rows are unverified");
	}

	[Test]
	[Category("Unit")]
	[Description("A transport failure leaves record-created unknown, because the request may have been applied before the error surfaced on the client side.")]
	public void Create_Should_Report_RecordCreated_Unknown_On_Transport_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new HttpRequestException("connection reset"));
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		response.Results[0].RecordCreated.Should().BeNull(
			because: "a client-side transport error cannot prove the server did not apply the insert");
		response.Results[0].RetryGuidance.Should().NotBeNullOrWhiteSpace(
			because: "the caller must verify before re-sending");
	}

	#endregion

	/// <summary>
	/// Minimal CSDL declaring Account with a text column and a date-time column, so the value guard can be
	/// shown to key on the declared Edm type rather than on the literal's shape alone.
	/// </summary>
	private const string AccountCsdl = """
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="Account">
		        <Key><PropertyRef Name="Id" /></Key>
		        <Property Name="Id" Type="Edm.Guid" Nullable="false" />
		        <Property Name="Name" Type="Edm.String" />
		        <Property Name="DueDate" Type="Edm.DateTimeOffset" />
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

	/// <summary>
	/// Builds an odata-create tool over a stubbed client whose <c>$metadata</c> answers
	/// <paramref name="metadataBody"/> and whose POST always succeeds.
	/// </summary>
	private static (ODataCreateTool tool, IApplicationClient client) CreateFixture(string metadataBody) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataBody);
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		return (new ODataCreateTool(resolver), client);
	}

	[Test]
	[Category("Unit")]
	[Description("A row whose date-time value has no UTC designator or offset fails locally before any POST, and is reported as definitely not created (GitHub issue #1369).")]
	public void Create_Should_Reject_A_Row_With_A_Zoneless_DateTime() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(AccountCsdl);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\",\"DueDate\":\"2024-01-01T04:00:00.000\"}]")
		});

		// Assert
		response.Created.Should().Be(0, because: "the row never left clio, so nothing was inserted");
		response.Results[0].RecordCreated.Should().BeFalse(
			because: "a row rejected locally has a KNOWN side effect - it is safe to fix and re-send");
		response.Results[0].Error.Should().Contain("DueDate",
			because: "the caller can only fix the row when the refusal names the offending field");
		client.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A date-shaped string bound to an Edm.String column is inserted: the guard keys on the declared Edm type.")]
	public void Create_Should_Insert_A_Date_Shaped_String_On_A_Text_Column() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(AccountCsdl);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"2024-01-01T04:00:00\"}]")
		});

		// Assert
		response.Created.Should().Be(1,
			because: "Name is Edm.String, so a date-shaped text value is a legitimate insert");
		client.Received(1).ExecutePostRequest(
			"http://creatio/odata/Account", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("An unreadable $metadata endpoint does not fail the insert: the batch proceeds and the guard falls back to the literal's shape.")]
	public void Create_Should_Insert_When_Metadata_Is_Unavailable() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(string.Empty);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\"},{\"DueDate\":\"2024-01-01T04:00:00\"}]")
		});

		// Assert
		response.Created.Should().Be(1,
			because: "odata-create does not validate field names, so an unresolved metadata endpoint must never "
				+ "turn a valid row into a failure");
		response.Results[1].Success.Should().BeFalse(
			because: "without a declared type the shape alone decides, and fail-closed is the honest answer");
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A batch whose rows carry no date-time-shaped value does not download the service CSDL at all.")]
	public void Create_Should_Not_Read_Metadata_Without_A_Date_Shaped_Value() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(AccountCsdl);

		// Act
		tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"},{\"Name\":\"Initech\"}]")
		});

		// Assert
		client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null, 0, 0, 0);
		client.Received(3).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A batch containing a date-time-shaped value reads $metadata exactly once, however many rows it inserts.")]
	public void Create_Should_Read_Metadata_Once_Per_Batch() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(AccountCsdl);

		// Act
		tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"2024-01-01T04:00:00\"},{\"Name\":\"Globex\"},{\"Name\":\"Initech\"}]")
		});

		// Assert
		client.Received(1).ExecuteGetRequest(
			"http://creatio/odata/$metadata", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A $metadata read that throws does not fail the batch: the type map degrades to null and the guard falls back to the literal's shape (GitHub issue #1369).")]
	public void Create_Should_Insert_When_The_Metadata_Read_Throws() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(AccountCsdl);
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new System.InvalidOperationException("metadata endpoint is down"));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\"},{\"DueDate\":\"2024-01-01T04:00:00\"}]")
		});

		// Assert
		response.Created.Should().Be(1,
			because: "the type map is an optimization for the value guard, never a precondition of the write, "
				+ "so a throwing metadata endpoint must not turn a valid row into a failure");
		response.Results[1].Success.Should().BeFalse(
			because: "with no declared type the literal's shape alone decides, and fail-closed is the honest answer");
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("The optional CSDL read of odata-create uses a short single-attempt budget: it only sharpens the guard, so a stalled $metadata must not hold the batch before its first POST.")]
	public void Create_Should_Read_Metadata_On_A_Short_Single_Attempt_Budget() {
		// Arrange
		(ODataCreateTool tool, IApplicationClient client) = CreateFixture(AccountCsdl);

		// Act
		tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"2024-01-01T04:00:00\"}]")
		});

		// Assert
		client.Received(1).ExecuteGetRequest(
			"http://creatio/odata/$metadata",
			ODataFieldValidation.OptionalMetadataTimeoutMs,
			ODataFieldValidation.OptionalMetadataAttempts,
			Arg.Any<int>());
	}
}
