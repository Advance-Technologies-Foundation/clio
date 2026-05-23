using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class SqlSchemaInstallToolTests {

	[Test]
	[Category("Unit")]
	public void InstallSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaInstallCommand defaultCommand = new();
		FakeSqlSchemaInstallCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaInstallCommand>(Arg.Any<SqlSchemaInstallOptions>())
			.Returns(resolvedCommand);
		SqlSchemaInstallTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaInstallResponse response = tool.InstallSchema(new SqlSchemaInstallArgs(
			"UsrScript", "dev", null, null, null));

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrScript");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void InstallSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaInstallCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaInstallCommand>(Arg.Any<SqlSchemaInstallOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		SqlSchemaInstallTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaInstallResponse response = tool.InstallSchema(new SqlSchemaInstallArgs(
			"UsrScript", "missing", null, null, null));

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeSqlSchemaInstallCommand : SqlSchemaInstallCommand {
		public SqlSchemaInstallOptions CapturedOptions { get; private set; }

		public FakeSqlSchemaInstallCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryInstall(SqlSchemaInstallOptions options, out SqlSchemaInstallResponse response) {
			CapturedOptions = options;
			response = new SqlSchemaInstallResponse {
				Success = true,
				SchemaName = options.SchemaName
			};
			return true;
		}
	}
}
