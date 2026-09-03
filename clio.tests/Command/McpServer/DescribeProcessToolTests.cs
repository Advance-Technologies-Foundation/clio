using System.Linq;
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
	[Description("The describe-business-process description states the version contract: which field reaches the running version, and that absent version fields mean unknown rather than unversioned.")]
	public void DescribeProcess_ShouldStateTheVersionContract_WhenItsDescriptionIsRead() {
		// Arrange
		MethodInfo method = typeof(DescribeProcessTool).GetMethod(nameof(DescribeProcessTool.DescribeProcess))!;

		// Act
		System.ComponentModel.DescriptionAttribute description = (System.ComponentModel.DescriptionAttribute)method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single();
		McpServerToolAttribute toolAttribute = (McpServerToolAttribute)method
			.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();

		// Assert
		description.Description.Should().Contain("activeVersionSchemaUId",
			because: "an agent told the graph is not the running one needs the field that addresses the one that is");
		description.Description.Should().Contain("versionReadWarning",
			because: "the description must name where the reason for absent version facts is reported");
		description.Description.Should().Contain("never 'unversioned'",
			because: "reading absent version fields as 'this process has no versions' reproduces the ENG-94374 defect with the fields in place");
		description.Description.Should().Contain("FAMILY state",
			because: "a family entry's enabled flag is per-family, and an agent that reads it per-version reports the wrong thing");
		toolAttribute.ReadOnly.Should().BeTrue(
			because: "the version read is an additional read on the describe path; it must not change the tool's classification");
	}

	[Test]
	[Category("Unit")]
	[Description("Cross-channel drift guard: the active-version invariant must appear in BOTH clio-owned describe channels — the tool [Description] and the describe prompt — so dropping it from either fails the build.")]
	public void ActiveVersionInvariant_Should_Appear_In_All_Clio_Channels() {
		// Arrange
		string toolDescription = ((System.ComponentModel.DescriptionAttribute)typeof(DescribeProcessTool)
			.GetMethod(nameof(DescribeProcessTool.DescribeProcess))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;

		// Act
		string prompt = DescribeProcessPrompt.DescribeProcessGuidance("UsrProcess_493d4c9", "dev");

		// Assert — the guidance article that repeats this lives in clio-knowledge and is guarded there, not here.
		toolDescription.Should().Contain("isActiveVersion",
			because: "the tool contract has to name the flag that decides whether the returned graph can be trusted");
		prompt.Should().Contain("isActiveVersion",
			because: "an agent following the prompt instead of the tool description must reach the same check");
		toolDescription.Should().Contain("activeVersionSchemaUId",
			because: "both channels have to point at the same field for reaching the running version");
		prompt.Should().Contain("activeVersionSchemaUId",
			because: "a prompt that says to check the flag but not how to act on it leaves the agent stuck");
		prompt.Should().Contain("BEFORE narrating",
			because: "the check is worthless after the answer is written, so the prompt has to order it first");
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
