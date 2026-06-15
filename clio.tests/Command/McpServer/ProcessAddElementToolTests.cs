using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Command.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Common.ProcessDesigner;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessAddElementToolTests {

	[Test]
	[Category("Unit")]
	[Description("process-add-element is not read-only and not idempotent; its static Destructive default is false (new-process path).")]
	public void ProcessAddElement_ShouldCarryExpectedSafetyFlags_WhenInspected() {
		// Arrange
		MethodInfo method = typeof(ProcessAddElementTool).GetMethod(nameof(ProcessAddElementTool.ProcessAddElement));
		McpServerToolAttribute attribute = method!.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		attribute.Should().NotBeNull(because: "process-add-element must be exposed as an MCP tool");
		attribute!.ReadOnly.Should().BeFalse(because: "the tool mutates the designer and saves a process");
		attribute.Idempotent.Should().BeFalse(because: "running it again creates another process");
		attribute.Destructive.Should().BeFalse(
			because: "the primary slice path creates a NEW process; supplying process-id (documented) modifies an existing one");
		attribute.OpenWorld.Should().BeFalse(because: "it acts on a known environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware command and maps MCP arguments into command options.")]
	public void ProcessAddElement_ShouldResolveCommandAndMapArguments_WhenInvoked() {
		// Arrange
		FakeProcessAddElementCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ProcessAddElementCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		ProcessAddElementTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ProcessAddElement(new ProcessAddElementArgs(
			EnvironmentName: "dev",
			ElementType: "read-data",
			ReadObject: "Contact",
			ProcessId: null,
			ProcessCaption: "My read",
			Headed: null));

		// Assert
		result.ExitCode.Should().Be(0, because: "a valid request executes the resolved environment-aware command");
		commandResolver.Received(1).Resolve<ProcessAddElementCommand>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command receives mapped options");
		resolvedCommand.CapturedOptions!.ElementType.Should().Be("read-data", because: "element-type must be forwarded");
		resolvedCommand.CapturedOptions.ReadObject.Should().Be("Contact", because: "read-object must be forwarded");
		resolvedCommand.CapturedOptions.ProcessCaption.Should().Be("My read", because: "the caption must be forwarded");
		resolvedCommand.CapturedOptions.Headed.Should().BeTrue(because: "omitted headed defaults to true");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment must be preserved for resolution");
	}

	[Test]
	[Description("The prompt references the exact tool name and the read-data flow.")]
	[Category("Unit")]
	public void ProcessAddElementPrompt_ShouldMentionToolNameAndFlow_WhenRendered() {
		// Act
		string prompt = ProcessAddElementPrompt.ProcessAddElementGuidance("Contact", "dev");

		// Assert
		prompt.Should().Contain(ProcessAddElementTool.ToolName, because: "the prompt references the production tool name");
		prompt.Should().Contain("read-data", because: "the prompt names the supported element type");
		prompt.Should().Contain("validate-process-graph", because: "the prompt steers callers to validate first");
	}

	private sealed class FakeProcessAddElementCommand : ProcessAddElementCommand {
		public ProcessAddElementOptions CapturedOptions { get; private set; }

		public FakeProcessAddElementCommand()
			: base(
				Substitute.For<IProcessGraphValidator>(),
				Substitute.For<IBrowserSessionService>(),
				Substitute.For<IAuthenticatedBrowserLauncher>(),
				Substitute.For<IProcessDesignerDriver>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(ProcessAddElementOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
