using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Prompts.ProcessDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DescribeProcessToolTests {

	[Test]
	[Category("Unit")]
	[Description("describe-business-process is a read-only, non-destructive, idempotent, closed-world MCP tool.")]
	public void DescribeProcess_ShouldCarryReadOnlySafetyFlags_WhenInspected() {
		// Arrange
		MethodInfo method = typeof(DescribeProcessTool).GetMethod(nameof(DescribeProcessTool.DescribeProcess));
		McpServerToolAttribute attribute = method!.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		attribute.Should().NotBeNull(because: "describe-business-process must be exposed as an MCP tool");
		attribute!.ReadOnly.Should().BeTrue(because: "describe-business-process only reads a process");
		attribute.Destructive.Should().BeFalse(because: "describe-business-process changes nothing");
		attribute.Idempotent.Should().BeTrue(because: "reading the same process yields the same description");
		attribute.OpenWorld.Should().BeFalse(because: "it reads a known environment, not an open world");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware describe-business-process command and maps MCP arguments into command options.")]
	public void DescribeProcess_ShouldResolveCommandAndMapArguments_WhenInvoked() {
		// Arrange
		FakeDescribeProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DescribeProcessCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		DescribeProcessTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DescribeProcess(new DescribeProcessArgs(
			EnvironmentName: "dev",
			ProcessName: "UsrProcess_493d4c9",
			ProcessUid: null,
			ProcessCaption: null,
			Culture: "uk-UA"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid describe-business-process request executes the resolved environment-aware command");
		commandResolver.Received(1).Resolve<DescribeProcessCommand>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command receives mapped options");
		resolvedCommand.CapturedOptions!.ProcessName.Should().Be("UsrProcess_493d4c9",
			because: "the process name must be forwarded from MCP arguments");
		resolvedCommand.CapturedOptions.Culture.Should().Be("uk-UA",
			because: "the requested culture must be forwarded to the command");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "the environment name must be preserved for environment-aware resolution");
	}

	[Test]
	[Category("Unit")]
	[Description("Omitted optional culture falls back to the command default so MCP and CLI stay aligned.")]
	public void DescribeProcess_ShouldUseDefaultCulture_WhenCultureOmitted() {
		// Arrange
		FakeDescribeProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DescribeProcessCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		DescribeProcessTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		tool.DescribeProcess(new DescribeProcessArgs("dev", null, null, "AI PoC Read Contact", null));

		// Assert
		resolvedCommand.CapturedOptions!.Culture.Should().Be("en-US",
			because: "omitting culture should use the command default");
		resolvedCommand.CapturedOptions.ProcessCaption.Should().Be("AI PoC Read Contact",
			because: "the caption identity must be forwarded");
	}

	[Test]
	[Category("Unit")]
	[Description("The prompt references the exact tool name and keeps the identity arguments visible.")]
	public void DescribeProcessPrompt_ShouldMentionToolNameAndArguments_WhenRendered() {
		// Act
		string prompt = DescribeProcessPrompt.DescribeProcessGuidance("UsrProcess_493d4c9", "dev");

		// Assert
		prompt.Should().Contain(DescribeProcessTool.ToolName, because: "the prompt references the production tool name");
		prompt.Should().Contain("process-name", because: "the prompt keeps the identity arguments visible");
		prompt.Should().Contain("process-modeling", because: "the prompt points callers at the narration guidance");
	}

	[Test]
	[Category("Unit")]
	[Description("The prompt's per-flow field list names EVERY field describe reports, and this pins it field by field because the prompt does NOT defer that list to guidance - step 2 sends the caller to get-guidance for the element catalog and connection-rule vocabulary only. An agent driven by this prompt is told what a flow carries and has no reason to look further, so a field added to the tool and not to the prompt is invisible to it. That is exactly what happened to `label`: the tool, the capability map, the unit tests and the e2e all gained it while the prompt still enumerated six fields, and nothing turned red because this fixture asserted only the tool name and the identity arguments. Add the next flow field here as well as to the prompt.")]
	public void DescribeProcessPrompt_ShouldEnumerateEveryFlowFieldDescribeReports() {
		// Act
		string prompt = DescribeProcessPrompt.DescribeProcessGuidance("UsrProcess_493d4c9", "dev");

		// Assert
		foreach (string field in new[] {
				"name", "source", "target", "kind", "label", "condition", "branchesOnActivityResult"
			}) {
			prompt.Should().Contain(field,
				because: $"a caller reading this prompt learns a flow's shape from it, so '{field}' being "
					+ "absent means an agent never looks for it");
		}

		prompt.Should().Contain("1.6.0.8",
			because: "an absent label has two meanings - no label, or a package that cannot report one - and "
				+ "the prompt has to name the version that separates them, or an agent reports the ambiguous "
				+ "read as a fact");
	}

	private sealed class FakeDescribeProcessCommand : DescribeProcessCommand {
		public DescribeProcessOptions CapturedOptions { get; private set; }

		public FakeDescribeProcessCommand()
			: base(Substitute.For<IProcessDescriber>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(DescribeProcessOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
