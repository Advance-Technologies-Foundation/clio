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

	[TestCase(null, 1)]
	[TestCase(2, 3)]
	[Category("Unit")]
	[Description("Maps default or explicit native SQL options into the requested environment command.")]
	public void CreateSchema_ShouldResolveRequestedEnvironment_WhenNativeOptionsAreSupplied(int? engine, int phase) {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSqlSchemaCreateCommand defaultCommand = new();
		FakeSqlSchemaCreateCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SqlSchemaCreateCommand>(Arg.Any<SqlSchemaCreateOptions>())
			.Returns(resolvedCommand);
		SqlSchemaCreateTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SqlSchemaCreateResponse response = tool.CreateSchema(new SqlSchemaCreateArgs("UsrScript", "Custom") {
			Caption = "Script caption", Description = "Script description", EnvironmentName = "dev", DbEngineType = engine, InstallType = phase });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command supplies the result");
		resolvedCommand.CapturedOptions.DbEngineType.Should().Be(engine, because: "the dialect override must reach the command");
		resolvedCommand.CapturedOptions.InstallType.Should().Be(phase, because: "the installation phase must reach the command");
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

		SqlSchemaCreateResponse response = tool.CreateSchema(new SqlSchemaCreateArgs("UsrScript", "Custom") {
			EnvironmentName = "missing" });

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
