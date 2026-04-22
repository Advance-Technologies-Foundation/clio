using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class SqlSchemaUpdateToolTests {

	[Test]
	[Category("Unit")]
	public void UpdateSchema_Should_Resolve_Command_And_Map_Options() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaUpdateCommand defaultCommand = new();
		FakeSqlSchemaUpdateCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaUpdateCommand>(Arg.Any<SqlSchemaUpdateOptions>())
			.Returns(resolvedCommand);
		SqlSchemaUpdateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaUpdateResponse response = tool.UpdateSchema(new SqlSchemaUpdateArgs(
			"UsrScript", "SELECT 1;", "/tmp/body.sql", true, "dev", null, null, null));

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrScript");
		resolvedCommand.CapturedOptions.Body.Should().Be("SELECT 1;");
		resolvedCommand.CapturedOptions.BodyFile.Should().Be("/tmp/body.sql");
		resolvedCommand.CapturedOptions.DryRun.Should().BeTrue();
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void UpdateSchema_Should_Default_DryRun_To_False_When_Null() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaUpdateCommand defaultCommand = new();
		FakeSqlSchemaUpdateCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaUpdateCommand>(Arg.Any<SqlSchemaUpdateOptions>())
			.Returns(resolvedCommand);
		SqlSchemaUpdateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		tool.UpdateSchema(new SqlSchemaUpdateArgs(
			"UsrScript", "SELECT 1;", null, null, "dev", null, null, null));

		resolvedCommand.CapturedOptions.DryRun.Should().BeFalse();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void UpdateSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaUpdateCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaUpdateCommand>(Arg.Any<SqlSchemaUpdateOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		SqlSchemaUpdateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaUpdateResponse response = tool.UpdateSchema(new SqlSchemaUpdateArgs(
			"UsrScript", "SELECT 1;", null, null, "missing", null, null, null));

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeSqlSchemaUpdateCommand : SqlSchemaUpdateCommand {
		public SqlSchemaUpdateOptions CapturedOptions { get; private set; }

		public FakeSqlSchemaUpdateCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryUpdateSchema(SqlSchemaUpdateOptions options, out SqlSchemaUpdateResponse response) {
			CapturedOptions = options;
			response = new SqlSchemaUpdateResponse {
				Success = true,
				SchemaName = options.SchemaName
			};
			return true;
		}
	}
}
