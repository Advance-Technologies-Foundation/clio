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
[Property("Module", "McpServer")]
public sealed class ODataCreateToolTests {
	private static JsonElement Arr(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, non-read-only, destructive, non-idempotent MCP tool name for odata-create.")]
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
		attribute.Destructive.Should().BeTrue(because: "odata-create inserts durable Creatio records; a data mutation MCP hosts must gate for approval and audit (GH-953)");
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
}
