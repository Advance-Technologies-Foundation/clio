using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ODataDeleteToolTests {
	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, destructive, idempotent MCP tool name for odata-delete.")]
	public void Delete_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataDeleteTool)
			.GetMethod(nameof(ODataDeleteTool.Delete))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ODataDeleteTool.ToolName);
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeTrue(because: "delete removes remote state");
		attribute.Idempotent.Should().BeTrue(because: "deleting an already-deleted record yields the same end state");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends a DELETE to the addressed entity key with an empty body.")]
	public void Delete_Should_Delete_Addressed_Key() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Confirm = true
		});

		response.Success.Should().BeTrue();
		response.Id.Should().Be(Guid);
		urlBuilder.Received(1).Build($"odata/Contact({Guid})");
		client.Received(1).ExecuteDeleteRequest($"http://creatio/odata/Contact({Guid})", string.Empty, 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a destructive delete when confirm is omitted, without any remote call.")]
	public void Delete_Should_Refuse_Without_Confirm() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("confirm");
		client.DidNotReceive().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing or non-GUID id without any remote call to guard against keyless mass deletes.")]
	public void Delete_Should_Reject_NonGuid_Id() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = " "
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("must be a record GUID");
		client.DidNotReceive().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a validation failure without any remote call when entity is missing.")]
	public void Delete_Should_Fail_When_Entity_Missing() {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataDeleteTool tool = new(resolver);

		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = " ", Id = Guid
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Be("entity is required.");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("A non-JSON response body (an IIS/proxy error page instead of Creatio's OData pipeline) must never be reported as a successful delete — the write's transport layer never throws on a non-2xx status, so the body is the only signal available.")]
	public void Delete_Should_Fail_When_Response_Is_Not_Json() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>401 - Unauthorized: Access is denied due to invalid credentials.</title></head></html>");
		ODataDeleteTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "an HTML error page proves the request never reached Creatio's OData pipeline");
		response.Error.Should().Contain("was not JSON", because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
		response.Error.Should().Contain("HTTP 401 error page",
			because: "the write transport neither throws nor exposes a status, so naming the status the page states "
				+ "is the only way the caller learns this was an auth hop rather than a routing miss");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty DELETE response body (Creatio's normal 204 No Content on success) is reported as success.")]
	public void Delete_Should_Succeed_On_Empty_Response_Body() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		ODataDeleteTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "an empty body is Creatio's normal successful DELETE response");
	}

	[Test]
	[Category("Unit")]
	[Description("A recognized Creatio OData error body returned with a non-failing HTTP status is reported as a failure, not swallowed as a successful delete — a before-delete business rule or an FK constraint rejection is the most likely non-empty DELETE body.")]
	public void Delete_Should_Fail_When_Response_Is_ODataError() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"The DELETE request violates a foreign key constraint\"}}");
		ODataDeleteTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "an OData error envelope must not be reported as a successful delete");
		response.Error.Should().Be("The DELETE request violates a foreign key constraint");
	}
}
