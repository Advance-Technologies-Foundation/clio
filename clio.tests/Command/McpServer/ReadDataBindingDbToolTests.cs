using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
///     ENG-88474: proving a binding's transfer contract used to mean exporting the package and parsing
///     <c>Data/&lt;binding&gt;/data.json</c>. This read replaces that, so it must resolve against the environment
///     carried by the current MCP call — reading a different stand's binding would "confirm" a projection that was
///     never shipped to the target.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class ReadDataBindingDbToolTests {

	[Test]
	[Description("Resolves the read-data-binding-db command for the requested environment and forwards the package and binding names into command options.")]
	[Category("Unit")]
	public void ReadDataBindingDb_Should_Resolve_Command_For_Requested_Environment(){
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeReadDataBindingDbCommand defaultCommand = new();
		FakeReadDataBindingDbCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ReadDataBindingDbCommand>(Arg.Any<ReadDataBindingDbOptions>())
			.Returns(resolvedCommand);
		ReadDataBindingDbTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ReadDataBindingDb(
			new ReadDataBindingDbArgs("docker_fix2", "UsrTodo", "SysWorkplace_Todo"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a well-formed read must forward a command payload rather than fail validation");
		commandResolver.Received(1).Resolve<ReadDataBindingDbCommand>(Arg.Is<ReadDataBindingDbOptions>(options =>
			options.Environment == "docker_fix2"
			&& options.PackageName == "UsrTodo"
			&& options.BindingName == "SysWorkplace_Todo"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware path must not execute the startup-time command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the command resolved for this call should receive the forwarded options");
		resolvedCommand.CapturedOptions!.BindingName.Should().Be("SysWorkplace_Todo",
			because: "the binding name is what selects the projection being proved, so it must survive the mapping");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeReadDataBindingDbCommand : ReadDataBindingDbCommand {

		public FakeReadDataBindingDbCommand()
			: base(Substitute.For<IDataBindingDbService>(), Substitute.For<ILogger>()){
		}

		public ReadDataBindingDbOptions? CapturedOptions { get; private set; }

		public override int Execute(ReadDataBindingDbOptions options){
			CapturedOptions = options;
			return 0;
		}

	}

}
