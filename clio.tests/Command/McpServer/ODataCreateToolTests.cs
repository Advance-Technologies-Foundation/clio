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
	private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement.Clone();

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
	[Description("Posts the JSON body to the entity set URL and returns the created record with its Id.")]
	public void Create_Should_Post_Body_And_Return_Created_Record() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Acme\"}");
		ODataCreateTool tool = new(resolver);

		ODataWriteResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Data = Obj("{\"Name\":\"Acme\"}")
		});

		response.Success.Should().BeTrue();
		response.Id.Should().Be("11111111-1111-1111-1111-111111111111");
		urlBuilder.Received(1).Build("odata/Account");
		client.Received(1).ExecutePostRequest("http://creatio/odata/Account", "{\"Name\":\"Acme\"}", 30_000, 1, 1);
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

		tool.Create(new ODataCreateArgs { EnvironmentName = "dev", Entity = "Account", Data = Obj("{\"Name\":\"A\"}") });

		resolver.Received(1).Resolve<IApplicationClient>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a validation failure without any remote call when entity is missing.")]
	public void Create_Should_Fail_When_Entity_Missing() {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver);

		ODataWriteResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = " ", Data = Obj("{\"Name\":\"A\"}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Be("entity is required.");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a validation failure without any remote call when data is empty.")]
	public void Create_Should_Fail_When_Data_Empty() {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver);

		ODataWriteResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Data = Obj("{}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("data is required");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("An OData error body on the response is surfaced as a structured failure.")]
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

		ODataWriteResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Data = Obj("{\"X\":1}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Be("Column Name is required");
	}

	[Test]
	[Category("Unit")]
	[Description("An ASP.NET server error body (e.g. EDM model NullReferenceException) returned with a non-failing status is reported as a failure, not a created record.")]
	public void Create_Should_Surface_AspNet_ServerError_As_Failure() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"Object reference not set to an instance of an object.\",\"ExceptionType\":\"System.NullReferenceException\",\"StackTrace\":\"   at Terrasoft.Web.OData.ODataEntityModelBuilder...\"}");
		ODataCreateTool tool = new(resolver);

		ODataWriteResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Data = Obj("{\"Name\":\"Office\"}")
		});

		response.Success.Should().BeFalse(because: "a server error body must never be reported as a successful create");
		response.Error.Should().Contain("Object reference", because: "the ExceptionMessage should be surfaced to the caller");
		response.Id.Should().BeNull(because: "no record was created");
	}

	[Test]
	[Category("Unit")]
	[Description("A success-status body without an Id is treated as a failure, since a real OData create always echoes the record Id.")]
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

		ODataWriteResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Data = Obj("{\"Name\":\"Office\"}")
		});

		response.Success.Should().BeFalse(because: "a created record without an Id indicates the body is not a real create response");
		response.Error.Should().Contain("did not return a record Id", because: "the failure should explain the missing Id");
	}
}
