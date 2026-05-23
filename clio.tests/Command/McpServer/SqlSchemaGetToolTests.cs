using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class SqlSchemaGetToolTests {

	[Test]
	[Category("Unit")]
	public void GetSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaGetCommand defaultCommand = new();
		FakeSqlSchemaGetCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaGetCommand>(Arg.Any<SqlSchemaGetOptions>())
			.Returns(resolvedCommand);
		SqlSchemaGetTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaGetResponse response = tool.GetSchema(new SqlSchemaGetArgs("UsrScript") {
			OutputFile = "/tmp/out.sql", EnvironmentName = "dev" });

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrScript");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/out.sql");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void GetSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaGetCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaGetCommand>(Arg.Any<SqlSchemaGetOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		SqlSchemaGetTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		SqlSchemaGetResponse response = tool.GetSchema(new SqlSchemaGetArgs("UsrScript") {
			EnvironmentName = "missing" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeSqlSchemaGetCommand : SqlSchemaGetCommand {
		public SqlSchemaGetOptions CapturedOptions { get; private set; }

		public FakeSqlSchemaGetCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSchema(SqlSchemaGetOptions options, out SqlSchemaGetResponse response) {
			CapturedOptions = options;
			response = new SqlSchemaGetResponse {
				Success = true,
				SchemaName = options.SchemaName
			};
			return true;
		}
	}
}
