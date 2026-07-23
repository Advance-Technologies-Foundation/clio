using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ClientUnitSchemaUpdateToolTests {

	[Test]
	[Category("Unit")]
	[Description("UpdateSchema redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller (parity with the get-* schema tools).")]
	public void UpdateSchema_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClientUnitSchemaUpdateCommand defaultCommand = new();
		FakeClientUnitSchemaUpdateCommand resolvedCommand = new() {
			ResponseToReturn = new ClientUnitSchemaUpdateResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ClientUnitSchemaUpdateCommand>(Arg.Any<ClientUnitSchemaUpdateOptions>())
			.Returns(resolvedCommand);
		ClientUnitSchemaUpdateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ClientUnitSchemaUpdateResponse response = tool.UpdateSchema(
			new ClientUnitSchemaUpdateArgs("NetworkUtilities", "var x = 1;", null, false, "dev", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the env-sensitive tool must run the resolved command, never the startup-time injected one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("UpdateSchema redacts a sensitive URI/host in a command-resolution exception before returning it to the MCP caller.")]
	public void UpdateSchema_Should_Redact_Sensitive_Error_When_Command_Resolution_Fails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClientUnitSchemaUpdateCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ClientUnitSchemaUpdateCommand>(Arg.Any<ClientUnitSchemaUpdateOptions>())
			.Returns(_ => throw new System.InvalidOperationException(
				"connect to https://secret-host.example.com/0 failed"));
		ClientUnitSchemaUpdateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ClientUnitSchemaUpdateResponse response = tool.UpdateSchema(
			new ClientUnitSchemaUpdateArgs("NetworkUtilities", "var x = 1;", null, false, "dev", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must surface as a failed response, not an exception");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the resolution exception must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeClientUnitSchemaUpdateCommand : ClientUnitSchemaUpdateCommand {
		public ClientUnitSchemaUpdateOptions CapturedOptions { get; private set; }
		public ClientUnitSchemaUpdateResponse ResponseToReturn { get; init; }

		public FakeClientUnitSchemaUpdateCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryUpdateSchema(
			ClientUnitSchemaUpdateOptions options, out ClientUnitSchemaUpdateResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn ?? new ClientUnitSchemaUpdateResponse { Success = true };
			return response.Success;
		}
	}
}
