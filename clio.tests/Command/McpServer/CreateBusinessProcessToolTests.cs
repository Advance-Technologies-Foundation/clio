using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class CreateBusinessProcessToolTests {
	private const string SampleDescriptor =
		"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	[Test]
	[Description("Resolves the create-business-process MCP tool for the requested environment and forwards the inline descriptor and package override into command options.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Resolve_Command_And_Forward_Descriptor() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand defaultCommand = new();
		FakeCreateBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>())
			.Returns(resolvedCommand);
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", SampleDescriptor, "MyApp"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the create-business-process tool should forward a valid command payload for the requested environment");
		commandResolver.Received(1).Resolve<CreateBusinessProcessCommand>(Arg.Is<CreateBusinessProcessOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.DescriptorJson == SampleDescriptor &&
			options.PackageName == "MyApp"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the startup one");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded create-business-process options");
		resolvedCommand.CapturedOptions!.DescriptorJson.Should().Be(SampleDescriptor,
			because: "the inline descriptor must be carried through to the command without modification");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the environment name is empty.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("   ", SampleDescriptor, null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment name is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the descriptor is empty.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Fail_When_Descriptor_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", "   ", null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty descriptor is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateBusinessProcessCommand : CreateBusinessProcessCommand {
		public CreateBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeCreateBusinessProcessCommand()
			: base(Substitute.For<ICreateBusinessProcessService>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(CreateBusinessProcessOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
