using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class GetClientUnitSchemaToolTests {

	[Test]
	[Category("Unit")]
	[Description("GetSchema maps args to options and executes the command resolved for the requested environment.")]
	public void GetSchema_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		FakeGetClientUnitSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(resolvedCommand);
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClientUnitSchemaResponse response = tool.GetSchema(new GetClientUnitSchemaArgs("NetworkUtilities") {
			OutputFile = "/tmp/out.js", EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command succeeded");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command must receive the mapped options");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("NetworkUtilities", because: "schema-name maps through");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/out.js", because: "output-file maps through");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name maps to the options environment");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run; only the env-resolved one does");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema maps the full-hierarchy and schema-uid args onto the resolved command options.")]
	public void GetSchema_Should_Map_FullHierarchy_And_SchemaUid_Args() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		FakeGetClientUnitSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(resolvedCommand);
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		tool.GetSchema(new GetClientUnitSchemaArgs("ContactPageV2", FullHierarchy: true, SchemaUId: "layer-uid") {
			EnvironmentName = "dev" });

		// Assert
		resolvedCommand.CapturedOptions.FullHierarchy.Should().BeTrue(
			because: "full-hierarchy must map through to the command options");
		resolvedCommand.CapturedOptions.SchemaUId.Should().Be("layer-uid",
			because: "schema-uid must map through to the command options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema supports a schema-uid-only call: schema-name stays null on the mapped options.")]
	public void GetSchema_Should_Map_SchemaUidOnly_Call_Without_SchemaName() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		FakeGetClientUnitSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(resolvedCommand);
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		tool.GetSchema(new GetClientUnitSchemaArgs(SchemaUId: "layer-uid") { EnvironmentName = "dev" });

		// Assert
		resolvedCommand.CapturedOptions.SchemaName.Should().BeNull(
			because: "schema-uid alone must be a valid invocation — the name is optional on the MCP surface");
		resolvedCommand.CapturedOptions.SchemaUId.Should().Be("layer-uid",
			because: "the UId drives the direct fetch");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema returns a failed response (not an exception) when command resolution fails.")]
	public void GetSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClientUnitSchemaResponse response = tool.GetSchema(new GetClientUnitSchemaArgs("NetworkUtilities") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must surface as a failed response");
		response.Error.Should().Contain("boom", because: "the underlying error message must be preserved");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void GetSchema_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		FakeGetClientUnitSchemaCommand resolvedCommand = new() {
			ResponseToReturn = new GetClientUnitSchemaResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(resolvedCommand);
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClientUnitSchemaResponse response = tool.GetSchema(new GetClientUnitSchemaArgs("NetworkUtilities") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeGetClientUnitSchemaCommand : GetClientUnitSchemaCommand {
		public GetClientUnitSchemaOptions CapturedOptions { get; private set; }
		public GetClientUnitSchemaResponse ResponseToReturn { get; init; }

		public FakeGetClientUnitSchemaCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IFileSystem>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSchema(GetClientUnitSchemaOptions options, out GetClientUnitSchemaResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn ?? new GetClientUnitSchemaResponse {
				Success = true,
				SchemaName = options.SchemaName
			};
			return response.Success;
		}
	}
}
