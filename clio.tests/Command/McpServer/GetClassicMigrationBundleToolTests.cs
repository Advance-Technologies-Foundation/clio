using System;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class GetClassicMigrationBundleToolTests {

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	[Test]
	[Category("Unit")]
	[Description("GetBundle maps args to options and executes the command resolved for the requested environment.")]
	public void GetBundle_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		FakeGetClassicMigrationBundleCommand defaultCommand = new();
		FakeGetClassicMigrationBundleCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicMigrationBundleCommand>(Arg.Any<GetClassicMigrationBundleOptions>())
			.Returns(resolvedCommand);
		GetClassicMigrationBundleTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicMigrationBundleResponse response = tool.GetBundle(
			new GetClassicMigrationBundleArgs("ContactPageV2", "Contact") {
				OutputFile = "/tmp/bundle.json", EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command succeeded");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command must receive the mapped options");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContactPageV2", because: "schema-name maps through");
		resolvedCommand.CapturedOptions.Entity.Should().Be("Contact", because: "entity maps through");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/bundle.json", because: "output-file maps through");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name maps to the options environment");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run; only the env-resolved one does");
	}

	[Test]
	[Category("Unit")]
	[Description("GetBundle returns a failed response (not an exception) when command resolution fails.")]
	public void GetBundle_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		FakeGetClassicMigrationBundleCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicMigrationBundleCommand>(Arg.Any<GetClassicMigrationBundleOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		GetClassicMigrationBundleTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicMigrationBundleResponse response = tool.GetBundle(
			new GetClassicMigrationBundleArgs("ContactPageV2") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must surface as a failed response");
		response.Error.Should().Contain("boom", because: "the underlying error message must be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("GetBundle redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void GetBundle_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		FakeGetClassicMigrationBundleCommand defaultCommand = new();
		FakeGetClassicMigrationBundleCommand resolvedCommand = new() {
			ResponseToReturn = new GetClassicMigrationBundleResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicMigrationBundleCommand>(Arg.Any<GetClassicMigrationBundleOptions>())
			.Returns(resolvedCommand);
		GetClassicMigrationBundleTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicMigrationBundleResponse response = tool.GetBundle(
			new GetClassicMigrationBundleArgs("ContactPageV2") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
	}

	private sealed class FakeGetClassicMigrationBundleCommand : GetClassicMigrationBundleCommand {
		public GetClassicMigrationBundleOptions CapturedOptions { get; private set; }
		public GetClassicMigrationBundleResponse ResponseToReturn { get; init; }

		public FakeGetClassicMigrationBundleCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IRemoteEntitySchemaColumnManager>(),
				Substitute.For<IFileSystem>(),
				Substitute.For<System.IO.Abstractions.IFileSystem>(),
				ConsoleLogger.Instance) {
		}

		public override bool TryAssembleBundle(
			GetClassicMigrationBundleOptions options, out GetClassicMigrationBundleResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn
				?? new GetClassicMigrationBundleResponse { Success = true, SchemaName = options.SchemaName };
			return response.Success;
		}
	}
}
