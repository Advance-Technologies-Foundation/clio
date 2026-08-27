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
public sealed class ODataUpdateToolTests {
	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement.Clone();

	/// <summary>
	/// A valid single-record OData response for the pre-write $select probe: none of the
	/// recognized error shapes, so the probe confirms the fields exist.
	/// </summary>
	private static string ProbeOk(string field) =>
		$"{{\"@odata.context\":\"http://creatio/odata/$metadata#Contact({Guid})\",\"Id\":\"{Guid}\",\"{field}\":\"probe\"}}";

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, destructive, idempotent MCP tool name for odata-update.")]
	public void Update_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataUpdateTool)
			.GetMethod(nameof(ODataUpdateTool.Update))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ODataUpdateTool.ToolName);
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeTrue(because: "an update mutates existing remote state");
		attribute.Idempotent.Should().BeTrue(because: "re-applying the same field values is idempotent");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends a PATCH to the addressed entity key with the JSON body via the shared application client.")]
	public void Update_Should_Patch_Addressed_Key_With_Body() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ProbeOk("Name"));
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Id = Guid,
			Data = Obj("{\"Name\":\"New\"}"),
			Confirm = true
		});

		response.Success.Should().BeTrue();
		response.Id.Should().Be(Guid);
		urlBuilder.Received(1).Build($"odata/Contact({Guid})");
		urlBuilder.Received(1).Build($"odata/Contact({Guid})?$select=Id%2CName");
		client.Received(1).ExecutePatchRequest($"http://creatio/odata/Contact({Guid})", "{\"Name\":\"New\"}", 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing or non-GUID id without any remote call to guard against keyless mass updates.")]
	public void Update_Should_Reject_NonGuid_Id() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = "all", Data = Obj("{\"Name\":\"x\"}"), Confirm = true
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("must be a record GUID");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a destructive update when confirm is omitted, without any remote call.")]
	public void Update_Should_Refuse_Without_Confirm() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("confirm");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects empty data without any remote call.")]
	public void Update_Should_Reject_Empty_Data() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{}")
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("data is required");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("An empty PATCH response body (Creatio's normal 204 No Content on success) is reported as success.")]
	public void Update_Should_Succeed_On_Empty_Response_Body() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ProbeOk("Name"));
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		ODataUpdateTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "an empty body is Creatio's normal successful PATCH response");
	}

	[Test]
	[Category("Unit")]
	[Description("A non-JSON response body (an IIS/proxy error page instead of Creatio's OData pipeline) must never be reported as a successful update — the write's transport layer never throws on a non-2xx status, so the body is the only signal available.")]
	public void Update_Should_Fail_When_Response_Is_Not_Json() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ProbeOk("Name"));
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>404 - File or directory not found.</title></head></html>");
		ODataUpdateTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "an HTML error page proves the request never reached Creatio's OData pipeline");
		response.Error.Should().Contain("was not JSON", because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
	}

	[Test]
	[Category("Unit")]
	[Description("A recognized Creatio OData error body returned with a non-failing HTTP status is reported as a failure, not swallowed as a successful update.")]
	public void Update_Should_Fail_When_Response_Is_ODataError() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ProbeOk("Name"));
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"Column Name is required\"}}");
		ODataUpdateTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "an OData error envelope must not be reported as a successful update");
		response.Error.Should().Be("Column Name is required");
	}

	[Test]
	[Category("Unit")]
	[Description("A valid JSON response body that is not one of the recognized Creatio error shapes is reported as success — this pins the third ValidateWriteResponse branch, so a broadened error detector cannot start failing every PATCH that answers with a plain body.")]
	public void Update_Should_Succeed_When_Response_Is_Valid_Json_Without_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ProbeOk("Name"));
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{}");
		ODataUpdateTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "an empty JSON object carries none of the recognized error members, so it is consistent with a successful PATCH");
		response.Error.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("A data field the OData type does not have must fail the call before the PATCH goes out, so success:true cannot mean a write that never happened (GitHub #1212).")]
	public void Update_Should_Reject_Unknown_Field_Before_Write() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"message\":\"The query specified in the URI is not valid. Could not find a property named 'labNoSuchColumnXyz' on type 'Terrasoft.Configuration.OData.Contact'.\"}}");
		ODataUpdateTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Id = Guid,
			Data = Obj("{\"labNoSuchColumnXyz\":\"#000000\"}"),
			Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("'labNoSuchColumnXyz'")
			.And.Contain("do not exist")
			.And.Contain("nothing was written");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("The OData service reports only the FIRST unknown $select property, so the tool must probe each remaining field individually to name every bad field in one round trip.")]
	public void Update_Should_Report_Every_Unknown_Field() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(
				Arg.Is<string>(url => url.Contains("Id%2ClabA%2ClabB")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"message\":\"Could not find a property named 'labA' on type 'Terrasoft.Configuration.OData.Contact'.\"}}");
		client.ExecuteGetRequest(
				Arg.Is<string>(url => url.Contains("Id%2ClabB")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"message\":\"Could not find a property named 'labB' on type 'Terrasoft.Configuration.OData.Contact'.\"}}");
		ODataUpdateTool tool = new(resolver);

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Id = Guid,
			Data = Obj("{\"labA\":\"#1\",\"labB\":\"#2\"}"),
			Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("'labA'").And.Contain("'labB'");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A malformed data field name is rejected locally before any remote call, because it would also corrupt the probe's $select list.")]
	public void Update_Should_Reject_Malformed_Field_Name_Locally() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Bad Field!\":\"x\"}"), Confirm = true
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("not a valid OData field name");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("When the pre-write probe returns an empty body the fields are unverified - the update must fail, not proceed, because an unverifiable write would reintroduce the silent success.")]
	public void Update_Should_Fail_When_Probe_Returns_Empty_Body() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		response.Success.Should().BeFalse(because: "unverified fields must not be reported as a write");
		response.Error.Should().Contain("could not be verified");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("When the pre-write probe returns a non-JSON body (a proxy error page) the fields are unverified - the update must fail, not proceed.")]
	public void Update_Should_Fail_When_Probe_Returns_NonJson_Body() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>503 - Service Unavailable</title></head></html>");
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		response.Success.Should().BeFalse(because: "unverified fields must not be reported as a write");
		response.Error.Should().Contain("could not be verified");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("When the probe fails for a reason other than a missing property (e.g. the record does not exist), that error is surfaced verbatim and no write is attempted.")]
	public void Update_Should_Surface_NonProperty_Probe_Error() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"message\":\"The requested resource does not exist.\"}}");
		ODataUpdateTool tool = new(resolver);

		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("The requested resource does not exist.");
		client.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
