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
public sealed class ODataCreateToolTests {
	private static JsonElement Arr(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, non-read-only, non-destructive, non-idempotent MCP tool name for odata-create.")]
	public void Create_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataCreateTool)
			.GetMethod(nameof(ODataCreateTool.Create))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ODataCreateTool.ToolName);
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeFalse(because: "creating a record does not destroy existing state");
		attribute.Idempotent.Should().BeFalse(because: "repeating a create inserts another record");
	}

	[Test]
	[Category("Unit")]
	[Description("Posts each row to the entity set URL and returns the created records with their Ids.")]
	public void Create_Should_Post_Rows_And_Return_Created_Records() {
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

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]")
		});

		response.Created.Should().Be(2);
		response.Failed.Should().Be(0);
		response.Results[0].Index.Should().Be(0);
		response.Results[0].Id.Should().Be("11111111-1111-1111-1111-111111111111");
		response.Results[1].Index.Should().Be(1);
		response.Results[1].Id.Should().Be("22222222-2222-2222-2222-222222222222");
		urlBuilder.Received(1).Build("odata/Account");
		client.Received(2).ExecutePostRequest("http://creatio/odata/Account", Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves environment-scoped dependencies for the provided environment name.")]
	public void Create_Should_Resolve_Environment_Scoped_Client() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://env/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver);

		tool.Create(new ODataCreateArgs { EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"A\"}]") });

		resolver.Received(1).Resolve<IApplicationClient>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a request-level failure without any remote call when entity is missing.")]
	public void Create_Should_Fail_When_Entity_Missing() {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver);

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = " ", Rows = Arr("[{\"Name\":\"A\"}]")
		});

		response.Error.Should().Be("entity is required.");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a request-level failure without any remote call when rows is empty.")]
	public void Create_Should_Fail_When_Rows_Empty() {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver);

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[]")
		});

		response.Error.Should().Contain("rows is required");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("An OData error body on a row is surfaced as a structured per-row failure.")]
	public void Create_Should_Surface_ODataError_As_Failure() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"Column Name is required\"}}");
		ODataCreateTool tool = new(resolver);

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"X\":1}]")
		});

		response.Failed.Should().Be(1);
		response.Results.Single().Success.Should().BeFalse();
		response.Results.Single().Error.Should().Be("Column Name is required");
	}

	[Test]
	[Category("Unit")]
	[Description("An ASP.NET server error body returned with a non-failing status is reported as a per-row failure, not a created record.")]
	public void Create_Should_Surface_AspNet_ServerError_As_Failure() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"Object reference not set to an instance of an object.\",\"ExceptionType\":\"System.NullReferenceException\"}");
		ODataCreateTool tool = new(resolver);

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		response.Results.Single().Success.Should().BeFalse(because: "a server error body must never be reported as a successful create");
		response.Results.Single().Error.Should().Contain("Object reference");
		response.Results.Single().Id.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("A success-status body without an Id is treated as a per-row failure, since a real OData create always echoes the record Id.")]
	public void Create_Should_Fail_When_Response_Has_No_Id() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Name\":\"Office\"}");
		ODataCreateTool tool = new(resolver);

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		response.Results.Single().Success.Should().BeFalse();
		response.Results.Single().Error.Should().Contain("did not return a record Id");
	}

	[Test]
	[Category("Unit")]
	[Description("By default a failed row does not abort the batch: remaining rows are still inserted and reported.")]
	public void Create_Should_Continue_After_Row_Failure_By_Default() {
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

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Bad\"},{\"Name\":\"Good\"}]")
		});

		response.Created.Should().Be(1);
		response.Failed.Should().Be(1);
		response.Results.Should().HaveCount(2, because: "continue-on-error attempts every row");
		client.Received(2).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("With stop-on-error the batch aborts after the first failed row and does not attempt later rows.")]
	public void Create_Should_Stop_After_Row_Failure_When_Requested() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"bad row\"}}");
		ODataCreateTool tool = new(resolver);

		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", StopOnError = true,
			Rows = Arr("[{\"Name\":\"Bad\"},{\"Name\":\"NeverTried\"}]")
		});

		response.Failed.Should().Be(1);
		response.Results.Should().HaveCount(1, because: "stop-on-error aborts before the second row");
		client.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000, 1, 1);
	}
}
