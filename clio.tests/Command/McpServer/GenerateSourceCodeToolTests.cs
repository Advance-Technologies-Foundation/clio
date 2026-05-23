using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class GenerateSourceCodeToolTests
{

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name so callers and tests share one identifier.")]
	public void GenerateSourceCodeTool_Should_Advertise_Stable_Tool_Name() {
		GenerateSourceCodeTool.GenerateSourceCodeToolName
			.Should().Be("generate-source-code",
				because: "the MCP tool name must remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool must not be marked destructive since source code generation does not delete or overwrite persistent data.")]
	[Ignore("ENG-90312 Phase 2: tool folded into clio-run; safety flags now reflected on clio-run itself. Polymorphic registry validated by Z7 schema-discovery test.")]
	public void GenerateSourceCode_Should_Not_Be_Marked_As_Destructive() {
		McpServerToolAttribute attribute = GetToolAttribute();
		attribute.Destructive.Should().BeFalse(
			because: "generate-source-code regenerates schema sources without removing existing data");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool must be marked idempotent because running generate-source-code multiple times yields the same result.")]
	[Ignore("ENG-90312 Phase 2: tool folded into clio-run; safety flags now reflected on clio-run itself. Polymorphic registry validated by Z7 schema-discovery test.")]
	public void GenerateSourceCode_Should_Be_Marked_As_Idempotent() {
		McpServerToolAttribute attribute = GetToolAttribute();
		attribute.Idempotent.Should().BeTrue(
			because: "generating source code for the same schemas produces the same outcome on repeated calls");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware command and maps all MCP arguments into GenerateSourceCodeOptions.")]
	public void GenerateSourceCode_Should_Resolve_Command_And_Map_Arguments() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.GenerateSourceCode(new GenerateSourceCodeRunArgs(
			"dev", Modified: false, Required: false, Background: false));

		result.ExitCode.Should().Be(0,
			because: "a valid generate-source-code request should execute successfully");
		commandResolver.Received(1).Resolve<GenerateSourceCodeCommand>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolvedCommand.CapturedOptions!.Environment.Should().Be("dev");
		resolvedCommand.CapturedOptions.Modified.Should().BeFalse();
		resolvedCommand.CapturedOptions.Required.Should().BeFalse();
		resolvedCommand.CapturedOptions.Background.Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the --modified flag to the command options when the caller sets modified=true.")]
	public void GenerateSourceCode_Should_Forward_Modified_Flag() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeRunArgs("dev", Modified: true, Required: false, Background: false));

		resolvedCommand.CapturedOptions!.Modified.Should().BeTrue(
			because: "the modified flag must be forwarded from MCP args to command options");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the --required flag to the command options when the caller sets required=true.")]
	public void GenerateSourceCode_Should_Forward_Required_Flag() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeRunArgs("dev", Modified: false, Required: true, Background: false));

		resolvedCommand.CapturedOptions!.Required.Should().BeTrue(
			because: "the required flag must be forwarded from MCP args to command options");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the --background flag to the command options when the caller sets background=true.")]
	public void GenerateSourceCode_Should_Forward_Background_Flag() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeRunArgs("dev", Modified: false, Required: false, Background: true));

		resolvedCommand.CapturedOptions!.Background.Should().BeTrue(
			because: "the background flag must be forwarded from MCP args to command options");
	}

	[Test]
	[Category("Unit")]
	[Description("Defaults all optional flags to false when the caller omits them.")]
	public void GenerateSourceCode_Should_Default_All_Flags_To_False_When_Omitted() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeRunArgs("dev", null, null, null));

		resolvedCommand.CapturedOptions!.Modified.Should().BeFalse(
			because: "omitting modified should default to false (generate all)");
		resolvedCommand.CapturedOptions.Required.Should().BeFalse(
			because: "omitting required should default to false");
		resolvedCommand.CapturedOptions.Background.Should().BeFalse(
			because: "omitting background should default to false (synchronous)");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error result when the requested environment cannot be resolved.")]
	public void GenerateSourceCode_Should_Report_Invalid_Environment_As_Command_Result() {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment with key 'missing-env' not found."));
		GenerateSourceCodeTool tool = new(new FakeGenerateSourceCodeCommand(), ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.GenerateSourceCode(
			new GenerateSourceCodeRunArgs("missing-env", null, null, null));

		result.ExitCode.Should().Be(-1,
			because: "resolver failures should be returned as structured error envelopes");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			message.Value != null &&
			message.Value.ToString()!.Contains("missing-env"),
			because: "the environment-resolution failure must surface in the output");
	}

	private static McpServerToolAttribute GetToolAttribute() =>
		typeof(GenerateSourceCodeTool)
			.GetMethod(nameof(GenerateSourceCodeTool.GenerateSourceCode))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

	private sealed class FakeGenerateSourceCodeCommand : GenerateSourceCodeCommand
	{
		public GenerateSourceCodeOptions? CapturedOptions { get; private set; }

		public FakeGenerateSourceCodeCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) { }

		public override int Execute(GenerateSourceCodeOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

}
