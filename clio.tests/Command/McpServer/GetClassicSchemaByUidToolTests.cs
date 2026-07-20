using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;
using System.Linq;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class GetClassicSchemaByUidToolTests {

	private const string SampleUId = "948080fc-031e-4d88-9239-47bcedaa92bc";

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema resolves the command for the requested environment and maps all arguments into options.")]
	public void GetSchema_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		FakeGetClassicSchemaByUidCommand defaultCommand = new();
		FakeGetClassicSchemaByUidCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicSchemaByUidCommand>(Arg.Any<GetClassicSchemaByUidOptions>())
			.Returns(resolvedCommand);
		GetClassicSchemaByUidTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicSchemaByUidResponse response = tool.GetSchema(new GetClassicSchemaByUidArgs(SampleUId) {
			OutputFile = "/tmp/out.js", EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved fake command returns a successful response");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the environment-scoped command must execute");
		resolvedCommand.CapturedOptions.SchemaUId.Should().Be(SampleUId, because: "schema-uid is the required lookup key");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/out.js", because: "output-file must be passed through");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name drives command resolution");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run for environment-scoped calls");
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema returns a redacted error response when environment-scoped command resolution fails.")]
	public void GetSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		FakeGetClassicSchemaByUidCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicSchemaByUidCommand>(Arg.Any<GetClassicSchemaByUidOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		GetClassicSchemaByUidTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicSchemaByUidResponse response = tool.GetSchema(new GetClassicSchemaByUidArgs(SampleUId) {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "resolver failures must be returned as typed tool failures");
		response.Error.Should().Contain("boom", because: "the caller needs the resolver failure reason");
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema returns a typed failure when the MCP request explicitly passes null args.")]
	public void GetSchema_Should_Return_Error_When_Args_Are_Null() {
		// Arrange
		FakeGetClassicSchemaByUidCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetClassicSchemaByUidTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicSchemaByUidResponse response = tool.GetSchema(null);

		// Assert
		response.Success.Should().BeFalse(because: "args:null is invalid but should not escape as an NRE");
		response.Error.Should().Contain("args", because: "the failure should name the missing argument object");
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void GetSchema_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		FakeGetClassicSchemaByUidCommand defaultCommand = new();
		FakeGetClassicSchemaByUidCommand resolvedCommand = new() {
			ResponseToReturn = new GetClassicSchemaByUidResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicSchemaByUidCommand>(Arg.Any<GetClassicSchemaByUidOptions>())
			.Returns(resolvedCommand);
		GetClassicSchemaByUidTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicSchemaByUidResponse response = tool.GetSchema(new GetClassicSchemaByUidArgs(SampleUId) {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("GetSchema declares non-read-only MCP metadata because output-file writes to local disk.")]
	public void GetSchema_Should_Declare_NonReadOnly_Metadata_When_OutputFile_Can_Write() {
		// Arrange
		McpServerToolAttribute attribute = typeof(GetClassicSchemaByUidTool)
			.GetMethods()
			.Single(method => method.Name == nameof(GetClassicSchemaByUidTool.GetSchema))
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool readOnly = attribute.ReadOnly;

		// Assert
		readOnly.Should().BeFalse(because: "output-file can write a local schema body file");
		attribute.Destructive.Should().BeFalse(because: "the tool does not mutate remote Creatio state");
	}

	private sealed class FakeGetClassicSchemaByUidCommand : GetClassicSchemaByUidCommand {
		public GetClassicSchemaByUidOptions CapturedOptions { get; private set; }
		public GetClassicSchemaByUidResponse ResponseToReturn { get; init; }

		public FakeGetClassicSchemaByUidCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSchema(GetClassicSchemaByUidOptions options, out GetClassicSchemaByUidResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn ?? new GetClassicSchemaByUidResponse {
				Success = true,
				SchemaUId = options.SchemaUId
			};
			return response.Success;
		}
	}
}
