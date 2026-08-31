using System.Linq;
using System.IO;
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
	[Test]
	[Category("Unit")]
	[Description("Reads a large update payload from rows-file and sends it as the PATCH body after confirmation.")]
	public void Update_Should_Read_Data_From_Rows_File() {
		// Arrange
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-update-{System.Guid.NewGuid():N}.json");
		File.WriteAllText(rowsFile, "{\"Name\":\"New\"}");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact(" + Guid + ")");
		try {
			// Act
			ODataWriteResponse response = new ODataUpdateTool(resolver, new System.IO.Abstractions.FileSystem()).Update(new ODataUpdateArgs {
				EnvironmentName = "dev", Entity = "Contact", Id = Guid, RowsFile = rowsFile, Confirm = true
			});

			// Assert
			response.Success.Should().BeTrue(because: "a valid file payload should follow the same PATCH path as inline data");
			client.Received(1).ExecutePatchRequest("http://creatio/odata/Contact(" + Guid + ")", "{\"Name\":\"New\"}", 30_000, 1, 1);
		} finally {
			if (File.Exists(rowsFile)) File.Delete(rowsFile);
		}
	}
	[Test]
	[Category("Unit")]
	[Description("Rejects data and rows-file together instead of silently preferring one, so a caller never PATCHes a payload it did not choose.")]
	public void Update_Should_Reject_Data_And_RowsFile_Together() {
		// Arrange
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-update-{System.Guid.NewGuid():N}.json");
		File.WriteAllText(rowsFile, "{\"Name\":\"FromFile\"}");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);

		try {
			// Act
			ODataWriteResponse response = new ODataUpdateTool(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>()).Update(new ODataUpdateArgs {
				EnvironmentName = "dev", Entity = "Contact", Id = Guid,
				Data = Obj("{\"Name\":\"Inline\"}"), RowsFile = rowsFile, Confirm = true
			});

			// Assert
			response.Success.Should().BeFalse(
				because: "two payload sources are ambiguous and picking one silently would write data the caller did not choose");
			response.Error.Should().Contain("not both",
				because: "the caller has to be told which argument to drop");
			client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		} finally {
			if (File.Exists(rowsFile)) File.Delete(rowsFile);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("An exploratory confirm=false call answers with the confirmation prompt even when rows-file does not exist: the confirm gate is the first guard, so the agent learns the operation is destructive before it fixes the path.")]
	public void Update_Should_Require_Confirmation_Before_Touching_RowsFile() {
		// Arrange
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-update-absent-{System.Guid.NewGuid():N}.json");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);

		// Act
		ODataWriteResponse response = new ODataUpdateTool(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>()).Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, RowsFile = rowsFile, Confirm = false
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "an unconfirmed destructive update never proceeds");
		(response.Error ?? string.Empty).Should().NotContain("was not found",
			because: "the confirm gate must answer first - a path error here would make the agent fix the path "
				+ "before it knows the operation needs confirmation at all");
		client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a missing rows-file as a structured failure once confirmation has passed, rather than letting the file exception escape as a protocol error.")]
	public void Update_Should_Report_Missing_RowsFile_After_Confirmation() {
		// Arrange
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-update-absent-{System.Guid.NewGuid():N}.json");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);

		// Act
		ODataWriteResponse response = new ODataUpdateTool(resolver, new System.IO.Abstractions.FileSystem()).Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, RowsFile = rowsFile, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "an absent payload file is a request error, not a transport failure");
		response.Error.Should().Contain("was not found",
			because: "the caller has to know the path did not resolve to a file");
		client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports malformed rows-file JSON as a structured failure instead of surfacing a raw parser exception.")]
	public void Update_Should_Report_Invalid_RowsFile_Json() {
		// Arrange
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-update-bad-{System.Guid.NewGuid():N}.json");
		File.WriteAllText(rowsFile, "{ not json");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);

		try {
			// Act
			ODataWriteResponse response = new ODataUpdateTool(resolver, new System.IO.Abstractions.FileSystem()).Update(new ODataUpdateArgs {
				EnvironmentName = "dev", Entity = "Contact", Id = Guid, RowsFile = rowsFile, Confirm = true
			});

			// Assert
			response.Success.Should().BeFalse(
				because: "an unparseable payload must fail the request, not the MCP protocol frame");
			response.Error.Should().Contain("must contain valid JSON",
				because: "the caller has to know the file content is at fault, not the request shape");
			client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		} finally {
			if (File.Exists(rowsFile)) File.Delete(rowsFile);
		}
	}

	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement.Clone();

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
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		client.Received(1).ExecutePatchRequest($"http://creatio/odata/Contact({Guid})", "{\"Name\":\"New\"}", 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing or non-GUID id without any remote call to guard against keyless mass updates.")]
	public void Update_Should_Reject_NonGuid_Id() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>404 - File or directory not found.</title></head></html>");
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"Column Name is required\"}}");
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

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
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{}");
		ODataUpdateTool tool = new(resolver, Substitute.For<System.IO.Abstractions.IFileSystem>());

		// Act
		ODataWriteResponse response = tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = Guid, Data = Obj("{\"Name\":\"New\"}"), Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "an empty JSON object carries none of the recognized error members, so it is consistent with a successful PATCH");
		response.Error.Should().BeNull();
	}
}
