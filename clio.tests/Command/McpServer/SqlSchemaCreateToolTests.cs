using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class SqlSchemaCreateToolTests {

	[Test]
	[Category("Unit")]
	public void CreateSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaCreateCommand defaultCommand = new();
		FakeSqlSchemaCreateCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaCreateCommand>(Arg.Any<SqlSchemaCreateOptions>())
			.Returns(resolvedCommand);
		SqlSchemaCreateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaCreateResponse response = tool.CreateSchema(new SqlSchemaCreateArgs(
			"UsrScript", "Custom", "Script caption", "Script description", "dev", null, null, null));

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrScript");
		resolvedCommand.CapturedOptions.PackageName.Should().Be("Custom");
		resolvedCommand.CapturedOptions.Caption.Should().Be("Script caption");
		resolvedCommand.CapturedOptions.Description.Should().Be("Script description");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void CreateSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaCreateCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaCreateCommand>(Arg.Any<SqlSchemaCreateOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		SqlSchemaCreateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaCreateResponse response = tool.CreateSchema(new SqlSchemaCreateArgs(
			"UsrScript", "Custom", null, null, "missing", null, null, null));

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeSqlSchemaCreateCommand : SqlSchemaCreateCommand {
		public SqlSchemaCreateOptions CapturedOptions { get; private set; }

		public FakeSqlSchemaCreateCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryCreate(SqlSchemaCreateOptions options, out SqlSchemaCreateResponse response) {
			CapturedOptions = options;
			response = new SqlSchemaCreateResponse {
				Success = true,
				SchemaName = options.SchemaName,
				PackageName = options.PackageName
			};
			return true;
		}
	}
}
