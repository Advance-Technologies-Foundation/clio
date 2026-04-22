using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class DeleteSchemaToolTests {

	[Test]
	[Category("Unit")]
	public void DeleteSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteSchemaCommand defaultCommand = new();
		FakeDeleteSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DeleteSchemaCommand>(Arg.Any<DeleteSchemaOptions>())
			.Returns(resolvedCommand);
		DeleteSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.DeleteSchema(new DeleteSchemaArgs(
			"UsrMyTask",
			"docker_fix2",
			@"C:\Projects\clio-with-core-and-ui\workspace",
			null));

		result.ExitCode.Should().Be(0);
		commandResolver.Received(1).Resolve<DeleteSchemaCommand>(Arg.Is<DeleteSchemaOptions>(options =>
			options.SchemaName == "UsrMyTask"
			&& options.Environment == "docker_fix2"
			&& options.WorkspacePath == @"C:\Projects\clio-with-core-and-ui\workspace"
			&& options.Remote == false));
		defaultCommand.CapturedOptions.Should().BeNull();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		resolvedCommand.CapturedOptions.WorkspacePath.Should()
			.Be(@"C:\Projects\clio-with-core-and-ui\workspace");
		resolvedCommand.CapturedOptions.Remote.Should().BeFalse();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void DeleteSchema_Should_Forward_Remote_Flag_To_Options() {
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteSchemaCommand defaultCommand = new();
		FakeDeleteSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DeleteSchemaCommand>(Arg.Any<DeleteSchemaOptions>())
			.Returns(resolvedCommand);
		DeleteSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.DeleteSchema(new DeleteSchemaArgs(
			"UsrLegacyHelper",
			"docker_fix2",
			null,
			true));

		result.ExitCode.Should().Be(0);
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrLegacyHelper");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		resolvedCommand.CapturedOptions.Remote.Should().BeTrue();
		resolvedCommand.CapturedOptions.WorkspacePath.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeDeleteSchemaCommand : DeleteSchemaCommand {
		public DeleteSchemaOptions CapturedOptions { get; private set; }

		public FakeDeleteSchemaCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IWorkspacePathBuilder>(),
				Substitute.For<IJsonConverter>(),
				Substitute.For<IFileSystem>()) {
		}

		public override int Execute(DeleteSchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
