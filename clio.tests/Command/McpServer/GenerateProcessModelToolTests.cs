using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class GenerateProcessModelToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for generate-process-model so prompts, unit tests, and E2E tests share one identifier.")]
	public void GenerateProcessModelTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = GenerateProcessModelTool.GenerateProcessModelToolName;

		// Assert
		toolName.Should().Be("generate-process-model",
			because: "the MCP tool name must remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the generate-process-model MCP method as destructive because it writes and can overwrite the generated process model file.")]
	public void GenerateProcessModel_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(GenerateProcessModelTool)
			.GetMethod(nameof(GenerateProcessModelTool.GenerateProcessModel))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "generate-process-model writes a source file for the requested process");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware generate-process-model command and maps explicit MCP arguments into command options.")]
	public void GenerateProcessModel_Should_Resolve_Command_And_Map_Explicit_Arguments() {
		// Arrange
		FakeGenerateProcessModelCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateProcessModelCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateProcessModelTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.GenerateProcessModel(new GenerateProcessModelArgs(
			"UsrProcess",
			@"src\generated",
			"Contoso.ProcessModels",
			"uk-UA",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid generate-process-model request should execute the resolved environment-aware command");
		commandResolver.Received(1).Resolve<GenerateProcessModelCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive mapped process-model options");
		resolvedCommand.CapturedOptions!.Code.Should().Be("UsrProcess",
			because: "the positional process code must be forwarded from MCP arguments");
		resolvedCommand.CapturedOptions.DestinationPath.Should().Be(@"src\generated",
			because: "the provided destination-path must be preserved exactly");
		resolvedCommand.CapturedOptions.Namespace.Should().Be("Contoso.ProcessModels",
			because: "the requested namespace must be forwarded to the command");
		resolvedCommand.CapturedOptions.Culture.Should().Be("uk-UA",
			because: "the requested culture must be forwarded to the command");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "the requested environment name must be preserved for environment-aware resolution");
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to the command defaults for omitted optional MCP arguments so non-CLI invocations stay aligned with CLI parsing.")]
	public void GenerateProcessModel_Should_Use_Defaults_For_Omitted_Optional_Arguments() {
		// Arrange
		FakeGenerateProcessModelCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateProcessModelCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateProcessModelTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.GenerateProcessModel(new GenerateProcessModelArgs(
			"UsrProcess",
			null,
			null,
			null,
			"dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "omitting optional MCP arguments should still produce a valid command invocation");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should still receive options when optional MCP arguments are omitted");
		resolvedCommand.CapturedOptions!.DestinationPath.Should().Be(".",
			because: "the MCP tool should use the command's default destination path when none is provided");
		resolvedCommand.CapturedOptions.Namespace.Should().Be("AtfTIDE.ProcessModels",
			because: "the MCP tool should use the command's default namespace when none is provided");
		resolvedCommand.CapturedOptions.Culture.Should().Be("en-US",
			because: "the MCP tool should use the command's default culture when none is provided");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured command execution error when the requested environment cannot be resolved for generate-process-model.")]
	public void GenerateProcessModel_Should_Report_Invalid_Environment_As_Command_Result() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateProcessModelCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment with key 'missing-env' not found."));
		GenerateProcessModelTool tool = new(
			new FakeGenerateProcessModelCommand(),
			ConsoleLogger.Instance,
			commandResolver);

		// Act
		CommandExecutionResult result = tool.GenerateProcessModel(new GenerateProcessModelArgs(
			"UsrProcess",
			null,
			null,
			null,
			"missing-env"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "resolver failures should be returned as normal command execution envelopes");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "Environment with key 'missing-env' not found."),
			because: "the failure should surface the environment-resolution problem to the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for generate-process-model references the exact tool name and keeps the MCP arguments visible to callers.")]
	public void GenerateProcessModelPrompt_Should_Mention_Tool_Name_And_Arguments() {
		// Arrange

		// Act
		string prompt = GenerateProcessModelPrompt.GenerateProcessModel(
			"UsrProcess",
			"dev",
			@"src\generated",
			"Contoso.ProcessModels",
			"en-US");

		// Assert
		prompt.Should().Contain(GenerateProcessModelTool.GenerateProcessModelToolName,
			because: "the prompt should reference the exact production tool name");
		prompt.Should().Contain("`code`",
			because: "the prompt should keep the process code argument visible to callers");
		prompt.Should().Contain("`destination-path`",
			because: "the prompt should keep the destination-path argument visible to callers");
		prompt.Should().Contain("explicit `.cs` file path",
			because: "the prompt should explain that destination-path can target a specific file as well as a folder");
		prompt.Should().Contain("`namespace`",
			because: "the prompt should keep the namespace argument visible to callers");
		prompt.Should().Contain("`culture`",
			because: "the prompt should keep the culture argument visible to callers");
		prompt.Should().Contain("`environment-name`",
			because: "the prompt should keep the environment-name argument visible to callers");
	}

	private sealed class FakeGenerateProcessModelCommand : GenerateProcessModelCommand {
		public GenerateProcessModelCommandOptions? CapturedOptions { get; private set; }

		public FakeGenerateProcessModelCommand()
			: base(
				Substitute.For<IProcessModelGenerator>(),
				Substitute.For<ILogger>(),
				Substitute.For<IProcessModelWriter>()) {
		}

		public override int Execute(GenerateProcessModelCommandOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
