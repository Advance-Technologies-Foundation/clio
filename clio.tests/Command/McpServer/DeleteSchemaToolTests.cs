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
			@"C:\Projects\clio-with-core-and-ui\workspace"));

		result.ExitCode.Should().Be(0);
		commandResolver.Received(1).Resolve<DeleteSchemaCommand>(Arg.Is<DeleteSchemaOptions>(options =>
			options.SchemaName == "UsrMyTask"
			&& options.Environment == "docker_fix2"
			&& options.WorkspacePath == @"C:\Projects\clio-with-core-and-ui\workspace"));
		defaultCommand.CapturedOptions.Should().BeNull();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		resolvedCommand.CapturedOptions.WorkspacePath.Should()
			.Be(@"C:\Projects\clio-with-core-and-ui\workspace");
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
